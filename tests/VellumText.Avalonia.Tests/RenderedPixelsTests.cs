using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// That <see cref="RenderedPixels"/> reads red as red in both of the layouts a render comes back in.
/// </summary>
/// <remarks>
/// macOS renders <c>Rgba8888</c> where Windows and Linux render <c>Bgra8888</c>, so these stand in
/// for the platform that cannot be run here: each builds a bitmap in one layout, with one opaque
/// red pixel and one opaque blue one, and asserts they come out the same either way.
/// </remarks>
public class RenderedPixelsTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RedAndBlueComeOutTheSameWhicheverOrderTheBitmapStoresThem(bool rgba)
    {
        using var bitmap = TwoPixels(rgba);

        Assert.Equal([0xFFFF0000u, 0xFF0000FFu], RenderedPixels.Argb(bitmap));
        Assert.Equal([255, 0, 0, 255, 0, 0, 255, 255], RenderedPixels.Rgba(bitmap));
    }

    [AvaloniaFact]
    public void AFormatWithNoReaderIsRefusedRatherThanMisread()
    {
        using var bitmap = new WriteableBitmap(
            new PixelSize(1, 1), new Vector(96, 96), PixelFormats.Rgb565, AlphaFormat.Opaque);

        Assert.Throws<NotSupportedException>(() => RenderedPixels.Argb(bitmap));
    }

    private static WriteableBitmap TwoPixels(bool rgba)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(2, 1),
            new Vector(96, 96),
            rgba ? PixelFormats.Rgba8888 : PixelFormats.Bgra8888,
            AlphaFormat.Premul);

        byte[] red = rgba ? [255, 0, 0, 255] : [0, 0, 255, 255];
        byte[] blue = rgba ? [0, 0, 255, 255] : [255, 0, 0, 255];

        using (var locked = bitmap.Lock())
        {
            Marshal.Copy([.. red, .. blue], 0, locked.Address, 8);
        }

        return bitmap;
    }
}