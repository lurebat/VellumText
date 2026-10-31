namespace VellumText.Core.Tests;

/// <summary>
/// <see cref="TableGeometry.Place"/> agrees with the hash-set algorithm it replaced, on tables
/// built to be malformed as well as well-formed ones, and costs the placements it returns rather
/// than a set of every slot.
/// </summary>
/// <remarks>
/// The array version tracks, per column, the row a span above keeps it busy until. That is the
/// same thing as slot occupancy only because cells are placed row by row and every span starts on
/// the row being placed — the reason the old algorithm is kept here as the oracle rather than
/// trusted to stay that way. See <c>docs/plans/2026-10-28-table-edit-scaling-research.md</c>,
/// option A.
/// </remarks>
public sealed class TableGeometryEquivalenceTests
{
    /// <summary>The slot-set placement <see cref="TableGeometry.Place"/> used before, verbatim.</summary>
    private static CellPlacement[] Oracle(TableNode table, out int width)
    {
        var placements = new List<CellPlacement>();
        var occupied = new HashSet<(int Row, int Column)>();

        width = 0;

        for (var row = 0; row < table.Rows.Length; row++)
        {
            var column = 0;

            foreach (var cell in table.Rows[row].Cells)
            {
                while (!IsFree(occupied, row, column, cell.ColumnSpan))
                {
                    column++;
                }

                var placement = new CellPlacement(
                    row, column, row + cell.RowSpan, column + cell.ColumnSpan);
                var bottom = Math.Min(placement.Bottom, table.Rows.Length);

                for (var r = placement.Top; r < bottom; r++)
                {
                    for (var c = placement.Left; c < placement.Right; c++)
                    {
                        occupied.Add((r, c));
                    }
                }

                placements.Add(placement);
                width = Math.Max(width, placement.Right);
                column = placement.Right;
            }
        }

        return [.. placements];
    }

    private static bool IsFree(HashSet<(int Row, int Column)> occupied, int row, int column, int span)
    {
        for (var c = column; c < column + span; c++)
        {
            if (occupied.Contains((row, c)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Rows of random length holding cells of random spans, with no attempt to tile.</summary>
    private static TableNode Random(Random random)
    {
        var rows = random.Next(1, 9);
        var wide = random.Next(10) == 0;

        return new TableNode(Enumerable.Range(0, rows).Select(_ =>
            new TableRowNode(Enumerable.Range(0, random.Next(0, 7)).Select(_ =>
                new TableCellNode(
                    [ParagraphNode.FromText("c")],
                    random.Next(3) == 0 ? random.Next(1, rows + 3) : 1,
                    wide ? random.Next(1, 40) : random.Next(3) == 0 ? random.Next(1, 4) : 1)))));
    }

    [Fact]
    public void Placing_agrees_with_the_slot_set_algorithm_on_3000_random_tables()
    {
        var random = new Random(20261028);
        var spanned = 0;

        for (var i = 0; i < 3_000; i++)
        {
            var table = Random(random);
            var expected = Oracle(table, out var expectedWidth);
            var actual = TableGeometry.Place(table, out var actualWidth);

            Assert.True(
                expected.SequenceEqual(actual) && expectedWidth == actualWidth,
                $"case {i}: expected width {expectedWidth} [{string.Join(" ", expected)}], "
                + $"got width {actualWidth} [{string.Join(" ", actual)}]");

            spanned += table.Rows.Any(r => r.Cells.Any(c => c.RowSpan > 1)) ? 1 : 0;
        }

        // Row spans are what make the per-column shortcut a claim; most cases must have some.
        Assert.True(spanned > 2_000, $"only {spanned} tables had a row span");
    }

    [Fact]
    public void Placing_a_large_table_allocates_the_placements_not_a_slot_set()
    {
        var table = new TableNode(Enumerable.Range(0, 10_000).Select(_ =>
            new TableRowNode(Enumerable.Range(0, 5).Select(_ => TableCellNode.FromText("cell")))));

        TableGeometry.Place(table, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var placements = TableGeometry.Place(table, out var width);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(5, width);
        Assert.Equal(50_000, placements.Length);

        // 50,000 placements of four ints are 800 KB; the slot set on top of them was 5.8 MB.
        Assert.True(allocated < 1_500_000, $"placing allocated {allocated / 1024} KB");
    }
}
