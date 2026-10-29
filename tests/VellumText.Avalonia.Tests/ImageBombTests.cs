using System.Buffers.Binary;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Decompression bombs in every format are refused from their header, before any pixels are
/// allocated, and a payload whose size cannot be read from its header is refused undecoded.
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

    /// <summary>A PNG whose IHDR claims the given size, with its checksum fixed so the decoder believes it.</summary>
    private static byte[] PatchedPng(int width, int height)
    {
        var png = Png(1, 1);

        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), Crc32(png.AsSpan(12, 17)));

        return png;

        static uint Crc32(ReadOnlySpan<byte> bytes)
        {
            var crc = 0xFFFFFFFFu;

            foreach (var b in bytes)
            {
                crc ^= b;

                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }

            return ~crc;
        }
    }

    /// <summary>
    /// <paramref name="jpeg"/> with <paramref name="stray"/> inserted before its frame header, where
    /// libjpeg discards it.
    /// </summary>
    private static byte[] StrayBytesBeforeSof(byte[] jpeg, params byte[] stray)
    {
        var sof = 2;

        while (!(jpeg[sof] == 0xFF && jpeg[sof + 1] is 0xC0 or 0xC1 or 0xC2))
        {
            sof++;
        }

        return [.. jpeg[..sof], .. stray, .. jpeg[sof..]];
    }

    /// <summary>A little-endian, uncompressed, 8-bit grey TIFF claiming the given size.</summary>
    private static byte[] Tiff(int width, int height)
    {
        (ushort Tag, ushort Type, int Value)[] entries =
        [
            (256, 4, width), (257, 4, height), (258, 3, 8), (259, 3, 1), (262, 3, 1),
            (273, 4, 8 + 2 + (9 * 12) + 4), (277, 3, 1), (278, 4, height), (279, 4, 4),
        ];

        var bytes = new byte[8 + 2 + (entries.Length * 12) + 4 + 4];

        "II"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)entries.Length);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = bytes.AsSpan(10 + (i * 12));

            BinaryPrimitives.WriteUInt16LittleEndian(entry, entries[i].Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], entries[i].Type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], entries[i].Value);
        }

        return bytes;
    }

    /// <summary>A WBMP: one bit a pixel, its size as two multi-byte integers after a two-byte header.</summary>
    private static byte[] Wbmp(int width, int height) =>
        [0x00, 0x00, (byte)width, (byte)height, .. new byte[(width + 7) / 8 * height]];

    /// <summary>An ICO holding one PNG image.</summary>
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

    /// <summary>
    /// A GIF with a 1×1 logical screen whose only frame is one pixel at (<paramref name="left"/>,
    /// <paramref name="top"/>), behind a graphic control extension the reader must step over.
    /// </summary>
    private static byte[] OffsetGif(int left, int top) =>
    [
        (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
        0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
        0x21, 0xF9, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2C, (byte)left, (byte)(left >> 8), (byte)top, (byte)(top >> 8), 0x01, 0x00, 0x01, 0x00, 0x00,
        0x02, 0x02, 0x4C, 0x01, 0x00,
        0x3B,
    ];

    /// <summary>
    /// How far a decode may raise the process's peak working set and still count as not having
    /// allocated the bomb: every bomb here decodes to 400 MB or more.
    /// </summary>
    private const long BombHeadroom = 200L * 1024 * 1024;

    /// <summary>How much <paramref name="action"/> raised the process's peak working set.</summary>
    /// <remarks>
    /// The decoder's pixels are native memory, so the managed allocation counters do not see them,
    /// and the bitmap is disposed before the call returns, so only the peak shows it was there.
    /// </remarks>
    private static long PeakGrowth(Action action)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var before = process.PeakWorkingSet64;

        action();
        process.Refresh();

        return process.PeakWorkingSet64 - before;
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

    /// <summary>
    /// A GIF whose 1×1 logical screen holds a 1×1 first frame at (9999, 9999): the decoder grows
    /// the canvas to cover that frame, so it is 10000², not the 1² its screen declares.
    /// </summary>
    [AvaloniaFact]
    public void A_gif_first_frame_outside_its_screen_is_refused_without_decoding_it()
    {
        using var renderer = new BitmapEmbedRenderer();
        var gif = OffsetGif(9999, 9999);

        var grown = PeakGrowth(() =>
            Assert.Equal(Fallback, renderer.Measure(Embed(gif, "image/gif"), Properties())));

        Assert.Equal(0, renderer.PixelDecodes);
        Assert.True(grown < BombHeadroom, $"peak working set grew by {grown / (1024 * 1024)} MB");
        Assert.True(renderer.IsOversized(gif));
    }

    [AvaloniaFact]
    public void A_gif_first_frame_outside_its_screen_within_the_limit_decodes_to_the_grown_canvas()
    {
        using var renderer = new BitmapEmbedRenderer();
        var gif = OffsetGif(29, 19);

        Assert.False(renderer.IsOversized(gif));
        Assert.Equal(new global::Avalonia.Size(30, 20), renderer.Measure(Embed(gif, "image/gif"), Properties()));
        Assert.Equal(1, renderer.PixelDecodes);
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
    /// Payloads whose size the header reader used to miss, and which were therefore decoded in full
    /// before anything checked them: a JPEG with one stray byte before its frame header (libjpeg
    /// skips it; the reader gave up), an ICO wrapping a bomb, and a TIFF, which has no reader and
    /// no decoder here, and so is refused unread rather than handed over to find out.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("jpeg")]
    [InlineData("ico")]
    [InlineData("tiff")]
    public void A_bomb_whose_size_the_header_reader_used_to_miss_is_refused_without_decoding_it(string format)
    {
        using var renderer = new BitmapEmbedRenderer();

        var (bomb, mediaType) = format switch
        {
            "jpeg" => (StrayBytesBeforeSof(Fixture("patched-23170.jpg"), 0x00), "image/jpeg"),
            "ico" => (Ico(PatchedPng(23170, 23170), 0, 0), "image/x-icon"),
            _ => (Tiff(23170, 23170), "image/tiff"),
        };

        var grown = PeakGrowth(() =>
            Assert.Equal(Fallback, renderer.Measure(Embed(bomb, mediaType), Properties())));

        Assert.Equal(0, renderer.PixelDecodes);
        Assert.True(grown < BombHeadroom, $"peak working set grew by {grown / (1024 * 1024)} MB");
    }

    [AvaloniaFact]
    public void A_jpeg_with_stray_bytes_before_its_frame_header_is_still_sized_from_it()
    {
        using var renderer = new BitmapEmbedRenderer();

        Assert.True(renderer.IsOversized(StrayBytesBeforeSof(Fixture("patched-23170.jpg"), 0x00)));
        Assert.True(renderer.IsOversized(StrayBytesBeforeSof(Fixture("patched-23170.jpg"), 0x12, 0xFF, 0x00, 0x34)));
    }

    [AvaloniaFact]
    public void An_ico_honours_MaximumPixels_from_its_header()
    {
        // This used to be decoded and refused afterwards (PixelDecodes == 1): ICO had no header
        // reader. It has one now, and a format without one is refused undecoded.
        var ico = Ico(Png(8, 8), 8, 8);

        using var strict = new BitmapEmbedRenderer { MaximumPixels = 16 };
        using var generous = new BitmapEmbedRenderer { MaximumPixels = 64 };

        Assert.True(strict.IsOversized(ico));

        Assert.Equal(new global::Avalonia.Size(8, 8), generous.Measure(Embed(ico, "image/x-icon"), Properties()));
        Assert.Equal(Fallback, strict.Measure(Embed(ico, "image/x-icon"), Properties()));
        Assert.Equal(0, strict.PixelDecodes);
    }

    /// <summary>The largest image in an icon is the one the decoder picks, so it is the one counted.</summary>
    [AvaloniaFact]
    public void An_ico_is_sized_by_its_largest_image()
    {
        var small = Png(2, 2);
        var large = PatchedPng(23170, 23170);
        var bytes = new byte[6 + 32 + small.Length + large.Length];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6 + 8), small.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6 + 12), 38);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22 + 8), large.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22 + 12), 38 + small.Length);
        small.CopyTo(bytes, 38);
        large.CopyTo(bytes, 38 + small.Length);

        using var renderer = new BitmapEmbedRenderer();

        Assert.Equal(Fallback, renderer.Measure(Embed(bytes, "image/x-icon"), Properties()));
        Assert.Equal(0, renderer.PixelDecodes);
    }

    /// <summary>
    /// A local source does not go through the header check, so the check after decoding is still
    /// its bound. Inline images used to exercise it too; they are refused before decoding now.
    /// </summary>
    [AvaloniaFact]
    public void A_local_image_over_the_limit_is_still_refused_after_decoding()
    {
        var file = Path.Combine(Path.GetTempPath(), $"VellumText-{Guid.NewGuid():N}.png");

        try
        {
            File.WriteAllBytes(file, Png(8, 8));

            using var strict = new BitmapEmbedRenderer { AllowLocalSources = true, MaximumPixels = 16 };
            using var generous = new BitmapEmbedRenderer { AllowLocalSources = true, MaximumPixels = 64 };

            Assert.Equal(new global::Avalonia.Size(8, 8), generous.Measure(new ImageEmbed(file), Properties()));
            Assert.Equal(Fallback, strict.Measure(new ImageEmbed(file), Properties()));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaTheory]
    [InlineData("webp")]
    [InlineData("jpeg")]
    [InlineData("jpeg-stray-byte")]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("png")]
    [InlineData("ico")]
    [InlineData("wbmp")]
    public void Images_within_the_limit_still_decode(string format)
    {
        using var renderer = new BitmapEmbedRenderer();

        var (bytes, mediaType) = format switch
        {
            "webp" => (Encoded(SkiaSharp.SKEncodedImageFormat.Webp), "image/webp"),
            "jpeg" => (Encoded(SkiaSharp.SKEncodedImageFormat.Jpeg), "image/jpeg"),
            "jpeg-stray-byte" => (StrayBytesBeforeSof(Encoded(SkiaSharp.SKEncodedImageFormat.Jpeg), 0x00), "image/jpeg"),
            "bmp" => (Bmp(30, 20), "image/bmp"),
            "gif" => (BitmapCacheBudgetTests.Gif(30, 20), "image/gif"),
            "ico" => (Ico(Png(30, 20), 30, 20), "image/x-icon"),
            "wbmp" => (Wbmp(30, 20), "image/vnd.wap.wbmp"),
            _ => (Png(30, 20), "image/png"),
        };

        Assert.False(renderer.IsOversized(bytes));
        Assert.Equal(new global::Avalonia.Size(30, 20), renderer.Measure(Embed(bytes, mediaType), Properties()));
        Assert.Equal(1, renderer.PixelDecodes);

        static byte[] Encoded(SkiaSharp.SKEncodedImageFormat format)
        {
            using var bitmap = new SkiaSharp.SKBitmap(30, 20);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 90);

            return data.ToArray();
        }
    }
}
