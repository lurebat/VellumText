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
/// This is not a decoder and does not validate anything: it reads the numbers the decoder will
/// size its canvas from, where each format puts them, and answers "unknown" for anything it does
/// not recognise or that is cut off. The renderer refuses an unknown image undecoded rather than
/// decoding it to find out, so every format the decoder (Skia) can open needs a reader here, or
/// it is never shown. TIFF has none, on the assumption, true when this was written, that Skia
/// has no TIFF codec to show one with; if that changes, a TIFF is still refused, never decoded
/// unchecked.
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
        || TryWebP(payload, out width, out height)
        || TryIco(payload, out width, out height)
        || TryWbmp(payload, out width, out height);

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
    private static bool TryBmp(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        if (p.Length < 26 || p[0] != (byte)'B' || p[1] != (byte)'M')
        {
            return false;
        }

        return TryDib(p[14..], out width, out height);
    }

    /// <summary>The size in a BMP's DIB header, which is also how an ICO stores a bitmap image.</summary>
    /// <remarks>
    /// A negative height means the rows are stored top-down; the image is just as tall. The
    /// 12-byte OS/2 core header stores both as 16-bit; every later header as 32-bit.
    /// </remarks>
    private static bool TryDib(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        if (p.Length < 12)
        {
            return false;
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(p[..4]);

        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(p[4..6]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(p[6..8]);

            return true;
        }

        width = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(p[4..8]));
        height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(p[8..12]));

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
            // libjpeg discards whatever between segments is not a marker - a stray byte, a fill
            // 0xFF, a stuffed 0xFF00 - and reads on, so one inserted byte must not hide the frame
            // header from this when it does not hide it from the decoder.
            if (p[i] != 0xFF || p[i + 1] is 0xFF or 0x00)
            {
                i++;
                continue;
            }

            var marker = p[i + 1];

            switch (marker)
            {
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

    /// <summary>ICO and CUR: the largest of the images the directory lists.</summary>
    /// <remarks>
    /// The decoder opens the largest image, and each one states its own size: an embedded PNG in
    /// its IHDR, a bitmap in its DIB header. The directory's one-byte sizes are not used, because
    /// the decoder does not use them either. A bitmap's DIB height counts its transparency mask
    /// too, so it is twice what is decoded; counting it whole only errs towards refusing. One
    /// image this cannot size makes the whole icon unknown, since that image may be the one
    /// decoded.
    /// </remarks>
    private static bool TryIco(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        if (p.Length < 6
            || BinaryPrimitives.ReadUInt16LittleEndian(p[..2]) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(p[2..4]) is not (1 or 2))
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(p[4..6]);

        if (count == 0 || 6 + (count * 16) > p.Length)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(p[(6 + (i * 16) + 12)..]);

            if (offset >= (uint)p.Length)
            {
                return false;
            }

            var image = p[(int)offset..];

            if (!TryPng(image, out var imageWidth, out var imageHeight)
                && !TryDib(image, out imageWidth, out imageHeight))
            {
                return false;
            }

            if (imageWidth * imageHeight > width * height)
            {
                (width, height) = (imageWidth, imageHeight);
            }
        }

        return true;
    }

    /// <summary>WBMP: type 0, then the width and height as multi-byte integers.</summary>
    private static bool TryWbmp(ReadOnlySpan<byte> p, out long width, out long height)
    {
        (width, height) = (0, 0);

        // The only type is 0, and Skia refuses a fixed header byte announcing extension headers.
        if (p.Length < 4 || p[0] != 0 || (p[1] & 0x9F) != 0)
        {
            return false;
        }

        var i = 2;

        return TryMultiByte(p, ref i, out width) && TryMultiByte(p, ref i, out height);

        // Seven bits a byte, most significant first, the top bit set on all but the last. Skia
        // refuses anything over 16 bits, so a few bytes is all a real one has.
        static bool TryMultiByte(ReadOnlySpan<byte> p, ref int i, out long value)
        {
            value = 0;

            for (var length = 0; length < 5 && i < p.Length; length++)
            {
                var b = p[i++];

                value = (value << 7) | (b & 0x7FL);

                if ((b & 0x80) == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
