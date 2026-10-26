using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The decoded-image cache is bounded by the memory it holds, not only by how many images.
/// </summary>
/// <remarks>
/// A GIF states its canvas size in its first ten bytes and is decoded to that canvas whatever its
/// frames hold, so a forty-byte GIF can decode to hundreds of megabytes. The cache used to bound
/// only the <em>count</em> of images (64), and the renderer every presenter shares is
/// process-wide: about 1 KB of pasted tiny GIFs pinned gigabytes, across every clipboard item a
/// history had ever shown.
/// </remarks>
public class BitmapCacheBudgetTests
{
    /// <summary>
    /// A valid GIF whose canvas is <paramref name="width"/>×<paramref name="height"/> and whose
    /// one frame is a single pixel; <paramref name="variant"/> makes otherwise equal sources
    /// distinct.
    /// </summary>
    internal static byte[] Gif(int width, int height, byte variant = 0) =>
    [
        (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
        (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8),
        0x80, 0x00, 0x00,

        // Global colour table: two entries, the first one varied.
        variant, 0xFF, 0xFF, 0x00, 0x00, 0x00,

        // Image descriptor: one pixel at the origin, no local table.
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,

        // LZW minimum code size 2, one sub-block of image data, terminator.
        0x02, 0x02, 0x44, 0x01, 0x00,
        0x3B,
    ];

    internal static string DataUri(byte[] bytes, string mediaType) =>
        $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

    private static TextRunProperties Properties() =>
        new GenericTextRunProperties(new Typeface(FontFamily.Default), fontRenderingEmSize: 12);

    private static long PrivateBytesAfterCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();

        return process.PrivateMemorySize64;
    }

    [AvaloniaFact]
    public void A_tiny_gif_decodes_to_its_declared_canvas()
    {
        // The fixture the other tests rely on: if Skia stopped honouring the canvas size, they
        // would pass without testing anything.
        using var renderer = new BitmapEmbedRenderer();

        Assert.Equal(
            new global::Avalonia.Size(300, 200),
            renderer.Measure(new ImageEmbed(DataUri(Gif(300, 200), "image/gif")), Properties()));
    }

    /// <summary>
    /// Ten distinct 4000² GIFs are about 640 MB decoded; with the default budget the cache must
    /// let go of all but 256 MiB of them.
    /// </summary>
    [AvaloniaFact]
    public void Many_large_images_do_not_stay_decoded_past_the_default_budget()
    {
        using var renderer = new BitmapEmbedRenderer();
        var before = PrivateBytesAfterCollection();

        for (byte i = 0; i < 10; i++)
        {
            renderer.Measure(new ImageEmbed(DataUri(Gif(4000, 4000, i), "image/gif")), Properties());
        }

        var held = (PrivateBytesAfterCollection() - before) / (1024 * 1024);

        Assert.True(held < 256 + 100, $"the cache held {held} MB after ten 4000x4000 images");
    }

    [AvaloniaFact]
    public void The_cache_stays_within_its_byte_budget()
    {
        // 1000x1000 at four bytes a pixel is 4,000,000 bytes, so three of them fit in 12 MB.
        using var renderer = new BitmapEmbedRenderer { CacheBudgetBytes = 12_000_000 };

        for (byte i = 0; i < 8; i++)
        {
            renderer.Measure(new ImageEmbed(DataUri(Gif(1000, 1000, i), "image/gif")), Properties());

            Assert.True(renderer.CachedBytes <= 12_000_000, $"held {renderer.CachedBytes} bytes");
        }

        Assert.Equal(3, renderer.CachedCount);
        Assert.Equal(12_000_000, renderer.CachedBytes);
    }

    /// <summary>The image used most recently is the last to go.</summary>
    [AvaloniaFact]
    public void Eviction_is_least_recently_used()
    {
        using var renderer = new BitmapEmbedRenderer { CacheBudgetBytes = 8_000_000 };
        var a = new ImageEmbed(DataUri(Gif(1000, 1000, 1), "image/gif"));
        var b = new ImageEmbed(DataUri(Gif(1000, 1000, 2), "image/gif"));
        var c = new ImageEmbed(DataUri(Gif(1000, 1000, 3), "image/gif"));

        renderer.Measure(a, Properties());
        renderer.Measure(b, Properties());
        renderer.Measure(a, Properties());
        renderer.Measure(c, Properties());

        var decodes = renderer.DecodeCount;

        renderer.Measure(a, Properties());
        Assert.Equal(decodes, renderer.DecodeCount);

        renderer.Measure(b, Properties());
        Assert.Equal(decodes + 1, renderer.DecodeCount);
    }

    /// <summary>
    /// An image larger than the whole budget is still kept, alone: an image the cache does not own
    /// is decoded again on every layout pass and never disposed.
    /// </summary>
    [AvaloniaFact]
    public void An_image_larger_than_the_budget_is_held_alone_rather_than_decoded_every_pass()
    {
        using var renderer = new BitmapEmbedRenderer { CacheBudgetBytes = 1 };
        var small = new ImageEmbed(DataUri(Gif(10, 10, 1), "image/gif"));
        var large = new ImageEmbed(DataUri(Gif(100, 100, 2), "image/gif"));

        renderer.Measure(small, Properties());
        renderer.Measure(large, Properties());

        var decodes = renderer.DecodeCount;

        renderer.Measure(large, Properties());
        renderer.Measure(large, Properties());

        Assert.Equal(decodes, renderer.DecodeCount);
        Assert.Equal(1, renderer.CachedCount);
    }
}
