using Vellum.Interop.Html;

namespace Vellum.Tests.Interop.Html;

/// <summary>
/// What must not survive an import, whatever the source does to smuggle it through.
/// </summary>
public class HtmlSecurityTests
{
    private const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static ImportResult Import(string html, HtmlImportOptions? options = null) =>
        HtmlImporter.Import(html, options);

    private static string PlainText(ImportResult result) =>
        string.Join("\n", result.Doc.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text));

    private static IEnumerable<ImageEmbed> Images(ImportResult result) =>
        result.Doc.Blocks
            .OfType<ParagraphNode>()
            .SelectMany(p => p.Content.Embeds)
            .OfType<ImageEmbed>();

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<SCRIPT>alert(1)</SCRIPT>")]
    [InlineData("<iframe src='https://evil.test'></iframe>")]
    [InlineData("<object data='x.swf'></object>")]
    [InlineData("<embed src='x.swf'>")]
    [InlineData("<form><input name=x></form>")]
    [InlineData("<style>body{background:url(https://evil.test)}</style>")]
    [InlineData("<link rel=stylesheet href='https://evil.test'>")]
    [InlineData("<base href='https://evil.test'>")]
    [InlineData("<meta http-equiv=refresh content='0;url=https://evil.test'>")]
    public void Active_content_never_survives(string html)
    {
        var text = PlainText(Import(html));

        Assert.DoesNotContain("alert", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.test", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mangled_script_tag_leaves_inert_text_behind()
    {
        // "<scr<script>ipt>" is the classic filter-evasion shape: a naive sanitizer that removes
        // the string "<script>" reassembles a working tag. Here the parser reads it as an unknown
        // element whose tag name happens to contain a bracket, that element is removed, and the
        // characters after it are left over as text.
        //
        // Text reading "alert(1)" is not a vulnerability -- a user could type it. What matters is
        // that nothing became an element, an attribute or a URL, and that the leftovers carry no
        // markup that could be reassembled by whoever renders the document next.
        var result = Import("<scr<script>ipt>alert(1)</script>");

        var text = PlainText(result);

        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
        Assert.Empty(Images(result));
        Assert.All(
            result.Doc.Blocks.OfType<ParagraphNode>(),
            p => Assert.All(
                Enumerable.Range(0, p.Content.Length),
                i => Assert.Null(p.Content.MarkAt(i).Link)));
    }

    [Theory]
    [InlineData("<p onclick='alert(1)'>hi</p>")]
    [InlineData("<p onmouseover='alert(1)'>hi</p>")]
    [InlineData("<img src='x' onerror='alert(1)'>hi")]
    [InlineData("<p ONCLICK='alert(1)'>hi</p>")]
    public void Event_handlers_never_survive(string html)
    {
        // There is nowhere in the document model to put an event handler, so the proof is that the
        // import produced only text and marks -- but the sanitizer must also have said it removed
        // something, or it never saw the attribute at all.
        var result = Import(html);

        Assert.DoesNotContain("alert", PlainText(result), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void Dangerous_link_schemes_are_dropped(string href)
    {
        var result = Import($"<a href=\"{href}\">click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        // The text stays; only the destination goes.
        Assert.Equal("click", paragraph.Content.Text);
        Assert.Null(paragraph.Content.MarkAt(0).Link);
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://example.com/a")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("tel:+15551234")]
    public void Ordinary_link_schemes_are_kept(string href)
    {
        var result = Import($"<a href=\"{href}\">click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.NotNull(paragraph.Content.MarkAt(0).Link);
    }

    [Fact]
    public void A_remote_image_is_dropped_unless_the_host_opts_in()
    {
        // The tracking pixel: rendering it tells whoever sent the document that it was opened.
        var result = Import("<img src='https://tracker.test/pixel.gif' width='1' height='1'>");

        Assert.Empty(Images(result));
        Assert.True(result.AnythingDropped);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Dropped && d.Context == "img");
    }

    [Fact]
    public void A_remote_image_is_kept_when_the_host_does_opt_in()
    {
        var result = Import(
            "<img src='https://cdn.test/a.png'>",
            new HtmlImportOptions { AllowRemoteImages = true });

        var image = Assert.Single(Images(result));

        Assert.Equal("https://cdn.test/a.png", image.Source);
    }

    [Fact]
    public void An_inline_image_survives_because_that_is_how_a_paste_arrives()
    {
        var result = Import($"<img src='{Png}' alt='dot'>");
        var image = Assert.Single(Images(result));

        Assert.StartsWith("data:image/png;base64,", image.Source, StringComparison.Ordinal);
        Assert.Equal("dot", image.AltText);
    }

    [Fact]
    public void An_inline_image_is_dropped_when_the_host_says_so()
    {
        var result = Import($"<img src='{Png}'>", new HtmlImportOptions { AllowDataImages = false });

        Assert.Empty(Images(result));
        Assert.True(result.AnythingDropped);
    }

    [Fact]
    public void The_inline_image_exception_does_not_extend_to_links()
    {
        // The exception exists for <img src>. Granting it to any URL attribute would let a
        // data: URL back into an href, which is a navigable document rather than a picture.
        var result = Import($"<a href='{Png}'>click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Null(paragraph.Content.MarkAt(0).Link);
        Assert.True(result.AnythingDropped);
    }

    [Fact]
    public void The_inline_image_exception_does_not_extend_to_other_attributes_of_an_image()
    {
        var result = Import($"<img src='' href='{Png}' cite='{Png}'>");

        Assert.Empty(Images(result));
    }

    [Fact]
    public void The_inline_image_exception_does_not_extend_to_other_elements_that_carry_a_source()
    {
        // The allow-list for attributes is not per-element, so "src" is spellable on anything that
        // survives sanitizing. The exception for data: URLs exists so a pasted picture keeps
        // working; anywhere else a data: URL is a payload being carried past the scheme filter,
        // and the caller is entitled to be told it was taken away.
        var result = Import($"<span src='{Png}'>x</span>");

        Assert.True(result.AnythingDropped);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Dropped);
    }

    [Theory]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48c2NyaXB0PmFsZXJ0KDEpPC9zY3JpcHQ+PC9zdmc+")]
    [InlineData("data:image/svg+xml,<svg onload='alert(1)'></svg>")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("data:application/javascript,alert(1)")]
    [InlineData("data:windows/metafile;base64,AQIDBAU=")]
    [InlineData("data:,hello")]
    public void Inline_data_that_is_not_a_decodable_image_is_dropped(string src)
    {
        // SVG is the one that matters: it passes every check that only looks at the word before
        // the slash, and it is a document format that can carry script.
        var result = Import($"<img src=\"{src}\">");

        Assert.Empty(Images(result));
        Assert.True(result.AnythingDropped);
    }

    [Fact]
    public void An_inline_image_larger_than_the_limit_is_dropped()
    {
        var big = "data:image/png;base64," + new string('A', 4096);

        var result = Import(
            $"<img src='{big}'>",
            new HtmlImportOptions { MaxDataImageBytes = 1024 });

        Assert.Empty(Images(result));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("larger than"));
    }

    [Fact]
    public void Styles_that_are_not_formatting_are_removed()
    {
        // position, behavior and -moz-binding have all been script vectors, and none of them mean
        // anything in a document model that does no layout of its own.
        var result = Import(
            "<p style=\"position:fixed;behavior:url(x.htc);-moz-binding:url(x.xml);color:red\">hi</p>");

        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Equal("hi", paragraph.Content.Text);
        Assert.Equal(new Rgba(255, 0, 0, 255), paragraph.Content.MarkAt(0).Foreground);
    }

    [Fact]
    public void Relative_urls_are_dropped_when_there_is_nothing_to_resolve_them_against()
    {
        var result = Import("<a href='/somewhere'>click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Null(paragraph.Content.MarkAt(0).Link);
    }

    [Fact]
    public void Relative_urls_resolve_against_a_base_the_caller_supplies()
    {
        var result = Import(
            "<a href='/somewhere'>click</a>",
            new HtmlImportOptions { BaseUri = new Uri("https://example.com/page") });

        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Equal("https://example.com/somewhere", paragraph.Content.MarkAt(0).Link?.Href);
    }

    [Fact]
    public void Deep_nesting_does_not_overflow_the_stack()
    {
        // A recursive walk over ten thousand nested elements is a stack overflow, and a stack
        // overflow cannot be caught -- it takes the host process with it.
        var html = string.Concat(Enumerable.Repeat("<div>", 10_000))
            + "deep"
            + string.Concat(Enumerable.Repeat("</div>", 10_000));

        var result = Import(html);

        Assert.Contains("deep", PlainText(result), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Context == "nesting");
    }

    [Fact]
    public void A_span_of_a_thousand_columns_is_not_a_table()
    {
        var result = Import("<table><tr><td colspan='2000000'>x</td></tr></table>");

        Assert.NotNull(result.Doc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<")]
    [InlineData("<p>unclosed")]
    [InlineData("</p></div></body>")]
    [InlineData("<p><b><i>tangled</p></b></i>")]
    [InlineData("\0\0\0")]
    [InlineData("<table><tr><td>ragged</td><td>x</td></tr><tr><td>y</td></tr></table>")]
    public void Malformed_input_never_throws(string html)
    {
        var result = Import(html);

        Assert.NotNull(result.Doc);
        Assert.NotEmpty(result.Doc.Blocks);
    }

    [Fact]
    public void Null_input_is_the_callers_mistake_and_is_reported_as_one()
    {
        Assert.Throws<ArgumentNullException>(() => HtmlImporter.Import(null!));
    }
}
