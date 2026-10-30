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
    public async Task An_imported_font_family_cannot_inject_a_css_declaration_on_export()
    {
        const string Css =
            "font-family:&quot;x\\&quot; ;background-image:url(http://tracker.invalid/p.png);&quot;";

        var imported = (await HtmlImporter.ImportAsync($"<p><span style=\"{Css}\">t</span></p>")).Doc;

        // What the CSS actually says the family is: the escapes resolved, nothing else.
        Assert.Equal("x\" ;background-image:url(http://tracker.invalid/p.png);", FamilyOf(imported));

        var html = HtmlExporter.Instance.Export(imported);
        var style = RecipientStyle(html);

        Assert.True(
            string.IsNullOrEmpty(style.GetPropertyValue("background-image")),
            $"recipient read background-image from: {html}");
        Assert.Equal(FamilyOf(imported), FamilyOf((await HtmlImporter.ImportAsync(html)).Doc));
    }

    [Theory]
    [InlineData("a\\b")]
    [InlineData("a\"b")]
    [InlineData("a\\\"b\\")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    [InlineData("Segoe UI")]
    [InlineData("Arial")]
    public async Task A_family_name_comes_back_as_itself(string family)
    {
        var html = HtmlExporter.Instance.Export(WithFamily(family));

        Assert.Equal(family, FamilyOf((await HtmlImporter.ImportAsync(html)).Doc));
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

    /// <summary>
    /// A comma inside a name the host quoted belongs to the name (rubber-duck review finding 5.3).
    /// Splitting on every comma wrote <c>"\"ACME", "Inc.\"", Arial</c>, which a recipient reads as
    /// three families, the first of them <c>"ACME</c>.
    /// </summary>
    [Theory]
    [InlineData("\"ACME, Inc.\", Arial")]
    [InlineData("'ACME, Inc.', Arial")]
    [InlineData("  \"ACME, Inc.\" ,Arial,")]
    public async Task A_comma_inside_a_quoted_family_name_stays_in_the_name(string family)
    {
        var html = HtmlExporter.Instance.Export(WithFamily(family));

        Assert.Equal(["ACME, Inc.", "Arial"], RecipientFamilies(html));

        // Stored quoted, because the name holds a comma: the one family ACME, Inc.
        Assert.Equal("\"ACME, Inc.\"", FamilyOf((await HtmlImporter.ImportAsync(html)).Doc));
    }

    /// <summary>An escaped quote does not close the name it is in, so the comma after it does not split.</summary>
    [Fact]
    public void An_escaped_quote_does_not_end_a_quoted_family_name()
    {
        var html = HtmlExporter.Instance.Export(WithFamily("\"ACME \\\", Inc.\", Arial"));
        var families = RecipientFamilies(html);

        Assert.Equal(2, families.Length);
        Assert.EndsWith(", Inc.", families[0], StringComparison.Ordinal);
        Assert.Equal("Arial", families[1]);
    }

    /// <summary>
    /// A quote that never closes is a character of the name, not the start of one that swallows
    /// the rest of the stack.
    /// </summary>
    [Fact]
    public void An_unclosed_quote_does_not_swallow_the_rest_of_the_stack()
    {
        var html = HtmlExporter.Instance.Export(WithFamily("\"ACME, Arial"));

        Assert.Equal(["\"ACME", "Arial"], RecipientFamilies(html));
    }

    /// <summary>
    /// A name the CSS quoted because it holds a comma is stored quoted, so the exporter reads it back
    /// as one family. Stored bare, <c>ACME, Inc.</c> went back out as the two families <c>ACME</c>
    /// and <c>Inc.</c>.
    /// </summary>
    [Theory]
    [InlineData("<span style='font-family:\"ACME, Inc.\", Arial'>x</span>", "ACME, Inc.")]
    [InlineData("<span style='font-family:\"  ACME, Inc. \"'>x</span>", "ACME, Inc.")]
    [InlineData("<span style=\"font-family:'A&quot;B, C'\">x</span>", "A\"B, C")]
    [InlineData("<span style='font-family:\"A\\\\B, C\"'>x</span>", "A\\B, C")]
    public async Task An_imported_family_name_with_a_comma_stays_one_family_through_re_export(
        string html,
        string name)
    {
        var imported = (await HtmlImporter.ImportAsync(html)).Doc;
        var exported = HtmlExporter.Instance.Export(imported);

        Assert.Equal([name], RecipientFamilies(exported));

        var reimported = (await HtmlImporter.ImportAsync(exported)).Doc;

        Assert.Equal(FamilyOf(imported), FamilyOf(reimported));
        Assert.Equal([name], RecipientFamilies(HtmlExporter.Instance.Export(reimported)));
    }

    /// <summary>A name without a comma is stored exactly as before: the quoting is for commas only.</summary>
    [Theory]
    [InlineData("font-family:\"Segoe UI\", Arial", "Segoe UI")]
    [InlineData("font-family:Arial", "Arial")]
    [InlineData("font-family:'a\"b'", "a\"b")]
    [InlineData("font-family:monospace", "Consolas")]
    public async Task An_imported_family_name_without_a_comma_is_stored_bare(string css, string family)
    {
        var html = $"<span style='{css.Replace("'", "&#39;", StringComparison.Ordinal)}'>x</span>";

        Assert.Equal(family, FamilyOf((await HtmlImporter.ImportAsync(html)).Doc));
    }

    /// <summary>
    /// A name holding a comma and both kinds of quote, or ending in the backslash that would escape
    /// the closing quote, has no quoted form the exporter would read back as written, so it is
    /// stored bare, as it always was.
    /// </summary>
    [Theory]
    [InlineData("<span style='font-family:\"A\\\"B&#39;, C\"'>x</span>", "A\"B', C")]
    [InlineData("<span style='font-family:\"ACME, Inc.\\\\\"'>x</span>", "ACME, Inc.\\")]
    public async Task A_comma_name_no_quote_can_hold_is_stored_bare(string html, string family) =>
        Assert.Equal(family, FamilyOf((await HtmlImporter.ImportAsync(html)).Doc));

    /// <summary>The family names a recipient reads out of the exported span's style, in order.</summary>
    private static string[] RecipientFamilies(string html)
    {
        var property = RecipientStyle(html).Single(p => p.Name == "font-family");

        return ((System.Collections.IEnumerable)property.RawValue!)
            .Cast<ICssValue>()
            .Select(value => value switch
            {
                AngleSharp.Css.Values.CssStringValue quoted => quoted.Value,
                AngleSharp.Css.Values.CssIdentifierValue bare => bare.Value,
                _ => value.CssText,
            })
            .ToArray();
    }
}
