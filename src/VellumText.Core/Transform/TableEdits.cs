using System.Collections.Immutable;

namespace VellumText;

/// <summary>
/// The structural edits a table supports: rows, columns, merging and splitting.
/// </summary>
/// <remarks>
/// <para>
/// Each of these works out the whole replacement table and hands back one
/// <see cref="TableStructureStep"/>, or null when the edit does not apply. Null is the answer for
/// "the caret is not in a table", "that would delete the last row" and "there is nothing there to
/// split" alike, so a command can offer them all and let each one decline.
/// </para>
/// <para>
/// Everything here reasons about cells as rectangles on a grid and derives the spans from the
/// rectangles at the end, rather than adjusting <see cref="TableCellNode.RowSpan"/> and
/// <see cref="TableCellNode.ColumnSpan"/> as it goes. Spans and geometry can disagree; two
/// rectangles either overlap or they do not. The invariant every one of these must preserve is
/// the one <see cref="DocumentSchema"/> checks — that the cells tile the grid exactly, with no
/// hole a renderer would have to invent a cell for.
/// </para>
/// <para>
/// Where a rectangle of cells is selected, the row and column operations act on as many rows or
/// columns as it covers, which is what a word processor does and what makes "select three rows,
/// insert" mean three rows.
/// </para>
/// </remarks>
public static class TableEdits
{
    /// <summary>Adds rows above or below the selected ones.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="anchorCell">The position before one corner cell of the selection.</param>
    /// <param name="headCell">The position before the other, or the same cell for a caret.</param>
    /// <param name="below">Whether the new rows go below the selection rather than above it.</param>
    /// <returns>The step, or null if there is no table there.</returns>
    /// <remarks>
    /// A cell that spans across the line the rows go in is stretched rather than cut in two,
    /// which is what a reader means by inserting a row through a merged cell. The new cells are
    /// one column wide each and take their header flag and background from the row they were
    /// added beside, so inserting a row into a header keeps it looking like a header.
    /// </remarks>
    public static TableStructureStep? InsertRows(
        DocumentNode doc, int anchorCell, int headCell, bool below) =>
        InsertRows(doc, anchorCell, headCell, below, known: null);

    /// <inheritdoc cref="InsertRows(DocumentNode, int, int, bool)"/>
    internal static TableStructureStep? InsertRows(
        DocumentNode doc, int anchorCell, int headCell, bool below, TableGrid? known)
    {
        if (Region(doc, anchorCell, headCell, known) is not { } region)
        {
            return null;
        }

        var (table, tablePos, layout, rectangle) = region;
        var count = rectangle.Bottom - rectangle.Top;
        var line = below ? rectangle.Bottom : rectangle.Top;
        var reference = below ? rectangle.Bottom - 1 : rectangle.Top;
        var cells = new List<Placed>(layout.Cells.Count + (count * layout.Width));

        foreach (var placed in layout.Cells)
        {
            cells.Add(placed switch
            {
                // Straddles the line: the merge grows to swallow the new rows.
                _ when placed.Top < line && placed.Bottom > line =>
                    placed with { Bottom = placed.Bottom + count },
                _ when placed.Top >= line =>
                    placed with { Top = placed.Top + count, Bottom = placed.Bottom + count },
                _ => placed,
            });
        }

        for (var column = 0; column < layout.Width; column++)
        {
            if (layout.At(Math.Min(line, layout.Height - 1), column) is { } straddler
                && straddler.Top < line && straddler.Bottom > line)
            {
                continue;
            }

            var model = layout.At(reference, column);

            for (var offset = 0; offset < count; offset++)
            {
                cells.Add(Blank(model, line + offset, column));
            }
        }

        return Step(
            tablePos, table, cells, layout.Height + count, table.ColumnWidths);
    }

    /// <summary>Removes the selected rows.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="anchorCell">The position before one corner cell of the selection.</param>
    /// <param name="headCell">The position before the other, or the same cell for a caret.</param>
    /// <returns>The step, or null if there is no table there or nothing would be left of it.</returns>
    /// <remarks>
    /// A cell that begins inside the removed rows and continues past them keeps its content: it
    /// moves down to the first surviving row, the way a word processor keeps the text of a merged
    /// cell when the row it started in goes. Removing every row is refused rather than producing
    /// an empty table, because deleting a table is a different thing from editing one.
    /// </remarks>
    public static TableStructureStep? DeleteRows(
        DocumentNode doc, int anchorCell, int headCell) =>
        DeleteRows(doc, anchorCell, headCell, known: null);

    /// <inheritdoc cref="DeleteRows(DocumentNode, int, int)"/>
    internal static TableStructureStep? DeleteRows(
        DocumentNode doc, int anchorCell, int headCell, TableGrid? known)
    {
        if (Region(doc, anchorCell, headCell, known) is not { } region)
        {
            return null;
        }

        var (table, tablePos, layout, rectangle) = region;
        var count = rectangle.Bottom - rectangle.Top;

        if (count >= layout.Height)
        {
            return null;
        }

        var cells = new List<Placed>(layout.Cells.Count);

        foreach (var placed in layout.Cells)
        {
            var top = Shift(placed.Top, rectangle.Top, rectangle.Bottom);
            var bottom = ShiftEnd(placed.Bottom, rectangle.Top, rectangle.Bottom);

            if (bottom > top)
            {
                cells.Add(placed with { Top = top, Bottom = bottom });
            }
        }

        return Step(tablePos, table, cells, layout.Height - count, table.ColumnWidths);
    }

    /// <summary>Adds columns to the left or the right of the selected ones.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="anchorCell">The position before one corner cell of the selection.</param>
    /// <param name="headCell">The position before the other, or the same cell for a caret.</param>
    /// <param name="after">Whether the new columns go to the right of the selection.</param>
    /// <returns>The step, or null if there is no table there.</returns>
    public static TableStructureStep? InsertColumns(
        DocumentNode doc, int anchorCell, int headCell, bool after) =>
        InsertColumns(doc, anchorCell, headCell, after, known: null);

    /// <inheritdoc cref="InsertColumns(DocumentNode, int, int, bool)"/>
    internal static TableStructureStep? InsertColumns(
        DocumentNode doc, int anchorCell, int headCell, bool after, TableGrid? known)
    {
        if (Region(doc, anchorCell, headCell, known) is not { } region)
        {
            return null;
        }

        var (table, tablePos, layout, rectangle) = region;
        var count = rectangle.Right - rectangle.Left;
        var line = after ? rectangle.Right : rectangle.Left;
        var reference = after ? rectangle.Right - 1 : rectangle.Left;
        var cells = new List<Placed>(layout.Cells.Count + (count * layout.Height));

        foreach (var placed in layout.Cells)
        {
            cells.Add(placed switch
            {
                _ when placed.Left < line && placed.Right > line =>
                    placed with { Right = placed.Right + count },
                _ when placed.Left >= line =>
                    placed with { Left = placed.Left + count, Right = placed.Right + count },
                _ => placed,
            });
        }

        for (var row = 0; row < layout.Height; row++)
        {
            if (layout.At(row, Math.Min(line, layout.Width - 1)) is { } straddler
                && straddler.Left < line && straddler.Right > line)
            {
                continue;
            }

            var model = layout.At(row, reference);

            for (var offset = 0; offset < count; offset++)
            {
                cells.Add(Blank(model, row, line + offset));
            }
        }

        return Step(
            tablePos,
            table,
            cells,
            layout.Height,
            WidenWidths(table.ColumnWidths, line, count, reference));
    }

    /// <summary>Removes the selected columns.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="anchorCell">The position before one corner cell of the selection.</param>
    /// <param name="headCell">The position before the other, or the same cell for a caret.</param>
    /// <returns>The step, or null if there is no table there or nothing would be left of it.</returns>
    /// <remarks>
    /// A row whose only cells were in the removed columns is left carrying nothing, which is
    /// legal exactly when the rest of the table covers it — the schema decides that, not this.
    /// </remarks>
    public static TableStructureStep? DeleteColumns(
        DocumentNode doc, int anchorCell, int headCell) =>
        DeleteColumns(doc, anchorCell, headCell, known: null);

    /// <inheritdoc cref="DeleteColumns(DocumentNode, int, int)"/>
    internal static TableStructureStep? DeleteColumns(
        DocumentNode doc, int anchorCell, int headCell, TableGrid? known)
    {
        if (Region(doc, anchorCell, headCell, known) is not { } region)
        {
            return null;
        }

        var (table, tablePos, layout, rectangle) = region;
        var count = rectangle.Right - rectangle.Left;

        if (count >= layout.Width)
        {
            return null;
        }

        var cells = new List<Placed>(layout.Cells.Count);

        foreach (var placed in layout.Cells)
        {
            var left = Shift(placed.Left, rectangle.Left, rectangle.Right);
            var right = ShiftEnd(placed.Right, rectangle.Left, rectangle.Right);

            if (right > left)
            {
                cells.Add(placed with { Left = left, Right = right });
            }
        }

        return Step(
            tablePos,
            table,
            cells,
            layout.Height,
            NarrowWidths(table.ColumnWidths, rectangle.Left, count));
    }

    /// <summary>Merges the selected cells into one.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="anchorCell">The position before one corner cell of the selection.</param>
    /// <param name="headCell">The position before the other.</param>
    /// <returns>The step, or null if fewer than two cells are selected.</returns>
    /// <remarks>
    /// <para>
    /// The merged cell keeps the block content of every cell it absorbed, in document order,
    /// under the top-left cell's own background and header flag. A cell holding nothing but one
    /// empty paragraph contributes nothing, because merging four cells of which three are empty
    /// should not produce three blank lines to delete afterwards.
    /// </para>
    /// <para>
    /// The rectangle needs no raggedness check. It comes from
    /// <see cref="TableGrid.Rectangle"/>, which grows until it is closed under every cell it
    /// touches, so a merged cell hanging off the edge widens the selection rather than producing
    /// a selection the merge would have to refuse.
    /// </para>
    /// </remarks>
    public static TableStructureStep? MergeCells(
        DocumentNode doc, int anchorCell, int headCell) =>
        MergeCells(doc, anchorCell, headCell, known: null);

    /// <inheritdoc cref="MergeCells(DocumentNode, int, int)"/>
    internal static TableStructureStep? MergeCells(
        DocumentNode doc, int anchorCell, int headCell, TableGrid? known)
    {
        if (Region(doc, anchorCell, headCell, known) is not { } region)
        {
            return null;
        }

        var (table, tablePos, layout, rectangle) = region;
        var absorbed = new List<Placed>();
        var cells = new List<Placed>(layout.Cells.Count);

        foreach (var placed in layout.Cells)
        {
            if (Contains(rectangle, placed))
            {
                absorbed.Add(placed);
            }
            else
            {
                cells.Add(placed);
            }
        }

        if (absorbed.Count < 2)
        {
            return null;
        }

        var target = absorbed[0];
        var blocks = ImmutableArray.CreateBuilder<BlockNode>();

        foreach (var placed in absorbed)
        {
            if (!IsBlank(placed.Cell))
            {
                blocks.AddRange(placed.Cell.Blocks);
            }
        }

        if (blocks.Count == 0)
        {
            blocks.Add(ParagraphNode.Empty);
        }

        cells.Add(new Placed(
            new TableCellNode(
                blocks.ToImmutable(),
                background: target.Cell.Background,
                isHeader: target.Cell.IsHeader),
            rectangle.Top,
            rectangle.Left,
            rectangle.Bottom,
            rectangle.Right));

        return Step(tablePos, table, cells, layout.Height, table.ColumnWidths);
    }

    /// <summary>Splits a merged cell back into one cell per slot it covered.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="cellPos">The position directly before the cell.</param>
    /// <returns>The step, or null if the cell covers one slot already.</returns>
    /// <remarks>
    /// The content stays where it is, in the top-left slot. The cells that appear around it are
    /// empty and inherit the split cell's background and header flag, so splitting a shaded
    /// header cell gives shaded header cells rather than a hole in the shading.
    /// </remarks>
    public static TableStructureStep? SplitCell(
        DocumentNode doc, int cellPos) =>
        SplitCell(doc, cellPos, known: null);

    /// <inheritdoc cref="SplitCell(DocumentNode, int)"/>
    internal static TableStructureStep? SplitCell(
        DocumentNode doc, int cellPos, TableGrid? known)
    {
        if (Region(doc, cellPos, cellPos, known) is not { } region)
        {
            return null;
        }

        var (table, tablePos, layout, rectangle) = region;

        if (rectangle.Bottom - rectangle.Top == 1 && rectangle.Right - rectangle.Left == 1)
        {
            return null;
        }

        var cells = new List<Placed>(layout.Cells.Count);
        TableCellNode? split = null;

        foreach (var placed in layout.Cells)
        {
            if (Contains(rectangle, placed))
            {
                split = placed.Cell;
            }
            else
            {
                cells.Add(placed);
            }
        }

        if (split is null)
        {
            return null;
        }

        for (var row = rectangle.Top; row < rectangle.Bottom; row++)
        {
            for (var column = rectangle.Left; column < rectangle.Right; column++)
            {
                cells.Add(row == rectangle.Top && column == rectangle.Left
                    ? new Placed(
                        new TableCellNode(
                            split.Blocks,
                            background: split.Background,
                            isHeader: split.IsHeader),
                        row,
                        column,
                        row + 1,
                        column + 1)
                    : new Placed(
                        new TableCellNode(
                            [ParagraphNode.Empty],
                            background: split.Background,
                            isHeader: split.IsHeader),
                        row,
                        column,
                        row + 1,
                        column + 1));
            }
        }

        return Step(tablePos, table, cells, layout.Height, table.ColumnWidths);
    }

    /// <summary>Restates a table's column widths.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="tablePos">The position directly before the table.</param>
    /// <param name="widths">One width per column, as ratios rather than measurements.</param>
    /// <returns>
    /// The step, or null if there is no table there, if the count does not match the table's
    /// columns, or if the table already states these widths.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A width is not a positive finite number.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The widths are shares of whatever width the table is given, not measurements, so
    /// <c>[1, 2]</c> and <c>[100, 200]</c> describe the same table. Callers that mean a proportion
    /// should say so by passing one — a vector summing to 1 leaves nothing to misread.
    /// </para>
    /// <para>
    /// The count is checked against the grid's width rather than against any row's cell count,
    /// because a row whose cells span columns carries fewer cells than the table is wide.
    /// </para>
    /// <para>
    /// This is the one edit here that moves nothing, and it is still a
    /// <see cref="TableStructureStep"/>: the rows are handed through untouched, so they stay
    /// reference-identical and the step's map comes out empty. Nothing else would be gained by a
    /// step of its own, which would have to repeat the rebasing guard and the schema validation.
    /// </para>
    /// <para>
    /// A mismatched count returns null rather than throwing, matching the rest of this class: the
    /// table may have changed under a caller holding an older column count, and that is a fact
    /// about the document. A width that is not a width is a fault in the argument instead, so it
    /// throws exactly as <see cref="TableNode"/>'s own constructor would a line later.
    /// </para>
    /// </remarks>
    public static TableStructureStep? SetColumnWidths(
        DocumentNode doc, int tablePos, IEnumerable<double> widths)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(widths);

        var stated = widths.ToImmutableArray();

        foreach (var width in stated)
        {
            if (!double.IsFinite(width) || width <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(widths), width, "A column width must be a positive finite number.");
            }
        }

        if (tablePos < 0
            || tablePos > doc.ContentSize
            || doc.Resolve(tablePos).NodeAfter is not TableNode table)
        {
            return null;
        }

        TableGeometry.Place(table, out var columns);

        if (stated.Length != columns)
        {
            return null;
        }

        var after = table.WithColumnWidths(stated);

        return after.Equals(table) ? null : new TableStructureStep(tablePos, table, after);
    }

    /// <summary>Where one cell sits, as a rectangle rather than as a pair of spans.</summary>
    private readonly record struct Placed(
        TableCellNode Cell, int Top, int Left, int Bottom, int Right);

    /// <summary>The area a selection covers.</summary>
    private readonly record struct Area(int Top, int Left, int Bottom, int Right);

    /// <summary>A table's cells with their positions on the grid worked out.</summary>
    private sealed class Layout
    {
        private readonly Placed?[,] _slots;

        private Layout(List<Placed> cells, Placed?[,] slots, int width, int height)
        {
            Cells = cells;
            _slots = slots;
            Width = width;
            Height = height;
        }

        public List<Placed> Cells { get; }

        public int Width { get; }

        public int Height { get; }

        /// <param name="table">The table.</param>
        /// <param name="placements">
        /// Its placements, from <see cref="TableGeometry.Place"/> - the same placement the schema
        /// and the view use, so a command can never disagree with them about where a cell sits.
        /// </param>
        /// <param name="width">The width <see cref="TableGeometry.Place"/> reported.</param>
        public static Layout Of(TableNode table, ImmutableArray<CellPlacement> placements, int width)
        {
            var cells = new List<Placed>(placements.Length);
            var next = 0;

            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    var placement = placements[next++];

                    cells.Add(new Placed(
                        cell,
                        placement.Top,
                        placement.Left,
                        placement.Bottom,
                        placement.Right));
                }
            }

            var height = table.Rows.Length;
            var slots = new Placed?[height, Math.Max(width, 1)];

            foreach (var placed in cells)
            {
                for (var r = placed.Top; r < Math.Min(placed.Bottom, height); r++)
                {
                    for (var c = placed.Left; c < placed.Right; c++)
                    {
                        slots[r, c] = placed;
                    }
                }
            }

            return new Layout(cells, slots, width, height);
        }

        public Placed? At(int row, int column) =>
            row < 0 || column < 0 || row >= Height || column >= _slots.GetLength(1)
                ? null
                : _slots[row, column];
    }

    /// <summary>Resolves a selection into the table it is in and the area it covers.</summary>
    /// <remarks>
    /// A grid the caller already built is the one <see cref="TableGrid.ForCell"/> would build
    /// when the anchor is one of its cells and its table is still the very node at its position:
    /// a cell position resolves to one table, and a grid is a function of the table and where it
    /// sits. Anything else - another document, an edited table, a moved one - builds afresh.
    /// </remarks>
    private static (TableNode Table, int TablePos, Layout Layout, Area Rectangle)? Region(
        DocumentNode doc, int anchorCell, int headCell, TableGrid? known)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var grid = known is not null
            && known.Cells.ContainsKey(anchorCell)
            && known.TablePos >= 0
            && known.TablePos <= doc.ContentSize
            && ReferenceEquals(doc.Resolve(known.TablePos).NodeAfter, known.Table)
                ? known
                : TableGrid.ForCell(doc, anchorCell);

        if (grid is null || doc.Resolve(grid.TablePos).NodeAfter is not TableNode table)
        {
            return null;
        }

        var selected = grid.Cells.ContainsKey(headCell)
            ? grid.Rectangle(anchorCell, headCell)
            : grid.Rectangle(anchorCell, anchorCell);

        if (selected.IsEmpty)
        {
            return null;
        }

        var top = int.MaxValue;
        var left = int.MaxValue;
        var bottom = 0;
        var right = 0;

        foreach (var pos in selected)
        {
            var slot = grid.Cells[pos];

            top = Math.Min(top, slot.Top);
            left = Math.Min(left, slot.Left);
            bottom = Math.Max(bottom, slot.Bottom);
            right = Math.Max(right, slot.Right);
        }

        return (table, grid.TablePos, Layout.Of(table, grid.Placements, grid.Columns), new Area(top, left, bottom, right));
    }

    /// <summary>Assembles the replacement table and pairs it with the one it replaces.</summary>
    /// <remarks>
    /// The rows are rebuilt by walking the grid rather than by keeping the old row lists, so each
    /// cell's spans come from the rectangle it ended up with and cannot drift out of step with
    /// where it sits. A cell appears in the row it starts in, and the cells of a row come out in
    /// column order, which is the order the document stores them in.
    /// </remarks>
    private static TableStructureStep? Step(
        int tablePos,
        TableNode before,
        List<Placed> cells,
        int height,
        ImmutableArray<double> widths)
    {
        var rows = new List<Placed>[height];

        foreach (var placed in cells)
        {
            if (placed.Top < 0 || placed.Top >= height)
            {
                return null;
            }

            (rows[placed.Top] ??= []).Add(placed);
        }

        var built = ImmutableArray.CreateBuilder<TableRowNode>(height);

        for (var row = 0; row < height; row++)
        {
            if (rows[row] is not { } inRow)
            {
                built.Add(new TableRowNode([]));

                continue;
            }

            inRow.Sort(static (left, right) => left.Left.CompareTo(right.Left));

            var inOrder = ImmutableArray.CreateBuilder<TableCellNode>(inRow.Count);

            foreach (var placed in inRow)
            {
                inOrder.Add(WithSpans(placed));
            }

            built.Add(new TableRowNode(inOrder.ToImmutable()));
        }

        var after = new TableNode(built.ToImmutable(), widths.IsEmpty ? null : widths);

        return after.Equals(before) ? null : new TableStructureStep(tablePos, before, after);
    }

    /// <summary>
    /// A cell whose spans agree with the rectangle it occupies.
    /// </summary>
    /// <remarks>
    /// Returning the same object when nothing changed is not a micro-optimisation. It is what
    /// lets <see cref="TableStructureStep"/> match untouched rows by reference and report a small
    /// range per changed row instead of one range over the whole table.
    /// </remarks>
    private static TableCellNode WithSpans(Placed placed) =>
        placed.Cell.RowSpan == placed.Bottom - placed.Top
            && placed.Cell.ColumnSpan == placed.Right - placed.Left
            ? placed.Cell
            : new TableCellNode(
                placed.Cell.Blocks,
                placed.Bottom - placed.Top,
                placed.Right - placed.Left,
                placed.Cell.Background,
                placed.Cell.IsHeader);

    private static bool Contains(Area area, Placed placed) =>
        placed.Top >= area.Top
            && placed.Left >= area.Left
            && placed.Bottom <= area.Bottom
            && placed.Right <= area.Right;

    /// <summary>Whether a cell holds nothing but one empty paragraph.</summary>
    private static bool IsBlank(TableCellNode cell) =>
        cell.Blocks is [ParagraphNode only] && only.Content.Length == 0;

    /// <summary>An empty cell taking its look from the cell it was added beside.</summary>
    private static Placed Blank(Placed? model, int row, int column) =>
        new(
            new TableCellNode(
                [ParagraphNode.Empty],
                background: model?.Cell.Background,
                isHeader: model?.Cell.IsHeader ?? false),
            row,
            column,
            row + 1,
            column + 1);

    /// <summary>Where a row or column index lands once a run of them is removed.</summary>
    private static int Shift(int index, int from, int to) =>
        index < from ? index
        : index >= to ? index - (to - from)
        : from;

    /// <summary>The same, for an exclusive end, which belongs to the run before it.</summary>
    private static int ShiftEnd(int end, int from, int to) =>
        end <= from ? end
        : end >= to ? end - (to - from)
        : from;

    private static ImmutableArray<double> WidenWidths(
        ImmutableArray<double> widths, int at, int count, int model)
    {
        if (widths.IsEmpty)
        {
            return widths;
        }

        var width = model >= 0 && model < widths.Length ? widths[model] : widths[^1];

        return widths.InsertRange(Math.Min(at, widths.Length), Enumerable.Repeat(width, count));
    }

    private static ImmutableArray<double> NarrowWidths(
        ImmutableArray<double> widths, int at, int count) =>
        widths.IsEmpty || at >= widths.Length
            ? widths
            : widths.RemoveRange(at, Math.Min(count, widths.Length - at));
}
