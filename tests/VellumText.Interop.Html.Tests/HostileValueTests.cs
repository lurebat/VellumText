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
    public void Text_carrying_the_embed_placeholder_keeps_the_rest_of_the_paragraph()
    {
        // U+FFFC is what Word writes for an object it cannot otherwise express, and what any
        // content that already passed through a rich text model carries. Treating it as content
        // makes the placeholder count disagree with the embed count, which the model rejects —
        // and the whole import used to be discarded because of one character.
        var result = HtmlImporter.Import("<p>before\uFFFCafter</p>");

        Assert.False(result.IsEmpty);
        Assert.Equal("beforeafter", TextOf(result.Doc));
        Assert.DoesNotContain(InlineContent.Placeholder, TextOf(result.Doc));
    }

    [Fact]
    public void A_smuggled_placeholder_does_not_disturb_a_real_embed_beside_it()
    {
        var result = HtmlImporter.Import($"<p>a\uFFFCb<img src='{RealPng}'></p>");

        Assert.False(result.IsEmpty);
        Assert.Equal(1, EmbedsOf(result.Doc));

        // Exactly one placeholder, and it belongs to the image rather than to the text.
        Assert.Equal("ab\uFFFC", TextOf(result.Doc));
    }

    [Fact]
    public void A_paragraph_that_is_nothing_but_placeholders_survives_as_an_empty_one()
    {
        var result = HtmlImporter.Import("<p>\uFFFC\uFFFC\uFFFC</p><p>kept</p>");

        Assert.Contains("kept", TextOf(result.Doc));
        Assert.DoesNotContain(InlineContent.Placeholder, TextOf(result.Doc));
        Assert.True(DocumentSchema.IsValid(result.Doc));
    }

    [Theory]
    [InlineData("####not-base64####")]
    [InlineData("iVBORw0KGgo")] // A length that is not a multiple of four and has no padding.
    [InlineData("")]
    [InlineData("=")]
    public void An_inline_image_whose_data_does_not_decode_is_dropped(string payload)
    {
        var result = HtmlImporter.Import($"<img src='data:image/png;base64,{payload}'>");

        // Left in, it would reach the document as an embed and fail at draw time instead, far from
        // anything that could explain it.
        Assert.Equal(0, EmbedsOf(result.Doc));
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Dropped);
    }

    [Fact]
    public void An_inline_image_whose_data_does_decode_is_kept()
    {
        var result = HtmlImporter.Import($"<img src='{RealPng}'>");

        Assert.Equal(1, EmbedsOf(result.Doc));
    }

    [Fact]
    public void The_size_limit_is_measured_in_decoded_bytes_not_url_characters()
    {
        // 96 bytes of image encode to 128 characters of base64, plus a 22-character header.
        var bytes = new byte[96];
        Random.Shared.NextBytes(bytes);
        var url = "data:image/png;base64," + Convert.ToBase64String(bytes);

        Assert.True(url.Length > 100, "the URL has to be longer than the limit for this to mean anything");

        var options = new HtmlImportOptions { MaxDataImageBytes = 100 };
        var result = HtmlImporter.Import($"<img src='{url}'>", options);

        // Judged by URL length it is over the limit; judged by the limit's own name it is under.
        Assert.Equal(1, EmbedsOf(result.Doc));
    }

    [Fact]
    public void An_image_over_the_decoded_size_limit_is_still_dropped()
    {
        var bytes = new byte[512];
        Random.Shared.NextBytes(bytes);
        var url = "data:image/png;base64," + Convert.ToBase64String(bytes);

        var result = HtmlImporter.Import($"<img src='{url}'>", new HtmlImportOptions { MaxDataImageBytes = 100 });

        Assert.Equal(0, EmbedsOf(result.Doc));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("larger than", StringComparison.Ordinal));
    }

    [Fact]
    public void A_vast_base64_payload_is_rejected_without_being_decoded()
    {
        // The bound has to be applied before room is allocated for the result, or a hostile paste
        // buys a large allocation for the price of a string.
        var url = "data:image/png;base64," + new string('A', 4 * 1024 * 1024);

        var result = HtmlImporter.Import($"<img src='{url}'>", new HtmlImportOptions { MaxDataImageBytes = 1024 });

        Assert.Equal(0, EmbedsOf(result.Doc));
    }

    [Theory]
    [InlineData("99999999999in")]
    [InlineData("1e300px")]
    [InlineData("100000px")]
    public void An_absurd_font_size_is_ignored_rather_than_believed(string size)
    {
        var result = HtmlImporter.Import($"<p><span style='font-size:{size}'>x</span></p>");

        // Finite and positive is not a bound. The model would accept nine trillion pixels and text
        // layout would then be asked to shape it.
        Assert.All(SizesOf(result.Doc), s => Assert.Null(s));
        Assert.Equal("x", TextOf(result.Doc));
    }

    [Theory]
    [InlineData("48px", 48.0)]
    [InlineData("12pt", 16.0)]
    [InlineData("4096px", 4096.0)]
    public void A_font_size_a_document_could_really_contain_is_kept(string size, double expected)
    {
        var result = HtmlImporter.Import($"<p><span style='font-size:{size}'>x</span></p>");

        Assert.All(SizesOf(result.Doc), s => Assert.Equal(expected, s!.Value, 3));
    }

    [Theory]
    [InlineData("width='1e300'")]
    [InlineData("width='99999999'")]
    [InlineData("style='width:1e300px'")]
    public void An_absurd_image_dimension_falls_back_to_the_intrinsic_size(string attribute)
    {
        var result = HtmlImporter.Import($"<img src='{RealPng}' {attribute}>");

        var embed = Assert.Single(result.Doc.Blocks.OfType<ParagraphNode>().SelectMany(p => p.Content.Embeds));
        Assert.Null(Assert.IsType<ImageEmbed>(embed).Width);
    }

    [Fact]
    public void An_image_dimension_a_document_could_really_contain_is_kept()
    {
        var result = HtmlImporter.Import($"<img src='{RealPng}' width='640' height='480'>");

        var embed = Assert.Single(result.Doc.Blocks.OfType<ParagraphNode>().SelectMany(p => p.Content.Embeds));
        var image = Assert.IsType<ImageEmbed>(embed);
        Assert.Equal(640.0, image.Width);
        Assert.Equal(480.0, image.Height);
    }
}
