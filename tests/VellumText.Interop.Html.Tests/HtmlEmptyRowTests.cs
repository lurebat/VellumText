using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// A table whose merge leaves a row carrying no cells of its own.
/// </summary>
/// <remarks>
/// <para>
/// Merging a whole rectangle produces one, and until 2026-08-18 the schema refused it, so this
/// exporter had never written a <c>&lt;tr&gt;</c> with nothing in it and this importer had never
/// had to read one. It dropped it, which left the merged cell's row span hanging past the last
/// row — a ragged table, so the whole thing downgraded to plain paragraphs.
/// </para>
/// <para>
/// The first version of this test asserted only that what came back was schema-valid. A document
/// of one paragraph is schema-valid, so it passed while the table was being lost. Asserting the
/// shape is what found it.
/// </para>
/// </remarks>
public class HtmlEmptyRowTests
{
    [Fact]
    public void ARowCoveredEntirelyByAMergeSurvivesTheRoundTrip()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode(
                        [ParagraphNode.FromText("merged")], rowSpan: 2, columnSpan: 2),
                ]),
                new TableRowNode([]),
            ]),
        ]);

        var html = HtmlFormat.Instance.Export(doc);

        Assert.Contains("rowspan=\"2\"", html, StringComparison.Ordinal);

        var back = HtmlFormat.Instance.Import(html);
        var table = Assert.IsType<TableNode>(Assert.Single(back.Doc.Blocks));

        Assert.Equal(2, table.Rows.Length);
        Assert.Empty(table.Rows[1].Cells);
        Assert.Equal(2, table.Rows[0].Cells[0].RowSpan);
        Assert.Equal(2, table.Rows[0].Cells[0].ColumnSpan);
        Assert.True(
            DocumentSchema.IsValid(back.Doc),
            string.Join("; ", DocumentSchema.Validate(back.Doc)));
    }

    [Fact]
    public void AStrayEmptyRowNothingCoversIsStillDropped()
    {
        // What arrives from a word processor. Keeping it would make the table ragged, and a
        // ragged table loses its grid entirely, so the row goes and the table survives.
        var back = HtmlFormat.Instance.Import(
            "<table><tr><td>a</td><td>b</td></tr><tr></tr><tr><td>c</td><td>d</td></tr></table>");

        var table = Assert.IsType<TableNode>(Assert.Single(back.Doc.Blocks));

        Assert.Equal(2, table.Rows.Length);
        Assert.All(table.Rows, row => Assert.Equal(2, row.Cells.Length));
    }

    [Fact]
    public void ATableOfNothingButEmptyRowsIsNotATable()
    {
        var back = HtmlFormat.Instance.Import("<table><tr></tr><tr></tr></table>");

        Assert.DoesNotContain(back.Doc.Blocks, block => block is TableNode);
        Assert.True(
            DocumentSchema.IsValid(back.Doc),
            string.Join("; ", DocumentSchema.Validate(back.Doc)));
    }
}
