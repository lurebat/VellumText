using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Html.Parser;
using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// Font family names through HTML, judged by how a CSS-honouring recipient reads the exported
/// <c>style</c> attribute (F-28, audit of 0282015).
/// </summary>
/// <remarks>
/// The recipient here is AngleSharp's own CSS parser, standing in for a browser or a mail composer:
/// the question is not what the exporter meant to write but what a reader of it ends up with.
/// </remarks>
public class CssFontFamilyTests
{
    private static string? FamilyOf(DocumentNode doc) =>
        doc.Blocks.OfType<ParagraphNode>().First().Content.MarkAt(0).FontFamily;

    private static DocumentNode WithFamily(string family) =>
        new([new ParagraphNode(InlineContent.Create(
            "t",
            [new ValueSpan<MarkSet>(0, 1, new MarkSet { FontFamily = family })],
            []))]);

    /// <summary>The declaration a recipient parses out of the exported span's style.</summary>
    private static ICssStyleDeclaration RecipientStyle(string html)
    {
        var parsed = new HtmlParser().ParseDocument(html);
        var style = parsed.QuerySelector("span")!.GetAttribute("style")!;

        return new CssParser().ParseDeclaration(style)!;
    }

    /// <summary>
    /// The audit's reproduction. The sanitizer removed the <c>background-image</c>, but the
    /// importer kept the CSS escapes in the family name raw, and the exporter escaped the quote
    /// and not the backslash — so <c>\"</c> came out as an escaped backslash followed by a quote
    /// that closed the string, and the tracking fetch was live again in whatever read the copy.
    /// </summary>
    [Fact]
    public void An_imported_font_family_cannot_inject_a_css_declaration_on_export()
    {
        const string Css =
            "font-family:&quot;x\\&quot; ;background-image:url(http://tracker.invalid/p.png);&quot;";

        var imported = HtmlImporter.Import($"<p><span style=\"{Css}\">t</span></p>").Doc;

        // What the CSS actually says the family is: the escapes resolved, nothing else.
        Assert.Equal("x\" ;background-image:url(http://tracker.invalid/p.png);", FamilyOf(imported));

        var html = HtmlExporter.Instance.Export(imported);
        var style = RecipientStyle(html);

        Assert.True(
            string.IsNullOrEmpty(style.GetPropertyValue("background-image")),
            $"recipient read background-image from: {html}");
        Assert.Equal(FamilyOf(imported), FamilyOf(HtmlImporter.Import(html).Doc));
    }

    [Theory]
    [InlineData("a\\b")]
    [InlineData("a\"b")]
    [InlineData("a\\\"b\\")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    [InlineData("Segoe UI")]
    [InlineData("Arial")]
    public void A_family_name_comes_back_as_itself(string family)
    {
        var html = HtmlExporter.Instance.Export(WithFamily(family));

        Assert.Equal(family, FamilyOf(HtmlImporter.Import(html).Doc));
        Assert.Single(RecipientStyle(html), p => p.Name == "font-family");
    }

    /// <summary>
    /// A family the host set as a list of fallbacks stays a list, each name written as a name: a
    /// recipient sees two families, not one long one.
    /// </summary>
    [Fact]
    public void A_list_of_fallbacks_stays_a_list()
    {
        var html = HtmlExporter.Instance.Export(WithFamily("Segoe UI, Arial"));
        var property = RecipientStyle(html).Single(p => p.Name == "font-family");
        var items = ((System.Collections.IEnumerable)property.RawValue!).Cast<ICssValue>().ToArray();

        Assert.Equal(2, items.Length);
        Assert.Contains("Segoe UI", items[0].CssText, StringComparison.Ordinal);
        Assert.Contains("Arial", items[1].CssText, StringComparison.Ordinal);
    }
}
