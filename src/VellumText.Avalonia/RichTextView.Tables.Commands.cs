using System.Collections.Immutable;

namespace VellumText.Avalonia;

/// <summary>
/// Editing a table's structure: rows, columns, merging and splitting.
/// </summary>
/// <remarks>
/// <para>
/// Each command reads the selection, asks <see cref="TableEdits"/> for a step, and says where the
/// caret should end up. None of them decides anything about tables themselves — the shape of the
/// replacement table, and whether the edit is possible at all, belong to the model, which is
/// where they can be tested without a control.
/// </para>
/// <para>
/// The caret is placed by grid coordinates rather than by mapping the old position forward. The
/// cell a reader should land in after inserting a row did not exist a moment ago, so there is no
/// old position to map; and after deleting one, the position that mapped forward is in whatever
/// cell moved up into the gap, which is not where anyone was looking.
/// </para>
/// </remarks>
public partial class RichTextView
{
    /// <summary>Adds a row above the selection.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool InsertRowAbove() =>
        TableAt() is { } region
        && Structure(
            TableEdits.InsertRows(_state.Doc, region.Anchor, region.Head, below: false),
            region.Top,
            region.Left);

    /// <summary>Adds a row below the selection.</summary>
    /// <returns>Whether anything changed.</returns>
    /// <remarks>As many rows go in as the selection covers, which is what a rectangle means.</remarks>
    public bool InsertRowBelow() =>
        TableAt() is { } region
        && Structure(
            TableEdits.InsertRows(_state.Doc, region.Anchor, region.Head, below: true),
            region.Bottom,
            region.Left);

    /// <summary>Removes the selected rows.</summary>
    /// <returns>Whether anything changed.</returns>
    /// <remarks>
    /// Refused when it would empty the table: removing a table is a different gesture from
    /// editing one, and there is no key here that means it.
    /// </remarks>
    public bool DeleteRow() =>
        TableAt() is { } region
        && Structure(
            TableEdits.DeleteRows(_state.Doc, region.Anchor, region.Head),
            region.Top,
            region.Left);

    /// <summary>Adds a column to the left of the selection.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool InsertColumnLeft() =>
        TableAt() is { } region
        && Structure(
            TableEdits.InsertColumns(_state.Doc, region.Anchor, region.Head, after: false),
            region.Top,
            region.Left);

    /// <summary>Adds a column to the right of the selection.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool InsertColumnRight() =>
        TableAt() is { } region
        && Structure(
            TableEdits.InsertColumns(_state.Doc, region.Anchor, region.Head, after: true),
            region.Top,
            region.Right);

    /// <summary>Removes the selected columns.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool DeleteColumn() =>
        TableAt() is { } region
        && Structure(
            TableEdits.DeleteColumns(_state.Doc, region.Anchor, region.Head),
            region.Top,
            region.Left);

    /// <summary>Merges the selected cells into one.</summary>
    /// <returns>Whether anything changed.</returns>
    /// <remarks>
    /// Needs a rectangle of at least two cells, so it does nothing with a plain caret. The caret
    /// lands at the end of the merged cell, past everything it absorbed, which is where a reader
    /// carries on typing.
    /// </remarks>
    public bool MergeCells() =>
        TableAt() is { } region
        && Structure(
            TableEdits.MergeCells(_state.Doc, region.Anchor, region.Head),
            region.Top,
            region.Left,
            CellCaret.End);

    /// <summary>Splits a merged cell back into one cell per slot it covered.</summary>
    /// <returns>Whether anything changed.</returns>
    /// <remarks>
    /// Acts on the cell the caret is in, or on the cell a rectangle was dragged from. A rectangle
    /// of several merged cells is not one gesture with one answer, and picking the cell the drag
    /// began in is at least the one the reader pointed at first.
    /// </remarks>
    public bool SplitCell() =>
        TableAt() is { } region
        && Structure(
            TableEdits.SplitCell(_state.Doc, region.Anchor),
            region.Top,
            region.Left,
            CellCaret.Offset,
            _state.Selection.From - (region.Anchor + 1));

    /// <summary>Whether the selection is inside a table, and so whether these commands can act.</summary>
    public bool IsInTable => TableAt() is not null;

    /// <summary>
    /// Moves the caret to the next or previous cell, adding a row past the last one.
    /// </summary>
    /// <param name="backwards">Whether to move to the previous cell.</param>
    /// <returns>Whether the caret moved or a row was added.</returns>
    /// <remarks>
    /// <para>
    /// Cells are visited in document order, which for a table that tiles its grid is the order a
    /// reader sees them in: left to right, then down. A merged cell is visited once, at the slot
    /// it starts in.
    /// </para>
    /// <para>
    /// Tab past the last cell adds a row, as Word does, so a table can be filled in without ever
    /// reaching for the mouse. It adds exactly what "insert row below" would add from that cell,
    /// which for a cell spanning several rows is several — the same answer to the same question.
    /// </para>
    /// <para>
    /// Shift+Tab in the first cell has nowhere to go and reports that it did nothing. The key is
    /// still consumed by the caller; see <c>Tab</c> in RichTextView.Blocks.Commands.cs.
    /// </para>
    /// </remarks>
    internal bool MoveToCell(bool backwards)
    {
        if (TableAt() is not { } region)
        {
            return false;
        }

        var ordered = Ordered(region.Grid);
        var current = _state.Selection is CellSelection rectangle ? rectangle.HeadCell : region.Anchor;
        var index = ordered.IndexOf(current);

        if (index < 0)
        {
            return false;
        }

        if (backwards)
        {
            if (index == 0)
            {
                return false;
            }

            MoveTo(ordered[index - 1] + 1, extend: false);

            return true;
        }

        if (index + 1 < ordered.Length)
        {
            MoveTo(ordered[index + 1] + 1, extend: false);

            return true;
        }

        return Structure(
            TableEdits.InsertRows(_state.Doc, ordered[index], ordered[index], below: true),
            region.Grid.Rows,
            0);
    }

    /// <summary>The table the selection is in and the area of the grid it covers.</summary>
    /// <param name="Grid">The table, laid out.</param>
    /// <param name="Anchor">The position before the cell the selection started in.</param>
    /// <param name="Head">The position before the cell it ends in.</param>
    /// <param name="Top">The first row it covers.</param>
    /// <param name="Left">The first column.</param>
    /// <param name="Bottom">One past the last row.</param>
    /// <param name="Right">One past the last column.</param>
    private readonly record struct TableRegion(
        TableGrid Grid, int Anchor, int Head, int Top, int Left, int Bottom, int Right);

    /// <summary>
    /// Resolves the selection into a table and the cells it covers, or null if it is not in one.
    /// </summary>
    /// <remarks>
    /// A caret counts as a one-cell rectangle. Every command here works the same way on both, so
    /// making the caret the degenerate case is what keeps them from each having two branches.
    /// </remarks>
    private TableRegion? TableAt()
    {
        var doc = _state.Doc;
        var anchor = _state.Selection is CellSelection rectangle
            ? rectangle.AnchorCell
            : TableGrid.CellContaining(doc, _state.Selection.From);

        if (anchor is not { } anchorCell || TableGrid.ForCell(doc, anchorCell) is not { } grid)
        {
            return null;
        }

        var head = _state.Selection is CellSelection selected ? selected.HeadCell : anchorCell;
        var cells = grid.Cells.ContainsKey(head)
            ? grid.Rectangle(anchorCell, head)
            : grid.Rectangle(anchorCell, anchorCell);

        if (cells.IsEmpty)
        {
            return null;
        }

        var top = int.MaxValue;
        var left = int.MaxValue;
        var bottom = 0;
        var right = 0;

        foreach (var pos in cells)
        {
            var slot = grid.Cells[pos];

            top = Math.Min(top, slot.Top);
            left = Math.Min(left, slot.Left);
            bottom = Math.Max(bottom, slot.Bottom);
            right = Math.Max(right, slot.Right);
        }

        return new TableRegion(grid, anchorCell, head, top, left, bottom, right);
    }

    /// <summary>Applies a table step and puts the caret in the cell at a grid slot.</summary>
    /// <param name="step">The step, or null when the edit does not apply.</param>
    /// <param name="row">Which row of the resulting table to leave the caret in.</param>
    /// <param name="column">Which column.</param>
    /// <param name="caret">Where in that cell the caret goes.</param>
    /// <param name="offset">How far into the cell, when <paramref name="caret"/> asks for it.</param>
    /// <remarks>
    /// The slot is clamped rather than checked, because a command may name a slot the edit
    /// removed — deleting the last row asks for the row that is no longer there — and the nearest
    /// surviving cell is a better answer than leaving the caret where the table used to be.
    /// </remarks>
    private bool Structure(
        TableStructureStep? step,
        int row,
        int column,
        CellCaret caret = CellCaret.Start,
        int offset = 0)
    {
        if (step is null)
        {
            return false;
        }

        var transaction = _state.Transaction().As(TransactionKind.Structure).Step(step);

        if (!transaction.ChangedDoc)
        {
            return false;
        }

        if (transaction.Doc.Resolve(step.Pos).NodeAfter is TableNode table)
        {
            var grid = TableGrid.Build(step.Pos, table);
            var slot = grid.At(
                Math.Clamp(row, 0, grid.Rows - 1), Math.Clamp(column, 0, grid.Columns - 1));

            if (slot is { } cell)
            {
                transaction.SetSelection(
                    TextSelection.Cursor(CaretIn(transaction.Doc, cell.Pos, caret, offset)));
            }
        }

        transaction.SetStoredMarks(null);
        _goalX = null;

        return Apply(transaction);
    }

    /// <summary>The cell positions of a table in document order.</summary>
    private static ImmutableArray<int> Ordered(TableGrid grid) => [.. grid.Cells.Keys.Order()];

    /// <summary>Where a caret goes inside a cell.</summary>
    /// <remarks>
    /// Searched for rather than computed, because a cell's first block need not be a paragraph:
    /// a cell that begins with a list has two more boundaries to step over before there is any
    /// text, and a cell of nothing but a rule has none at all.
    /// </remarks>
    private static int CaretIn(DocumentNode doc, int cellPos, CellCaret caret, int offset)
    {
        if (doc.Resolve(cellPos).NodeAfter is not TableCellNode cell)
        {
            return cellPos;
        }

        var from = cellPos + 1;
        var to = from + cell.ContentSize;

        if (caret == CellCaret.End)
        {
            for (var pos = to; pos >= from; pos--)
            {
                if (doc.Resolve(pos).IsInText)
                {
                    return pos;
                }
            }

            return to;
        }

        // Forward from where the reader was, which for Start is the front of the cell. Splitting
        // a cell leaves its content exactly where it was, so the caret has somewhere real to go
        // back to and jumping it to either end of the cell would be a move nobody asked for.
        var wanted = caret == CellCaret.Offset ? Math.Clamp(from + offset, from, to) : from;

        for (var pos = wanted; pos <= to; pos++)
        {
            if (doc.Resolve(pos).IsInText)
            {
                return pos;
            }
        }

        for (var pos = wanted - 1; pos >= from; pos--)
        {
            if (doc.Resolve(pos).IsInText)
            {
                return pos;
            }
        }

        return from;
    }

    /// <summary>Where the caret goes in the cell a command leaves it in.</summary>
    private enum CellCaret
    {
        /// <summary>The first place text can sit — a new or emptied cell.</summary>
        Start,

        /// <summary>The last place text can sit, past everything a merge absorbed.</summary>
        End,

        /// <summary>As far in as it was, for an edit that left the cell's content alone.</summary>
        Offset,
    }
}
