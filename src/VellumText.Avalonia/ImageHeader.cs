using System.Buffers.Binary;

namespace VellumText.Avalonia;

/// <summary>
/// Reads the pixel dimensions an encoded image declares, without decoding it.
/// </summary>
/// <remarks>
/// <para>
/// A few bytes of image can describe an enormous one: a 38-byte WebP decodes to 576 MB, and a
/// 64-byte run-length BMP or a 287-byte JPEG to 2 GB. Checking the decoded bitmap is too late,
/// because the allocation is what the bomb is for. Every format the renderer is likely to meet
/// states its size near the start, so it is read from there and an oversized image is refused
/// before anything is allocated for it.
/// </para>
/// <para>
/// This is not a decoder and does not validate anything: it reads two numbers from where each
/// format puts them, and answers "unknown" for anything it does not recognise or that is cut off.
/// An unknown image is still decoded, and still checked after decoding, exactly as before.
/// </para>
/// </remarks>
internal static class ImageHeader
{
    /// <summary>The width and height <paramref name="payload"/> declares, if its format is known.</summary>
    /// <param name="payload">The encoded image.</param>
    /// <param name="width">The declared width in pixels.</param>
    /// <param name="height">The declared height in pixels.</param>
    /// <returns>Whether the format was recognised and its dimensions read.</returns>
    internal static bool TryReadSize(ReadOnlySpan<byte> payload, out long width, out long height) =>
        TryPng(payload, out width, out height)
        || TryGif(payload, out width, out height)
        || TryBmp(payload, out width, out height)
        || TryJpeg(payload, out width, out height)
        || TryWebP(payload, out width, out height);

    /// <summary>PNG: the IHDR chunk is always first, at a fixed offset.</summary>
    private static bool TryPng(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        if (p.Length < 24 || !p[..8].SequenceEqual(signature))
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt32BigEndian(p[16..20]);
        height = BinaryPrimitives.ReadUInt32BigEndian(p[20..24]);

        return true;
    }

    /// <summary>
    /// GIF: the logical screen, grown to cover the first frame, which is the canvas the decoder
    /// allocates.
    /// </summary>
    /// <remarks>
    /// The screen alone is not enough: Skia's decoder widens its canvas to reach the first frame's
    /// far edge, so a 1×1 screen holding one pixel at (9999, 9999) decodes to 10000². Later frames
    /// are clipped to that canvas and cannot grow it. A GIF cut off before its first frame is
    /// answered as unknown, since the canvas it would get cannot be told.
    /// </remarks>
    private static bool TryGif(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        if (p.Length < 13 || !(p[..6].SequenceEqual("GIF87a"u8) || p[..6].SequenceEqual("GIF89a"u8)))
        {
            return false;
        }

        long screenWidth = BinaryPrimitives.ReadUInt16LittleEndian(p[6..8]);
        long screenHeight = BinaryPrimitives.ReadUInt16LittleEndian(p[8..10]);
        var i = 13 + ColourTableLength(p[10]);

        while (i < p.Length)
        {
            switch (p[i])
            {
                // An extension: a label, then data sub-blocks up to an empty one.
                case 0x21:
                    i += 2;

                    while (i < p.Length && p[i] != 0)
                    {
                        i += 1 + p[i];
                    }

                    i++;
                    continue;

                // The first image descriptor: its position and size.
                case 0x2C when i + 9 <= p.Length:
                    var left = BinaryPrimitives.ReadUInt16LittleEndian(p[(i + 1)..(i + 3)]);
                    var top = BinaryPrimitives.ReadUInt16LittleEndian(p[(i + 3)..(i + 5)]);
                    var frameWidth = BinaryPrimitives.ReadUInt16LittleEndian(p[(i + 5)..(i + 7)]);
                    var frameHeight = BinaryPrimitives.ReadUInt16LittleEndian(p[(i + 7)..(i + 9)]);

                    width = Math.Max(screenWidth, (long)left + frameWidth);
                    height = Math.Max(screenHeight, (long)top + frameHeight);

                    return true;

                // The trailer, with no frame before it: nothing grows the screen.
                case 0x3B:
                    (width, height) = (screenWidth, screenHeight);

                    return true;

                default:
                    return false;
            }
        }

        return false;

        // The global table follows the screen descriptor when its flag is set: three bytes for each
        // of 2^(n+1) colours.
        static int ColourTableLength(byte flags) => (flags & 0x80) == 0 ? 0 : 3 << ((flags & 0x07) + 1);
    }

    /// <summary>BMP: the DIB header after the 14-byte file header.</summary>
    /// <remarks>
    /// A negative height means the rows are stored top-down; the image is just as tall. The
    /// 12-byte OS/2 core header stores both as 16-bit; every later header as 32-bit.
    /// </remarks>
    private static bool TryBmp(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        if (p.Length < 26 || p[0] != (byte)'B' || p[1] != (byte)'M')
        {
            return false;
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(p[14..18]);

        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(p[18..20]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(p[20..22]);

            return true;
        }

        width = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(p[18..22]));
        height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(p[22..26]));

        return true;
    }

    /// <summary>JPEG: the first start-of-frame segment, found by stepping over the ones before it.</summary>
    private static bool TryJpeg(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        if (p.Length < 4 || p[0] != 0xFF || p[1] != 0xD8)
        {
            return false;
        }

        var i = 2;

        while (i + 4 <= p.Length)
        {
            if (p[i] != 0xFF)
            {
                return false;
            }

            var marker = p[i + 1];

            switch (marker)
            {
                // Fill byte before a marker.
                case 0xFF:
                    i++;
                    continue;

                // Markers that stand alone, with no length.
                case 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7):
                    i += 2;
                    continue;

                // Scan data or the end before any frame header: nothing to read.
                case 0xD9 or 0xDA:
                    return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(p[(i + 2)..(i + 4)]);

            if (length < 2)
            {
                return false;
            }

            // SOF0–SOF15, except DHT (C4), JPG (C8) and DAC (CC), which share the range.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (i + 9 > p.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(p[(i + 5)..(i + 7)]);
                width = BinaryPrimitives.ReadUInt16BigEndian(p[(i + 7)..(i + 9)]);

                return true;
            }

            i += 2 + length;
        }

        return false;
    }

    /// <summary>WebP: the first chunk of the RIFF container, in whichever of its three forms.</summary>
    private static bool TryWebP(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        if (p.Length < 30 || !p[..4].SequenceEqual("RIFF"u8) || !p[8..12].SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var chunk = p[12..16];

        // Lossy: a VP8 key frame, 14-bit dimensions after its start code.
        if (chunk.SequenceEqual("VP8 "u8))
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(p[26..28]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(p[28..30]) & 0x3FFF;

            return true;
        }

        // Lossless: 14-bit width and height, each minus one, packed after a signature byte.
        if (chunk.SequenceEqual("VP8L"u8))
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(p[21..25]);

            width = (bits & 0x3FFF) + 1;
            height = ((bits >> 14) & 0x3FFF) + 1;

            return true;
        }

        // Extended: a 24-bit canvas size, each minus one.
        if (chunk.SequenceEqual("VP8X"u8))
        {
            width = (p[24] | (p[25] << 8) | (p[26] << 16)) + 1L;
            height = (p[27] | (p[28] << 8) | (p[29] << 16)) + 1L;

            return true;
        }

        return false;
    }
}
