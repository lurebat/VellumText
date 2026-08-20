using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// The guards on what a Markdown import is allowed to bring in.
/// </summary>
/// <remarks>
/// <para>
/// Markdown looks harmless and is not. A link destination is an arbitrary string, so
/// <c>[click](javascript:alert(1))</c> is a scriptable link written in four words; and CommonMark
/// requires raw HTML to be passed through, so any Markdown document can carry any HTML attack at
/// all.
/// </para>
/// <para>
/// Two of this library's three security defects were in exactly this area, both in the HTML
/// importer: a scheme allow-list implemented as <c>UriKind.Absolute</c>, which let
/// <c>//evil.test/steal</c> and <c>\\evil.test\share</c> through as <c>file:</c> links; and
/// <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> bodies arriving as visible text. Markdown reaches
/// both through the same code, on purpose, so both regressions are covered here as well as in the
/// HTML package's own tests — an import path is only as safe as the tests that watch it.
/// </para>
/// </remarks>
public class MarkdownSecurityTests
{
    private static ImportResult Import(string markdown) => MarkdownImporter.Import(markdown);

    private static ParagraphNode OnlyPara(ImportResult result) =>
        Assert.IsType<ParagraphNode>(Assert.Single(result.Doc.Blocks));

    private static string AllText(ImportResult result) =>
        string.Join("\n", Paragraphs(result.Doc.Blocks).Select(p => p.Content.Text));

    private static IEnumerable<ParagraphNode> Paragraphs(IEnumerable<BlockNode> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is ParagraphNode paragraph)
            {
                yield return paragraph;
            }

            foreach (var nested in Paragraphs(block.Children.OfType<BlockNode>()))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// The schemes a destination may not have, and the two forms that are dangerous precisely
    /// because they do not look like schemes: <c>System.Uri</c> reads <c>//host/path</c> as
    /// <c>file:</c>, which on Windows is an NTLM credential leak to whichever host is named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>\\host\share</c> row does not reach that mechanism and is not here for it. CommonMark
    /// collapses the two backslashes to one inside a link destination, so what arrives at the URL
    /// layer is <c>%5Cevil.test%5Cshare</c> — rejected as a relative destination with no base rather
    /// than as a UNC path. It is kept because <c>\\host\share</c> is what a host pastes and this
    /// pins that it does not become a link, not because of how.
    /// </para>
    /// <para>
    /// <c>JaVaScRiPt:</c> likewise pins observable behaviour rather than a mechanism: the URL parser
    /// lowercases the scheme before any allow-list sees it, so the comparer the allow-list uses is
    /// not what this row tests — mutating both of them to <see cref="StringComparison.Ordinal"/>
    /// leaves the suite green. What it does pin is that no future reader may compare the raw text.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("//evil.test/steal")]
    [InlineData(@"\\evil.test\share")]
    public void A_link_whose_scheme_is_not_allowed_loses_its_destination(string href)
    {
        var result = Import($"[click]({href})");
        var paragraph = OnlyPara(result);

        Assert.Equal("click", paragraph.Content.Text);
        Assert.Null(paragraph.Content.MarkAt(0).Link);
        Assert.True(result.AnythingDropped);
    }

    [Theory]
    [InlineData("https://example.test/a")]
    [InlineData("http://example.test/a")]
    [InlineData("mailto:someone@example.test")]
    [InlineData("tel:+441234567890")]
    public void A_link_whose_scheme_is_allowed_keeps_its_destination(string href)
    {
        var paragraph = OnlyPara(Import($"[click]({href})"));

        Assert.Equal(href, paragraph.Content.MarkAt(0).Link?.Href);
    }

    /// <summary>
    /// A relative destination has no meaning in the document being pasted into, so it goes unless
    /// the host says what it should be relative to.
    /// </summary>
    [Fact]
    public void A_relative_destination_is_resolved_only_against_a_base_the_host_supplied()
    {
        Assert.Null(OnlyPara(Import("[click](page.html)")).Content.MarkAt(0).Link);

        var resolved = MarkdownImporter.Import(
            "[click](page.html)",
            new MarkdownImportOptions { BaseUri = new Uri("https://example.test/dir/index.html") });

        Assert.Equal(
            "https://example.test/dir/page.html",
            OnlyPara(resolved).Content.MarkAt(0).Link?.Href);
    }

    /// <summary>
    /// The second of the two fixed defects: a script body is text as far as an HTML parser is
    /// concerned, so an importer that strips the tag and keeps the children pastes the script into
    /// the document as visible words.
    /// </summary>
    /// <remarks>
    /// Only the <c>&lt;div&gt;</c> row isolates the block guard. Script and style are caught twice
    /// over — here and again by the sanitizer that treats them as non-content — so their rows
    /// survive either guard failing on its own and go red only when both do. That is defence in
    /// depth working, and it is also why those rows cannot say which guard broke:
    /// <see cref="A_raw_inline_non_content_tag_is_removed_with_its_contents"/> is what isolates the
    /// sanitizer's half. The <c>&lt;iframe&gt;</c> row needs a forbidden string that is real text,
    /// because no import path turns an attribute into paragraph text and a row asserting the
    /// absence of one asserts nothing.
    /// </remarks>
    [Theory]
    [InlineData("<script>alert(1)</script>", "alert(1)")]
    [InlineData("<style>body{display:none}</style>", "display:none")]
    [InlineData("<div onclick=\"steal()\">bait</div>", "bait")]
    [InlineData("<iframe src=\"https://evil.test/\">fallback</iframe>", "fallback")]
    public void A_raw_html_block_is_removed_with_its_contents(string html, string forbidden)
    {
        var result = Import($"before\n\n{html}\n\nafter");

        Assert.DoesNotContain(forbidden, AllText(result), StringComparison.Ordinal);
        Assert.Contains("before", AllText(result), StringComparison.Ordinal);
        Assert.Contains("after", AllText(result), StringComparison.Ordinal);
        Assert.True(result.AnythingDropped);
    }

    /// <summary>
    /// The same two elements reached inline rather than as a block, which is the form the block
    /// guard above never sees.
    /// </summary>
    /// <remarks>
    /// Markdig splits <c>a &lt;script&gt;steal()&lt;/script&gt; b</c> into five inline nodes, of
    /// which only two are tags, so the rule that keeps a formatting tag's words — correct for
    /// <c>&lt;b&gt;</c> — hands a script body to the document as prose. That is the defect
    /// <c>c02d649</c> fixed in the HTML reader in August 2026, arriving through a second importer.
    /// The element list is the HTML package's, not a copy: a copy is what drifts.
    /// </remarks>
    [Theory]
    [InlineData("<script>steal()</script>", "steal()", "script")]
    [InlineData("<style>body{display:none}</style>", "display:none", "style")]
    [InlineData("<title>secret</title>", "secret", "title")]
    [InlineData("<noscript>fallback</noscript>", "fallback", "noscript")]
    public void A_raw_inline_non_content_tag_is_removed_with_its_contents(
        string html,
        string forbidden,
        string context)
    {
        var result = Import($"a {html} b");

        Assert.DoesNotContain(forbidden, AllText(result), StringComparison.Ordinal);
        Assert.Equal("a b", AllText(result));
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Dropped && d.Context == context);
    }

    /// <summary>
    /// Allowing raw HTML must not restore the inline form either — the sanitizer is the second of
    /// the two guards, and this is the case that asks it on its own.
    /// </summary>
    [Fact]
    public void Allowed_raw_inline_html_still_loses_its_script()
    {
        var result = MarkdownImporter.Import(
            "a <script>steal()</script> b",
            new MarkdownImportOptions { AllowRawHtml = true });

        Assert.DoesNotContain("steal()", AllText(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// An inline tag is a node of its own, separate from the literal text around it, so removing
    /// the tag keeps the words. Only the formatting the tag would have applied is lost.
    /// </summary>
    [Fact]
    public void A_raw_inline_tag_is_removed_but_its_text_is_kept()
    {
        var result = Import("a <b>bold</b> c");
        var paragraph = OnlyPara(result);

        Assert.Equal("a bold c", paragraph.Content.Text);
        Assert.Equal(TextStyle.None, paragraph.Content.MarkAt(2).Style);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == "b");
    }

    /// <summary>
    /// The two spaces either side of the removed comment collapse into one, which is HTML's
    /// whitespace rule rather than anything to do with the comment.
    /// </summary>
    [Fact]
    public void A_raw_html_comment_is_removed() =>
        Assert.Equal("a c", OnlyPara(Import("a <!-- secret --> c")).Content.Text);

    /// <summary>
    /// With raw HTML allowed the markup is read rather than discarded — and it is read by the same
    /// sanitizer every HTML import goes through, not by a second one written for Markdown.
    /// </summary>
    [Fact]
    public void Allowed_raw_html_is_read_through_the_html_sanitizer()
    {
        var result = MarkdownImporter.Import(
            "a <b>bold</b> c",
            new MarkdownImportOptions { AllowRawHtml = true });

        var paragraph = OnlyPara(result);

        Assert.Equal("a bold c", paragraph.Content.Text);
        Assert.Equal(TextStyle.Bold, paragraph.Content.MarkAt(2).Style);
    }

    /// <summary>
    /// Allowing raw HTML is not trusting it. This is the case that matters: a host that turns the
    /// option on to keep <c>&lt;br&gt;</c> and <c>&lt;sup&gt;</c> must not thereby accept script.
    /// </summary>
    [Theory]
    [InlineData("<script>alert(1)</script>", "alert(1)")]
    [InlineData("<style>body{display:none}</style>", "display:none")]
    public void Allowed_raw_html_still_loses_its_script_and_style(string html, string forbidden)
    {
        var result = MarkdownImporter.Import(
            $"before\n\n{html}\n\nafter",
            new MarkdownImportOptions { AllowRawHtml = true });

        Assert.DoesNotContain(forbidden, AllText(result), StringComparison.Ordinal);
        Assert.Contains("before", AllText(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Allowed_raw_html_still_loses_a_dangerous_link_scheme()
    {
        var result = MarkdownImporter.Import(
            "<a href=\"javascript:alert(1)\">click</a>",
            new MarkdownImportOptions { AllowRawHtml = true });

        Assert.Null(OnlyPara(result).Content.MarkAt(0).Link);
        Assert.True(result.AnythingDropped);
    }

    /// <summary>
    /// An image whose source is a remote URL is a tracking pixel: rendering one tells whoever wrote
    /// the document that it was opened, from where and when.
    /// </summary>
    [Fact]
    public void A_remote_image_needs_the_host_to_ask_for_it()
    {
        var result = Import("![](https://tracker.test/pixel.gif)");

        Assert.Empty(OnlyPara(result).Content.Embeds);
        Assert.True(result.AnythingDropped);
    }

    [Fact]
    public void The_defaults_are_the_paranoid_ones()
    {
        var options = MarkdownImportOptions.Default;

        Assert.False(options.AllowRemoteImages);
        Assert.False(options.AllowRawHtml);
        Assert.True(options.AllowDataImages);
        Assert.Null(options.BaseUri);
        Assert.Equal(8 * 1024 * 1024, options.MaxDataImageBytes);
    }
}
