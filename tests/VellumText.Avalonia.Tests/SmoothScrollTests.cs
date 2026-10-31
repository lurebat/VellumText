using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Scrolling that eases over a few frames instead of jumping: the wheel, Page Up and Down, and a
/// far jump to the caret.
/// </summary>
/// <remarks>
/// <para>
/// Every view here is inside a real <see cref="RichTextEditor"/>, so the scroll viewer that would
/// otherwise handle the wheel and the paging keys is present: the tests show what the view takes
/// over from it and what it leaves.
/// </para>
/// <para>
/// The animation runs on a clock the test moves (<see cref="ManualTime"/>) and its frames are the
/// headless render timer's, so a test can stop it part-way and look.
/// </para>
/// </remarks>
public class SmoothScrollTests
{
    private const int Width = 400;
    private const int Height = 300;

    [AvaloniaFact]
    public void ItIsOnByDefault()
    {
        Assert.True(new RichTextView().SmoothScrolling);
        Assert.True(new RichTextViewer().SmoothScrolling);
        Assert.True(new RichTextEditor().SmoothScrolling);
    }

    // ------------------------------------------------------------------ the wheel

    [AvaloniaFact]
    public void AWheelNotchEasesRatherThanJumping()
    {
        var (window, view, time) = Shown();
        var step = Step(view);

        Assert.True(Wheel(window, view, -1).Handled);
        Assert.Equal(0, view.Offset.Y, 0.5);

        Frame(window, time, 30);
        var middle = view.Offset.Y;

        Assert.True(middle > 1 && middle < step - 1, $"after 30 ms the view was at {middle} of {step}");

        Frame(window, time, 200);

        Assert.Equal(step, view.Offset.Y, 0.5);
        Assert.False(view.IsScrollAnimating);
    }

    [AvaloniaFact]
    public void NotchesInQuickSuccessionAddUp()
    {
        var (window, view, time) = Shown();
        var step = Step(view);

        Wheel(window, view, -1);
        Frame(window, time, 30);
        Wheel(window, view, -1);
        Wheel(window, view, -1);
        Frame(window, time, 300);
        Frame(window, time, 300);

        Assert.Equal(3 * step, view.Offset.Y, 0.5);
    }

    [AvaloniaFact]
    public void UnderReducedMotionTheWheelJumps()
    {
        var (window, view, _) = Shown(motion: MotionPreference.Reduced);

        Wheel(window, view, -1);
        Pump(window);

        Assert.Equal(Step(view), view.Offset.Y, 0.5);
        Assert.False(view.IsScrollAnimating);
    }

    [AvaloniaFact]
    public void TurnedOffTheWheelJumps()
    {
        var (window, view, _) = Shown(smooth: false);

        Wheel(window, view, -1);
        Pump(window);

        Assert.Equal(Step(view), view.Offset.Y, 0.5);
    }

    [AvaloniaFact]
    public void APrecisionTouchpadIsNotEasedAgain()
    {
        // A touchpad sends a stream of fractional deltas that is already smooth; easing each one
        // would make the content lag the fingers.
        var (window, view, _) = Shown();

        Wheel(window, view, -0.4);
        Pump(window);

        Assert.True(view.Offset.Y > 0, "a fractional delta should scroll at once");
        Assert.False(view.IsScrollAnimating);
    }

    [AvaloniaFact]
    public void AtTheEndTheWheelIsHandedOnToWhateverEncloses()
    {
        // An editor in a scrolling page: once the editor cannot scroll further, the wheel belongs
        // to the page. A handled event would trap it.
        var (window, view, time) = Shown();

        view.Offset = new Vector(0, view.Extent.Height);
        window.UpdateLayout();
        Frame(window, time, 10);

        Assert.False(Wheel(window, view, -1).Handled);
        Assert.False(view.IsScrollAnimating);

        Assert.True(Wheel(window, view, +1).Handled);
    }

    [AvaloniaFact]
    public void AtTheTopTheWheelUpIsHandedOn()
    {
        var (window, view, _) = Shown();

        Assert.False(Wheel(window, view, +1).Handled);
    }

    [AvaloniaFact]
    public void AModifiedWheelIsLeftAlone()
    {
        // Ctrl+wheel is zoom in most hosts and Shift+wheel is sideways; neither is the view's.
        var (window, view, _) = Shown();

        Wheel(window, view, -1, KeyModifiers.Control);

        Assert.False(view.IsScrollAnimating);
    }

    [AvaloniaFact]
    public void SettingTheOffsetStopsAnAnimation()
    {
        // The scroll bar dragged, or the host scrolling: the newest instruction wins, and an
        // animation still in flight must not drag the view back.
        var (window, view, time) = Shown();

        Wheel(window, view, -1);
        Frame(window, time, 30);

        view.Offset = new Vector(0, 0);
        Frame(window, time, 200);

        Assert.Equal(0, view.Offset.Y, 0.5);
        Assert.False(view.IsScrollAnimating);
    }

    [AvaloniaFact]
    public void TheAnimationAsksForFramesUntilItLands()
    {
        // Nothing else moves the view mid-animation; without frame requests it would stop after
        // the first step.
        var (window, view, time) = Shown();

        Wheel(window, view, -1);

        for (var i = 0; i < 12; i++)
        {
            Frame(window, time, 16);
        }

        Assert.Equal(Step(view), view.Offset.Y, 0.5);
    }

    [AvaloniaFact]
    public void TheViewerEasesToo()
    {
        var time = new ManualTime();
        var viewer = new RichTextViewer { Document = Paragraphs(200), Motion = MotionPreference.Full, Time = time };
        var window = new Window { Width = Width, Height = Height, Content = new ScrollViewer { Content = viewer } };

        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

        window.Show();
        window.UpdateLayout();

        Wheel(window, viewer, -1);
        Frame(window, time, 30);

        Assert.True(viewer.Offset.Y > 1 && viewer.IsScrollAnimating);
    }

    [AvaloniaFact]
    public void ScrollingIntoBlocksNobodyMeasuredMovesAboutTheDistanceAsked()
    {
        // Twenty long paragraphs among short ones, and the view parked just below them: their
        // heights are estimated from the short ones around them, and are wrong. Easing up through
        // them lays them out, and keeping the content at the top of the viewport still corrects the
        // offset as their real heights arrive. The animation has to carry that correction, or it
        // undoes it on its next frame and lands somewhere other than the distance the wheel asked.
        var time = new ManualTime();
        var paragraphs = Enumerable.Range(0, 800).Select(i => i is >= 400 and < 420
            ? string.Join(" ", Enumerable.Repeat($"long paragraph {i}", 20))
            : $"short {i}").ToArray();
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)),
            Motion = MotionPreference.Full,
        };
        var window = new Window { Width = Width, Height = Height, Content = editor };

        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

        var view = editor.EnsureView();

        view.Time = time;
        window.Show();
        window.UpdateLayout();

        for (var i = 0; i < 3; i++)
        {
            view.Offset = new Vector(0, view.Offset.Y + view.BlockOrigin(425).Y);
            window.UpdateLayout();
        }

        var step = Step(view);

        for (var i = 0; i < 6; i++)
        {
            Wheel(window, view, +1);
        }

        // What the reader sees move: frame by frame, how far the block at the top of the viewport
        // travelled down the screen. Blocks measured below it move what is under them, not it.
        var travelled = 0.0;

        for (var frame = 0; frame < 30 && view.IsScrollAnimating; frame++)
        {
            var top = TopBlock(view);
            var was = view.BlockOrigin(top).Y;

            Frame(window, time, 16);
            travelled += view.BlockOrigin(top).Y - was;
        }

        // Measured: the reader sees about a fifth more than six notches, because a block that was
        // only an estimate is drawn at its real height the moment it is laid out - an instant scroll
        // lands on the same content. Without carrying the layout's corrections, the frame after one
        // leapt back to an offset worked out before it, thousands of pixels from the content shown.
        Assert.False(view.IsScrollAnimating);
        Assert.InRange(travelled, 6 * step, 1.5 * 6 * step);
    }

    [AvaloniaFact]
    public void AWheelNotchWhileEasingToTheEndKeepsGoingToTheEnd()
    {
        // The target is already the end, so the notch adds nothing - but passing it on let the scroll
        // viewer move the view a notch from wherever the animation had got to, which stopped the
        // animation short of the end.
        var (window, view, time) = Shown();
        var end = view.Extent.Height - view.Viewport.Height;

        view.Offset = new Vector(0, end - view.Viewport.Height);
        window.UpdateLayout();

        Press(view, Key.PageDown);
        Frame(window, time, 10);

        Assert.True(view.IsScrollAnimating);
        Assert.True(Wheel(window, view, -1).Handled);

        Settle(window, time);

        Assert.Equal(view.Extent.Height - view.Viewport.Height, view.Offset.Y, 0.5);
    }

    [AvaloniaFact]
    public void AnAnimationDoesNotSurviveTheViewLeavingTheWindow()
    {
        var (window, view, time) = Shown();
        var editor = (Control)window.Content!;

        Wheel(window, view, -1);
        Frame(window, time, 30);

        window.Content = null;
        window.Content = editor;
        window.UpdateLayout();

        Assert.False(view.IsScrollAnimating);
    }
    // ------------------------------------------------------------------ the paging keys

    [AvaloniaFact]
    public void PageDownEasesAndLeavesTheCaret()
    {
        var (window, view, time) = Shown();
        var caret = view.State.Selection;
        var page = view.Viewport.Height;

        Press(view, Key.PageDown);
        Frame(window, time, 30);

        Assert.True(view.Offset.Y > 1 && view.Offset.Y < page - 1, $"after 30 ms the view was at {view.Offset.Y} of {page}");

        Frame(window, time, 200);

        Assert.Equal(page, view.Offset.Y, 0.5);
        Assert.Equal(caret, view.State.Selection);

        Press(view, Key.PageUp);
        Frame(window, time, 200);
        Frame(window, time, 200);

        Assert.Equal(0, view.Offset.Y, 0.5);
    }

    [AvaloniaFact]
    public void UnderReducedMotionPageDownJumps()
    {
        var (window, view, _) = Shown(motion: MotionPreference.Reduced);

        Press(view, Key.PageDown);
        Pump(window);

        Assert.Equal(view.Viewport.Height, view.Offset.Y, 0.5);
    }

    // ------------------------------------------------------------------ revealing the caret

    [AvaloniaFact]
    public void CtrlEndCutsMostOfTheWayAndGlidesTheRest()
    {
        // Animating through two thousand paragraphs would lay out every one of them on the way.
        // Like VS Code's smooth scrolling, the jump cuts to a viewport short and eases the rest.
        var (window, view, time) = Shown(2000);

        Press(view, Key.End, KeyModifiers.Control);
        window.UpdateLayout();

        var end = Math.Max(0, view.Extent.Height - view.Viewport.Height);

        Assert.True(view.Offset.Y > end - (1.5 * view.Viewport.Height), $"the cut left the view at {view.Offset.Y} of {end}");
        Assert.True(view.IsScrollAnimating, "the last viewport should be eased");

        Settle(window, time);

        var caret = view.CaretRect();

        Assert.False(view.IsScrollAnimating);
        Assert.True(caret.Top >= 0 && caret.Bottom <= view.Viewport.Height + 0.5, $"the caret ended at {caret} in a viewport of {view.Viewport.Height}");
    }

    [AvaloniaFact]
    public void AnArrowKeyAtTheEdgeScrollsAtOnce()
    {
        // Line by line the scroll must keep pace with the caret, including under key repeat.
        var (window, view, _) = Shown();

        while (view.CaretRect().Bottom < view.Viewport.Height - 30)
        {
            Press(view, Key.Down);
            window.UpdateLayout();
        }

        var before = view.Offset.Y;

        for (var i = 0; i < 4; i++)
        {
            Press(view, Key.Down);
            window.UpdateLayout();
        }

        Assert.True(view.Offset.Y > before);
        Assert.False(view.IsScrollAnimating);
        Assert.True(view.CaretRect().Bottom <= view.Viewport.Height + 0.5);
    }

    [AvaloniaFact]
    public void AHostLoadingADocumentIsNotAnimated()
    {
        // A restored document arriving with its caret at the end opens there; easing the reader
        // down through it would be the control performing for nobody.
        var (window, view, _) = Shown();
        var doc = Paragraphs(400);

        view.State = EditorState.Create(doc, TextSelection.Cursor(doc.ContentSize - 2));
        window.UpdateLayout();

        Assert.False(view.IsScrollAnimating);
        Assert.True(view.CaretRect().Bottom <= view.Viewport.Height + 0.5);
    }

    // ------------------------------------------------------------------ drag-select autoscroll

    [AvaloniaFact]
    public void DragSelectScrollsByTheTimeThatPassedNotByATick()
    {
        // A fixed step per 50 ms timer tick was 20 jerks a second. A frame's worth of the same
        // speed is what keeps the motion even at any refresh rate.
        var (window, view, _) = Shown();

        window.MouseDown(view.TranslatePoint(new Point(40, 20), window)!.Value, MouseButton.Left);
        window.MouseMove(view.TranslatePoint(new Point(200, view.Bounds.Height + 40), window)!.Value);

        Assert.True(view.AutoScrolling);

        // A frame scrolls, and a frame that took no time does not stop the scrolling.
        var before = view.Offset.Y;

        view.AutoScrollFrame(TimeSpan.Zero);
        Assert.True(view.AutoScrolling);

        view.AutoScrollFrame(TimeSpan.FromMilliseconds(16));
        Assert.True(view.Offset.Y > before);

        // 480 pixels a second, which is the old 24 pixels per 50 ms tick, and a long gap capped.
        Assert.Equal(480 * 0.016, RichTextView.AutoScrollDistance(TimeSpan.FromMilliseconds(16)), 6);
        Assert.Equal(480 * 0.064, RichTextView.AutoScrollDistance(TimeSpan.FromMilliseconds(64)), 6);
        Assert.Equal(48, RichTextView.AutoScrollDistance(TimeSpan.FromSeconds(2)), 6);
        Assert.Equal(0, RichTextView.AutoScrollDistance(TimeSpan.FromMilliseconds(-5)), 6);

        window.MouseUp(view.TranslatePoint(new Point(200, view.Bounds.Height + 40), window)!.Value, MouseButton.Left);

        Assert.False(view.AutoScrolling);
    }

    // ------------------------------------------------------------------ helpers

    private static DocumentNode Paragraphs(int count) =>
        DocumentNode.FromParagraphs(Enumerable.Range(0, count)
            .Select(i => $"Paragraph {i} with enough words in it to need shaping and to wrap.")
            .ToArray());

    private static (Window Window, RichTextView View, ManualTime Time) Shown(
        int paragraphs = 200,
        MotionPreference motion = MotionPreference.Full,
        bool smooth = true)
    {
        var time = new ManualTime();
        var editor = new RichTextEditor
        {
            State = EditorState.Create(Paragraphs(paragraphs)),
            Motion = motion,
            SmoothScrolling = smooth,
        };
        var window = new Window { Width = Width, Height = Height, Content = editor };

        // A scroll viewer with no theme has no template and scrolls nothing; the paths the view leaves
        // to it need a real one. Into the window, not the application every test shares, and before
        // the editor is templated.
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

        var view = editor.EnsureView();

        view.Time = time;
        window.Show();
        window.UpdateLayout();
        view.Focus();
        view.MoveTo(1, extend: false);
        window.UpdateLayout();

        return (window, view, time);
    }

    /// <summary>The realized block at the top edge of the viewport.</summary>
    private static int TopBlock(RichTextView view)
    {
        var realized = view.RealizedBlocks;

        for (var i = realized.Start.Value; i < realized.End.Value; i++)
        {
            var y = view.BlockOrigin(i).Y;

            if (y <= 0 && y + view.Blocks[i].Size.Height > 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("no realized block is at the top of the viewport");
    }

    private static double Step(DocumentPresenter view) =>
        ((global::Avalonia.Controls.Primitives.ILogicalScrollable)view).ScrollSize.Height;

    /// <summary>One frame: the clock moves, the frame the animation asked for is delivered, layout catches up.</summary>
    /// <remarks>
    /// The first frame request is serviced by the dispatcher; the headless render timer does not
    /// service one made from inside a frame callback, so later frames are delivered by hand - and
    /// only if the animation asked for them.
    /// </remarks>
    private static void Frame(Window window, ManualTime time, double ms)
    {
        time.Advance(TimeSpan.FromMilliseconds(ms));
        Dispatcher.UIThread.RunJobs();

        foreach (var view in window.GetVisualDescendants().OfType<DocumentPresenter>())
        {
            view.DeliverScrollFrame();
        }

        window.UpdateLayout();
    }

    /// <summary>Runs what the scroll viewer posted, then layout.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Settle(Window window, ManualTime time)
    {
        for (var i = 0; i < 6; i++)
        {
            Frame(window, time, 100);
        }
    }

    private static PointerWheelEventArgs Wheel(
        Window window, DocumentPresenter view, double delta, KeyModifiers modifiers = KeyModifiers.None)
    {
        var at = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), window)!.Value;
        var e = new PointerWheelEventArgs(
            view,
            new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true),
            window,
            at,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            modifiers,
            new Vector(0, delta))
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent,
        };

        view.RaiseEvent(e);

        return e;
    }

    private static void Press(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
}
