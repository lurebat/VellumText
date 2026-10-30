using System.Globalization;
using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// What must not survive an import, whatever the source does to smuggle it through.
/// </summary>
public class HtmlSecurityTests
{
    private const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static async Task<ImportResult> Import(string html, HtmlImportOptions? options = null) =>
        await HtmlImporter.ImportAsync(html, options);

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
    public async Task Active_content_never_survives(string html)
    {
        var text = PlainText(await Import(html));

        Assert.DoesNotContain("alert", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.test", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mangled_script_tag_leaves_inert_text_behind()
    {
        // "<scr<script>ipt>" is the classic filter-evasion shape: a naive sanitizer that removes
        // the string "<script>" reassembles a working tag. Here the parser reads it as an unknown
        // element whose tag name happens to contain a bracket, that element is removed, and the
        // characters after it are left over as text.
        //
        // Text reading "alert(1)" is not a vulnerability -- a user could type it. What matters is
        // that nothing became an element, an attribute or a URL, and that the leftovers carry no
        // markup that could be reassembled by whoever renders the document next.
        var result = await Import("<scr<script>ipt>alert(1)</script>");

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
    public async Task Event_handlers_never_survive(string html)
    {
        // There is nowhere in the document model to put an event handler, so the proof is that the
        // import produced only text and marks -- but the sanitizer must also have said it removed
        // something, or it never saw the attribute at all.
        var result = await Import(html);

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
    public async Task Dangerous_link_schemes_are_dropped(string href)
    {
        var result = await Import($"<a href=\"{href}\">click</a>");
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
    public async Task Ordinary_link_schemes_are_kept(string href)
    {
        var result = await Import($"<a href=\"{href}\">click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.NotNull(paragraph.Content.MarkAt(0).Link);
    }

    [Fact]
    public async Task A_remote_image_is_dropped_unless_the_host_opts_in()
    {
        // The tracking pixel: rendering it tells whoever sent the document that it was opened.
        var result = await Import("<img src='https://tracker.test/pixel.gif' width='1' height='1'>");

        Assert.Empty(Images(result));
        Assert.True(result.AnythingDropped);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Dropped && d.Context == "img");
    }

    [Fact]
    public async Task A_remote_image_is_kept_when_the_host_does_opt_in()
    {
        var result = await Import(
            "<img src='https://cdn.test/a.png'>",
            new HtmlImportOptions { AllowRemoteImages = true });

        var image = Assert.Single(Images(result));

        Assert.Equal("https://cdn.test/a.png", image.Source);
    }

    [Fact]
    public async Task An_inline_image_survives_because_that_is_how_a_paste_arrives()
    {
        var result = await Import($"<img src='{Png}' alt='dot'>");
        var image = Assert.Single(Images(result));

        Assert.StartsWith("data:image/png;base64,", image.Source, StringComparison.Ordinal);
        Assert.Equal("dot", image.AltText);
    }

    [Fact]
    public async Task An_inline_image_is_dropped_when_the_host_says_so()
    {
        var result = await Import($"<img src='{Png}'>", new HtmlImportOptions { AllowDataImages = false });

        Assert.Empty(Images(result));
        Assert.True(result.AnythingDropped);
    }

    [Fact]
    public async Task The_inline_image_exception_does_not_extend_to_links()
    {
        // The exception exists for <img src>. Granting it to any URL attribute would let a
        // data: URL back into an href, which is a navigable document rather than a picture.
        var result = await Import($"<a href='{Png}'>click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Null(paragraph.Content.MarkAt(0).Link);
        Assert.True(result.AnythingDropped);
    }

    [Fact]
    public async Task The_inline_image_exception_does_not_extend_to_other_attributes_of_an_image()
    {
        var result = await Import($"<img src='' href='{Png}' cite='{Png}'>");

        Assert.Empty(Images(result));
    }

    [Fact]
    public async Task The_inline_image_exception_does_not_extend_to_other_elements_that_carry_a_source()
    {
        // The allow-list for attributes is not per-element, so "src" is spellable on anything that
        // survives sanitizing. The exception for data: URLs exists so a pasted picture keeps
        // working; anywhere else a data: URL is a payload being carried past the scheme filter,
        // and the caller is entitled to be told it was taken away.
        var result = await Import($"<span src='{Png}'>x</span>");

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
    public async Task Inline_data_that_is_not_a_decodable_image_is_dropped(string src)
    {
        // SVG is the one that matters: it passes every check that only looks at the word before
        // the slash, and it is a document format that can carry script.
        var result = await Import($"<img src=\"{src}\">");

        Assert.Empty(Images(result));
        Assert.True(result.AnythingDropped);
    }

    [Fact]
    public async Task An_inline_image_larger_than_the_limit_is_dropped()
    {
        var big = "data:image/png;base64," + new string('A', 4096);

        var result = await Import(
            $"<img src='{big}'>",
            new HtmlImportOptions { MaxDataImageBytes = 1024 });

        Assert.Empty(Images(result));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("larger than"));
    }

    /// <summary>
    /// Every refusal of an image's source is reported in context <c>img</c>, whatever the reason.
    /// </summary>
    /// <remarks>
    /// A paste reads that context to decide whether the rich flavour lost a picture and the bitmap
    /// beside it should win instead (OD-4b). Unsupported types, relative addresses and disallowed
    /// schemes were reported in contexts of their own and so read as nothing lost.
    /// </remarks>
    [Theory]
    [InlineData("<img src='https://tracker.test/pixel.gif'>", null)]
    [InlineData("<img src='foo.png'>", null)]
    [InlineData("<img src='javascript:alert(1)'>", null)]
    [InlineData("<img src='data:image/svg+xml;base64,PHN2Zz48L3N2Zz4='>", null)]
    [InlineData("<img src='data:image/png;base64,!!!!'>", null)]
    [InlineData("<img src='data:image/png;base64,AAAAAAAAAAAAAAAA'>", 4)]
    [InlineData("<img src='data:image/png;base64,AAAA'>", -1)]
    [InlineData("<img src='data:image/png,%89PNG'>", null)]
    public async Task Every_refused_image_source_is_reported_as_a_removed_image(string html, int? limit)
    {
        var options = limit switch
        {
            null => new HtmlImportOptions(),
            -1 => new HtmlImportOptions { AllowDataImages = false },
            _ => new HtmlImportOptions { MaxDataImageBytes = limit.Value },
        };

        var result = await Import(html, options);

        Assert.Empty(Images(result));
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Dropped && d.Context == "img");
    }

    /// <summary>
    /// Images refused for different reasons are each reported, all in context <c>img</c>.
    /// </summary>
    /// <remarks>
    /// Sharing one context must not make the first refusal hide the others: the caller would be
    /// told a picture was relative when another was also refused for its type and a third for its
    /// size.
    /// </remarks>
    [Fact]
    public async Task Images_refused_for_different_reasons_report_every_reason()
    {
        var result = await Import(
            "<img src='data:image/svg+xml;base64,PHN2Zz48L3N2Zz4='>"
            + "<img src='foo.png'>"
            + "<img src='data:image/png;base64,AAAAAAAAAAAAAAAA'>",
            new HtmlImportOptions { MaxDataImageBytes = 4 });

        var messages = result.Diagnostics.Where(d => d.Context == "img").ToList();

        Assert.Empty(Images(result));
        Assert.Equal(3, messages.Count);
        Assert.All(messages, d => Assert.Equal(DiagnosticSeverity.Dropped, d.Severity));
        Assert.Contains(messages, d => d.Message.Contains("supported image type", StringComparison.Ordinal));
        Assert.Contains(messages, d => d.Message.Contains("relative", StringComparison.Ordinal));
        Assert.Contains(messages, d => d.Message.Contains("larger than 4 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Images_refused_for_the_same_reason_are_reported_once()
    {
        var html = string.Concat(Enumerable.Repeat("<img src='foo.png'>", 100));

        var result = await Import(html);

        Assert.Empty(Images(result));
        Assert.Single(result.Diagnostics, d => d.Context == "img");
    }

    [Theory]
    [InlineData("<a href='data:text/plain,x'>link</a>")]
    [InlineData("<a href='javascript:alert(1)'>link</a>")]
    [InlineData("<a href='foo.html'>link</a>")]
    public async Task A_refused_address_that_is_not_an_image_source_is_not_reported_as_one(string html)
    {
        var result = await Import(html);

        Assert.True(result.AnythingDropped);
        Assert.DoesNotContain(result.Diagnostics, d => d.Context == "img");
    }

    [Fact]
    public async Task Styles_that_are_not_formatting_are_removed()
    {
        // position, behavior and -moz-binding have all been script vectors, and none of them mean
        // anything in a document model that does no layout of its own.
        var result = await Import(
            "<p style=\"position:fixed;behavior:url(x.htc);-moz-binding:url(x.xml);color:red\">hi</p>");

        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Equal("hi", paragraph.Content.Text);
        Assert.Equal(new Rgba(255, 0, 0, 255), paragraph.Content.MarkAt(0).Foreground);
    }

    /// <summary>
    /// The bodies of code-bearing elements must not become document text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every input here is wrapped in a <c>&lt;div&gt;</c>, and that is the entire point. A bare
    /// <c>&lt;script&gt;</c> is parked in <c>&lt;head&gt;</c> by the parser, and the importer walks
    /// <c>&lt;body&gt;</c> — so the sibling theory above, which feeds bare fragments, passes
    /// whether or not the contents leak. Wrapping puts them in the body, where a real paste puts
    /// them.
    /// </para>
    /// <para>
    /// This is not script execution; nothing here runs. It is a stylesheet arriving as the first
    /// paragraph of a Word paste, which is what the leak actually looked like.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("<div><script>alert(1)</script>hello</div>", "alert")]
    [InlineData("<div><SCRIPT>alert(1)</SCRIPT>hello</div>", "alert")]
    [InlineData("<div><style>p.MsoNormal {margin:0cm;}</style>text</div>", "margin")]
    [InlineData("<div><section><style>a{b:c}</style></section>x</div>", "b:c")]
    [InlineData("<div><noscript>fallback</noscript>ok</div>", "fallback")]
    [InlineData("<div><noembed>fallback</noembed>ok</div>", "fallback")]
    [InlineData("<div><noframes>fallback</noframes>ok</div>", "fallback")]
    [InlineData("<div><template><p>inert</p></template>ok</div>", "inert")]
    [InlineData("<body><title>Doc Title</title><p>body text</p></body>", "Doc Title")]
    public async Task Code_bearing_elements_do_not_leak_their_bodies_as_text(string html, string forbidden)
    {
        var text = PlainText(await Import(html));

        Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removing code-bearing elements must not disturb the surrounding content.
    /// </summary>
    /// <remarks>
    /// The companion to the theory above, and the reason it is not enough on its own: dropping
    /// every unrecognised element wholesale would pass every one of those cases and quietly throw
    /// away the text inside an unknown wrapper, which is exactly what <c>KeepChildNodes</c> exists
    /// to preserve.
    /// </remarks>
    [Theory]
    [InlineData("<div><script>alert(1)</script>hello</div>", "hello")]
    [InlineData("<div><style>a{b:c}</style>text</div>", "text")]
    [InlineData("<foo>kept text</foo>", "kept text")]
    [InlineData("<bar><baz>deep text</baz></bar>", "deep text")]
    [InlineData("<div><p>para</p></div>", "para")]
    public async Task Surrounding_content_survives_the_removal(string html, string expected)
    {
        var text = PlainText(await Import(html));

        Assert.Contains(expected, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An attribute the model cannot represent must not cost the whole document.
    /// </summary>
    /// <remarks>
    /// A negative <c>start</c> is valid HTML and <see cref="ListNode"/> refuses it, so passing it
    /// through threw — and the importer's outer catch turns any throw into an empty document.
    /// One attribute nobody looks at discarded every paragraph in the paste. The assertion is on
    /// the surrounding content rather than on the list, because the failure was never about
    /// numbering.
    /// </remarks>
    [Theory]
    [InlineData("-1")]
    [InlineData("-2147483648")]
    public async Task A_negative_list_start_does_not_discard_the_document(string start)
    {
        var result = await Import($"<p>before</p><ol start='{start}'><li>x</li></ol><p>after</p>");

        var text = string.Join(
            " ",
            result.Doc.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text));

        Assert.Contains("before", text, StringComparison.Ordinal);
        Assert.Contains("after", text, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Downgraded);
    }

    /// <summary>A positive start is still honoured, so the guard has not swallowed the feature.</summary>
    [Fact]
    public async Task A_positive_list_start_is_still_kept()
    {
        var result = await Import("<ol start='5'><li>x</li></ol>");
        var list = Assert.IsType<ListNode>(result.Doc.Blocks[0]);

        Assert.Equal(5, list.Start);
    }

    /// <summary>
    /// An inline image the renderer could never decode is refused at the door.
    /// </summary>
    /// <remarks>
    /// A percent-encoded <c>data:</c> payload is a legal data URL that every renderer here
    /// rejects, because they all require <c>;base64</c>. Accepting it put an embed in the
    /// document that could only ever draw as a missing-image box, with no diagnostic to say why.
    /// Importer and renderer each tested their own assumption and nothing tested the contract
    /// between them.
    /// </remarks>
    /// <remarks>
    /// The payload has to be one the importer could otherwise have used, or the test proves
    /// nothing: a percent-encoded payload is also invalid base64, so dropping the <c>;base64</c>
    /// requirement altogether still refuses it and the requirement itself goes unmeasured. The
    /// second case is a real PNG's base64 with the marker taken off, which the importer would
    /// happily decode if it did not insist on being told.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_data_image_that_is_not_base64_is_refused(bool decodable)
    {
        var encoded = Png["data:image/png;base64,".Length..];
        var payload = decodable
            ? encoded
            : string.Concat(
                Convert.FromBase64String(encoded).Select(b => "%" + b.ToString("X2", CultureInfo.InvariantCulture)));

        var result = await Import(
            $"<img src='data:image/png,{payload}'>",
            new HtmlImportOptions { AllowDataImages = true });

        Assert.Empty(Images(result));
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task Relative_urls_are_dropped_when_there_is_nothing_to_resolve_them_against()
    {
        var result = await Import("<a href='/somewhere'>click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Null(paragraph.Content.MarkAt(0).Link);
    }

    /// <summary>
    /// A scheme-less address must be dropped even when <see cref="Uri"/> is willing to read it as
    /// a file path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the cases the sibling test above cannot reach. It uses <c>/somewhere</c>, which
    /// <see cref="Uri"/> rejects as absolute on Windows and accepts as a file path on Unix — so on
    /// the developer's machine it passed for the right reason, and on Linux it failed, and no
    /// Windows run could tell you the check was asking the wrong question.
    /// </para>
    /// <para>
    /// These inputs fail on <em>every</em> platform, because Windows reads both of them as UNC
    /// paths. That matters beyond tidiness: a host that opens a link with the shell would have
    /// made an outbound SMB connection to a host the document chose, which on Windows hands over
    /// an NTLM handshake before anything asks the user.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("//evil.test/steal")]
    [InlineData(@"\\evil.test\share\payload")]
    [InlineData("/etc/passwd")]
    [InlineData("relative/path")]
    public async Task Scheme_less_addresses_are_dropped_however_the_platform_reads_them(string href)
    {
        var result = await Import($"<a href='{href}'>click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Null(paragraph.Content.MarkAt(0).Link);
    }

    /// <summary>Only the four allowed schemes survive, whatever else parses as absolute.</summary>
    [Theory]
    [InlineData("https://ok.test/", true)]
    [InlineData("http://ok.test/", true)]
    [InlineData("mailto:someone@ok.test", true)]
    [InlineData("tel:+123456", true)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("C:/Windows/win.ini", false)]
    public async Task Only_allowed_schemes_survive(string href, bool kept)
    {
        var result = await Import($"<a href='{href}'>click</a>");
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);
        var link = paragraph.Content.MarkAt(0).Link;

        if (kept)
        {
            Assert.NotNull(link);
        }
        else
        {
            Assert.Null(link);
        }
    }

    /// <summary>
    /// A protocol-relative address is still relative, so a base resolves it rather than dropping it.
    /// </summary>
    /// <remarks>
    /// The companion to the drop tests above. Without this, a fix that simply refused everything
    /// scheme-less would pass every one of them and quietly break the case the feature exists for.
    /// </remarks>
    [Fact]
    public async Task A_protocol_relative_address_resolves_against_a_base()
    {
        var result = await Import(
            "<a href='//other.test/p'>click</a>",
            new HtmlImportOptions { BaseUri = new Uri("https://example.test/page") });

        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Equal("https://other.test/p", paragraph.Content.MarkAt(0).Link?.Href);
    }

    [Fact]
    public async Task Relative_urls_resolve_against_a_base_the_caller_supplies()
    {
        var result = await Import(
            "<a href='/somewhere'>click</a>",
            new HtmlImportOptions { BaseUri = new Uri("https://example.com/page") });

        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Equal("https://example.com/somewhere", paragraph.Content.MarkAt(0).Link?.Href);
    }

    /// <summary>
    /// A base the host supplies outranks the <c>SourceURL</c> a clipboard payload claims for itself.
    /// </summary>
    /// <remarks>
    /// The payload's address is written by whichever application put it on the clipboard, which
    /// is to say by the page being copied from. The host's base is the one decision about where
    /// relative links point that the paste does not get to make.
    /// </remarks>
    [Fact]
    public async Task An_explicit_base_outranks_the_address_the_clipboard_claims()
    {
        var payload = ClipboardHtml.Wrap("<a href='/p'>x</a>", new Uri("https://attacker.test/"));

        var claimed = await HtmlImporter.ImportClipboardAsync(payload);
        var result = await HtmlImporter.ImportClipboardAsync(
            payload,
            new HtmlImportOptions { BaseUri = new Uri("https://host.test/") });

        // The payload's own address is used when the host gives none, so the second import
        // differs from the first only by which base won.
        Assert.Equal(
            "https://attacker.test/p",
            Assert.IsType<ParagraphNode>(claimed.Doc.Blocks[0]).Content.MarkAt(0).Link?.Href);
        Assert.Equal(
            "https://host.test/p",
            Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]).Content.MarkAt(0).Link?.Href);
    }

    [Fact]
    public async Task Deep_nesting_does_not_overflow_the_stack()
    {
        // A recursive walk over ten thousand nested elements is a stack overflow, and a stack
        // overflow cannot be caught -- it takes the host process with it.
        var html = string.Concat(Enumerable.Repeat("<div>", 10_000))
            + "deep"
            + string.Concat(Enumerable.Repeat("</div>", 10_000));

        var result = await Import(html);

        Assert.Contains("deep", PlainText(result), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Context == "nesting");
    }

    /// <summary>
    /// The nesting limit a host sets is the one the walk stops at, not the default.
    /// </summary>
    /// <remarks>
    /// Fifty levels is far inside the default, so the default-options import is the control: it
    /// shows the diagnostic below comes from the configured limit and not from the input alone.
    /// </remarks>
    [Fact]
    public async Task The_nesting_limit_a_host_sets_is_the_one_used()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 50))
            + "x"
            + string.Concat(Enumerable.Repeat("</div>", 50));

        var byDefault = await Import(html);
        var limited = await Import(html, new HtmlImportOptions { MaxDepth = 10 });

        Assert.DoesNotContain(byDefault.Diagnostics, d => d.Context == "nesting");
        Assert.Contains(limited.Diagnostics, d => d.Context == "nesting");
        Assert.Contains("x", PlainText(limited), StringComparison.Ordinal);
    }

    /// <summary>
    /// A cell may not claim more columns than a table could plausibly have.
    /// </summary>
    /// <remarks>
    /// The span is what has to be pinned, not the fact that something came back: a document
    /// holding one paragraph is a perfectly well-formed import, so asserting only that the
    /// importer returned a document leaves every clamp it applies unchecked.
    /// </remarks>
    [Theory]
    [InlineData("2000000", 1000)]
    [InlineData("1001", 1000)]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("three", 1)]
    public async Task A_span_of_a_thousand_columns_is_not_a_table(string attribute, int expected)
    {
        var result = await Import($"<table><tr><td colspan='{attribute}'>x</td></tr></table>");
        var table = Assert.IsType<TableNode>(result.Doc.Blocks[0]);

        TableGeometry.Place(table, out var width);

        Assert.Equal(expected, table.Rows[0].Cells[0].ColumnSpan);
        Assert.Equal(expected, width);
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
    public async Task Malformed_input_never_throws(string html)
    {
        var result = await Import(html);

        Assert.NotNull(result.Doc);
        Assert.NotEmpty(result.Doc.Blocks);
    }

    [Fact]
    public async Task Null_input_is_the_callers_mistake_and_is_reported_as_one()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => HtmlImporter.ImportAsync(null!));
    }
}
