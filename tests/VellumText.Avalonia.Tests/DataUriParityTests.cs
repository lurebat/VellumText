using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The importer admits a <c>data:</c> image exactly when the renderer can draw it.
/// </summary>
/// <remarks>
/// The sanitizer and the renderer each parsed <c>data:</c> URLs by their own rules, so a URL with
/// leading whitespace, or with a parameter after <c>;base64</c>, was imported without a diagnostic
/// and then drawn as a missing-image box. Both now read them through one parser.
/// </remarks>
public class DataUriParityTests
{
    private static readonly string Payload = PngBase64(3, 2);

    private static string PngBase64(int width, int height)
    {
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new global::Avalonia.PixelSize(width, height));
        using var buffer = new MemoryStream();

        bitmap.Save(buffer, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return Convert.ToBase64String(buffer.ToArray());
    }

    private static TextRunProperties Properties() =>
        new GenericTextRunProperties(new Typeface(FontFamily.Default), fontRenderingEmSize: 12);

    private static bool Draws(BitmapEmbedRenderer renderer, string source) =>
        renderer.Measure(new ImageEmbed(source), Properties()) == new global::Avalonia.Size(3, 2);

    [AvaloniaTheory]
    [InlineData("data:image/png;base64,{0}", true)]
    [InlineData("data:image/png;name=a.png;base64,{0}", true)]
    [InlineData("DATA:IMAGE/PNG;BASE64,{0}", true)]
    [InlineData(" data:image/png;base64,{0}", true)]
    [InlineData("\tdata:image/png;base64,{0} ", true)]
    [InlineData("data:image/png;base64;x=y,{0}", false)]
    [InlineData("data:image/png;base64x,{0}", false)]
    [InlineData("data:image/png,{0}", false)]
    public async Task An_image_the_importer_admits_is_one_the_renderer_draws(string template, bool admitted)
    {
        var src = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Payload);
        var result = await HtmlImporter.ImportAsync($"<p><img src=\"{src}\"></p>");
        var images = result.Doc.Blocks
            .OfType<ParagraphNode>()
            .SelectMany(p => p.Content.Embeds)
            .OfType<ImageEmbed>()
            .ToList();

        using var renderer = new BitmapEmbedRenderer();

        Assert.Equal(admitted, images.Count == 1);

        if (admitted)
        {
            Assert.True(Draws(renderer, images[0].Source), $"admitted '{images[0].Source[..24]}…' draws as a box");
        }
        else
        {
            // Refused at the door, saying why, and not something the renderer would have drawn.
            Assert.Contains(
                result.Diagnostics,
                d => d.Severity == DiagnosticSeverity.Dropped
                    && d.Message.Contains("base64", StringComparison.Ordinal));
            Assert.False(Draws(renderer, src));
        }
    }

    [AvaloniaFact]
    public async Task What_the_image_store_saves_the_renderer_draws_and_the_importer_admits()
    {
        var source = new DataUriImageStore().Save(Convert.FromBase64String(Payload), "image/png");

        Assert.NotNull(source);
        Assert.True(DataUri.TryParse(source, out var mediaType, out _));
        Assert.Equal("image/png", mediaType);

        using var renderer = new BitmapEmbedRenderer();

        Assert.True(Draws(renderer, source));

        var images = (await HtmlImporter.ImportAsync($"<p><img src=\"{source}\"></p>")).Doc.Blocks
            .OfType<ParagraphNode>()
            .SelectMany(p => p.Content.Embeds)
            .OfType<ImageEmbed>();

        Assert.Equal(source, Assert.Single(images).Source);
    }
}
