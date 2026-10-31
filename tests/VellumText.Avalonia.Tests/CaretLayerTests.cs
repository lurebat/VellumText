using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The caret as its own layer: how it blinks, and how it moves.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here reads a frame the compositor produced, because that is the only place
/// the two things under test exist. A hard blink is the layer's opacity and a smooth one is a
/// composition animation, neither of which a direct call to <c>Render</c> would show; a glide is
/// a sequence of frames.
/// </para>
/// <para>
/// The caret is painted red over white so that a pixel can be read as "caret", "part caret" or
/// "no caret" by how much redder it is than it is green, whatever the text around it is doing.
/// </para>
/// </remarks>
public class CaretLayerTests
{
    private const int Width = 400;
    private const int Height = 120;

    // ------------------------------------------------------------------ blinking

    [AvaloniaFact]
    public void ABlinkDoesNotRepaintTheDocument()
    {
        // Each tick of the hard blink used to invalidate the whole view: every visible block
        // was drawn again to turn one 1x20 rectangle on or off.
        var (window, view) = Shown("hello world", blink: TimeSpan.FromHours(1));

        Assert.True(Redness(window) > 200);

        var before = view.RenderCount;

        view.BlinkTick();
        Assert.Equal(0, Redness(window));

        view.BlinkTick();
        Assert.True(Redness(window) > 200);

        Assert.Equal(before, view.RenderCount);
    }

    [AvaloniaFact]
    public void TheSystemBlinkRateIsTheOneUsed()
    {
        var (_, view) = Shown("hello", blink: TimeSpan.FromMilliseconds(220));

        Assert.True(Timer(view).IsEnabled);
        Assert.Equal(TimeSpan.FromMilliseconds(220), Timer(view).Interval);
    }

    [AvaloniaFact]
    public void ASystemThatTurnedBlinkingOffGetsASolidCaret()
    {
        // Windows' "no blink" accessibility setting: GetCaretBlinkTime returns INFINITE.
        var (window, view) = Shown("hello", systemBlinkOff: true);

        Assert.False(Timer(view).IsEnabled);
        Assert.True(Redness(window) > 200);
        Assert.True(view.IsCaretVisible);
    }

    [AvaloniaFact]
    public void ASolidCaretNeverBlinks()
    {
        var (window, view) = Shown("hello", style: CaretBlinkStyle.Solid);

        Assert.False(Timer(view).IsEnabled);
        Assert.True(Redness(window) > 200);
    }

    [AvaloniaFact]
    public void SwitchingTheStyleWhileFocusedTakesEffectAtOnce()
    {
        var (_, view) = Shown("hello");

        Assert.True(Timer(view).IsEnabled);

        view.CaretBlink = CaretBlinkStyle.Solid;

        Assert.False(Timer(view).IsEnabled);

        view.CaretBlink = CaretBlinkStyle.System;

        Assert.True(Timer(view).IsEnabled);
    }

    [AvaloniaFact]
    public void ASmoothBlinkFadesRatherThanSwitching()
    {
        // VS Code's "smooth": solid for one interval after a move, then a fade out and back.
        var (window, view) = Shown("hello", style: CaretBlinkStyle.Smooth);

        Assert.False(Timer(view).IsEnabled);

        var samples = Sample(window, TimeSpan.FromMilliseconds(1500));

        Assert.True(samples[0] > 200, "the caret should be solid as soon as it arrives");
        Assert.Contains(samples, s => s < 20);
        Assert.Contains(samples, s => s is > 40 and < 200);
    }

    [AvaloniaFact]
    public void AHardBlinkNeverShowsAHalfDrawnCaret()
    {
        // The control for the test above: sampled the same way, a timer blink is only ever fully
        // on or fully off. The ticks are driven, and the timer is set too slow to add its own: the
        // headless dispatcher runs a timer that is due, and one ticking alongside these turned the
        // caret back on as soon as they turned it off.
        var (window, view) = Shown("hello", blink: TimeSpan.FromHours(1));

        var samples = Sample(window, TimeSpan.FromMilliseconds(900), tick: view.BlinkTick, invalidate: true);
        var seen = string.Join(",", samples);

        Assert.True(samples.Any(s => s > 200), seen);
        Assert.True(samples.Any(s => s < 20), seen);
        Assert.False(samples.Any(s => s is > 40 and < 200), seen);
    }

    [AvaloniaFact]
    public void ASmoothBlinkUnderReducedMotionIsAnOrdinaryOne()
    {
        var (window, view) = Shown(
            "hello", style: CaretBlinkStyle.Smooth, motion: MotionPreference.Reduced,
            blink: TimeSpan.FromHours(1));

        Assert.True(Timer(view).IsEnabled);

        var samples = Sample(window, TimeSpan.FromMilliseconds(900), tick: view.BlinkTick, invalidate: true);

        Assert.DoesNotContain(samples, s => s is > 40 and < 200);
    }

    [AvaloniaFact]
    public void ASmoothBlinkStillHonoursASystemThatTurnedBlinkingOff()
    {
        var (window, view) = Shown("hello", style: CaretBlinkStyle.Smooth, systemBlinkOff: true);

        Assert.False(Timer(view).IsEnabled);
        Assert.All(Sample(window, TimeSpan.FromMilliseconds(1200)), s => Assert.True(s > 200, $"the caret faded to {s}"));
    }

    [AvaloniaFact]
    public void TheCaretLayerIsInvisibleToAccessibilityTools()
    {
        var (_, view) = Shown("hello");
        var peer = ControlAutomationPeer.CreatePeerForElement(view);

        Assert.Empty(peer.GetChildren());
    }

    [AvaloniaFact]
    public void StoppingAFadeHalfwayLeavesTheCaretVisible()
    {
        // The compositor holds the opacity a stopped animation last reached. Writing back the value
        // the control already had sent nothing, and a caret switched to Solid while faded out
        // stayed invisible.
        var (window, view) = Shown("hello", style: CaretBlinkStyle.Smooth);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        while (Sample(window, TimeSpan.FromMilliseconds(40)).Last() > 20)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), "the caret never faded");
        }

        view.CaretBlink = CaretBlinkStyle.Solid;

        Assert.All(Sample(window, TimeSpan.FromMilliseconds(300)), s => Assert.True(s > 200, $"the caret stayed at {s}"));
    }
    // ------------------------------------------------------------------ gliding

    [AvaloniaFact]
    public void WithoutSmoothCaretAMoveLandsAtOnce()
    {
        var (window, view, time) = Gliding("hello world, and more", smooth: false);
        var before = CaretColumn(window);

        view.MoveTo(view.BlockStart + 15, extend: false);

        Assert.Equal(Target(view), CaretColumn(window), 1);
        Assert.True(CaretColumn(window) > before);
        _ = time;
    }

    [AvaloniaFact]
    public void ASmoothCaretGlidesToWhereItIsGoing()
    {
        var (window, view, time) = Gliding("hello world, and more", smooth: true);
        var start = CaretColumn(window);

        view.MoveTo(view.BlockStart + 15, extend: false);
        var end = Target(view);

        Assert.Equal(start, CaretColumn(window), 1);

        time.Advance(TimeSpan.FromMilliseconds(30));
        var middle = CaretColumn(window);

        Assert.True(middle > start + 2 && middle < end - 2, $"at 30 ms the caret was at {middle}, between {start} and {end}");

        time.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(end, CaretColumn(window), 1);
    }

    [AvaloniaFact]
    public void AGlideKeepsAskingForFramesUntilItLands()
    {
        // Nothing else invalidates the caret mid-glide, so without the frame requests the
        // compositor keeps showing wherever the first frame put it.
        var (window, view, time) = Gliding("hello world, and more", smooth: true);

        view.MoveTo(view.BlockStart + 15, extend: false);
        CaretColumn(window);

        time.Advance(TimeSpan.FromMilliseconds(200));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Target(view), CaretColumn(window, invalidate: false), 1);
    }

    [AvaloniaFact]
    public void TypingDoesNotGlide()
    {
        // Word 2013's typing animation, which users turned off in droves: the caret must never
        // trail the text it is in front of.
        var (window, view, _) = Gliding("hello", smooth: true);

        view.MoveTo(view.BlockStart + 5, extend: false);
        CaretColumn(window);

        Assert.True(view.InsertText("wwww"));

        Assert.Equal(Target(view), CaretColumn(window), 1);
    }

    [AvaloniaFact]
    public void ReducedMotionSnapsTheCaret()
    {
        var (window, view, _) = Gliding("hello world, and more", smooth: true, motion: MotionPreference.Reduced);

        view.MoveTo(view.BlockStart + 15, extend: false);

        Assert.Equal(Target(view), CaretColumn(window), 1);
    }

    [AvaloniaFact]
    public void TheOperatingSystemsSettingIsWhatAutoFollows()
    {
        var (window, view, _) = Gliding("hello world, and more", smooth: true, motion: MotionPreference.Auto);

        view.SystemPrefersReducedMotion = () => true;
        view.MoveTo(view.BlockStart + 15, extend: false);

        Assert.Equal(Target(view), CaretColumn(window), 1);
    }

    [AvaloniaFact]
    public void AGlideIsInDocumentCoordinatesSoScrollingDoesNotDragTheCaret()
    {
        // A sideways glide while the view scrolls must stay on its line. Kept in the view's own
        // coordinates, the start of the glide would still be where the line was before the
        // scroll, and mid-glide the caret would be drawn part of the way between the two.
        var (window, view, time) = Gliding(
            string.Join(" ", Enumerable.Repeat("word", 400)), smooth: true, width: 200);

        view.MoveTo(view.BlockStart + 130, extend: false);
        CaretColumn(window);
        time.Advance(TimeSpan.FromMilliseconds(500));
        CaretColumn(window);

        view.MoveTo(view.BlockStart + 133, extend: false);
        CaretColumn(window);
        view.Offset = new Vector(0, view.Offset.Y + 20);
        window.UpdateLayout();
        time.Advance(TimeSpan.FromMilliseconds(30));

        Assert.True(view.CaretRect().Top > 0, "the caret's line should still be on screen");
        Assert.Equal(Math.Round(view.CaretRect().Top), CaretRow(window), 1);
    }

    [AvaloniaFact]
    public void ACaretMovedByLayoutRatherThanByTheUserDoesNotGlide()
    {
        // A resize rewraps the paragraph and moves the caret with its text; so does a height the
        // layout learns above it. The caret did not move, its text did, and a glide would show it
        // sliding off the text and back.
        var (window, view, _) = Gliding(string.Join(" ", Enumerable.Repeat("word", 60)), smooth: true, width: 200);

        view.MoveTo(view.BlockStart + 40, extend: false);
        CaretColumn(window);

        var before = view.CaretRect();

        window.Width = 400;
        window.UpdateLayout();

        Assert.NotEqual(before, view.CaretRect());
        Assert.Equal(Target(view), CaretColumn(window), 1);
        Assert.Equal(Math.Round(view.CaretRect().Top), CaretRow(window), 1);
    }

    // ------------------------------------------------------------------ helpers

    private static (Window Window, RichTextView View) Shown(
        string text,
        CaretBlinkStyle style = CaretBlinkStyle.System,
        MotionPreference motion = MotionPreference.Full,
        TimeSpan? blink = null,
        bool systemBlinkOff = false)
    {
        var window = Grey(new Window { Width = Width, Height = Height, Background = Brushes.White });
        var view = new RichTextView
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(text)),
            CaretBrush = Brushes.Red,
            CaretBlink = style,
            Motion = motion,
        };

        var interval = systemBlinkOff ? (TimeSpan?)null : blink ?? TimeSpan.FromMilliseconds(500);

        view.SystemCaretBlinkInterval = () => interval;
        window.Content = view;
        window.Show();
        view.Focus();
        window.UpdateLayout();

        return (window, view);
    }

    /// <summary>A shown view whose animations run on a clock the test moves.</summary>
    private static (Window Window, RichTextView View, ManualTime Time) Gliding(
        string text,
        bool smooth,
        MotionPreference motion = MotionPreference.Full,
        double width = Width)
    {
        var time = new ManualTime();
        var window = Grey(new Window { Width = width, Height = Height, Background = Brushes.White });
        var view = new RichTextView
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(text)),
            CaretBrush = Brushes.Red,
            CaretBlink = CaretBlinkStyle.Solid,
            SmoothCaret = smooth,
            Motion = motion,
            Time = time,
        };

        window.Content = view;
        window.Show();
        view.Focus();
        window.UpdateLayout();
        view.MoveTo(view.BlockStart, extend: false);
        CaretColumn(window);
        time.Advance(TimeSpan.FromSeconds(1));
        CaretColumn(window);

        return (window, view, time);
    }

    private static double Target(RichTextView view) => Math.Round(view.CaretRect().X);

    private static global::Avalonia.Threading.DispatcherTimer Timer(RichTextView view) =>
        (global::Avalonia.Threading.DispatcherTimer)typeof(RichTextView)
            .GetField("_blink", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(view)!;

    /// <summary>
    /// Frames sampled over a stretch of real time: a composition animation runs on the
    /// compositor's clock, which no test clock reaches.
    /// </summary>
    private static List<int> Sample(Window window, TimeSpan span, Action? tick = null, bool invalidate = false)
    {
        var samples = new List<int>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var n = 0;

        while (clock.Elapsed < span)
        {
            if (tick is not null && ++n % 5 == 0)
            {
                tick();
            }

            Dispatcher.UIThread.RunJobs();

            // Twice: a captured frame can trail the latest commit by one tick.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            samples.Add(Redness(window, invalidate));
            Thread.Sleep(30);
        }

        return samples;
    }

    /// <summary>How red the reddest pixel is, over its green: 255 is the caret, 0 is none.</summary>
    private static int Redness(Window window, bool invalidate = true)
    {
        var (pixels, w, h) = Frame(window, invalidate);
        var best = 0;

        for (var i = 0; i < w * h; i++)
        {
            best = Math.Max(best, Score(pixels, i * 4));
        }

        return best;
    }

    /// <summary>
    /// How much redder than both green and blue a pixel is. Text is drawn in grey (see
    /// <see cref="Grey"/>), so only the caret scores.
    /// </summary>
    private static int Score(byte[] pixels, int i) =>
        Math.Min(pixels[i] - pixels[i + 1], pixels[i] - pixels[i + 2]);

    /// <summary>
    /// Greyscale text. Subpixel antialiasing fringes a black glyph with pure reds and magentas,
    /// which read as caret.
    /// </summary>
    private static Window Grey(Window window)
    {
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);

        return window;
    }

    /// <summary>The leftmost column with a fully drawn caret pixel in it.</summary>
    private static double CaretColumn(Window window, bool invalidate = true)
    {
        var (pixels, w, h) = Frame(window, invalidate);

        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var i = ((y * w) + x) * 4;

                if (Score(pixels, i) > 200)
                {
                    return x;
                }
            }
        }

        return double.NaN;
    }

    private static double CaretRow(Window window)
    {
        var (pixels, w, h) = Frame(window);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = ((y * w) + x) * 4;

                if (Score(pixels, i) > 200)
                {
                    return y;
                }
            }
        }

        return double.NaN;
    }

    private static (byte[] Pixels, int Width, int Height) Frame(Window window, bool invalidate = true)
    {
        if (invalidate)
        {
            // The layer asks for its next frame through the render loop; a test moving its own
            // clock stands in for that loop by invalidating directly.
            foreach (var layer in window.GetVisualDescendants().OfType<Control>().Where(c => c.GetType().Name == "CaretLayer"))
            {
                layer.InvalidateVisual();
            }
        }

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");

        return (RenderedPixels.Rgba(frame), frame.PixelSize.Width, frame.PixelSize.Height);
    }
}

/// <summary>A clock that moves only when a test moves it.</summary>
internal sealed class ManualTime : TimeProvider
{
    private long _ticks = 1_000_000_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
