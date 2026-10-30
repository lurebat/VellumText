using AngleSharp;
using AngleSharp.Html.Parser;
using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// Styles are sanitized once per distinct value and still sanitized (F-16, plan 7.6).
/// </summary>
/// <remarks>
/// The walker reads an allow-list of properties and validates their values, so a dangerous
/// declaration left in a style attribute would not reach the model today. The sanitizer's pass is
/// the layer that keeps it that way when the walker learns a new property, and this pins it: what
/// the document carries after sanitizing is what the sanitizer allowed, not what arrived.
/// </remarks>
public class StyleSanitizingTests
{
    private static AngleSharp.Dom.IDocument Sanitized(string html, out List<ImportDiagnostic> diagnostics)
    {
        // As the importer parses: without CSS support, so that styles are not parsed per element.
        var document = new HtmlParser(new HtmlParserOptions(), BrowsingContext.New(Configuration.Default))
            .ParseDocument(html);

        diagnostics = [];
        new SanitizingReader(HtmlImportOptions.Default, diagnostics).Sanitize(document);

        return document;
    }

    [Fact]
    public void A_style_comes_back_holding_only_what_the_sanitizer_allows()
    {
        var document = Sanitized(
            "<p style=\"color:red;position:absolute;behavior:url(x.htc)\">a</p>"
                + "<p style=\"color:red;position:absolute;behavior:url(x.htc)\">b</p>",
            out var diagnostics);

        foreach (var paragraph in document.QuerySelectorAll("p"))
        {
            var style = paragraph.GetAttribute("style") ?? string.Empty;

            Assert.Contains("color", style, StringComparison.Ordinal);
            Assert.DoesNotContain("position", style, StringComparison.Ordinal);
            Assert.DoesNotContain("behavior", style, StringComparison.Ordinal);
        }

        Assert.Contains(diagnostics, d => d.Context == "position");
    }

    /// <summary>
    /// An element the sanitizer removes takes its style with it: neither restored onto anything
    /// nor reported, since nothing that carried it is left to have lost it.
    /// </summary>
    [Fact]
    public void An_element_the_sanitizer_removes_takes_its_style_with_it()
    {
        var document = Sanitized("<foo style=\"color:red;position:absolute\"><span>kept</span></foo>", out var diagnostics);

        Assert.Equal("kept", document.Body!.TextContent);
        Assert.Empty(document.QuerySelectorAll("[style]"));
        Assert.DoesNotContain(diagnostics, d => d.Context == "position");
    }

    [Fact]
    public void A_style_with_nothing_allowed_left_is_removed()
    {
        var document = Sanitized("<p style=\"position:absolute\">a</p>", out _);

        Assert.Null(document.QuerySelector("p")!.GetAttribute("style"));
    }
}