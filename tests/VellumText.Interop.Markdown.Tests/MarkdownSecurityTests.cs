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
    private static async Task<ImportResult> Import(string markdown) => await MarkdownImporter.ImportAsync(markdown);

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
    public async Task A_link_whose_scheme_is_not_allowed_loses_its_destination(string href)
    {
        var result = await Import($"[click]({href})");
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
    public async Task A_link_whose_scheme_is_allowed_keeps_its_destination(string href)
    {
        var paragraph = OnlyPara(await Import($"[click]({href})"));

        Assert.Equal(href, paragraph.Content.MarkAt(0).Link?.Href);
    }

    /// <summary>
    /// A relative destination has no meaning in the document being pasted into, so it goes unless
    /// the host says what it should be relative to.
    /// </summary>
    [Fact]
    public async Task A_relative_destination_is_resolved_only_against_a_base_the_host_supplied()
    {
        Assert.Null(OnlyPara(await Import("[click](page.html)")).Content.MarkAt(0).Link);

        var resolved = await MarkdownImporter.ImportAsync(
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
    /// <see cref="A_non_content_body_is_never_text_however_the_document_splits_it"/> is what isolates
    /// them, because its two settings run the same input through one layer or through both. The
    /// <c>&lt;iframe&gt;</c> row needs a forbidden string that is real text,
    /// because no import path turns an attribute into paragraph text and a row asserting the
    /// absence of one asserts nothing.
    /// </remarks>
    [Theory]
    [InlineData("<script>alert(1)</script>", "alert(1)")]
    [InlineData("<style>body{display:none}</style>", "display:none")]
    [InlineData("<div onclick=\"steal()\">bait</div>", "bait")]
    [InlineData("<iframe src=\"https://evil.test/\">fallback</iframe>", "fallback")]
    public async Task A_raw_html_block_is_removed_with_its_contents(string html, string forbidden)
    {
        var result = await Import($"before\n\n{html}\n\nafter");

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
    /// These four are the single-container shape, where the tags and the body are siblings; see
    /// <see cref="A_non_content_body_is_never_text_however_the_document_splits_it"/> for the shapes
    /// where they are not, which is the whole reason the tags are left for the HTML stage rather
    /// than removed here. The element list is the HTML package's, not a copy: a copy is what drifts.
    /// </remarks>
    [Theory]
    [InlineData("<script>steal()</script>", "steal()", "script")]
    [InlineData("<style>body{display:none}</style>", "display:none", "style")]
    [InlineData("<title>secret</title>", "secret", "title")]
    [InlineData("<noscript>fallback</noscript>", "fallback", "noscript")]
    public async Task A_raw_inline_non_content_tag_is_removed_with_its_contents(
        string html,
        string forbidden,
        string context)
    {
        var result = await Import($"a {html} b");

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
    public async Task Allowed_raw_inline_html_still_loses_its_script()
    {
        var result = await MarkdownImporter.ImportAsync(
            "a <script>steal()</script> b",
            new MarkdownImportOptions { AllowRawHtml = true });

        Assert.DoesNotContain("steal()", AllText(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every shape in which a non-content element's body can be split by the Markdown around it,
    /// asserted identically whether or not the host allowed raw HTML.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the second attempt at the same defect, and the rows are chosen against the first
    /// one's shape rather than against the symptom. That fix walked <em>siblings</em>, deleting
    /// from an opening tag through the matching close, which is only correct where the tag, the
    /// body and the close are siblings in one container. Markdig makes no such promise: <c>**</c>
    /// opens a container, a blank line ends a block, and the two tags then have no relationship in
    /// the tree at all — so the walk released whatever it could not see as prose.
    /// <c>a **&lt;script&gt;evil** body &lt;/script&gt; after</c> imported as <c>a body after</c>.
    /// </para>
    /// <para>
    /// The pairs are the ones that make each row able to fail on its own: emphasis opened before
    /// the tag and after it, one blank line and three, a table cell and a list item and a
    /// blockquote, and the two spellings a scanner comparing raw text would miss. The nested row's
    /// surviving <c>tail</c> is not a leak and is the point of the row: HTML's raw text ends at the
    /// <em>first</em> close, so the body was <c>outer &lt;script&gt;inner</c>, <c>tail</c> is
    /// ordinary text after it, and the stray close is ignored. Counting nesting, as the first fix
    /// did, is a rule HTML does not have.
    /// </para>
    /// <para>
    /// Both settings assert the same text on purpose. That is the fix: the removal is now the HTML
    /// stage's, so <c>AllowRawHtml</c> decides whether raw formatting is <em>applied</em> and has
    /// never decided what a script body is.
    /// </para>
    /// </remarks>
    public static TheoryData<bool, string, string, string> NonContentBodies() => EitherSetting(
        ("a **<script>evil** body </script> after", "a after", "script"),
        ("a <script>evil **body </script> after** c", "a after c", "script"),
        ("a *<script>evil* body </script> after", "a after", "script"),
        ("before <script>hidden\n\nleaked </script> after", "before after", "script"),
        ("one <script>a\n\nb\n\nc\n\nd </script> two", "one two", "script"),
        ("a <script>evil\n\nmore text", "a", "script"),
        ("a <script>outer <script>inner</script> tail</script> b", "a tail b", "script"),
        ("a <SCRIPT>evil</SCRIPT> b", "a b", "script"),
        ("a <script >evil</script > b", "a b", "script"),
        ("a <script/> b", "a", "script"),
        ("x <style>*a* body{q}</style> y", "x y", "style"),
        ("a **<style>p{x:** y}</style> b", "a b", "style"),
        ("- a **<script>evil** body </script> after", "a after", "script"),
        ("> a **<script>evil** body </script> after", "a after", "script"),
        ("| h |\n| --- |\n| a **<script>evil** body </script> z |", "h\na z", "script"));

    [Theory]
    [MemberData(nameof(NonContentBodies))]
    public async Task A_non_content_body_is_never_text_however_the_document_splits_it(
        bool allowRawHtml,
        string markdown,
        string expected,
        string context)
    {
        var result = await MarkdownImporter.ImportAsync(
            markdown,
            new MarkdownImportOptions { AllowRawHtml = allowRawHtml });

        Assert.Equal(expected, AllText(result));
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Dropped && d.Context == context);
    }

    /// <summary>
    /// The false-positive direction, which matters as much: inside a code span or a code block a
    /// tag is text the author wrote, and must arrive intact.
    /// </summary>
    /// <remarks>
    /// It is safe by construction rather than by a rule about code, and that is why it is pinned.
    /// Markdig escapes every literal it renders, so the only unescaped markup in the HTML handed to
    /// the next stage is Markdig's own and the raw tags this importer chose to leave — a
    /// <c>&lt;script&gt;</c> in a code span is never one of them. The last row is the awkward one:
    /// a quoted opening tag followed by a real closing tag, where a scanner working on the source
    /// text would pair them and swallow the sentence between.
    /// <para>
    /// Nothing may be reported either. A diagnostic here would be the importer saying it removed
    /// something it did not, which is how a host learns to ignore the list.
    /// </para>
    /// </remarks>
    public static TheoryData<bool, string, string> QuotedScripts() => EitherSetting(
        ("a `<script>evil</script>` b", "a <script>evil</script> b"),
        ("```\n<script>fenced</script>\n```", "<script>fenced</script>"),
        ("    <script>indented</script>", "<script>indented</script>"),
        ("a `<script>` b </script> c", "a <script> b c"));

    [Theory]
    [MemberData(nameof(QuotedScripts))]
    public async Task A_script_written_as_code_is_text_and_survives(
        bool allowRawHtml,
        string markdown,
        string expected)
    {
        var result = await MarkdownImporter.ImportAsync(
            markdown,
            new MarkdownImportOptions { AllowRawHtml = allowRawHtml });

        Assert.Contains(expected, AllText(result), StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// A closing tag with nothing to close ends nothing and hides nothing, so the words either side
    /// of it are the author's and stay.
    /// </summary>
    /// <remarks>
    /// The row that would fail if the fix had been written as "a tag named <c>script</c> is
    /// dangerous": treating a stray close as an opener would take the rest of the document, and a
    /// guard that drops a document is a guard a host turns off.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_closing_tag_with_no_opener_removes_nothing(bool allowRawHtml)
    {
        var result = await MarkdownImporter.ImportAsync(
            "a </script> b",
            new MarkdownImportOptions { AllowRawHtml = allowRawHtml });

        Assert.Equal("a b", AllText(result));
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// An attribute is not text in any of these paths, and the element's own words are.
    /// </summary>
    /// <remarks>
    /// <c>div</c> is not a non-content element, so this is the case that pins the other half of the
    /// rule: leaving script and style for the HTML stage must not turn into leaving everything for
    /// it, which would let a raw tag's formatting apply on the path where the host asked for no raw
    /// HTML at all.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_attribute_that_carries_script_never_becomes_text(bool allowRawHtml)
    {
        var result = await MarkdownImporter.ImportAsync(
            "a <div onclick=\"steal()\">bait</div> b",
            new MarkdownImportOptions { AllowRawHtml = allowRawHtml });

        var text = AllText(result);

        Assert.DoesNotContain("steal()", text, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick", text, StringComparison.Ordinal);
        Assert.Contains("bait", text, StringComparison.Ordinal);
    }

    /// <summary>Repeats each row under both settings of <c>AllowRawHtml</c>.</summary>
    private static TheoryData<bool, string, string, string> EitherSetting(
        params (string Markdown, string Text, string Context)[] rows)
    {
        var data = new TheoryData<bool, string, string, string>();

        foreach (var (markdown, text, context) in rows)
        {
            data.Add(false, markdown, text, context);
            data.Add(true, markdown, text, context);
        }

        return data;
    }

    /// <summary>Repeats each row under both settings of <c>AllowRawHtml</c>.</summary>
    private static TheoryData<bool, string, string> EitherSetting(
        params (string Markdown, string Text)[] rows)
    {
        var data = new TheoryData<bool, string, string>();

        foreach (var (markdown, text) in rows)
        {
            data.Add(false, markdown, text);
            data.Add(true, markdown, text);
        }

        return data;
    }

    /// <summary>
    /// An inline tag is a node of its own, separate from the literal text around it, so removing
    /// the tag keeps the words. Only the formatting the tag would have applied is lost.
    /// </summary>
    [Fact]
    public async Task A_raw_inline_tag_is_removed_but_its_text_is_kept()
    {
        var result = await Import("a <b>bold</b> c");
        var paragraph = OnlyPara(result);

        Assert.Equal("a bold c", paragraph.Content.Text);
        Assert.Equal(TextStyle.None, paragraph.Content.MarkAt(2).Style);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == "b");
    }

    /// <summary>
    /// A raw tag inside emphasis or a link is removed as well: the walk goes into every inline
    /// container, not only the paragraph's top level.
    /// </summary>
    [Theory]
    [InlineData("*a <u>b</u> c*")]
    [InlineData("[a <u>b</u> c](https://example.test/)")]
    public async Task A_raw_inline_tag_inside_another_inline_is_removed_too(string markdown)
    {
        var result = await Import(markdown);
        var paragraph = OnlyPara(result);
        var at = paragraph.Content.Text.IndexOf('b', StringComparison.Ordinal);

        Assert.Equal("a b c", paragraph.Content.Text);
        Assert.False(paragraph.Content.MarkAt(at).Has(TextStyle.Underline));
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == "u");
    }

    /// <summary>
    /// The two spaces either side of the removed comment collapse into one, which is HTML's
    /// whitespace rule rather than anything to do with the comment.
    /// </summary>
    [Fact]
    public async Task A_raw_html_comment_is_removed() =>
        Assert.Equal("a c", OnlyPara(await Import("a <!-- secret --> c")).Content.Text);

    /// <summary>
    /// With raw HTML allowed the markup is read rather than discarded — and it is read by the same
    /// sanitizer every HTML import goes through, not by a second one written for Markdown.
    /// </summary>
    [Fact]
    public async Task Allowed_raw_html_is_read_through_the_html_sanitizer()
    {
        var result = await MarkdownImporter.ImportAsync(
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
    public async Task Allowed_raw_html_still_loses_its_script_and_style(string html, string forbidden)
    {
        var result = await MarkdownImporter.ImportAsync(
            $"before\n\n{html}\n\nafter",
            new MarkdownImportOptions { AllowRawHtml = true });

        Assert.DoesNotContain(forbidden, AllText(result), StringComparison.Ordinal);
        Assert.Contains("before", AllText(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allowed_raw_html_still_loses_a_dangerous_link_scheme()
    {
        var result = await MarkdownImporter.ImportAsync(
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
    public async Task A_remote_image_needs_the_host_to_ask_for_it()
    {
        var result = await Import("![](https://tracker.test/pixel.gif)");

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
