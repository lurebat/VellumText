namespace VellumText.Core.Tests;

/// <summary>
/// The cost of placing a table is bounded by the grid that exists, not by the spans its cells
/// declare.
/// </summary>
public sealed class TableGeometryCostTests
{
    private static TableCellNode Cell(int rowSpan, int columnSpan) =>
        new([new ParagraphNode(InlineContent.FromText("x"))], rowSpan, columnSpan);

    /// <summary>
    /// A row span reaching past the last row used to mark every slot it named: 32 cells of
    /// 1000×1000 on one row were 32 million slots — 11.5 s and 1.27 GB from 1 KB of HTML.
    /// </summary>
    [Fact]
    public void A_row_span_past_the_last_row_costs_only_the_rows_that_exist()
    {
        var table = new TableNode([new TableRowNode(Enumerable.Range(0, 32).Select(_ => Cell(1000, 1000)))]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var placements = TableGeometry.Place(table, out var width);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(32_000, width);
        Assert.True(allocated < 50L * 1024 * 1024, $"placing allocated {allocated / (1024 * 1024)} MB");

        // The placements still say what the cells declared, so the schema can still report a
        // span that hangs off the bottom.
        Assert.All(placements, p => Assert.Equal(1000, p.Bottom));
        Assert.NotEmpty(DocumentSchema.Validate(new DocumentNode([table])));
    }

    [Fact]
    public void Placements_are_unchanged_where_spans_fit()
    {
        var table = new TableNode(
        [
            new TableRowNode([Cell(2, 1), Cell(1, 2)]),
            new TableRowNode([Cell(1, 1), Cell(1, 1)]),
        ]);

        var placements = TableGeometry.Place(table, out var width);

        Assert.Equal(3, width);
        Assert.Equal(
            [
                new CellPlacement(0, 0, 2, 1),
                new CellPlacement(0, 1, 1, 3),
                new CellPlacement(1, 1, 2, 2),
                new CellPlacement(1, 2, 2, 3),
            ],
            placements.ToArray());
    }
}
