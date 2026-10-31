using VellumText;
using VellumText.Interop.Html;

namespace VellumText.Interop.Html.Tests;

/// <summary>
/// Values that are individually legal and jointly absurd: sizes that pass every finiteness check
/// and would still break layout, payloads that name an image and are not one, and the model's own
/// placeholder character arriving as ordinary text.
/// </summary>
public sealed class HostileValueTests
{
    private static string TextOf(DocumentNode doc) =>
        string.Concat(doc.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text));

    private static IEnumerable<double?> SizesOf(DocumentNode doc) =>
        doc.Blocks.OfType<ParagraphNode>()
            .SelectMany(p => Enumerable.Range(0, p.Content.Length).Select(i => p.Content.MarkAt(i).FontSize));

    private static int EmbedsOf(DocumentNode doc) =>
        doc.Blocks.OfType<ParagraphNode>().Sum(p => p.Content.Embeds.Count());

    // A one-pixel transparent PNG, which is genuine base64 and genuinely decodes.
    private const string RealPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Text_carrying_the_embed_placeholder_keeps_the_rest_of_the_paragraph()
    {
        // U+FFFC is what Word writes for an object it cannot otherwise express, and what any
        // content that already passed through a rich text model carries. Treating it as content
        // makes the placeholder count disagree with the embed count, which the model rejects —
        // and the whole import used to be discarded because of one character.
        var result = await HtmlImporter.ImportAsync("<p>before\uFFFCafter</p>", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsEmpty);
        Assert.Equal("beforeafter", TextOf(result.Doc));
        Assert.DoesNotContain(InlineContent.Placeholder, TextOf(result.Doc));
    }

    [Fact]
    public async Task A_smuggled_placeholder_does_not_disturb_a_real_embed_beside_it()
    {
        var result = await HtmlImporter.ImportAsync($"<p>a\uFFFCb<img src='{RealPng}'></p>", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsEmpty);
        Assert.Equal(1, EmbedsOf(result.Doc));

        // Exactly one placeholder, and it belongs to the image rather than to the text.
        Assert.Equal("ab\uFFFC", TextOf(result.Doc));
    }

    [Fact]
    public async Task A_paragraph_that_is_nothing_but_placeholders_survives_as_an_empty_one()
    {
        var result = await HtmlImporter.ImportAsync("<p>\uFFFC\uFFFC\uFFFC</p><p>kept</p>", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("kept", TextOf(result.Doc));
        Assert.DoesNotContain(InlineContent.Placeholder, TextOf(result.Doc));
        Assert.True(DocumentSchema.IsValid(result.Doc));
    }

    [Theory]
    [InlineData("####not-base64####")]
    [InlineData("iVBORw0KGgo")] // A length that is not a multiple of four and has no padding.
    [InlineData("")]
    [InlineData("=")]
    public async Task An_inline_image_whose_data_does_not_decode_is_dropped(string payload)
    {
        var result = await HtmlImporter.ImportAsync($"<img src='data:image/png;base64,{payload}'>", cancellationToken: TestContext.Current.CancellationToken);

        // Left in, it would reach the document as an embed and fail at draw time instead, far from
        // anything that could explain it.
        Assert.Equal(0, EmbedsOf(result.Doc));
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Dropped);
    }

    [Fact]
    public async Task An_inline_image_whose_data_does_decode_is_kept()
    {
        var result = await HtmlImporter.ImportAsync($"<img src='{RealPng}'>", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, EmbedsOf(result.Doc));
    }

    [Fact]
    public async Task The_size_limit_is_measured_in_decoded_bytes_not_url_characters()
    {
        // 96 bytes of image encode to 128 characters of base64, plus a 22-character header.
        var bytes = new byte[96];
        Random.Shared.NextBytes(bytes);
        var url = "data:image/png;base64," + Convert.ToBase64String(bytes);

        Assert.True(url.Length > 100, "the URL has to be longer than the limit for this to mean anything");

        var options = new HtmlImportOptions { MaxDataImageBytes = 100 };
        var result = await HtmlImporter.ImportAsync($"<img src='{url}'>", options, cancellationToken: TestContext.Current.CancellationToken);

        // Judged by URL length it is over the limit; judged by the limit's own name it is under.
        Assert.Equal(1, EmbedsOf(result.Doc));
    }

    [Fact]
    public async Task An_image_over_the_decoded_size_limit_is_still_dropped()
    {
        var bytes = new byte[512];
        Random.Shared.NextBytes(bytes);
        var url = "data:image/png;base64," + Convert.ToBase64String(bytes);

        var result = await HtmlImporter.ImportAsync($"<img src='{url}'>", new HtmlImportOptions { MaxDataImageBytes = 100 }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, EmbedsOf(result.Doc));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("larger than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_vast_base64_payload_is_rejected_without_being_decoded()
    {
        // The bound has to be applied before room is allocated for the result, or a hostile paste
        // buys a large allocation for the price of a string.
        //
        // The verdict alone cannot see that: the payload is valid base64, so decoding it and then
        // measuring what came out reaches exactly the same "too large" answer by exactly the route
        // this is supposed to rule out. What separates the two is the three megabytes, so the two
        // imports below are chosen to drop the same image on the same document by two routes that
        // differ in nothing else, and the allocation between them is the assertion.
        var html = $"<img src='data:image/png;base64,{new string('A', PayloadChars)}'>";

        var refusedOutright = new HtmlImportOptions { AllowDataImages = false };
        var refusedForSize = new HtmlImportOptions { MaxDataImageBytes = 1024 };

        // Parsing four megabytes of HTML allocates far more than the buffer does, but it allocates
        // the same amount both times and cancels. The warm-up keeps first-call JIT out of the
        // measurements, where it would otherwise land entirely on whichever ran first.
        await HtmlImporter.ImportAsync(html, refusedOutright, cancellationToken: TestContext.Current.CancellationToken);
        await HtmlImporter.ImportAsync(html, refusedForSize, cancellationToken: TestContext.Current.CancellationToken);

        // Measured on this thread, through the import stage itself: the public entry point parses
        // on a thread of its own, where a per-thread allocation counter sees none of it.
        var baseline = Allocated(() => HtmlImporter.ImportOnCurrentStack(html, refusedOutright));
        var measured = Allocated(() => HtmlImporter.ImportOnCurrentStack(html, refusedForSize));

        Assert.Equal(0, EmbedsOf((await HtmlImporter.ImportAsync(html, refusedForSize, cancellationToken: TestContext.Current.CancellationToken)).Doc));
        Assert.True(
            measured - baseline < DecodedBytes / 2,
            $"deciding the payload was too large cost {measured - baseline} bytes more than "
            + $"refusing it outright, so it was decoded on the way to that decision");
    }

    // Four megabytes of base64 decode to three, which is the allocation the guard exists to avoid.
    private const int PayloadChars = 4 * 1024 * 1024;

    private const long DecodedBytes = PayloadChars / 4L * 3L;

    private static long Allocated(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Theory]
    [InlineData("99999999999in")]
    [InlineData("1e300px")]
    [InlineData("100000px")]
    public async Task An_absurd_font_size_is_ignored_rather_than_believed(string size)
    {
        var result = await HtmlImporter.ImportAsync($"<p><span style='font-size:{size}'>x</span></p>", cancellationToken: TestContext.Current.CancellationToken);

        // Finite and positive is not a bound. The model would accept nine trillion pixels and text
        // layout would then be asked to shape it.
        Assert.All(SizesOf(result.Doc), s => Assert.Null(s));
        Assert.Equal("x", TextOf(result.Doc));
    }

    [Theory]
    [InlineData("48px", 48.0)]
    [InlineData("12pt", 16.0)]
    [InlineData("4096px", 4096.0)]
    public async Task A_font_size_a_document_could_really_contain_is_kept(string size, double expected)
    {
        var result = await HtmlImporter.ImportAsync($"<p><span style='font-size:{size}'>x</span></p>", cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(SizesOf(result.Doc), s => Assert.Equal(expected, s!.Value, 3));
    }

    [Theory]
    [InlineData("width='1e300'")]
    [InlineData("width='99999999'")]
    [InlineData("style='width:1e300px'")]
    public async Task An_absurd_image_dimension_falls_back_to_the_intrinsic_size(string attribute)
    {
        var result = await HtmlImporter.ImportAsync($"<img src='{RealPng}' {attribute}>", cancellationToken: TestContext.Current.CancellationToken);

        var embed = Assert.Single(result.Doc.Blocks.OfType<ParagraphNode>().SelectMany(p => p.Content.Embeds));
        Assert.Null(Assert.IsType<ImageEmbed>(embed).Width);
    }

    [Fact]
    public async Task An_image_dimension_a_document_could_really_contain_is_kept()
    {
        var result = await HtmlImporter.ImportAsync($"<img src='{RealPng}' width='640' height='480'>", cancellationToken: TestContext.Current.CancellationToken);

        var embed = Assert.Single(result.Doc.Blocks.OfType<ParagraphNode>().SelectMany(p => p.Content.Embeds));
        var image = Assert.IsType<ImageEmbed>(embed);
        Assert.Equal(640.0, image.Width);
        Assert.Equal(480.0, image.Height);
    }

    // ---- values the model refuses, repaired at the sink (OD-11) ------------------------------

    private static void AssertDowngradedNotLost(ImportResult result, string context)
    {
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Malformed);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == context);
        Assert.Equal("keep", Assert.IsType<ParagraphNode>(result.Doc.Blocks[^1]).Content.Text);
    }

    [Theory]
    [InlineData("a", 0xD800, "b")]
    [InlineData("a", 0xDC00, "b")]
    [InlineData("ab", 0xD800, "")]
    [InlineData("", 0xDC00, "ab")]
    public async Task An_unpaired_surrogate_becomes_a_replacement_character_and_the_rest_survives(
        string before, int unit, string after)
    {
        // The model refuses text that is not well-formed UTF-16, and the refusal used to discard
        // the whole paste. Half a character is now the one thing lost. (The code unit is passed as
        // a number because a lone surrogate does not survive test-case serialization.)
        var result = await HtmlImporter.ImportAsync($"<p>{before}{(char)unit}{after}</p><p>keep</p>", cancellationToken: TestContext.Current.CancellationToken);

        AssertDowngradedNotLost(result, "unpaired-surrogate");
        Assert.Equal(
            $"{before}\uFFFD{after}",
            Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]).Content.Text);
    }

    [Fact]
    public async Task A_low_surrogate_before_a_high_one_is_two_halves_not_a_pair()
    {
        var result = await HtmlImporter.ImportAsync("<p>\uDC00\uD800</p><p>keep</p>", cancellationToken: TestContext.Current.CancellationToken);

        AssertDowngradedNotLost(result, "unpaired-surrogate");
        Assert.Equal("\uFFFD\uFFFD", Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]).Content.Text);
    }

    [Fact]
    public async Task A_whole_surrogate_pair_is_left_alone()
    {
        var result = await HtmlImporter.ImportAsync("<p>a\uD83D\uDE00b</p>", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("a\uD83D\uDE00b", Assert.IsType<ParagraphNode>(Assert.Single(result.Doc.Blocks)).Content.Text);
    }

    [Fact]
    public async Task Formatting_that_starts_inside_a_character_is_moved_to_its_start()
    {
        var result = await HtmlImporter.ImportAsync("<p>x\uD83D<b>\uDE00</b>y</p><p>keep</p>", cancellationToken: TestContext.Current.CancellationToken);

        AssertDowngradedNotLost(result, "split-character");
        var p = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);
        Assert.Equal("x\uD83D\uDE00y", p.Content.Text);
        var bold = Assert.Single(p.Content.Marks);
        Assert.Equal((1, 2), (bold.Start, bold.Length));
        Assert.Equal(TextStyle.Bold, bold.Value.Style);
    }

    [Fact]
    public async Task Formatting_that_ends_inside_a_character_stops_before_it()
    {
        var result = await HtmlImporter.ImportAsync("<p><b>x\uD83D</b>\uDE00y</p><p>keep</p>", cancellationToken: TestContext.Current.CancellationToken);

        AssertDowngradedNotLost(result, "split-character");
        var p = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);
        Assert.Equal("x\uD83D\uDE00y", p.Content.Text);
        var bold = Assert.Single(p.Content.Marks);
        Assert.Equal((0, 1), (bold.Start, bold.Length));
    }

    [Theory]
    [InlineData("margin-left:48px", "margin-left:1e300px")]
    [InlineData("margin-left:48px", "margin-left:103079215104px")]
    [InlineData("padding-left:48px", "padding-left:1e38px")]
    [InlineData("margin-left:96px", "margin-left:1e300px")]
    public async Task A_margin_too_large_to_count_is_the_deepest_indent_not_a_lost_paste(string outer, string inner)
    {
        // The level was computed as int(margin / 48) and only then capped, so the conversion
        // saturated at int.MaxValue and adding the enclosing level wrapped it negative. A
        // paragraph refuses a negative indent, and the refusal discarded everything.
        var result = await HtmlImporter.ImportAsync(
            $"<div style='{outer}'><p style='{inner}'>Important text</p></div><p>more</p>", cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Malformed);
        Assert.Equal(2, result.Doc.Blocks.Length);
        var important = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);
        Assert.Equal("Important text", important.Content.Text);
        Assert.Equal(8, important.IndentLevel);
        Assert.Equal("more", Assert.IsType<ParagraphNode>(result.Doc.Blocks[1]).Content.Text);
    }
}
