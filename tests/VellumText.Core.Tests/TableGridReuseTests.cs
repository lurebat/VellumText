namespace VellumText.Core.Tests;

/// <summary>
/// The shortcuts a table command takes to work its grid out once give exactly the answers the
/// grid built afresh gave.
/// </summary>
public sealed class TableGridReuseTests
{
    private static TableNode RandomTable(Random random)
    {
        var rows = random.Next(1, 7);

        return new TableNode(Enumerable.Range(0, rows).Select(r =>
            new TableRowNode(Enumerable.Range(0, random.Next(0, 5)).Select(c =>
                new TableCellNode(
                    [ParagraphNode.FromText($"{r}.{c}")],
                    random.Next(4) == 0 ? random.Next(1, rows + 2) : 1,
                    random.Next(4) == 0 ? random.Next(1, 4) : 1)))));
    }

    /// <summary>A table that tiles its grid: every row has the same number of plain cells.</summary>
    private static TableNode GridTable(Random random)
    {
        var columns = random.Next(1, 5);

        return new TableNode(Enumerable.Range(0, random.Next(1, 6)).Select(r =>
            new TableRowNode(Enumerable.Range(0, columns).Select(c => TableCellNode.FromText($"{r}.{c}")))));
    }

    [Fact]
    public void The_cell_at_a_slot_is_the_one_the_grid_puts_there()
    {
        var random = new Random(20261028);

        for (var i = 0; i < 2_000; i++)
        {
            var table = RandomTable(random);
            var tablePos = random.Next(0, 5);
            var grid = TableGrid.Build(tablePos, table);
            var placements = TableGeometry.Place(table, out var width);

            for (var row = -1; row <= table.Rows.Length; row++)
            {
                for (var column = -1; column <= width; column++)
                {
                    Assert.True(
                        grid.At(row, column)?.Pos == TableGrid.CellAt(tablePos, table, placements, row, column),
                        $"case {i}: slot ({row}, {column})");
                }
            }
        }
    }

    private static IEnumerable<Func<DocumentNode, int, int, TableGrid?, TableStructureStep?>> Commands()
    {
        yield return (d, a, h, g) => TableEdits.InsertRows(d, a, h, below: true, g);
        yield return (d, a, h, g) => TableEdits.InsertRows(d, a, h, below: false, g);
        yield return (d, a, h, g) => TableEdits.DeleteRows(d, a, h, g);
        yield return (d, a, h, g) => TableEdits.InsertColumns(d, a, h, after: true, g);
        yield return (d, a, h, g) => TableEdits.InsertColumns(d, a, h, after: false, g);
        yield return (d, a, h, g) => TableEdits.DeleteColumns(d, a, h, g);
        yield return (d, a, h, g) => TableEdits.MergeCells(d, a, h, g);
        yield return (d, a, _, g) => TableEdits.SplitCell(d, a, g);
    }

    /// <summary>
    /// A grid passed in is used only when it is the grid of that table at that position; a stale
    /// one - built before an edit, for another document, or for the table before it moved - must
    /// change nothing.
    /// </summary>
    [Fact]
    public void A_command_given_a_grid_gives_the_step_it_gives_without_one()
    {
        var random = new Random(20261029);
        var commands = Commands().ToArray();
        var reused = 0;

        for (var i = 0; i < 600; i++)
        {
            var table = GridTable(random);
            var lead = random.Next(2) == 0;
            DocumentNode doc = lead
                ? new([ParagraphNode.FromText("lead"), table])
                : new([table]);
            var tablePos = lead ? doc.Blocks[0].NodeSize : 0;
            var grid = TableGrid.Build(tablePos, table);
            var cells = grid.Cells.Keys.Order().ToArray();
            var anchor = cells[random.Next(cells.Length)];
            var head = cells[random.Next(cells.Length)];

            // Grids that are not this table's, at this position, in this document.
            var moved = TableGrid.Build(tablePos + 1, table);
            var other = TableGrid.Build(tablePos, GridTable(random));
            var copy = TableGrid.Build(tablePos, new TableNode(table.Rows, table.ColumnWidths));

            foreach (var command in commands)
            {
                var expected = command(doc, anchor, head, null);

                foreach (var given in new[] { grid, moved, other, copy })
                {
                    var actual = command(doc, anchor, head, given);

                    Assert.True(
                        Equals(expected, actual),
                        $"case {i}: {(given == grid ? "its own grid" : "a stale grid")} changed the step");
                }

                reused++;
            }
        }

        Assert.True(reused > 4_000);
    }

    /// <summary>
    /// The grid of the table around a nested one is that table's grid, at that position, in that
    /// document - and still not the grid a command on a cell of the inner table needs.
    /// </summary>
    [Fact]
    public void A_command_in_a_nested_table_ignores_the_grid_of_the_table_around_it()
    {
        var inner = new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("i1"), TableCellNode.FromText("j1")]),
            new TableRowNode([TableCellNode.FromText("i2"), TableCellNode.FromText("j2")]),
        ]);
        var outer = new TableNode(
        [
            new TableRowNode([new TableCellNode([inner]), TableCellNode.FromText("b1")]),
            new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
        ]);
        var doc = new DocumentNode([outer]);
        var outerGrid = TableGrid.Build(0, outer);

        // Row 0, cell 0 of the outer table, then its table, then its first row and cell.
        var innerCell = 1 + 1 + 1 + 1 + 1;
        Assert.NotNull(TableGrid.ForCell(doc, innerCell));
        Assert.False(outerGrid.Cells.ContainsKey(innerCell));

        foreach (var command in Commands())
        {
            Assert.Equal(command(doc, innerCell, innerCell, null), command(doc, innerCell, innerCell, outerGrid));
        }
    }
}
