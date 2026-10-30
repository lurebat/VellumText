using VellumText;
using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// Excel's <c>CF_HTML</c>, captured from a live copy. Excel opens the fragment after
/// <c>&lt;table&gt;</c> and before <c>&lt;col&gt;</c>, so the marked fragment alone is rows and
/// cells with no table around them, and parsed on its own it flattened a pasted range into one
/// paragraph of every cell's text.
/// </summary>
public class ExcelClipboardTests
{
    private static async Task<ImportResult> ImportFixture(string capture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", capture, "10-HTML_Format.bin");

        using var stream = File.OpenRead(path);

        return await HtmlFormat.Instance.ImportAsync(stream);
    }

    private static string Text(TableCellNode cell) =>
        string.Join("\n", cell.Blocks.Select(block => Assert.IsType<ParagraphNode>(block).Content.Text));

    private static string[][] Grid(TableNode table) =>
        [.. table.Rows.Select(row => row.Cells.Select(Text).ToArray())];

    private static string Header() =>
        "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\n"
        + "StartFragment:0000000000\r\nEndFragment:0000000000\r\n";

    [Fact]
    public async Task A_copied_range_pastes_as_a_six_by_four_table()
    {
        var result = await ImportFixture("excel-range-A1D6");
        var table = Assert.IsType<TableNode>(Assert.Single(result.Doc.Blocks));

        TableGeometry.Place(table, out var width);

        Assert.Equal(4, width);
        Assert.Equal(
            [
                ["Name", "Qty", "Price", "Note"],
                ["Apples", "12", "1.5", "fresh"],
                ["Pears", "7", "2.25", "old"],
                ["Plums", "30", "0.8", "24"],
                ["", "", "", ""],
                ["Merged A6:B6", "", ""],
            ],
            Grid(table));
        Assert.Equal(2, table.Rows[5].Cells[0].ColumnSpan);

        // The page around the fragment (Excel's head, and its html and body attributes) is not
        // part of the paste, so nothing in it is reported as removed.
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Context is "xmlns" or "xmlns:x" or "link" or "vlink");
    }

    [Fact]
    public async Task A_range_away_from_the_corner_pastes_as_a_table()
    {
        var table = Assert.IsType<TableNode>(Assert.Single((await ImportFixture("excel-range-B2C3")).Doc.Blocks));

        Assert.Equal([["12", "1.5"], ["7", "2.25"]], Grid(table));
    }

    [Fact]
    public async Task A_single_cell_pastes_as_its_text()
    {
        // Excel marks a single cell differently: the fragment opens inside the row and holds only
        // the cell, and its RTF flavour of the same copy is a plain paragraph. Both agree.
        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single((await ImportFixture("excel-cell-A2")).Doc.Blocks));

        Assert.Equal("Apples", paragraph.Content.Text);
    }

    [Fact]
    public async Task Only_the_rows_inside_the_fragment_are_pasted()
    {
        var payload = Header()
            + "<html lang=en><head><link rel=stylesheet href='https://example.com/x.css'></head>"
            + "<body bgcolor=red><p onclick='x()'>before</p><table><tr><td>out</td></tr>"
            + "<!--StartFragment--><tr><td>in</td><td>too</td></tr><!--EndFragment-->"
            + "<tr><td>after</td></tr></table><img src='https://example.com/a.png'></body></html>";

        var result = await HtmlImporter.ImportClipboardAsync(payload, cancellationToken: TestContext.Current.CancellationToken);
        var table = Assert.IsType<TableNode>(Assert.Single(result.Doc.Blocks));

        Assert.Equal([["in", "too"]], Grid(table));

        // What was cut away is gone before the sanitizer looks, so none of it is reported.
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task A_fragment_with_no_end_marker_keeps_everything_after_its_start()
    {
        var payload = Header()
            + "<html><body><p>before</p><table><tr><td>out</td></tr>"
            + "<!--StartFragment--><tr><td>in</td></tr></table><p>tail</p></body></html>";

        var blocks = (await HtmlImporter.ImportClipboardAsync(payload, cancellationToken: TestContext.Current.CancellationToken)).Doc.Blocks;

        Assert.Equal(2, blocks.Length);
        Assert.Equal([["in"]], Grid(Assert.IsType<TableNode>(blocks[0])));
        Assert.Equal("tail", Assert.IsType<ParagraphNode>(blocks[1]).Content.Text);
    }

    /// <summary>
    /// The fragment comments are found in the text, but in a script or a style the parser reads
    /// them as that element's text, not as comments. With no marker to clip at, the whole page
    /// the fragment was cut from was imported instead of the fragment (rubber-duck review 4.1).
    /// </summary>
    [Theory]
    [InlineData(
        "<p a=1 b=2 c=3>OUTSIDE-before</p>\r\n<script><!--StartFragment--><tr><td>copied</td></tr><!--EndFragment--></script>\r\n"
        + "<p>OUTSIDE-after</p>")]
    [InlineData(
        "<p a=1 b=2 c=3>OUTSIDE-before</p><table><!--StartFragment--><tr><td>copied</td></tr><style><!--EndFragment--></style>"
        + "</table><p>OUTSIDE-after</p>")]
    public async Task Markers_the_parser_does_not_see_fall_back_to_the_fragment(string body)
    {
        // An attribute limit the page before the fragment breaks, which is not the paste's to report.
        var result = await HtmlImporter.ImportClipboardAsync(
            Header() + "<html><body>" + body + "</body></html>",
            HtmlImportOptions.Default with { MaxAttributesPerElement = 2 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("OUTSIDE", DocumentText.Of(result.Doc), StringComparison.Ordinal);
        Assert.Equal("copied", Assert.IsType<ParagraphNode>(Assert.Single(result.Doc.Blocks)).Content.Text);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Context == "attributes");
    }

    [Fact]
    public async Task Markers_inside_the_table_still_clip_in_its_context()
    {
        var payload = Header()
            + "<html><body><p>OUTSIDE-before</p><table><!--StartFragment--><tr><td>copied</td></tr>"
            + "<!--EndFragment--></table><p>OUTSIDE-after</p></body></html>";

        var table = Assert.IsType<TableNode>(Assert.Single((await HtmlImporter.ImportClipboardAsync(payload, cancellationToken: TestContext.Current.CancellationToken)).Doc.Blocks));

        Assert.Equal([["copied"]], Grid(table));
    }

    [Fact]
    public async Task A_fragment_outside_a_table_is_read_on_its_own_as_before()
    {
        var payload = Header()
            + "<html><body><table><tr><td>page</td></tr></table>"
            + "<!--StartFragment--><p>copied</p><!--EndFragment--></body></html>";

        var paragraph = Assert.IsType<ParagraphNode>(
            Assert.Single((await HtmlImporter.ImportClipboardAsync(payload, cancellationToken: TestContext.Current.CancellationToken)).Doc.Blocks));

        Assert.Equal("copied", paragraph.Content.Text);
    }
}
