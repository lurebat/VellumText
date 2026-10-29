using System.Diagnostics;

namespace VellumText.Interop.Html.Tests;

/// <summary>
/// Table spans multiply; the importer bounds the area its tables may cover.
/// </summary>
[Collection(VellumText.Tests.Interop.Html.ProcessWideAllocation.Name)]
public sealed class TableSlotBudgetTests
{
    private static async Task<(ImportResult Result, long Ms, long AllocatedMb)> Measure(string html, HtmlImportOptions? options = null)
    {
        // The import runs on its own thread, so the process-wide count is the one that sees it.
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var result = await HtmlImporter.ImportAsync(html, options);
        var ms = watch.ElapsedMilliseconds;
        var allocated = (GC.GetTotalAllocatedBytes(precise: true) - before) / (1024 * 1024);

        return (result, ms, allocated);
    }

    /// <summary>
    /// 1 KB of <c>&lt;td colspan=1000 rowspan=1000&gt;</c> on one row took 11.5 s and 1.27 GB,
    /// because placing the table marked every slot each span named.
    /// </summary>
    [Fact]
    public async Task Huge_spans_on_one_row_import_quickly()
    {
        var html = "<table><tr>" + string.Concat(Enumerable.Repeat("<td colspan=1000 rowspan=1000>x", 32)) + "</table>";

        await Measure(html);
        var (result, ms, allocated) = (await Measure(html));

        Assert.True(ms < 500, $"took {ms} ms");
        Assert.True(allocated < 100, $"allocated {allocated} MB");
        Assert.Equal(new string('x', 32), DocumentText.Of(result.Doc).Replace("\n", string.Empty, StringComparison.Ordinal));
    }

    /// <summary>
    /// With rows to span into, the same cells cover 32 million real slots; the budget turns the
    /// table into paragraphs before anything lays it out.
    /// </summary>
    [Fact]
    public async Task A_table_over_the_slot_budget_becomes_paragraphs_quickly()
    {
        var html = "<table><tr>"
            + string.Concat(Enumerable.Repeat("<td colspan=1000 rowspan=1000>x", 32))
            + string.Concat(Enumerable.Repeat("<tr>", 999))
            + "</table>";

        await Measure(html);
        var (result, ms, allocated) = (await Measure(html));

        Assert.True(ms < 1_000, $"took {ms} ms");
        Assert.True(allocated < 100, $"allocated {allocated} MB");
        Assert.DoesNotContain(result.Doc.Blocks, b => b is TableNode);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == "table" && d.Message.Contains("grid slots", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_slot_budget_is_honoured()
    {
        const string html = "<table><tr><td>a<td>b<tr><td>c<td>d</table>";

        Assert.IsType<TableNode>(Assert.Single((await HtmlImporter.ImportAsync(html)).Doc.Blocks));

        var limited = await HtmlImporter.ImportAsync(html, HtmlImportOptions.Default with { MaxTableSlots = 3 });

        Assert.DoesNotContain(limited.Doc.Blocks, b => b is TableNode);
        Assert.Equal("a\nb\nc\nd", DocumentText.Of(limited.Doc));
    }

    [Fact]
    public async Task The_budget_is_shared_by_every_table_in_one_import()
    {
        const string table = "<table><tr><td>a<td>b<tr><td>c<td>d</table>";

        var result = await HtmlImporter.ImportAsync(table + table, HtmlImportOptions.Default with { MaxTableSlots = 6 });

        Assert.Single(result.Doc.Blocks.OfType<TableNode>());
        Assert.IsType<TableNode>(result.Doc.Blocks[0]);
    }
}
