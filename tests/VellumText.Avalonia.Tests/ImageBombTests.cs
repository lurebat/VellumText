using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Decompression bombs in every format are refused from their header, before any pixels are
/// allocated, and the post-decode check still guards formats with no header check.
/// </summary>
/// <remarks>
/// Only PNG used to be checked before decoding. The fixtures are the measured bombs from the
/// 2026-10-25 audit: through import and one render, the 38-byte lossless WebP (12000²) peaked at
/// 642 MB, the 287-byte JPEG (SOF patched to 23170²) at 2,075 MB and the 64-byte RLE8 BMP (23170²)
/// at 2,074 MB — each decoded in full, then refused by <c>MaximumPixels</c>.
/// </remarks>
public class ImageBombTests
{
    private static readonly global::Avalonia.Size Fallback = new(12, 12);

    private static TextRunProperties Properties() =>
        new GenericTextRunProperties(new Typeface(FontFamily.Default), fontRenderingEmSize: 12);

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ImageBombs", name));

    /// <summary>An uncompressed 24-bit BMP of the given size.</summary>
    private static byte[] Bmp(int width, int height)
    {
        var stride = ((width * 3) + 3) & ~3;
        var pixels = stride * height;
        var bytes = new byte[54 + pixels];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.TryWriteBytes(bytes.AsSpan(2), bytes.Length);
        BitConverter.TryWriteBytes(bytes.AsSpan(10), 54);
        BitConverter.TryWriteBytes(bytes.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bytes.AsSpan(18), width);
        BitConverter.TryWriteBytes(bytes.AsSpan(22), height);
        BitConverter.TryWriteBytes(bytes.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(28), (short)24);
        BitConverter.TryWriteBytes(bytes.AsSpan(34), pixels);

        return bytes;
    }

    /// <summary>A real PNG of the given size.</summary>
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new global::Avalonia.PixelSize(width, height));
        using var buffer = new MemoryStream();

        bitmap.Save(buffer, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return buffer.ToArray();
    }

    /// <summary>An ICO holding one PNG image: a format with no header check here.</summary>
    private static byte[] Ico(byte[] png, int width, int height)
    {
        var bytes = new byte[22 + png.Length];

        BitConverter.TryWriteBytes(bytes.AsSpan(2), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), (short)1);
        bytes[6] = (byte)width;
        bytes[7] = (byte)height;
        BitConverter.TryWriteBytes(bytes.AsSpan(10), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), (short)32);
        BitConverter.TryWriteBytes(bytes.AsSpan(14), png.Length);
        BitConverter.TryWriteBytes(bytes.AsSpan(18), 22);
        png.CopyTo(bytes, 22);

        return bytes;
    }

    private static ImageEmbed Embed(byte[] bytes, string mediaType) =>
        new(BitmapCacheBudgetTests.DataUri(bytes, mediaType));

    [AvaloniaTheory]
    [InlineData("solid-12000.webp", "image/webp")]
    [InlineData("patched-23170.jpg", "image/jpeg")]
    [InlineData("rle8-23170.bmp", "image/bmp")]
    public void A_bomb_is_refused_without_decoding_it(string file, string mediaType)
    {
        using var renderer = new BitmapEmbedRenderer();
        var bomb = Fixture(file);

        Assert.True(renderer.IsOversized(bomb), $"{file} was not recognised as oversized");
        Assert.Equal(Fallback, renderer.Measure(Embed(bomb, mediaType), Properties()));
        Assert.Equal(0, renderer.PixelDecodes);
    }

    [AvaloniaFact]
    public void A_gif_canvas_over_the_limit_is_refused_without_decoding_it()
    {
        using var renderer = new BitmapEmbedRenderer();
        var gif = BitmapCacheBudgetTests.Gif(23170, 23170);

        Assert.True(renderer.IsOversized(gif));
        Assert.Equal(Fallback, renderer.Measure(Embed(gif, "image/gif"), Properties()));
        Assert.Equal(0, renderer.PixelDecodes);
    }

    [AvaloniaFact]
    public void A_small_bmp_honours_MaximumPixels_from_its_header()
    {
        var bmp = Bmp(8, 8);

        using var strict = new BitmapEmbedRenderer { MaximumPixels = 16 };
        using var generous = new BitmapEmbedRenderer { MaximumPixels = 64 };

        Assert.Equal(Fallback, strict.Measure(Embed(bmp, "image/bmp"), Properties()));
        Assert.Equal(0, strict.PixelDecodes);

        Assert.Equal(new global::Avalonia.Size(8, 8), generous.Measure(Embed(bmp, "image/bmp"), Properties()));
        Assert.Equal(1, generous.PixelDecodes);
    }

    /// <summary>
    /// A format the header reader does not know is still decoded, so the check after decoding is
    /// still its only bound, and must still hold.
    /// </summary>
    [AvaloniaFact]
    public void A_format_without_a_header_check_is_still_refused_after_decoding()
    {
        var ico = Ico(Png(8, 8), 8, 8);

        using var strict = new BitmapEmbedRenderer { MaximumPixels = 16 };
        using var generous = new BitmapEmbedRenderer { MaximumPixels = 64 };

        Assert.False(strict.IsOversized(ico));

        Assert.Equal(new global::Avalonia.Size(8, 8), generous.Measure(Embed(ico, "image/x-icon"), Properties()));
        Assert.Equal(Fallback, strict.Measure(Embed(ico, "image/x-icon"), Properties()));
        Assert.Equal(1, strict.PixelDecodes);
    }

    [AvaloniaTheory]
    [InlineData("webp")]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("png")]
    public void Images_within_the_limit_still_decode(string format)
    {
        using var renderer = new BitmapEmbedRenderer();

        var (bytes, mediaType) = format switch
        {
            "webp" => (Encoded(SkiaSharp.SKEncodedImageFormat.Webp), "image/webp"),
            "jpeg" => (Encoded(SkiaSharp.SKEncodedImageFormat.Jpeg), "image/jpeg"),
            "bmp" => (Bmp(30, 20), "image/bmp"),
            "gif" => (BitmapCacheBudgetTests.Gif(30, 20), "image/gif"),
            _ => (Png(30, 20), "image/png"),
        };

        Assert.False(renderer.IsOversized(bytes));
        Assert.Equal(new global::Avalonia.Size(30, 20), renderer.Measure(Embed(bytes, mediaType), Properties()));

        static byte[] Encoded(SkiaSharp.SKEncodedImageFormat format)
        {
            using var bitmap = new SkiaSharp.SKBitmap(30, 20);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 90);

            return data.ToArray();
        }
    }
}
