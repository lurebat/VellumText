using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// Field arguments and font names, which RTF quotes and terminates with characters a user's text
/// can contain (F-38, audit of 0282015).
/// </summary>
public class RtfFieldEscapingTests
{
    private static DocumentNode Linked(string href) =>
        new([new ParagraphNode(InlineContent.Create(
            "click",
            [new ValueSpan<MarkSet>(0, 5, new MarkSet { Link = new LinkMark(href) })],
            []))]);

    private static MarkSet MarkOfFirst(DocumentNode doc) =>
        Assert.IsType<ParagraphNode>(Assert.Single(doc.Blocks)).Content.MarkAt(0);

    private static async Task<DocumentNode> Trip(DocumentNode doc) =>
        (await RtfImporter.ImportAsync(RtfExporter.Instance.Export(doc))).Doc;

    /// <summary>
    /// A quote ended the HYPERLINK argument, so the link was dropped on the way back in — and an
    /// attacker's <c>" "javascript:…</c> became a second argument, which RtfPipe-based readers
    /// take as the target and Word accepts as further switches. A quote is percent-encoded, which
    /// is the same address to every URL parser.
    /// </summary>
    [Theory]
    [InlineData("https://ex.com/?q=\"x\"")]
    [InlineData("https://ok.example/\" \"javascript:alert(1)")]
    public async Task A_quote_in_a_link_target_cannot_end_the_argument(string href)
    {
        var rtf = RtfExporter.Instance.Export(Linked(href));

        Assert.DoesNotContain("\" \"javascript", rtf, StringComparison.Ordinal);

        var link = MarkOfFirst((await Trip(Linked(href)))).Link;

        Assert.NotNull(link);
        Assert.DoesNotContain("\"", link.Href, StringComparison.Ordinal);
        Assert.Equal(href, Uri.UnescapeDataString(link.Href));
    }

    /// <summary>
    /// A backslash is the field instruction's own escape character, so <c>a\b</c> came back as
    /// <c>ab</c> and a trailing one ate the closing quote. It is written escaped.
    /// </summary>
    [Theory]
    [InlineData("https://ex.com/a\\b")]
    [InlineData("https://ex.com/a\\")]
    public async Task A_backslash_in_a_link_target_survives(string href) =>
        Assert.Equal(href, MarkOfFirst((await Trip(Linked(href)))).Link?.Href);

    /// <summary>
    /// A semicolon ends a font table entry, so <c>Weird;Font</c> was read as the font "Weird"
    /// followed by stray text. It is removed from the name, which is the only form a font table can
    /// hold it in.
    /// </summary>
    [Fact]
    public async Task A_semicolon_in_a_font_name_does_not_end_the_entry()
    {
        var doc = new DocumentNode([new ParagraphNode(InlineContent.Create(
            "x",
            [new ValueSpan<MarkSet>(0, 1, new MarkSet { FontFamily = "Weird;Font" })],
            []))]);

        var rtf = RtfExporter.Instance.Export(doc);

        Assert.Contains(@"\fnil WeirdFont;}", rtf, StringComparison.Ordinal);

        var back = Assert.IsType<ParagraphNode>(Assert.Single((await Trip(doc)).Blocks));

        Assert.Equal("x", back.Content.Text);
        Assert.Equal("WeirdFont", back.Content.MarkAt(0).FontFamily);
    }
}
