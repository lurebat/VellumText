using System.Collections.Immutable;
using Avalonia;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>
/// A table, drawn as a grid.
/// </summary>
/// <remarks>
/// <para>
/// The first block view that hosts other block views. Everything else in the document is a
/// vertical run, which is why the presenter can flatten a document into an array and answer
/// every geometry question with a search. A table is the one structure that cannot be
/// flattened — its cells sit side by side — so it owns its children and answers for them.
/// </para>
/// <para>
/// The position arithmetic here mirrors <see cref="DocumentBlocks"/> exactly, because it has
/// to: the document offsets the presenter hands out are computed by that walk, and a table that
/// disagreed by one would put the caret in the wrong cell. Local position 0 is the first
/// position inside the table; each row occupies <c>NodeSize</c> positions starting one past the
/// row's opening boundary, and so on down to the blocks in a cell. The mapping is built as a list
/// of rows, each holding its cells and their child views with starts <em>relative to the row</em>,
/// plus one array of where each row starts. An edit inside one row changes its size and so moves
/// every row after it; keeping the starts relative is what lets that move be one pass over an
/// array of integers rather than a rebuild of every cell below.
/// </para>
/// <para>
/// Column widths follow the table when it states them and are divided evenly when it does not.
/// Even division is not what a browser does — a browser sizes columns to their content — but it
/// is predictable, it never collapses a column to nothing, and content-based sizing needs a
/// min/max width pass over every cell that the block views do not currently expose. Explicit
/// widths, which is what column resize and the JSON format produce, are honoured. Corrected
/// 2026-08-19: this said the HTML and RTF importers produce them too, and neither does — the HTML
/// importer builds every table with an empty width vector and the RTF exporter writes a fixed
/// <c>\cellx</c> step regardless, so a table's proportions do not survive either format.
/// </para>
/// </remarks>
internal sealed class TableBlockView : BlockView
{
    private const double CellPadding = 6;
    private const double BorderThickness = 1;

    private readonly Func<BlockNode, BlockView> _create;
    private readonly IBrush? _header;
    private readonly IPen _border;

    private TableNode _table;
    private Row[] _rows = [];
    private int[] _rowStarts = [0];
    private int[] _coverTop = [];
    private int _columns;

    private double[] _columnEdges = [];
    private double[] _rowEdges = [];
    private Size _size;

    /// <summary>Creates a view of a table.</summary>
    /// <param name="table">The table.</param>
    /// <param name="create">Makes a view for a block inside a cell.</param>
    /// <param name="border">The brush the grid lines are drawn in.</param>
    /// <param name="header">The brush a header cell is filled with, or null to leave it plain.</param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="header"/> is null.</exception>
    public TableBlockView(
        TableNode table,
        Func<BlockNode, BlockView> create,
        IBrush border,
        IBrush? header = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(border);

        _table = table;
        _create = create;
        _header = header;
        _border = new Pen(border, BorderThickness);

        Rebuild(table, []);
    }

    /// <summary>
    /// Brings the view up to date with an edited table, keeping the views of every cell the edit
    /// did not touch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rebuilding the view instead costs a fresh <c>TextLayout</c> for every block in every cell,
    /// on every keystroke. Measured on a 50×5 table, that was 18.5 ms per typed character against
    /// a 16.7 ms frame — the table was the only structure in the document that got slower the
    /// bigger it was, because it was the only one <c>Reconcile</c> could not update in place.
    /// </para>
    /// <para>
    /// The document is a persistent tree, so an edit inside one cell leaves every other row node
    /// reference-identical to the one already on screen. A row whose node is the same object is
    /// therefore left exactly as it is, and only the rows the edit replaced are walked (F-35):
    /// walking every cell to find the one that changed was 5.35 ms a keystroke at 1,000 × 5. That
    /// holds while the grid keeps its shape — the same rows, each with cells of the same spans, so
    /// every cell is placed where it was. A change to the shape places and walks everything again.
    /// </para>
    /// </remarks>
    /// <param name="table">The table as it is now.</param>
    /// <returns>Whether anything changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    public bool Update(TableNode table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (ReferenceEquals(table, _table))
        {
            return false;
        }

        if (!TryUpdateRows(table))
        {
            Rebuild(table, [.. _rows.SelectMany(row => row.Cells)]);
        }

        return true;
    }

    /// <summary>
    /// Places every cell again and walks the whole table into rows, reusing the views of
    /// <paramref name="previous"/> wherever the edit left a block untouched.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="previous">The cells as they were, in row-major order, or none.</param>
    private void Rebuild(TableNode table, Cell[] previous)
    {
        var placements = TableGeometry.Place(table, out _columns);
        var rows = new Row[table.Rows.Length];

        // Walked in exactly the order TableGeometry placed them, which is the order the rows and
        // their cells appear in, so placement i belongs to the i'th cell reached here.
        var index = 0;

        for (var r = 0; r < rows.Length; r++)
        {
            var node = table.Rows[r];
            var cells = new Cell[node.Cells.Length];

            // Cells are matched by position rather than by identity. An edit inside one cell
            // leaves the others alone, so the i'th cell of the new table is the i'th of the old
            // one; a structural change that makes that false only costs a rebuild, which is what
            // would have happened anyway.
            BuildCells(node, cells, i => placements[index + i], i => index + i < previous.Length ? previous[index + i] : null);

            index += cells.Length;
            rows[r] = new Row(node, cells);
        }

        _rows = rows;
        _coverTop = CoverTops(rows);
        _table = table;
        UpdateRowStarts();
    }

    /// <summary>
    /// Brings only the replaced rows up to date, if the edit kept the grid's shape.
    /// </summary>
    /// <returns>False, having changed nothing, if the shape changed.</returns>
    private bool TryUpdateRows(TableNode table)
    {
        var before = _table.Rows;
        var after = table.Rows;

        // Column widths are not part of the shape: they decide how wide a column is drawn, which
        // measuring works out from the table every time, not which slots a cell occupies.
        if (before.Length != after.Length)
        {
            return false;
        }

        // Checked in full before anything is touched, so a shape change found halfway down
        // leaves the view as it was for the rebuild to start from.
        for (var r = 0; r < after.Length; r++)
        {
            if (!ReferenceEquals(before[r], after[r]) && !SameShape(before[r], after[r]))
            {
                return false;
            }
        }

        for (var r = 0; r < after.Length; r++)
        {
            if (ReferenceEquals(before[r], after[r]))
            {
                continue;
            }

            var row = _rows[r];
            var old = row.Cells;
            var cells = new Cell[old.Length];

            // The same spans in the same rows place every cell where it was.
            BuildCells(after[r], cells, i => old[i].Placement, i => old[i]);

            row.Node = after[r];
            row.Cells = cells;
        }

        _table = table;
        UpdateRowStarts();

        return true;

        static bool SameShape(TableRowNode a, TableRowNode b)
        {
            if (a.Cells.Length != b.Cells.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Cells.Length; i++)
            {
                if (a.Cells[i].RowSpan != b.Cells[i].RowSpan || a.Cells[i].ColumnSpan != b.Cells[i].ColumnSpan)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Walks one row's cells into <paramref name="cells"/>, reusing views where it can.
    /// </summary>
    /// <remarks>
    /// The one place the local position arithmetic lives. It has to mirror
    /// <see cref="DocumentBlocks"/> exactly, and a second copy of it for the update path is the
    /// obvious way for the two to drift apart by one and put the caret in the wrong cell. Every
    /// start it records is relative to the row's opening boundary.
    /// </remarks>
    private void BuildCells(
        TableRowNode row,
        Cell[] cells,
        Func<int, CellPlacement> placement,
        Func<int, Cell?> previous)
    {
        // One past the row's opening boundary.
        var cellStart = 1;

        for (var i = 0; i < cells.Length; i++)
        {
            WorkCounters.TableCellsBuilt++;

            var cell = row.Cells[i];
            var was = previous(i);
            var blocks = ImmutableArray.CreateBuilder<Child>(cell.Blocks.Length);

            // Flattened, because a list has no view of its own: it is drawn as its paragraphs
            // wearing a depth and a marker. A cell that handed its blocks straight to create
            // would throw the moment one of them was a list.
            var slots = DocumentBlocks.Flatten(cell.Blocks, cellStart + 1);
            var at = 0;

            foreach (var slot in slots)
            {
                var before = was is not null && at < was.Blocks.Length ? was.Blocks[at] : null;
                var view = Reuse(before, slot.Node, Prepare) ?? _create(slot.Node);

                // A marker is not part of the paragraph node, so a reused view can be drawn
                // for the same paragraph under a different bullet and must be told each time.
                if (view is ParagraphView paragraph)
                {
                    paragraph.SetLead(slot.Depth, slot.Marker);
                }

                blocks.Add(new Child(slot.Node, view, slot.Start));
                at++;
            }

            cells[i] = new Cell(cell, placement(i), cellStart + 1, blocks.ToImmutable(), _header);
            cellStart += cell.NodeSize;
        }
    }

    /// <summary>Recomputes where each row starts, in place.</summary>
    /// <remarks>
    /// One pass over the rows' sizes, which the nodes already know. An edit in one row moves
    /// every row after it, and this is the whole of what that costs.
    /// </remarks>
    private void UpdateRowStarts()
    {
        var rows = _table.Rows;

        if (_rowStarts.Length != rows.Length + 1)
        {
            _rowStarts = new int[rows.Length + 1];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            _rowStarts[r + 1] = _rowStarts[r] + rows[r].NodeSize;
        }
    }

    /// <summary>For each row, the first row of any cell that covers it.</summary>
    /// <remarks>
    /// What lets a band of rows be drawn without looking at every row above it: a cell that
    /// reaches into the band from above starts no higher than this. Built only when the grid's
    /// shape changes; the cost is the slots the cells occupy, as placing them is.
    /// </remarks>
    private static int[] CoverTops(Row[] rows)
    {
        var tops = new int[rows.Length];

        for (var r = 0; r < tops.Length; r++)
        {
            tops[r] = r;
        }

        foreach (var row in rows)
        {
            foreach (var cell in row.Cells)
            {
                var bottom = Math.Min(cell.Placement.Bottom, tops.Length);

                for (var r = cell.Placement.Top + 1; r < bottom; r++)
                {
                    tops[r] = Math.Min(tops[r], cell.Placement.Top);
                }
            }
        }

        return tops;
    }

    /// <summary>The view already on screen for a block, if it can be kept, or null.</summary>
    /// <remarks>
    /// An unchanged block keeps its view outright. A changed one keeps it only if the view can be
    /// told to update itself, which a paragraph and a nested table can and an image and a rule
    /// cannot — and an image especially must not be rebuilt for nothing, because building one
    /// resolves its source.
    /// </remarks>
    private static BlockView? Reuse(Child? before, BlockNode block, Func<ParagraphNode, ParagraphNode>? prepare) => before switch
    {
        null => null,
        _ when ReferenceEquals(before.Node, block) => before.View,
        { View: ParagraphView view } when block is ParagraphNode paragraph => Updated(view, prepare?.Invoke(paragraph) ?? paragraph),
        { View: TableBlockView view } when block is TableNode nested => Updated(view, nested),
        _ => null,
    };

    private static BlockView Updated(ParagraphView view, ParagraphNode paragraph)
    {
        view.Update(paragraph);

        return view;
    }

    private static BlockView Updated(TableBlockView view, TableNode table)
    {
        view.Update(table);

        return view;
    }

    /// <inheritdoc/>
    public override int ContentSize => _table.ContentSize;

    /// <inheritdoc/>
    public override Size Size => _size;

    /// <summary>How many columns the table turned out to be.</summary>
    /// <remarks>
    /// Not <c>Rows[0].Cells.Length</c>: a row-spanning cell means a row can carry fewer cells
    /// than the table is wide.
    /// </remarks>
    public int Columns => _columns;

    /// <summary>
    /// What a changed paragraph goes through before a reused view is updated with it, as
    /// <c>create</c> does for a new one — syntax highlighting, from the presenter.
    /// </summary>
    /// <remarks>
    /// Without it an edit in a cell updated the view with the bare paragraph, and a highlighted
    /// code block in a table lost its colours on the first keystroke.
    /// </remarks>
    internal Func<ParagraphNode, ParagraphNode>? Prepare { get; init; }

    /// <summary>The paragraph views inside the cells, in document order.</summary>
    internal IEnumerable<ParagraphView> ParagraphViews =>
        _rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Blocks)
            .Select(child => child.View).OfType<ParagraphView>();

    /// <inheritdoc/>
    public override Size Measure(double availableWidth)
    {
        var width = double.IsFinite(availableWidth) && availableWidth > 0
            ? availableWidth
            : 0;

        _columnEdges = ColumnEdges(width);

        var heights = new double[_rows.Length];

        foreach (var row in _rows)
        {
            foreach (var cell in row.Cells)
            {
                var inner = CellWidth(cell.Placement) - (2 * CellPadding);
                var content = 0.0;

                foreach (var child in cell.Blocks)
                {
                    // Top is an offset within the cell's content box; the padding is added once,
                    // by ChildOrigin, so that measuring and drawing cannot disagree about it.
                    //
                    // At least one pixel to wrap to. A column narrower than twice the padding
                    // leaves nothing inside it, and a paragraph refuses a width of zero outright —
                    // so clamping to zero threw for a table that merely stated a very narrow
                    // column. Reachable from the JSON format, from a host building a TableNode
                    // directly, and now from a resize; not from HTML or RTF, neither of which
                    // carries widths at all — see the remarks on this class. The text overflows
                    // the cell at this width, which is what a column this narrow means.
                    child.Top = content;
                    content += child.View.Measure(Math.Max(1, inner)).Height;
                }

                var height = content + (2 * CellPadding);

                cell.Height = height;

                // A cell spanning rows is not the height of the row it starts in; it only has to
                // fit across all of them. Charging its whole height to the first row would make
                // that row as tall as the span, which is the classic way to get a table with one
                // enormous row.
                if (cell.Placement.Bottom - cell.Placement.Top == 1)
                {
                    heights[cell.Placement.Top] = Math.Max(heights[cell.Placement.Top], height);
                }
            }
        }

        // Now the spanning cells, against the row heights the single-row cells settled. Only a
        // cell that still does not fit pushes, and it pushes the last row it covers.
        foreach (var row in _rows)
        {
            foreach (var cell in row.Cells)
            {
                var span = cell.Placement.Bottom - cell.Placement.Top;

                if (span == 1)
                {
                    continue;
                }

                var covered = 0.0;

                for (var r = cell.Placement.Top; r < cell.Placement.Bottom && r < heights.Length; r++)
                {
                    covered += heights[r];
                }

                var last = Math.Min(cell.Placement.Bottom, heights.Length) - 1;

                if (last >= 0 && covered < cell.Height)
                {
                    heights[last] += cell.Height - covered;
                }
            }
        }

        _rowEdges = Edges(heights);
        _size = new Size(width, _rowEdges[^1]);

        return _size;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context, Point origin) =>
        Render(context, origin, null);

    /// <inheritdoc/>
    /// <remarks>
    /// Only the rows that meet <paramref name="visible"/> are drawn (F-35). Every cell's
    /// paragraph was drawn whatever the viewport showed, which was 93% of a frame showing the top
    /// of a 1,000-row table; the rows are found by a binary search over their edges, and a cell
    /// spanning into them from above is found from the row it starts in.
    /// </remarks>
    public override void Render(DrawingContext context, Point origin, Rect visible) =>
        Render(context, origin, (Rect?)visible);

    private void Render(DrawingContext context, Point origin, Rect? visible)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (first, last) = visible is { } clip ? RowsMeeting(clip) : (0, _rows.Length - 1);

        if (first > last)
        {
            return;
        }

        // Inflated by the border, which is drawn centred on a cell's edge and so reaches half its
        // width outside the cell.
        var reach = visible?.Inflate(BorderThickness);

        for (var r = _coverTop[first]; r <= last; r++)
        {
            foreach (var cell in _rows[r].Cells)
            {
                WorkCounters.TableCellsExamined++;

                var local = CellBounds(cell.Placement);

                if (reach is { } area && !local.Intersects(area))
                {
                    continue;
                }

                WorkCounters.TableCellsRendered++;

                var bounds = local.Translate(new Vector(origin.X, origin.Y));

                if (cell.Fill is { } fill)
                {
                    context.FillRectangle(fill, bounds);
                }

                context.DrawRectangle(null, _border, bounds);

                foreach (var child in cell.Blocks)
                {
                    var at = ChildOrigin(cell, child);
                    var childOrigin = new Point(origin.X + at.X, origin.Y + at.Y);

                    if (visible is { } within)
                    {
                        child.View.Render(context, childOrigin, within.Translate(new Vector(-at.X, -at.Y)));
                    }
                    else
                    {
                        child.View.Render(context, childOrigin);
                    }
                }
            }
        }
    }

    /// <summary>The first and last rows whose band meets a rectangle in local coordinates.</summary>
    /// <remarks>The first is past the last when no row does.</remarks>
    private (int First, int Last) RowsMeeting(Rect area)
    {
        var top = area.Top - BorderThickness;
        var bottom = area.Bottom + BorderThickness;

        // The first row ending below the top of the area.
        var low = 0;
        var high = _rows.Length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (_rowEdges[middle + 1] > top)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        var first = low;

        // The last row starting above the bottom of the area.
        low = -1;
        high = _rows.Length - 1;

        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);

            if (_rowEdges[middle] < bottom)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return (first, low);
    }

    /// <inheritdoc/>
    public override int HitTest(Point local)
    {
        if (NearestCell(local) is not { } nearest)
        {
            return 0;
        }

        var (cell, rowStart) = nearest;

        var bounds = CellBounds(cell.Placement);
        var inner = new Point(local.X - bounds.X - CellPadding, local.Y - bounds.Y - CellPadding);
        var child = NearestChild(cell, inner.Y);

        // An empty cell is not schema-valid, but the schema reports rather than throws and
        // nothing stops a caller building one, so there may be nothing to hand the point to.
        return child is null
            ? rowStart + cell.Start
            : rowStart + child.Start + child.View.HitTest(new Point(inner.X, inner.Y - child.Top));
    }

    /// <inheritdoc/>
    public override Rect GetCaretRect(int localPosition)
    {
        if (Find(localPosition) is not { } found)
        {
            // A boundary position — between two rows, say — has no glyph to sit beside. The top
            // left of the table is wrong but drawable, which beats throwing on a position the
            // caret can legitimately hold.
            return new Rect(0, 0, 0, _size.Height);
        }

        var (cell, child, rowStart) = found;
        var origin = ChildOrigin(cell, child);

        return child.View
            .GetCaretRect(Math.Clamp(localPosition - rowStart - child.Start, 0, child.View.ContentSize))
            .Translate(new Vector(origin.X, origin.Y));
    }

    /// <summary>
    /// The boxes of the given cells, for a rectangular selection.
    /// </summary>
    /// <param name="cells">
    /// The document positions directly before each selected cell, as
    /// <see cref="CellSelection.Cells"/> reports them.
    /// </param>
    /// <param name="start">This table's own first inner position, in document coordinates.</param>
    /// <remarks>
    /// Distinct from <see cref="GetSelectionRects"/>, which highlights the text a range covers.
    /// A rectangle selects cells rather than text, so an empty cell must still be filled and a
    /// half-empty one filled to its edges — anything else draws a ragged rectangle that does not
    /// look like a selection at all.
    /// </remarks>
    public IReadOnlyList<Rect> GetCellRects(IReadOnlyCollection<int> cells, int start)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var rects = new List<Rect>();

        Collect(cells, rects, start, default);

        return rects;
    }

    /// <summary>
    /// Finds the column boundary a point is on, if it is on one.
    /// </summary>
    /// <param name="point">The point, in the coordinates <paramref name="offset"/> is given in.</param>
    /// <param name="start">The table's first inner position, in document coordinates.</param>
    /// <param name="offset">Where this table's local origin sits in those same coordinates.</param>
    /// <param name="tolerance">How far from the line still counts as on it.</param>
    /// <returns>The boundary, or null if the point is not on one.</returns>
    /// <remarks>
    /// <para>
    /// Only the interior boundaries are offered. The outer two are the table's own edges, and
    /// dragging one would change the table's total width rather than redistribute it — the widths
    /// are shares of whatever the table is given, so there is nothing an outer edge could mean.
    /// </para>
    /// <para>
    /// A boundary is grabbable down the table's whole height, including where a cell spanning
    /// columns draws no line there. The boundary belongs to the grid, which is what the widths
    /// index; hiding it under a merged cell would make a column unresizable for the length of
    /// that cell without making the column any less real.
    /// </para>
    /// <para>
    /// Nested tables are asked first, matching <see cref="Collect"/>: an inner table's boundary
    /// lies inside one of this table's cells, so it is the more specific answer, and the outer
    /// table's own boundaries cannot be inside a cell to be confused with it.
    /// </para>
    /// <para>
    /// The vertical check below is against this table's own height and runs after that scan, so
    /// a nested table declines the pointer everywhere it is not drawn and the outer table answers
    /// there instead. Hoisting it above the scan would not do: a point inside a nested table is
    /// inside this table too, so this table's height cannot decide for the nested one.
    /// </para>
    /// </remarks>
    public ColumnBoundary? BoundaryAt(Point point, int start, Vector offset, double tolerance)
    {
        for (var r = 0; r < _rows.Length; r++)
        {
            foreach (var cell in _rows[r].Cells)
            {
                foreach (var child in cell.Blocks)
                {
                    if (child.View is not TableBlockView nested)
                    {
                        continue;
                    }

                    var origin = ChildOrigin(cell, child);
                    var found = nested.BoundaryAt(
                        point,
                        start + _rowStarts[r] + child.Start,
                        offset + new Vector(origin.X, origin.Y),
                        tolerance);

                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        var local = point - offset;

        // Only the vertical extent needs checking. The loop below already yields nothing for a
        // table of one column or none, so a guard on the edge count would be an early return that
        // no test could tell from its absence.
        if (local.Y < 0 || local.Y > _size.Height)
        {
            return null;
        }

        for (var boundary = 1; boundary < _columnEdges.Length - 1; boundary++)
        {
            if (Math.Abs(local.X - _columnEdges[boundary]) > tolerance)
            {
                continue;
            }

            var widths = ImmutableArray.CreateBuilder<double>(_columns);

            for (var i = 0; i < _columns; i++)
            {
                widths.Add(_columnEdges[i + 1] - _columnEdges[i]);
            }

            // start - 1 is the position before the table, which is what a step names.
            return new ColumnBoundary(start - 1, boundary, widths.MoveToImmutable(), offset.X);
        }

        return null;
    }

    private void Collect(IReadOnlyCollection<int> cells, List<Rect> into, int start, Vector offset)
    {
        for (var r = 0; r < _rows.Length; r++)
        {
            var rowStart = start + _rowStarts[r];

            foreach (var cell in _rows[r].Cells)
            {
                // A cell's local first inner position is one past the position before it, which
                // is what a cell selection names.
                if (cells.Contains(rowStart + cell.Start - 1))
                {
                    into.Add(CellBounds(cell.Placement).Translate(offset));

                    // A selected cell is covered whole, so there is nothing to find inside it. A
                    // rectangle cannot select a cell and one of its nested cells at once: they
                    // belong to different grids, and Across resolves to exactly one of them.
                    continue;
                }

                foreach (var child in cell.Blocks)
                {
                    if (child.View is not TableBlockView nested)
                    {
                        continue;
                    }

                    var origin = ChildOrigin(cell, child);

                    nested.Collect(
                        cells,
                        into,
                        rowStart + child.Start,
                        offset + new Vector(origin.X, origin.Y));
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No guard against an empty or inverted range: each cell clamps the range to its own
    /// content and skips anything that comes out empty, so those cases already produce no
    /// rectangles. A guard here would be an early return that no test could distinguish. Only the
    /// rows the range reaches into are visited.
    /// </remarks>
    public override IReadOnlyList<Rect> GetSelectionRects(int from, int to)
    {
        var rects = new List<Rect>();

        for (var r = Math.Max(0, RowAt(from)); r < _rows.Length && _rowStarts[r] < to; r++)
        {
            foreach (var cell in _rows[r].Cells)
            {
                foreach (var child in cell.Blocks)
                {
                    var childStart = _rowStarts[r] + child.Start;
                    var start = Math.Max(from - childStart, 0);
                    var end = Math.Min(to - childStart, child.View.ContentSize);

                    if (end <= start)
                    {
                        continue;
                    }

                    var origin = ChildOrigin(cell, child);

                    foreach (var rect in child.View.GetSelectionRects(start, end))
                    {
                        rects.Add(rect.Translate(new Vector(origin.X, origin.Y)));
                    }
                }
            }
        }

        return rects;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Recursive rather than a lookup, so a table inside a cell answers for its own cells and
    /// the editing layer never has to know how deep it went.
    /// </remarks>
    public override TextAt? ParagraphAt(int localPosition)
    {
        if (Find(localPosition) is not { } found)
        {
            return null;
        }

        var (cell, child, rowStart) = found;

        if (child.View.ParagraphAt(localPosition - rowStart - child.Start) is not { } inner)
        {
            return null;
        }

        var origin = ChildOrigin(cell, child);

        return inner with
        {
            Start = rowStart + child.Start + inner.Start,
            Origin = new Point(origin.X + inner.Origin.X, origin.Y + inner.Origin.Y),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Recursive, for the same reason <see cref="ParagraphAt"/> is.</remarks>
    public override Rect? GetImageRect(int localPosition)
    {
        if (Find(localPosition) is not { } found)
        {
            return null;
        }

        var (cell, child, rowStart) = found;

        if (child.View.GetImageRect(localPosition - rowStart - child.Start) is not { } inner)
        {
            return null;
        }

        var origin = ChildOrigin(cell, child);

        return inner.Translate(new Vector(origin.X, origin.Y));
    }

    /// <summary>The row whose range holds a local position, or -1 before the first.</summary>
    private int RowAt(int localPosition)
    {
        var low = -1;
        var high = _rows.Length - 1;

        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);

            if (_rowStarts[middle] <= localPosition)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>The block a local position is inside, the cell holding it, and its row's start.</summary>
    /// <remarks>
    /// A row's positions all lie inside the row, so the row is found by a binary search over the
    /// row starts and only its own cells are looked at.
    /// </remarks>
    private (Cell Cell, Child Child, int RowStart)? Find(int localPosition)
    {
        var r = RowAt(localPosition);

        if (r < 0)
        {
            return null;
        }

        var rowStart = _rowStarts[r];

        foreach (var cell in _rows[r].Cells)
        {
            foreach (var child in cell.Blocks)
            {
                if (localPosition >= rowStart + child.Start
                    && localPosition <= rowStart + child.Start + child.View.ContentSize)
                {
                    return (cell, child, rowStart);
                }
            }
        }

        return null;
    }

    private Point ChildOrigin(Cell cell, Child child)
    {
        var bounds = CellBounds(cell.Placement);

        return new Point(bounds.X + CellPadding, bounds.Y + CellPadding + child.Top);
    }

    private (Cell Cell, int RowStart)? NearestCell(Point local)
    {
        (Cell Cell, int RowStart)? nearest = null;
        var best = double.PositiveInfinity;

        for (var r = 0; r < _rows.Length; r++)
        {
            foreach (var cell in _rows[r].Cells)
            {
                var bounds = CellBounds(cell.Placement);

                if (bounds.Contains(local))
                {
                    return (cell, _rowStarts[r]);
                }

                // Squared distance to the cell's box, which is zero along any axis the point
                // already lies within. A click in the gap between cells, or outside the table
                // entirely, therefore lands in the cell it is nearest rather than nowhere.
                var dx = Math.Max(0, Math.Max(bounds.X - local.X, local.X - bounds.Right));
                var dy = Math.Max(0, Math.Max(bounds.Y - local.Y, local.Y - bounds.Bottom));
                var distance = (dx * dx) + (dy * dy);

                if (distance < best)
                {
                    best = distance;
                    nearest = (cell, _rowStarts[r]);
                }
            }
        }

        return nearest;
    }

    private static Child? NearestChild(Cell cell, double y)
    {
        Child? last = null;

        foreach (var child in cell.Blocks)
        {
            if (y < child.Top + child.View.Size.Height)
            {
                return child;
            }

            last = child;
        }

        return last;
    }

    private double CellWidth(CellPlacement placement) =>
        _columnEdges[Math.Min(placement.Right, _columns)] - _columnEdges[placement.Left];

    private Rect CellBounds(CellPlacement placement)
    {
        var left = _columnEdges[placement.Left];
        var top = _rowEdges[placement.Top];

        return new Rect(
            left,
            top,
            CellWidth(placement),
            _rowEdges[Math.Min(placement.Bottom, _table.Rows.Length)] - top);
    }

    private double[] ColumnEdges(double available)
    {
        var widths = new double[_columns];

        if (_table.ColumnWidths.Length == 0)
        {
            var each = _columns == 0 ? 0 : available / _columns;

            Array.Fill(widths, each);
        }
        else
        {
            // Stated widths are a proportion, not a promise: a table that states more than the
            // available width is scaled down to fit rather than drawn off the edge, and columns
            // the table said nothing about fall back to an even share of what is stated.
            var fallback = _table.ColumnWidths.Length == 0
                ? 0
                : _table.ColumnWidths.Average();

            for (var i = 0; i < _columns; i++)
            {
                widths[i] = i < _table.ColumnWidths.Length ? _table.ColumnWidths[i] : fallback;
            }

            var total = widths.Sum();

            if (total > 0 && available > 0)
            {
                var scale = available / total;

                for (var i = 0; i < widths.Length; i++)
                {
                    widths[i] *= scale;
                }
            }
        }

        return Edges(widths);
    }

    private static double[] Edges(double[] sizes)
    {
        var edges = new double[sizes.Length + 1];

        for (var i = 0; i < sizes.Length; i++)
        {
            edges[i + 1] = edges[i] + sizes[i];
        }

        return edges;
    }

    /// <summary>One row: its node, so an update can tell whether it was replaced, and its cells.</summary>
    private sealed class Row(TableRowNode node, Cell[] cells)
    {
        public TableRowNode Node { get; set; } = node;

        /// <summary>The cells the row's node holds, in order, which includes any spanning down.</summary>
        public Cell[] Cells { get; set; } = cells;
    }

    /// <summary>One cell, its place in the grid, and the views of the blocks inside it.</summary>
    private sealed class Cell(
        TableCellNode node,
        CellPlacement placement,
        int start,
        ImmutableArray<Child> blocks,
        IBrush? header)
    {
        public CellPlacement Placement { get; } = placement;

        /// <summary>The cell's own background, or null to draw nothing behind it.</summary>
        /// <remarks>
        /// Built once: a brush per cell per frame is a lot of garbage for a fill. A background the
        /// document states wins over the header tint, because the document was explicit and the
        /// tint is only the control saying what a header row usually looks like.
        /// </remarks>
        public IBrush? Fill { get; } = node.Background is { } colour
            ? new SolidColorBrush(colour.ToAvalonia())
            : node.IsHeader ? header : null;

        /// <summary>
        /// The cell's own first inner position, relative to its row's opening boundary, for a
        /// cell with nothing in it.
        /// </summary>
        public int Start { get; } = start;

        /// <summary>The blocks inside the cell, with where each is drawn.</summary>
        public ImmutableArray<Child> Blocks { get; } = blocks;

        /// <summary>What the cell measured to, before its row was settled.</summary>
        public double Height { get; set; }
    }

    /// <summary>One block inside a cell, with where it starts in both senses.</summary>
    private sealed class Child(BlockNode node, BlockView view, int start)
    {
        /// <summary>The block itself, so an update can tell whether this one was edited.</summary>
        public BlockNode Node { get; } = node;

        public BlockView View { get; } = view;

        /// <summary>
        /// The local position of the block's first inner position, relative to its row's opening
        /// boundary.
        /// </summary>
        public int Start { get; } = start;

        /// <summary>How far down the cell's content the block is drawn.</summary>
        public double Top { get; set; }
    }
}
