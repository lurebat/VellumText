using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Turning an <see cref="ImageEmbed"/> into pixels, and the sources that are refused.
/// </summary>
public class BitmapEmbedRendererTests
{
    /// <summary>A real PNG of the given size, as a data URL.</summary>
    /// <remarks>
    /// Encoded here rather than pasted in as a literal, because a hand-made PNG that Skia refuses
    /// to decode fails these tests in exactly the way a broken renderer would.
    /// </remarks>
    private static string Png(int width, int height)
    {
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new global::Avalonia.PixelSize(width, height));
        using var buffer = new MemoryStream();

        bitmap.Save(buffer, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return $"data:image/png;base64,{Convert.ToBase64String(buffer.ToArray())}";
    }

    private static TextRunProperties Properties() =>
        new GenericTextRunProperties(new Typeface(FontFamily.Default), fontRenderingEmSize: 12);

    [AvaloniaFact]
    public void AnImageIsMeasuredAtItsIntrinsicSizeWhenItAsksForNone()
    {
        using var renderer = new BitmapEmbedRenderer();

        var size = renderer.Measure(new ImageEmbed(Png(2, 1)), Properties());

        Assert.Equal(new global::Avalonia.Size(2, 1), size);
    }

    [AvaloniaFact]
    public void AnExplicitSizeWins()
    {
        // A resized image has to stay the size it was dragged to.
        using var renderer = new BitmapEmbedRenderer();

        var size = renderer.Measure(new ImageEmbed(Png(2, 1), 80, 40), Properties());

        Assert.Equal(new global::Avalonia.Size(80, 40), size);
    }

    [AvaloniaFact]
    public void OneDimensionScalesTheOtherByTheAspectRatio()
    {
        // The bug this prevents: an image given only a width arriving square, and squashed.
        using var renderer = new BitmapEmbedRenderer();

        Assert.Equal(
            new global::Avalonia.Size(100, 50),
            renderer.Measure(new ImageEmbed(Png(2, 1), width: 100), Properties()));

        Assert.Equal(
            new global::Avalonia.Size(100, 50),
            renderer.Measure(new ImageEmbed(Png(2, 1), height: 50), Properties()));
    }

    [AvaloniaFact]
    public void ASourceThatCannotBeDecodedFallsBackToABoxRatherThanThrowing()
    {
        using var renderer = new BitmapEmbedRenderer();

        foreach (var source in new[]
        {
            "data:image/png;base64,not-base64-at-all",
            "data:image/png;base64,",
            "data:image/png,unencoded",
            "data:",
            "no-such-file.png",
            "/nonexistent/path.png",
        })
        {
            var size = renderer.Measure(new ImageEmbed(source, 30, 20), Properties());

            Assert.Equal(new global::Avalonia.Size(30, 20), size);
        }
    }

    [AvaloniaFact]
    public void ANetworkSourceIsRefusedRatherThanFetched()
    {
        // A pasted document is untrusted input. Resolving http would let it make a request from
        // the user's machine to an address it chose - a tracking pixel, or a request forgery.
        using var renderer = new BitmapEmbedRenderer { AllowLocalSources = true };

        foreach (var source in new[]
        {
            "http://example.com/a.png",
            "https://example.com/a.png",
            "ftp://example.com/a.png",
        })
        {
            // Refused even with local sources allowed: this type never fetches, at any setting.
            var size = renderer.Measure(new ImageEmbed(source), Properties());

            Assert.Equal(new global::Avalonia.Size(12, 12), size);
        }
    }

    [AvaloniaFact]
    public void ALocalFileIsNotReadUnlessTheHostAsksForIt()
    {
        // The point: a document arriving by paste names the source, so resolving a path by
        // default would let pasted content put a local file on screen.
        var file = Path.Combine(Path.GetTempPath(), $"VellumText-{Guid.NewGuid():N}.png");

        try
        {
            File.WriteAllBytes(file, Convert.FromBase64String(Png(2, 1).Split(',')[1]));

            using (var guarded = new BitmapEmbedRenderer())
            {
                Assert.Equal(
                    new global::Avalonia.Size(12, 12),
                    guarded.Measure(new ImageEmbed(file), Properties()));

                Assert.Equal(
                    new global::Avalonia.Size(12, 12),
                    guarded.Measure(new ImageEmbed(new Uri(file).AbsoluteUri), Properties()));
            }

            using var trusting = new BitmapEmbedRenderer { AllowLocalSources = true };

            Assert.Equal(
                new global::Avalonia.Size(2, 1),
                trusting.Measure(new ImageEmbed(file), Properties()));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaFact]
    public void AnOversizedPayloadIsRefusedBeforeItIsDecoded()
    {
        using var renderer = new BitmapEmbedRenderer { MaximumSourceBytes = 16 };

        Assert.Equal(
            new global::Avalonia.Size(12, 12),
            renderer.Measure(new ImageEmbed(Png(8, 8)), Properties()));
    }

    [AvaloniaFact]
    public void APngClaimingMorePixelsThanAllowedIsRefusedFromItsHeader()
    {
        // A decompression bomb: a few bytes of PNG describing a picture too large to hold. The
        // dimensions are in the header at a fixed offset, so this costs no allocation to refuse.
        using var renderer = new BitmapEmbedRenderer { MaximumPixels = 16 };

        Assert.Equal(
            new global::Avalonia.Size(12, 12),
            renderer.Measure(new ImageEmbed(Png(8, 8)), Properties()));

        using var generous = new BitmapEmbedRenderer { MaximumPixels = 64 };

        Assert.Equal(
            new global::Avalonia.Size(8, 8),
            generous.Measure(new ImageEmbed(Png(8, 8)), Properties()));

        // Measuring cannot see which check refused it: Accept re-reads the pixel count after
        // decoding and reaches the same fallback size, so the header check could be deleted
        // outright and everything above would still pass. Asking it directly is what pins it.
        var payload = Convert.FromBase64String(Png(8, 8).Split(',')[1]);

        Assert.True(renderer.IsOversizedPng(payload));
        Assert.False(generous.IsOversizedPng(payload));
    }

    [AvaloniaFact]
    public void ThePngHeaderCheckOnlyJudgesPngs()
    {
        // It runs over every payload before decoding, so a false positive here would refuse a
        // perfectly good JPEG for whatever its bytes happened to hold at offset 16.
        using var renderer = new BitmapEmbedRenderer { MaximumPixels = 1 };

        Assert.False(renderer.IsOversizedPng([]));
        Assert.False(renderer.IsOversizedPng([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]));

        // A genuine PNG signature truncated before the IHDR dimensions it would be read from.
        Assert.False(renderer.IsOversizedPng([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
    }

    [AvaloniaFact]
    public void DrawingAnUndecodableImageDoesNotThrow()
    {
        using var renderer = new BitmapEmbedRenderer();
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new global::Avalonia.PixelSize(40, 40));
        using var context = bitmap.CreateDrawingContext();

        renderer.Draw(context, new global::Avalonia.Rect(0, 0, 20, 20), new ImageEmbed("broken"));
        renderer.Draw(context, new global::Avalonia.Rect(0, 0, 20, 20), new ImageEmbed(Png(2, 1)));
    }

    [AvaloniaFact]
    public void ANonImageEmbedIsLeftToTheFallback()
    {
        using var renderer = new BitmapEmbedRenderer();

        Assert.Null(renderer.Measure(new Unknown(), Properties()));
    }

    [AvaloniaFact]
    public void TheSameSourceIsDecodedOnceAndSurvivesInvalidation()
    {
        using var renderer = new BitmapEmbedRenderer();

        var first = renderer.Measure(new ImageEmbed(Png(2, 1)), Properties());

        renderer.Invalidate();

        Assert.Equal(first, renderer.Measure(new ImageEmbed(Png(2, 1)), Properties()));
    }

    [AvaloniaFact]
    public void ADisposedRendererStopsResolvingRatherThanUsingFreedBitmaps()
    {
        var renderer = new BitmapEmbedRenderer();

        renderer.Measure(new ImageEmbed(Png(2, 1)), Properties());
        renderer.Dispose();
        renderer.Dispose();

        // Falls back rather than handing back a bitmap it has already disposed.
        Assert.Equal(
            new global::Avalonia.Size(12, 12),
            renderer.Measure(new ImageEmbed(Png(2, 1)), Properties()));
    }

    [AvaloniaFact]
    public void TheCacheStaysBoundedRatherThanLeakingWhatItCannotHold()
    {
        // The bug this exists for: at the limit, the old code returned a decoded bitmap it had not
        // kept. Nothing owned it, so nothing disposed it, and because it was not cached the very
        // next layout pass decoded it again - leaking one native bitmap per image per pass, where
        // it never shows up as managed pressure.
        //
        // The count staying under the limit does NOT catch that, because the old code stayed under
        // it too. Re-decoding is the observable symptom, so that is what this asserts.
        using var renderer = new BitmapEmbedRenderer { CacheLimit = 4 };

        for (var i = 1; i <= 4; i++)
        {
            renderer.Measure(new ImageEmbed(Png(i, 1)), Properties());
        }

        Assert.Equal(4, renderer.CachedCount);

        var beyond = new ImageEmbed(Png(9, 1));
        var before = renderer.DecodeCount;

        renderer.Measure(beyond, Properties());
        renderer.Measure(beyond, Properties());
        renderer.Measure(beyond, Properties());

        Assert.Equal(before + 1, renderer.DecodeCount);
        Assert.True(
            renderer.CachedCount <= 4,
            $"cache held {renderer.CachedCount} against a limit of 4");

        // Eviction must not leave the renderer unable to resolve what it dropped.
        Assert.Equal(
            new global::Avalonia.Size(3, 1),
            renderer.Measure(new ImageEmbed(Png(3, 1)), Properties()));
    }

    [AvaloniaFact]
    public void TheRendererRejectsNulls()
    {
        using var renderer = new BitmapEmbedRenderer();

        Assert.Throws<ArgumentNullException>(() => renderer.Measure(null!, Properties()));
        Assert.Throws<ArgumentNullException>(() => renderer.Measure(new ImageEmbed("a"), null!));
    }

    [AvaloniaFact]
    public void AnEditorRendersImagesWithoutTheHostArrangingAnything()
    {
        // The default matters: a rich text control that cannot show an image is not one.
        Assert.IsType<BitmapEmbedRenderer>(new RichTextView().Embeds);
        Assert.IsType<BitmapEmbedRenderer>(new RichTextViewer().Embeds);
    }

    [AvaloniaFact]
    public void EveryPresenterSharesOneCacheByDefault()
    {
        // A history list showing two hundred documents is two hundred presenters. Giving each its
        // own cache holds the same image two hundred times and decodes it two hundred times, which
        // is what a per-instance default quietly did.
        Assert.Same(new RichTextView().Embeds, new RichTextViewer().Embeds);
        Assert.Same(BitmapEmbedRenderer.Shared, new RichTextView().Embeds);
    }

    [AvaloniaFact]
    public void TheSharedRendererCannotBeDisposedOutFromUnderEveryoneElse()
    {
        // A host that replaces Embeds and disposes what was there would otherwise turn images off
        // for every other control in the process, permanently.
        BitmapEmbedRenderer.Shared.Dispose();

        Assert.Equal(
            new global::Avalonia.Size(2, 1),
            BitmapEmbedRenderer.Shared.Measure(new ImageEmbed(Png(2, 1)), Properties()));
    }

    // ---- the image store ----------------------------------------------------------------------

    [AvaloniaFact]
    public void TheDataUriStoreRoundTripsThroughTheRenderer()
    {
        // The two halves of the same feature: what the store writes, the renderer must resolve.
        var store = new DataUriImageStore();
        var uri = Png(2, 1);
        var png = Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);

        var source = store.Save(png, "image/png");

        Assert.NotNull(source);

        using var renderer = new BitmapEmbedRenderer();

        Assert.Equal(
            new global::Avalonia.Size(2, 1),
            renderer.Measure(new ImageEmbed(source), Properties()));
    }

    [AvaloniaFact]
    public void TheStoreRefusesAnImageOverTheLimitRatherThanTruncatingIt()
    {
        var store = new DataUriImageStore(maximumBytes: 8);

        Assert.Null(store.Save(new byte[9], "image/png"));
        Assert.NotNull(store.Save(new byte[8], "image/png"));
    }

    [AvaloniaFact]
    public void TheStoreRefusesNothingAtAll()
    {
        Assert.Null(new DataUriImageStore().Save([], "image/png"));
    }

    [AvaloniaFact]
    public void TheStoreChecksItsArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataUriImageStore(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataUriImageStore(-1));
        Assert.Throws<ArgumentNullException>(() => new DataUriImageStore().Save(new byte[1], null!));
    }

    private sealed record Unknown : InlineEmbed
    {
        public override string PlainTextFallback => "?";
    }
}
