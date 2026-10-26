using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// What a Markdown import is supposed to keep: the structure and formatting a person would say
/// they had written.
/// </summary>
/// <remarks>
/// Anchored on the model rather than on a round trip. The importer and the exporter can agree with
/// each other while both being wrong about CommonMark, and a test that only asks whether the pair
/// is self-consistent would never notice.
/// </remarks>
public class MarkdownImportTests
{
    private const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static async Task<ImportResult> Import(string markdown) => await MarkdownImporter.ImportAsync(markdown);

    private static ParagraphNode OnlyPara(ImportResult result) =>
        Assert.IsType<ParagraphNode>(Assert.Single(result.Doc.Blocks));

    [Theory]
    [InlineData("# one", ParagraphKind.Heading1)]
    [InlineData("## one", ParagraphKind.Heading2)]
    [InlineData("### one", ParagraphKind.Heading3)]
    [InlineData("#### one", ParagraphKind.Heading4)]
    [InlineData("##### one", ParagraphKind.Heading5)]
    [InlineData("###### one", ParagraphKind.Heading6)]
    public async Task A_hash_run_is_a_heading(string markdown, ParagraphKind expected)
    {
        var p = OnlyPara(await Import(markdown));

        Assert.Equal("one", p.Content.Text);
        Assert.Equal(expected, p.Kind);
    }

    /// <summary>A setext heading is the other notation, and means the same thing.</summary>
    [Theory]
    [InlineData("one\n===", ParagraphKind.Heading1)]
    [InlineData("one\n---", ParagraphKind.Heading2)]
    public async Task An_underlined_line_is_a_heading(string markdown, ParagraphKind expected) =>
        Assert.Equal(expected, OnlyPara(await Import(markdown)).Kind);

    [Theory]
    [InlineData("**x**", TextStyle.Bold)]
    [InlineData("__x__", TextStyle.Bold)]
    [InlineData("*x*", TextStyle.Italic)]
    [InlineData("_x_", TextStyle.Italic)]
    [InlineData("`x`", TextStyle.Code)]
    [InlineData("~~x~~", TextStyle.Strikethrough)]
    [InlineData("***x***", TextStyle.Bold | TextStyle.Italic)]
    public async Task Emphasis_carries_its_formatting_onto_the_text(string markdown, TextStyle expected)
    {
        var p = OnlyPara(await Import(markdown));

        Assert.Equal("x", p.Content.Text);
        Assert.Equal(expected, p.Content.MarkAt(0).Style);
    }

    [Fact]
    public async Task An_escaped_delimiter_is_text_rather_than_formatting()
    {
        var p = OnlyPara(await Import(@"a \*b\* c"));

        Assert.Equal("a *b* c", p.Content.Text);
        Assert.Equal(TextStyle.None, p.Content.MarkAt(2).Style);
    }

    [Fact]
    public async Task A_link_keeps_its_destination_and_title()
    {
        var p = OnlyPara(await Import("[text](https://example.test/a \"tip\")"));

        Assert.Equal("text", p.Content.Text);
        Assert.Equal(
            new LinkMark("https://example.test/a", "tip"),
            p.Content.MarkAt(0).Link);
    }

    [Fact]
    public async Task An_autolink_keeps_its_destination()
    {
        var p = OnlyPara(await Import("<https://example.test/a>"));

        Assert.Equal("https://example.test/a", p.Content.Text);
        Assert.Equal(
            new LinkMark("https://example.test/a"),
            p.Content.MarkAt(0).Link);
    }

    [Fact]
    public async Task An_embedded_image_becomes_an_embed()
    {
        var p = OnlyPara(await Import($"see ![a picture]({Png})"));
        var embed = Assert.IsType<ImageEmbed>(Assert.Single(p.Content.Embeds));

        Assert.Equal(Png, embed.Source);
        Assert.Equal("a picture", embed.AltText);
    }

    [Fact]
    public async Task A_quote_becomes_a_quote_paragraph()
    {
        var p = OnlyPara(await Import("> quoted\n> more"));

        Assert.Equal(ParagraphKind.Quote, p.Kind);
        Assert.Equal("quoted more", p.Content.Text);
    }

    [Theory]
    [InlineData("```\nvar x = 1;\n```")]
    [InlineData("    var x = 1;")]
    public async Task A_code_block_becomes_a_code_paragraph(string markdown)
    {
        var p = OnlyPara(await Import(markdown));

        Assert.Equal(ParagraphKind.Code, p.Kind);
        Assert.Equal("var x = 1;\n", p.Content.Text);
    }

    [Fact]
    public async Task A_thematic_break_becomes_a_rule() =>
        Assert.IsType<RuleNode>(Assert.Single((await Import("---")).Doc.Blocks));

    [Fact]
    public async Task A_bulleted_list_becomes_an_unordered_list()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import("- a\n- b")).Doc.Blocks));

        Assert.Equal(ListKind.Unordered, list.Kind);
        Assert.Equal(2, list.Items.Length);
        Assert.Equal("a", Assert.IsType<ParagraphNode>(list.Items[0].Blocks[0]).Content.Text);
        Assert.Equal("b", Assert.IsType<ParagraphNode>(list.Items[1].Blocks[0]).Content.Text);
    }

    [Fact]
    public async Task A_numbered_list_keeps_the_number_it_started_at()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import("3. three\n4. four")).Doc.Blocks));

        Assert.Equal(ListKind.Ordered, list.Kind);
        Assert.Equal(3, list.Start);
    }

    [Fact]
    public async Task An_indented_list_nests_inside_its_item()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import("- a\n- b\n  - b1")).Doc.Blocks));
        var nested = Assert.IsType<ListNode>(list.Items[1].Blocks[1]);

        Assert.Equal("b1", Assert.IsType<ParagraphNode>(nested.Items[0].Blocks[0]).Content.Text);
    }

    [Fact]
    public async Task A_pipe_table_becomes_a_table()
    {
        var table = Assert.IsType<TableNode>(
            Assert.Single((await Import("| a | b |\n|---|---|\n| 1 | 2 |")).Doc.Blocks));

        Assert.Equal(2, table.Rows.Length);
        Assert.All(table.Rows[0].Cells, c => Assert.True(c.IsHeader));
        Assert.All(table.Rows[1].Cells, c => Assert.False(c.IsHeader));
        Assert.Equal(
            ["a", "b", "1", "2"],
            table.Rows
                .SelectMany(r => r.Cells)
                .Select(c => ((ParagraphNode)c.Blocks[0]).Content.Text)
                .ToArray());
    }

    /// <summary>
    /// An empty row is exactly the case an earlier bug in this library dropped, and the round-trip
    /// tests that were supposed to catch it asserted only that the result was well formed.
    /// </summary>
    [Fact]
    public async Task A_table_row_of_empty_cells_is_still_a_row()
    {
        var table = Assert.IsType<TableNode>(
            Assert.Single((await Import("| a | b |\n|---|---|\n|  |  |")).Doc.Blocks));

        Assert.Equal(2, table.Rows.Length);
        Assert.Equal(2, table.Rows[1].Cells.Length);
        Assert.All(
            table.Rows[1].Cells,
            c => Assert.Equal(string.Empty, ((ParagraphNode)c.Blocks[0]).Content.Text));
    }

    [Fact]
    public async Task A_hard_break_becomes_a_newline_inside_the_paragraph() =>
        Assert.Equal("one\ntwo", OnlyPara(await Import("one\\\ntwo")).Content.Text);

    [Fact]
    public async Task A_soft_break_joins_the_lines() =>
        Assert.Equal("one two", OnlyPara(await Import("one\ntwo")).Content.Text);

    [Fact]
    public async Task An_empty_source_imports_to_an_empty_document() =>
        Assert.True((await Import(string.Empty)).IsEmpty);

    [Fact]
    public async Task Import_rejects_a_null_source() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => MarkdownImporter.ImportAsync(null!));

    // ---- diagnostics -------------------------------------------------------------------------

    /// <summary>
    /// The model has no place to record a language, and the caller should hear about it from a
    /// message that names the language rather than from the sanitizer complaining about a class
    /// attribute it has never heard of.
    /// </summary>
    [Fact]
    public async Task A_code_fence_that_names_a_language_reports_it()
    {
        var result = await Import("```csharp\nvar x = 1;\n```");

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(DiagnosticSeverity.Downgraded, diagnostic.Severity);
        Assert.Equal("csharp", diagnostic.Context);
        Assert.Equal(ParagraphKind.Code, OnlyPara(result).Kind);
    }

    [Theory]
    [InlineData("|:--|", "left")]
    [InlineData("|--:|", "right")]
    [InlineData("|:-:|", "center")]
    public async Task A_table_column_alignment_is_reported_because_it_is_not_kept(
        string delimiter,
        string expected)
    {
        var result = await Import($"| a |\n{delimiter}\n| 1 |");

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(DiagnosticSeverity.Downgraded, diagnostic.Severity);
        Assert.Equal(expected, diagnostic.Context);
    }

    [Fact]
    public async Task An_unaligned_table_reports_nothing() =>
        Assert.Empty((await Import("| a |\n|---|\n| 1 |")).Diagnostics);

    /// <summary>
    /// A document's worth of the same removal would otherwise produce a list no host could show.
    /// </summary>
    [Fact]
    public async Task The_same_kind_of_loss_is_reported_once()
    {
        var result = await Import("```csharp\na\n```\n\n```csharp\nb\n```\n");

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task A_remote_image_is_dropped_by_default()
    {
        var result = await Import("![alt](https://example.test/i.png)");

        Assert.Empty(OnlyPara(result).Content.Embeds);
        Assert.True(result.AnythingDropped);
    }

    [Fact]
    public async Task A_remote_image_survives_when_the_host_allows_it()
    {
        var result = await MarkdownImporter.ImportAsync(
            "see ![alt](https://example.test/i.png)",
            new MarkdownImportOptions { AllowRemoteImages = true });

        var embed = Assert.IsType<ImageEmbed>(Assert.Single(OnlyPara(result).Content.Embeds));

        Assert.Equal("https://example.test/i.png", embed.Source);
    }
}
