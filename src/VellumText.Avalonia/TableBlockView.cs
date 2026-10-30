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
/// <b>Rows are realized as they are needed</b> (F-35). A row has no views until something needs
/// them — the window the presenter is about to show, or a geometry question about a position in
/// the row — and a row nobody has measured is priced at an estimate: the mean of the rows that
/// have been, as <see cref="BlockHeightIndex"/> does for blocks. Rows are realized and measured in
/// <em>bands</em>, the runs of rows that cells spanning rows tie together, because a spanning cell
/// settles its height against every row it covers. Row tops are prefix sums kept in an array and
/// recomputed from the first row whose height moved, in order, so a table every row of which has
/// been measured is laid out with exactly the arithmetic it always was.
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

    /// <summary>The height assumed for a row before any row has been measured.</summary>
    private const double SeedEstimate = (2 * CellPadding) + BlockHeightIndex.DefaultEstimate;

    private readonly Func<BlockNode, BlockView> _create;
    private readonly IBrush? _header;
    private readonly IPen _border;

    private TableNode _table;
    private Row[] _rows = [];
    private int[] _rowStarts = [0];
    private ImmutableArray<CellPlacement> _placements = [];
    private int[] _rowFirstCell = [0];
    private int[] _coverTop = [];
    private int[] _bandStart = [];
    private int[] _bandEnd = [];
    private int _columns;

    private double[] _columnEdges = [];
    private double _width = double.NaN;
    private double[] _rowHeights = [];
    private double[] _rowEdges = [0];
    private int _edgesFrom;
    private double _edgesEstimate = double.NaN;
    private readonly HeightEstimate _estimate = new(SeedEstimate);
    private int _version;
    private int _remapPrefix = int.MaxValue;
    private int _remapSuffix;
    private int _remapFrom;
    private int _remapTo;
    private int _windowRow;
    private double _windowFraction;
    private double _windowAbove;
    private double _windowBelow = double.PositiveInfinity;

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

        Rebuild(table);
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
    /// every cell is placed where it was. A change to the shape places every cell again, and
    /// realizes again only the rows that had been realized.
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

        _version++;

        if (TryUpdateRows(table))
        {
            // Every row is the row it was.
            _remapPrefix = int.MaxValue;
        }
        else
        {
            Rebuild(table);
        }

        return true;
    }

    /// <summary>
    /// Places every cell again and lays the table out as rows, realizing a row only where the
    /// rows as they were had realized it or a cell it takes the place of, and reusing those views
    /// wherever the edit left a block untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row whose cells the edit left alone is matched to the row it was — the rows before an
    /// inserted or deleted row and the rows after it, found from both ends by reference — and
    /// keeps its cells' views. It also keeps its measured height when nothing it was measured
    /// against has moved: its band is the same rows it was, and every cell in it spans the same
    /// columns and rows. Forgetting those heights priced every row at the mean, and with uneven
    /// rows the offset the viewport was at then named a row far from the one on screen.
    /// </para>
    /// <para>
    /// A row the edit replaced is matched by position to the replaced rows it stands among, and
    /// takes over the views of their cells; where that pairs the wrong cells it only costs a
    /// rebuild of those views.
    /// </para>
    /// </remarks>
    private void Rebuild(TableNode table)
    {
        var old = new Previous(_rows, _rowFirstCell, _bandStart, _bandEnd, _rowHeights);
        var placements = TableGeometry.Place(table, out _columns);
        var rows = new Row[table.Rows.Length];
        var firstCell = new int[rows.Length + 1];

        for (var r = 0; r < rows.Length; r++)
        {
            firstCell[r + 1] = firstCell[r] + table.Rows[r].Cells.Length;
            rows[r] = new Row(table.Rows[r]);
        }

        _rows = rows;
        _placements = placements;
        _rowFirstCell = firstCell;
        _table = table;

        Bands();
        UpdateRowStarts();

        _rowHeights = new double[rows.Length];
        _rowEdges = new double[rows.Length + 1];
        ForgetHeights();

        // The rows the edit did not touch, from each end.
        var limit = Math.Min(old.Rows.Length, rows.Length);
        var prefix = 0;

        while (prefix < limit && SameCells(old.Rows[prefix].Node, rows[prefix].Node))
        {
            prefix++;
        }

        var suffix = 0;

        while (suffix < limit - prefix && SameCells(old.Rows[^(suffix + 1)].Node, rows[^(suffix + 1)].Node))
        {
            suffix++;
        }

        _remapPrefix = prefix;
        _remapSuffix = suffix;
        _remapFrom = old.Rows.Length;
        _remapTo = rows.Length;

        int Was(int row) =>
            row < prefix ? row
            : row >= rows.Length - suffix ? row - rows.Length + old.Rows.Length
            : -1;

        for (var start = 0; start < rows.Length; start = _bandEnd[start])
        {
            if (Carries(start, old, Was))
            {
                for (var r = start; r < _bandEnd[start]; r++)
                {
                    var was = Was(r);
                    var cells = old.Rows[was].Cells!;
                    var moved = new Cell[cells.Length];

                    for (var c = 0; c < cells.Length; c++)
                    {
                        moved[c] = new Cell(rows[r].Node.Cells[c], placements[firstCell[r] + c], cells[c].Start, cells[c].Blocks, _header);
                    }

                    rows[r].Cells = moved;
                    SetRowHeight(r, old.Heights[was]);
                }

                continue;
            }

            for (var r = start; r < _bandEnd[start]; r++)
            {
                var node = rows[r].Node;
                var first = firstCell[r];
                var was = Was(r);

                // A row the edit replaced may take over the views of the cells that stood where it
                // stands, but only of the rows the edit replaced too: a kept row keeps its views,
                // and one view updated to two rows' cells would draw the wrong one in one of them.
                // The kept rows above it hold the same cells in both, so only the end is checked.
                Cell? Previous(int i) =>
                    was >= 0 ? old.Rows[was].Cells?[i]
                    : first + i < old.FirstCell[old.Rows.Length - suffix] ? CellAt(old.Rows, old.FirstCell, first + i)
                    : null;

                for (var i = 0; i < node.Cells.Length; i++)
                {
                    if (Previous(i) is not null)
                    {
                        rows[r].Cells = new Cell[node.Cells.Length];
                        BuildCells(node, rows[r].Cells!, i => placements[first + i], Previous);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether the band starting at <paramref name="start"/> is a band of the old layout, row for
    /// row, measured and unedited.
    /// </summary>
    /// <remarks>
    /// The placements need no comparing: a cell's spans are on the cell, the rows hold the very
    /// same cells, and a band holds every row whose cells push its cells sideways or down, so the
    /// same rows forming the same band place their cells in the same slots.
    /// </remarks>
    private bool Carries(int start, Previous old, Func<int, int> was)
    {
        var end = _bandEnd[start];
        var from = was(start);

        if (from < 0 || old.BandStart[from] != from || old.BandEnd[from] != from + (end - start))
        {
            return false;
        }

        for (var r = start; r < end; r++)
        {
            var row = old.Rows[from + r - start];

            if (was(r) != from + r - start
                || row.Cells is null
                || row.Dirty
                || double.IsNaN(old.Heights[from + r - start]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two rows hold the very same cells.</summary>
    /// <remarks>
    /// By the cells rather than the row: the row commands build every row of the table afresh around
    /// the cells they did not touch, so a row node is new even where nothing in it is.
    /// </remarks>
    private static bool SameCells(TableRowNode a, TableRowNode b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Cells.Length != b.Cells.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Cells.Length; i++)
        {
            if (!ReferenceEquals(a.Cells[i], b.Cells[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What a rebuild needs to know of the rows as they were.</summary>
    private readonly record struct Previous(
        Row[] Rows,
        int[] FirstCell,
        int[] BandStart,
        int[] BandEnd,
        double[] Heights);

    /// <summary>The cell with a row-major index among some rows, or null if it was never realized.</summary>
    private static Cell? CellAt(Row[] rows, int[] firstCell, int index)
    {
        if (rows.Length == 0 || index >= firstCell[^1])
        {
            return null;
        }

        var low = 0;
        var high = rows.Length - 1;

        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);

            if (firstCell[middle] <= index)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return rows[low].Cells?[index - firstCell[low]];
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
            var first = _rowFirstCell[r];

            row.Node = after[r];

            // A row nobody has realized has no views to bring up to date; it is built from its
            // node when it is first needed.
            if (row.Cells is { } old)
            {
                var cells = new Cell[old.Length];

                // The same spans in the same rows place every cell where it was.
                BuildCells(after[r], cells, i => _placements[first + i], i => old[i]);

                row.Cells = cells;
                row.Dirty = true;
            }
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

    /// <summary>Gives a row the views it did not have, if it had none.</summary>
    private Cell[] Realize(int row)
    {
        var target = _rows[row];

        if (target.Cells is { } cells)
        {
            return cells;
        }

        var first = _rowFirstCell[row];

        cells = new Cell[target.Node.Cells.Length];
        BuildCells(target.Node, cells, i => _placements[first + i], _ => null);

        target.Cells = cells;
        target.Dirty = true;

        return cells;
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

    /// <summary>
    /// Works out, from the placements, which rows the spanning cells tie together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>coverTop</c> is, for each row, the first row of any cell covering it: what lets a band of
    /// rows be drawn without looking at every row above it, since a cell that reaches into the band
    /// from above starts no higher. <c>bandStart</c> and <c>bandEnd</c> bound the run of rows that
    /// cells spanning rows join, which is the unit rows are measured in.
    /// </para>
    /// <para>
    /// Built only when the grid's shape changes; the cost is the slots the cells occupy, as placing
    /// them is.
    /// </para>
    /// </remarks>
    private void Bands()
    {
        var count = _rows.Length;
        var coverTop = new int[count];
        var bandStart = new int[count];
        var bandEnd = new int[count];
        var reach = 0;
        var start = 0;

        for (var r = 0; r < count; r++)
        {
            coverTop[r] = r;
        }

        for (var r = 0; r < count; r++)
        {
            // Nothing from a row above reaches this one, so a new band begins here.
            if (r >= reach)
            {
                start = r;
            }

            for (var i = _rowFirstCell[r]; i < _rowFirstCell[r + 1]; i++)
            {
                var placement = _placements[i];
                var bottom = Math.Min(placement.Bottom, count);

                reach = Math.Max(reach, bottom);

                for (var covered = placement.Top + 1; covered < bottom; covered++)
                {
                    coverTop[covered] = Math.Min(coverTop[covered], placement.Top);
                }
            }

            reach = Math.Max(reach, r + 1);
            bandStart[r] = start;
        }

        for (var r = count - 1; r >= 0; r--)
        {
            bandEnd[r] = r == count - 1 || bandStart[r + 1] != bandStart[r] ? r + 1 : bandEnd[r + 1];
        }

        _coverTop = coverTop;
        _bandStart = bandStart;
        _bandEnd = bandEnd;
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
    /// <remarks>
    /// Rows nobody has measured count at the estimate, so the height moves as the table is
    /// explored, as the document's own does.
    /// </remarks>
    public override Size Size => double.IsNaN(_width) ? default : new Size(_width, RowTop(_rows.Length));

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

    /// <summary>The paragraph views inside the cells of the rows realized so far, in document order.</summary>
    internal IEnumerable<ParagraphView> ParagraphViews =>
        _rows.SelectMany(row => row.Cells ?? []).SelectMany(cell => cell.Blocks)
            .Select(child => child.View).OfType<ParagraphView>();

    /// <summary>How many rows have views, for tests and diagnostics.</summary>
    internal int RealizedRows => _rows.Count(row => row.Cells is not null);

    /// <summary>Whether a row's height is a measurement of its cells as they are, for tests.</summary>
    internal bool IsRowMeasured(int row) => !double.IsNaN(_rowHeights[row]) && !_rows[row].Dirty;

    /// <summary>How many rows the table has, for tests.</summary>
    internal int RowCount => _rows.Length;

    /// <summary>The height assumed for a row nobody has measured at the current width.</summary>
    /// <remarks>Bounded against one giant row; see <see cref="HeightEstimate"/>.</remarks>
    private double Estimate => _estimate.Value;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The window is what <see cref="Measure"/> realizes and measures; rows outside it keep
    /// whatever they had, and rows never inside one are priced at the estimate.
    /// </para>
    /// <para>
    /// Kept as the row the anchor falls in, how far into that row, and the two distances — not as
    /// positions. Rows are measured outwards from the anchor row until their real heights cover
    /// the distances, so the rows the presenter keeps on screen are measured however far the
    /// estimate that placed the anchor was from the truth: rows taller than estimated, measured
    /// from a window top the estimate chose, used to run out of reach before the viewport began.
    /// Measuring any row also moves the estimate and so every unmeasured row, which is the other
    /// reason nothing here is remembered in pixels.
    /// </para>
    /// <para>
    /// Only the presenter calls this, and only for its own blocks. A table inside a cell is never
    /// given a window, so it keeps the infinite one it starts with and realizes and measures every
    /// one of its rows whenever the outer table measures the row holding it: a 10,000 × 3 table in a
    /// cell allocated 110 MB laying out the top of the document, against 5.5 MB for the same table
    /// at the top level. Passing a window down is not a local change. The outer table measures a
    /// band only when it is stale, so the nested table would have to be measured again on every
    /// pass and its height, which moves with its own estimate, pushed into the outer row; drawing
    /// skips rows outside the window, so a nested window that fell behind would draw blank rows;
    /// and the presenter keeps the viewport still by anchoring on a top-level row, which a nested
    /// estimate moving would slide out from under it.
    /// </para>
    /// </remarks>
    public override void SetWindow(double anchor, double above, double below)
    {
        _windowAbove = Math.Max(0, above);
        _windowBelow = below;

        if (_rows.Length == 0)
        {
            _windowRow = 0;
            _windowFraction = 0;

            return;
        }

        (_windowRow, _windowFraction) = RowAnchor(anchor);
    }

    /// <inheritdoc/>
    public override Size Measure(double availableWidth)
    {
        var width = double.IsFinite(availableWidth) && availableWidth > 0
            ? availableWidth
            : 0;

        var edges = ColumnEdges(width);

        // A width or a column resize changes every cell's width, so no row is the height it was.
        if (!width.Equals(_width) || !edges.AsSpan().SequenceEqual(_columnEdges))
        {
            _width = width;
            _columnEdges = edges;
            ForgetHeights();
        }

        if (_rows.Length > 0)
        {
            var anchor = Math.Min(_windowRow, _rows.Length - 1);
            var start = _bandStart[anchor];

            // Down from the anchor, until the rows measured reach the window's bottom from the
            // anchor point. Added up from real heights rather than asked of row positions, which
            // are priced at an estimate that every band measured moves.
            var below = 0.0;

            for (var row = start; row < _rows.Length; row = _bandEnd[row])
            {
                EnsureBand(row);

                for (var r = Math.Max(row, anchor); r < _bandEnd[row]; r++)
                {
                    below += HeightOf(r);
                }

                if (below >= (_windowFraction * HeightOf(anchor)) + _windowBelow)
                {
                    break;
                }
            }

            // And up from it, for the overscan above the viewport.
            var above = _windowFraction * HeightOf(anchor);

            for (var r = start; r < anchor; r++)
            {
                above += HeightOf(r);
            }

            for (var row = start - 1; row >= 0 && above < _windowAbove; row = _bandStart[row] - 1)
            {
                EnsureBand(row);

                for (var r = _bandStart[row]; r <= row; r++)
                {
                    above += HeightOf(r);
                }
            }
        }

        return Size;
    }

    /// <summary>A row's measured height, or the estimate when it has none.</summary>
    private double HeightOf(int row) => double.IsNaN(_rowHeights[row]) ? Estimate : _rowHeights[row];
    /// <summary>Marks every row unmeasured, keeping the arrays.</summary>
    private void ForgetHeights()
    {
        Array.Fill(_rowHeights, double.NaN);
        _estimate.Restart();
        _edgesFrom = 0;
    }

    /// <summary>
    /// Realizes the band of rows holding <paramref name="row"/>, and measures it if any row of it
    /// is unmeasured or was edited since.
    /// </summary>
    /// <remarks>
    /// Measured as the whole table used to be, restricted to the band: single-row cells settle
    /// their rows first, then each spanning cell that still does not fit pushes the last row it
    /// covers. A band is closed under spanning, so the answer is the one the whole table gave.
    /// </remarks>
    private void EnsureBand(int row)
    {
        var start = _bandStart[row];
        var end = _bandEnd[row];
        var stale = false;

        for (var r = start; r < end; r++)
        {
            Realize(r);
            stale |= _rows[r].Dirty || double.IsNaN(_rowHeights[r]);
        }

        // Without a width there is nothing to wrap to; the views exist, and are measured with the
        // rest of the band once there is one.
        if (!stale || double.IsNaN(_width))
        {
            return;
        }

        var heights = new double[end - start];

        for (var r = start; r < end; r++)
        {
            foreach (var cell in _rows[r].Cells!)
            {
                WorkCounters.TableCellsMeasured++;

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
                    heights[r - start] = Math.Max(heights[r - start], height);
                }
            }
        }

        // Now the spanning cells, against the row heights the single-row cells settled. Only a
        // cell that still does not fit pushes, and it pushes the last row it covers.
        for (var r = start; r < end; r++)
        {
            foreach (var cell in _rows[r].Cells!)
            {
                if (cell.Placement.Bottom - cell.Placement.Top == 1)
                {
                    continue;
                }

                var bottom = Math.Min(cell.Placement.Bottom, _rows.Length);
                var covered = 0.0;

                for (var k = cell.Placement.Top; k < bottom; k++)
                {
                    covered += heights[k - start];
                }

                if (bottom > cell.Placement.Top && covered < cell.Height)
                {
                    heights[bottom - 1 - start] += cell.Height - covered;
                }
            }
        }

        for (var r = start; r < end; r++)
        {
            SetRowHeight(r, heights[r - start]);
            _rows[r].Dirty = false;
        }
    }

    private void SetRowHeight(int row, double height)
    {
        var was = _rowHeights[row];

        if (!double.IsNaN(was))
        {
            _estimate.Remove(was);
        }

        _estimate.Add(height);
        _rowHeights[row] = height;

        if (!was.Equals(height))
        {
            _edgesFrom = Math.Min(_edgesFrom, row);
        }
    }

    /// <summary>How many times <see cref="Update"/> has changed the table, for <see cref="RowAfterUpdate"/>.</summary>
    internal int Version => _version;

    /// <summary>
    /// The index a row had at <paramref name="version"/> has now, or -1 if that cannot be said.
    /// </summary>
    /// <remarks>
    /// For the presenter, which takes the row at the top of the viewport before an edit is laid
    /// out and keeps that row there after it. Answered across one update: rows before and after
    /// the ones it replaced are where they were, moved by however many rows it added or removed,
    /// and a replaced row is taken to be the first row that changed.
    /// </remarks>
    internal int RowAfterUpdate(int row, int version)
    {
        if (version == _version)
        {
            return row;
        }

        if (version != _version - 1)
        {
            return -1;
        }

        if (row < _remapPrefix)
        {
            return row;
        }

        if (row >= _remapFrom - _remapSuffix)
        {
            return row - _remapFrom + _remapTo;
        }

        return Math.Min(_remapPrefix, _remapTo - 1);
    }

    /// <summary>The distance from the top of the table to the top of a row; the row count gives the height.</summary>
    private double RowTop(int row)
    {
        var estimate = Estimate;

        // Every unmeasured row is priced at the estimate, so a new estimate moves every row
        // below the first of them.
        if (!estimate.Equals(_edgesEstimate))
        {
            _edgesEstimate = estimate;
            _edgesFrom = 0;
        }

        // Recomputed in order from the first row whose height moved, so the sums are the ones a
        // single pass over the heights gives.
        for (var r = _edgesFrom; r < _rows.Length; r++)
        {
            var height = _rowHeights[r];

            _rowEdges[r + 1] = _rowEdges[r] + (double.IsNaN(height) ? estimate : height);
        }

        _edgesFrom = int.MaxValue;

        return _rowEdges[row];
    }

    /// <summary>The row covering a vertical position, clamped to the table.</summary>
    private int RowAtY(double y)
    {
        RowTop(0);

        var low = 0;
        var high = _rows.Length - 1;

        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);

            if (_rowEdges[middle] <= y)
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

    /// <summary>The row at a vertical position, and how far into it, as a share of its height.</summary>
    /// <remarks>
    /// For the presenter, which keeps the row at the top of the viewport where it was while rows
    /// above it are measured and their estimates replaced. A share rather than a distance because
    /// the row itself may be measured too: a distance into a row guessed taller than it turns out
    /// to be would land in the row after it.
    /// </remarks>
    internal (int Row, double Fraction) RowAnchor(double y)
    {
        if (_rows.Length == 0)
        {
            return (0, 0);
        }

        var row = RowAtY(y);
        var top = RowTop(row);
        var height = RowTop(row + 1) - top;

        return (row, height > 0 ? Math.Clamp((y - top) / height, 0, 1) : 0);
    }

    /// <summary>The vertical position a <see cref="RowAnchor"/> names now.</summary>
    internal double RowOffset(int row, double fraction)
    {
        if (_rows.Length == 0)
        {
            return 0;
        }

        row = Math.Clamp(row, 0, _rows.Length - 1);

        var top = RowTop(row);

        return top + (fraction * (RowTop(row + 1) - top));
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

        if (_rows.Length == 0)
        {
            return;
        }

        var (first, last) = visible is { } clip ? RowsMeeting(clip) : (0, _rows.Length - 1);

        if (first > last)
        {
            return;
        }

        // Inflated by the border, which is drawn centred on a cell's edge and so reaches half its
        // width outside the cell.
        var reach = visible?.Inflate(BorderThickness);
        var current = false;

        for (var r = _coverTop[first]; r <= last; r++)
        {
            // Drawing lays nothing out. A row is drawn only once its band is realized and measured
            // at this width, which the presenter's window sees to for every row on screen; laying
            // one out here would move the estimate, and with it every unmeasured row and the
            // table's height, in the middle of a frame and behind the presenter's back.
            //
            // Checked once a band, as the walk enters it: a cell spanning every row makes the
            // table one band, and checking the band again for each of its rows made drawing the
            // bottom of such a table quadratic in its rows.
            if (r == _coverTop[first] || r == _bandStart[r])
            {
                current = IsCurrent(r);
            }

            if (_rows[r].Cells is not { } cells || !current)
            {
                continue;
            }

            foreach (var cell in cells)
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

    /// <summary>Whether a row's band is realized and measured at the current width.</summary>
    private bool IsCurrent(int row)
    {
        for (var r = _bandStart[row]; r < _bandEnd[row]; r++)
        {
            WorkCounters.TableRowsChecked++;

            if (_rows[r].Cells is null || _rows[r].Dirty || double.IsNaN(_rowHeights[r]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The first and last rows whose band meets a rectangle in local coordinates.</summary>
    /// <remarks>The first is past the last when no row does.</remarks>
    private (int First, int Last) RowsMeeting(Rect area) =>
        RowsMeeting(area.Top - BorderThickness, area.Bottom + BorderThickness);

    private (int First, int Last) RowsMeeting(double top, double bottom)
    {
        RowTop(0);

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

    /// <summary>
    /// The first and last rows whose closed band <c>[top, bottom]</c> touches a vertical range.
    /// </summary>
    /// <remarks>
    /// Closed, unlike <see cref="RowsMeeting(Rect)"/>: a point on the edge between two rows is in
    /// the boxes of the cells on both sides, and a cell at exactly the best distance is still a
    /// candidate, so both must be looked at to give the answer a scan of every cell gives.
    /// </remarks>
    private (int First, int Last) RowsTouching(double top, double bottom)
    {
        RowTop(0);

        var low = 0;
        var high = _rows.Length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (_rowEdges[middle + 1] >= top)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        var first = low;

        low = -1;
        high = _rows.Length - 1;

        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);

            if (_rowEdges[middle] <= bottom)
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

        EnsureBand(nearest.Row);

        var cell = _rows[nearest.Row].Cells![nearest.Index];
        var rowStart = _rowStarts[nearest.Row];
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
            return new Rect(0, 0, 0, Size.Height);
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
    /// Distinct from <see cref="GetSelectionRects(int, int)"/>, which highlights the text a range
    /// covers. A rectangle selects cells rather than text, so an empty cell must still be filled and
    /// a half-empty one filled to its edges — anything else draws a ragged rectangle that does not
    /// look like a selection at all.
    /// </remarks>
    public IReadOnlyList<Rect> GetCellRects(IReadOnlyCollection<int> cells, int start)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var rects = new List<Rect>();

        Collect(cells, rects, start, default, null);

        return rects;
    }

    /// <summary>
    /// The boxes of the given cells that meet <paramref name="visible"/>, for painting.
    /// </summary>
    /// <param name="cells">The positions directly before each selected cell.</param>
    /// <param name="start">This table's own first inner position, in document coordinates.</param>
    /// <param name="visible">What of the table can be seen, in its own coordinates.</param>
    /// <remarks>
    /// Only the rows meeting <paramref name="visible"/> are looked at, so selecting every cell of
    /// a long table does not realize every row to paint the screenful that shows.
    /// </remarks>
    public IReadOnlyList<Rect> GetCellRects(IReadOnlyCollection<int> cells, int start, Rect visible)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var rects = new List<Rect>();

        Collect(cells, rects, start, default, visible);

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
    /// table's own boundaries cannot be inside a cell to be confused with it. Only the cells of
    /// the rows at the point's height can hold a nested table under it, so only they are asked.
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
        var local = point - offset;

        if (_rows.Length > 0 && !double.IsNaN(_width))
        {
            var (first, last) = RowsTouching(local.Y, local.Y);

            for (var r = first <= last ? _coverTop[first] : last + 1; r <= last; r++)
            {
                EnsureBand(r);

                foreach (var cell in _rows[r].Cells!)
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
        }

        // Only the vertical extent needs checking. The loop below already yields nothing for a
        // table of one column or none, so a guard on the edge count would be an early return that
        // no test could tell from its absence.
        if (local.Y < 0 || local.Y > Size.Height)
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

    private void Collect(IReadOnlyCollection<int> cells, List<Rect> into, int start, Vector offset, Rect? visible)
    {
        if (_rows.Length == 0)
        {
            return;
        }

        var (first, last) = visible is { } area ? RowsMeeting(area) : (0, _rows.Length - 1);

        for (var r = first <= last ? _coverTop[first] : last + 1; r <= last; r++)
        {
            EnsureBand(r);

            var rowStart = start + _rowStarts[r];

            foreach (var cell in _rows[r].Cells!)
            {
                var bounds = CellBounds(cell.Placement);

                if (visible is { } shown && !bounds.Intersects(shown.Inflate(BorderThickness)))
                {
                    continue;
                }

                // A cell's local first inner position is one past the position before it, which
                // is what a cell selection names.
                if (cells.Contains(rowStart + cell.Start - 1))
                {
                    into.Add(bounds.Translate(offset));

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
                        offset + new Vector(origin.X, origin.Y),
                        visible?.Translate(new Vector(-origin.X, -origin.Y)));
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No guard against an empty or inverted range: each cell clamps the range to its own
    /// content and skips anything that comes out empty, so those cases already produce no
    /// rectangles. A guard here would be an early return that no test could distinguish. Only the
    /// rows the range reaches into are visited, and they are realized to answer.
    /// </remarks>
    public override IReadOnlyList<Rect> GetSelectionRects(int from, int to) =>
        SelectionRects(from, to, null);

    /// <inheritdoc/>
    /// <remarks>
    /// Restricted to the rows meeting <paramref name="visible"/> as well as the range, so that a
    /// selection running the length of a long table paints its screenful without realizing every
    /// row it covers.
    /// </remarks>
    public override IReadOnlyList<Rect> GetSelectionRects(int from, int to, Rect visible) =>
        SelectionRects(from, to, visible);

    private List<Rect> SelectionRects(int from, int to, Rect? visible)
    {
        var rects = new List<Rect>();

        if (_rows.Length == 0)
        {
            return rects;
        }

        var first = Math.Max(0, RowAt(from));
        var last = _rows.Length - 1;

        if (visible is { } area)
        {
            var (top, bottom) = RowsMeeting(area);

            first = top <= bottom ? Math.Max(first, _coverTop[top]) : _rows.Length;
            last = bottom;
        }

        for (var r = first; r <= last && _rowStarts[r] < to; r++)
        {
            EnsureBand(r);

            foreach (var cell in _rows[r].Cells!)
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
    /// <remarks>
    /// The row is marked edited, so the next measurement of its band lays the paragraph out again
    /// and settles the row's height — composed text can wrap the cell onto another line — and
    /// drawing does not reach the row before then. Passed down, for a table inside the cell.
    /// </remarks>
    public override bool ParagraphChanged(ParagraphView paragraph, int localPosition)
    {
        // The row at the position almost always holds it; every row is searched only when the
        // document has moved the paragraph since.
        var hinted = RowAt(localPosition);

        if (hinted >= 0 && Holds(hinted))
        {
            return true;
        }

        for (var r = 0; r < _rows.Length; r++)
        {
            if (r != hinted && Holds(r))
            {
                return true;
            }
        }

        return false;

        bool Holds(int r)
        {
            // A row nobody realized has no paragraph views to have changed.
            if (_rows[r].Cells is not { } cells)
            {
                return false;
            }

            var rowStart = _rowStarts[r];

            foreach (var cell in cells)
            {
                foreach (var child in cell.Blocks)
                {
                    if (ReferenceEquals(child.View, paragraph)
                        || child.View.ParagraphChanged(paragraph, localPosition - rowStart - child.Start))
                    {
                        _rows[r].Dirty = true;

                        return true;
                    }
                }
            }

            return false;
        }
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
    /// row starts and only its own cells are looked at — realized, and measured, if they were not.
    /// </remarks>
    private (Cell Cell, Child Child, int RowStart)? Find(int localPosition)
    {
        var r = RowAt(localPosition);

        if (r < 0)
        {
            return null;
        }

        EnsureBand(r);

        var rowStart = _rowStarts[r];

        foreach (var cell in _rows[r].Cells!)
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

    /// <summary>The row and index within it of the cell nearest a point, or null for no cells.</summary>
    /// <remarks>
    /// <para>
    /// The answer is the one a scan of every cell in row-major order gives: the first cell at the
    /// least distance, which is the first whose box holds the point when any does. Only placements
    /// and row edges are needed for that, not views, and only rows that could hold the answer are
    /// looked at: the rows the point's height touches, and then the rows within the best distance
    /// found among them, each widened upwards to the first row of any cell reaching into it.
    /// </para>
    /// <para>
    /// A click in the gap between cells, or outside the table entirely, therefore lands in the cell
    /// it is nearest rather than nowhere.
    /// </para>
    /// </remarks>
    private (int Row, int Index)? NearestCell(Point local)
    {
        if (_rows.Length == 0 || _placements.IsEmpty)
        {
            return null;
        }

        var (first, last) = RowsTouching(local.Y, local.Y);

        if (first > last)
        {
            // Above or below every row: the nearest row is the one at that edge.
            first = last = Math.Clamp(first, 0, _rows.Length - 1);
        }

        // A cell holding the point is at distance zero, and the first of those in row-major order
        // is the first whose box holds it, so one search answers both questions.
        var best = Nearest(local, _coverTop[first], last, double.PositiveInfinity, out _);
        var reach = Math.Sqrt(best);

        (first, last) = double.IsFinite(reach)
            ? RowsTouching(local.Y - reach, local.Y + reach)
            : (0, _rows.Length - 1);

        if (first > last)
        {
            first = last = Math.Clamp(first, 0, _rows.Length - 1);
        }

        Nearest(local, _coverTop[first], last, double.PositiveInfinity, out var found);

        return found;
    }

    /// <summary>The least squared distance from a point to the cells of some rows, the first at it.</summary>
    private double Nearest(Point local, int fromRow, int toRow, double best, out (int Row, int Index)? found)
    {
        found = null;

        for (var r = fromRow; r <= toRow; r++)
        {
            for (var i = _rowFirstCell[r]; i < _rowFirstCell[r + 1]; i++)
            {
                var bounds = CellBounds(_placements[i]);

                // Squared distance to the cell's box, which is zero along any axis the point
                // already lies within.
                var dx = Math.Max(0, Math.Max(bounds.X - local.X, local.X - bounds.Right));
                var dy = Math.Max(0, Math.Max(bounds.Y - local.Y, local.Y - bounds.Bottom));
                var distance = (dx * dx) + (dy * dy);

                if (distance < best)
                {
                    best = distance;
                    found = (r, i - _rowFirstCell[r]);
                }
            }
        }

        return best;
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
        var top = RowTop(placement.Top);

        return new Rect(
            left,
            top,
            CellWidth(placement),
            RowTop(Math.Min(placement.Bottom, _rows.Length)) - top);
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

    /// <summary>One row: its node, and its cells once it has been realized.</summary>
    private sealed class Row(TableRowNode node)
    {
        /// <summary>The row's node, so an update can tell whether it was replaced.</summary>
        public TableRowNode Node { get; set; } = node;

        /// <summary>
        /// The cells the row's node holds, in order — which includes any spanning down — or null
        /// until something has needed them.
        /// </summary>
        public Cell[]? Cells { get; set; }

        /// <summary>Whether the cells changed since the row was last measured.</summary>
        public bool Dirty { get; set; }
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
