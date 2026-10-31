namespace VellumText.Core.Tests.Transform;

/// <summary>
/// Telling whether two versions of a table share any node costs a witness, not a set of every
/// node in the table, and gives the same answer the set did.
/// </summary>
/// <remarks>
/// <see cref="TableStructureStep.GetMap"/> asks it on every structural edit and on its undo. A set
/// of the 110,000 nodes of a 10,000 × 5 table was 14 MB each time, for a question a step built by
/// <see cref="TableEdits"/> answers with its first row.
/// </remarks>
public sealed class TableStructureShareTests
{
    /// <summary>
    /// The set answer the step falls back on: every row and cell of one version, looked up from
    /// the other. Paragraphs are left out, as the step leaves them out — every blank cell holds
    /// the one <see cref="ParagraphNode.Empty"/>, so a shared paragraph says nothing.
    /// </summary>
    private static bool Oracle(Node before, Node after)
    {
        var seen = new HashSet<Node>(RowsAndCells(before), ReferenceEqualityComparer.Instance);

        return RowsAndCells(after).Any(seen.Contains);

        static IEnumerable<Node> RowsAndCells(Node table)
        {
            foreach (var row in table.Children)
            {
                yield return row;

                foreach (var cell in row.Children)
                {
                    yield return cell;
                }
            }
        }
    }

    private static DocumentNode Document(int rows) =>
        new([new TableNode(Enumerable.Range(0, rows).Select(r =>
            new TableRowNode(Enumerable.Range(0, 5).Select(c => TableCellNode.FromText($"{r}.{c}")))))]);

    /// <summary>The position before row <paramref name="row"/>, column 0, of <see cref="Document"/>.</summary>
    private static int CellPos(DocumentNode doc, int row)
    {
        var table = (TableNode)doc.Blocks[0];
        var pos = 2;

        for (var r = 0; r < row; r++)
        {
            pos += table.Rows[r].NodeSize;
        }

        return pos;
    }

    private static long BytesOf(Action action)
    {
        action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        action();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// Above the first row the first rows no longer match, and below the last row the last rows
    /// no longer match; either end alone must be enough.
    /// </summary>
    [Theory]
    [InlineData(5_000, true)]
    [InlineData(0, false)]
    [InlineData(9_999, true)]
    public void Mapping_a_row_insert_and_its_undo_does_not_collect_every_node_of_the_table(int row, bool below)
    {
        var doc = Document(10_000);
        var cell = CellPos(doc, row);
        var step = TableEdits.InsertRows(doc, cell, cell, below)!;
        var undo = step.Invert(doc);

        var map = BytesOf(() => step.GetMap());
        var undoMap = BytesOf(() => undo.GetMap());

        // The set was 14 MB; the diff the map is then built from walks rows by reference.
        Assert.True(map < 2 * 1024 * 1024, $"mapping the insert allocated {map / 1024} KB");
        Assert.True(undoMap < 2 * 1024 * 1024, $"mapping its undo allocated {undoMap / 1024} KB");
    }

    [Fact]
    public void Sharing_agrees_with_the_set_of_every_row_and_cell()
    {
        var random = new Random(20261028);
        var shared = 0;
        var separate = 0;

        for (var i = 0; i < 2_000; i++)
        {
            var rows = random.Next(1, 6);
            var columns = random.Next(1, 5);
            var before = new TableNode(Enumerable.Range(0, rows).Select(r =>
                new TableRowNode(Enumerable.Range(0, columns).Select(c => TableCellNode.FromText($"{r}.{c}")))));
            var after = Variant(random, before);

            var expected = Oracle(before, after);

            Assert.True(
                expected == TableStructureStep.SharesAny(before, after),
                $"case {i} ({rows}x{columns}): expected {expected}");

            shared += expected ? 1 : 0;
            separate += expected ? 0 : 1;
        }

        Assert.True(shared > 500 && separate > 250, $"{shared} shared, {separate} separate");
    }

    /// <summary>Another version of <paramref name="table"/>, sharing nodes in some place or none.</summary>
    private static TableNode Variant(Random random, TableNode table)
    {
        var rows = table.Rows;

        TableRowNode Copy(TableRowNode row) =>
            new(row.Cells.Select(c => new TableCellNode(
                c.Blocks.Select(b => (BlockNode)ParagraphNode.FromText(((ParagraphNode)b).Content.Text)),
                c.RowSpan,
                c.ColumnSpan)));

        var copied = rows.Select(Copy).ToList();

        switch (random.Next(6))
        {
            case 0:
                // Nothing in common: a table a host built on its own.
                return new TableNode(copied);

            case 1:
            {
                // One row kept, anywhere - the middle is the case the ends cannot see.
                var r = random.Next(rows.Length);
                copied[r] = rows[r];

                return new TableNode(copied);
            }

            case 2:
            {
                // One cell kept inside an otherwise new row.
                var r = random.Next(rows.Length);
                var cells = copied[r].Cells;
                var c = random.Next(cells.Length);
                copied[r] = new TableRowNode(cells.SetItem(c, rows[r].Cells[c]));

                return new TableNode(copied);
            }

            case 3:
            {
                // One paragraph kept inside an otherwise new cell: not sharing, since what a
                // cell holds can be shared by tables that have nothing else in common.
                var r = random.Next(rows.Length);
                var c = random.Next(rows[r].Cells.Length);
                var cells = copied[r].Cells;
                copied[r] = new TableRowNode(cells.SetItem(c, new TableCellNode(rows[r].Cells[c].Blocks)));

                return new TableNode(copied);
            }

            case 4:
                // A row inserted at the top: the rows below are the same objects.
                return new TableNode(rows.Insert(0, Copy(rows[0])));

            default:
                // Every row rebuilt around the same cells, as TableEdits does.
                return new TableNode(rows.Select(row => new TableRowNode(row.Cells)));
        }
    }
}
