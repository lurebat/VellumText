namespace VellumText.Core.Tests;

/// <summary>
/// A position mapped through a row or column insertion stays in the row and column it was in.
/// </summary>
/// <remarks>
/// <see cref="TableStructureStep"/> works out its map by diffing the old table against the new one,
/// and matched children by value. A freshly inserted empty row or cell equals the empty ones beside
/// it — every new table is all empty cells — so the insertion was reported at the far end of the
/// run of equal neighbours, and a caret or rectangle mapped through it landed one column left or one
/// row up. The editor's own commands set the selection explicitly and so never saw it; a host
/// applying <see cref="TableEdits"/> in a transaction of its own did.
/// </remarks>
public sealed class TableStructureMapTests
{
    private static TableCellNode Empty() => new([new ParagraphNode(InlineContent.Empty)]);

    private static (EditorState State, int[] Cells, DocumentNode Doc) Setup(int caretCell)
    {
        var doc = new DocumentNode([new TableNode([
            new TableRowNode([Empty(), Empty(), Empty()]),
            new TableRowNode([Empty(), Empty(), Empty()]),
        ])]);
        var grid = TableGrid.Build(0, (TableNode)doc.Blocks[0]);
        var cells = grid.Cells.OrderBy(kv => kv.Key).Select(kv => kv.Key).ToArray();
        var state = EditorState.Create(doc);

        state = state.Apply(state.Transaction().SetSelection(TextSelection.Cursor(cells[caretCell] + 2)));

        return (state, cells, doc);
    }

    private static TableSlot SlotOfCaret(EditorState state)
    {
        var cell = TableGrid.CellContaining(state.Doc, state.Selection.Head)!.Value;

        return TableGrid.ForCell(state.Doc, cell)!.Cells[cell];
    }

    [Fact]
    public void A_caret_keeps_its_column_through_a_column_inserted_before_it()
    {
        var (state, cells, doc) = Setup(caretCell: 1);
        var step = TableEdits.InsertColumns(doc, cells[0], cells[0], after: false)!;

        var after = state.Apply(state.Transaction().Step(step));
        var slot = SlotOfCaret(after);

        Assert.Equal((0, 2), (slot.Top, slot.Left));
    }

    [Fact]
    public void A_caret_keeps_its_row_through_a_row_inserted_above_it()
    {
        var (state, cells, doc) = Setup(caretCell: 4);
        var step = TableEdits.InsertRows(doc, cells[0], cells[0], below: false)!;

        var after = state.Apply(state.Transaction().Step(step));
        var slot = SlotOfCaret(after);

        Assert.Equal((2, 1), (slot.Top, slot.Left));
    }

    [Fact]
    public void A_rectangle_keeps_its_columns_through_a_column_inserted_before_it()
    {
        var (state, cells, doc) = Setup(caretCell: 0);

        state = state.Apply(state.Transaction().SetSelection(CellSelection.Create(doc, cells[1], cells[5])!));

        var step = TableEdits.InsertColumns(doc, cells[0], cells[0], after: false)!;
        var after = state.Apply(state.Transaction().Step(step));
        var selection = Assert.IsType<CellSelection>(after.Selection);
        var grid = TableGrid.ForCell(after.Doc, selection.AnchorCell)!;

        Assert.Equal(2, grid.Cells[selection.AnchorCell].Left);
        Assert.Equal(3, grid.Cells[selection.HeadCell].Left);
    }

    /// <summary>
    /// The control: a host that builds the new table itself shares no nodes with the old one, and
    /// the map still bounds what changed and keeps a caret in an untouched row.
    /// </summary>
    [Fact]
    public void A_table_built_independently_still_maps_an_untouched_row()
    {
        var before = new TableNode([
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
        ]);
        var after = new TableNode([
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
            new TableRowNode([TableCellNode.FromText("e"), TableCellNode.FromText("f")]),
        ]);
        var map = new TableStructureStep(0, before, after).GetMap();

        // Inside "a": past the table, row and cell boundaries and the paragraph's own.
        Assert.Equal(4, map.Map(4));
    }
}
