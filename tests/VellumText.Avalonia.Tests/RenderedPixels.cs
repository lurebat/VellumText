using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Reads a rendered bitmap's pixels in a known channel order, whatever order the platform
/// stores them in.
/// </summary>
/// <remarks>
/// <para>
/// <c>CopyPixels</c> copies the bitmap's own layout, and that layout is not the same everywhere:
/// a <see cref="RenderTargetBitmap"/> is in Skia's native order, which is <c>Bgra8888</c> on
/// Windows and Linux but <c>Rgba8888</c> on macOS, while the headless window's captured frame is
/// <c>Rgba8888</c> on all three. Reading a macOS render as BGRA swaps red and blue, so red text
/// "renders blue" and every hue filter looks at the wrong channel.
/// </para>
/// <para>
/// Every <c>CopyPixels</c> read in this suite goes through here, so the order is decided once,
/// from <see cref="Bitmap.Format"/>, and an unexpected format fails loudly instead of being
/// misread. The few tests that <c>Lock()</c> a captured frame read it as <c>Rgba8888</c>, which is
/// what the headless frame is on every platform.
/// </para>
/// </remarks>
internal static class RenderedPixels
{
    /// <summary>Every pixel, row by row, as <c>0xAARRGGBB</c>.</summary>
    /// <param name="bitmap">The bitmap to read.</param>
    /// <exception cref="NotSupportedException">The bitmap is not 32-bit BGRA or RGBA.</exception>
    public static uint[] Argb(Bitmap bitmap)
    {
        var raw = Raw(bitmap, out var format);
        var pixels = MemoryMarshal.Cast<byte, uint>(raw).ToArray();

        if (format == PixelFormats.Rgba8888)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];

                pixels[i] = (p & 0xFF00FF00) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF);
            }
        }

        return pixels;
    }

    /// <summary>Every pixel, row by row, as four bytes: red, green, blue, alpha.</summary>
    /// <param name="bitmap">The bitmap to read.</param>
    /// <exception cref="NotSupportedException">The bitmap is not 32-bit BGRA or RGBA.</exception>
    public static byte[] Rgba(Bitmap bitmap)
    {
        var raw = Raw(bitmap, out var format);

        if (format == PixelFormats.Bgra8888)
        {
            for (var i = 0; i + 3 < raw.Length; i += 4)
            {
                (raw[i], raw[i + 2]) = (raw[i + 2], raw[i]);
            }
        }

        return raw;
    }

    private static byte[] Raw(Bitmap bitmap, out PixelFormat format)
    {
        format = bitmap.Format
            ?? throw new NotSupportedException("The bitmap does not report a pixel format.");

        if (format != PixelFormats.Bgra8888 && format != PixelFormats.Rgba8888)
        {
            throw new NotSupportedException($"No reader for {format} pixels.");
        }

        // The uint view in Argb is little-endian: B,G,R,A bytes read as 0xAARRGGBB.
        if (!BitConverter.IsLittleEndian)
        {
            throw new NotSupportedException("Pixel reads assume a little-endian machine.");
        }

        var size = bitmap.PixelSize;
        var raw = new byte[size.Width * size.Height * 4];
        var pin = GCHandle.Alloc(raw, GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(new PixelRect(size), pin.AddrOfPinnedObject(), raw.Length, size.Width * 4);
        }
        finally
        {
            pin.Free();
        }

        return raw;
    }
}