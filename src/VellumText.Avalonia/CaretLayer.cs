using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace VellumText.Avalonia;

/// <summary>
/// The caret, drawn by a visual of its own so that blinking and gliding repaint one rectangle
/// instead of the whole document.
/// </summary>
/// <remarks>
/// <para>
/// Drawn in the view's own <c>Render</c>, a blink tick repainted every visible block to turn a
/// one-pixel line on or off. Here a hard blink is a change to this visual's opacity, which the
/// compositor applies without anything being drawn again, and a smooth blink is a composition
/// animation that never touches the UI thread at all.
/// </para>
/// <para>
/// A glide is kept in document coordinates — the view's coordinates plus its scroll offset — so
/// a scroll during a glide does not drag the caret across the lines moving under it.
/// </para>
/// </remarks>
internal sealed class CaretLayer : Control
{
    /// <summary>How long a glide takes. VS Code's <c>cursorSmoothCaretAnimation</c> uses 80 ms.</summary>
    internal static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(80);

    private readonly RichTextView _owner;
    private Rect _from;
    private Rect _to;
    private long _start;
    private bool _placed;
    private DocumentNode? _doc;
    private bool _frameRequested;
    private bool _fading;
    private TimeSpan? _pendingFade;

    public CaretLayer(RichTextView owner)
    {
        _owner = owner;
        IsHitTestVisible = false;
    }

    /// <summary>Shows the caret fully on or fully off, ending any fade.</summary>
    /// <remarks>
    /// Opacity alone affects rendering in Avalonia; the invalidation makes the state the next
    /// frame draws explicit rather than relying on that. It redraws this layer, one rectangle,
    /// and leaves the document view alone.
    /// </remarks>
    public void Show(bool visible)
    {
        StopFade();
        Opacity = visible ? 1 : 0;
        InvalidateVisual();
    }

    /// <summary>Starts the smooth blink: solid for one interval, then fading out and in.</summary>
    /// <param name="interval">How long each half of the cycle takes.</param>
    public void Fade(TimeSpan interval)
    {
        Show(true);

        if (ElementComposition.GetElementVisual(this) is not { } visual)
        {
            // Not composited yet. The first render starts it.
            _pendingFade = interval;
            InvalidateVisual();

            return;
        }

        _pendingFade = null;

        // VS Code's "smooth" keyframes: opaque for the first fifth of a half-cycle, transparent
        // from three fifths, played forward and back.
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        var ease = new CubicEaseInOut();

        animation.InsertKeyFrame(0f, 1f);
        animation.InsertKeyFrame(0.2f, 1f);
        animation.InsertKeyFrame(0.6f, 0f, ease);
        animation.InsertKeyFrame(1f, 0f);
        animation.Duration = interval;
        animation.Direction = PlaybackDirection.Alternate;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;

        // Solid for one interval after a move, as a hard blink is. The default delay behaviour
        // applies the first keyframe only once the delay is over; measured, that left the caret
        // at whatever opacity it had - invisible, after the previous fade - for the whole delay.
        animation.DelayTime = interval;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        animation.Target = "Opacity";

        visual.StartAnimation("Opacity", animation);
        _fading = true;
    }

    private void StopFade()
    {
        _pendingFade = null;

        if (!_fading)
        {
            return;
        }

        _fading = false;

        if (ElementComposition.GetElementVisual(this) is { } visual)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = (float)Opacity;
        }
    }

    /// <inheritdoc/>
    

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (_pendingFade is { } interval)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_pendingFade == interval)
                {
                    Fade(interval);
                }
            });
        }

        if (_owner.CaretLayerRect() is not { } target || _owner.CaretLayerBrush() is not { } brush)
        {
            _placed = false;

            return;
        }

        var offset = _owner.Offset;
        var inDocument = target.Translate(offset);

        if (!_placed || inDocument != _to)
        {
            Retarget(inDocument);
        }

        _doc = _owner.State.Doc;

        var progress = Progress();
        var rect = progress >= 1 ? _to : Lerp(_from, _to, EaseOut(progress));

        context.FillRectangle(
            brush,
            RichTextView.SnapCaret(rect.Translate(-offset), TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));

        if (progress < 1)
        {
            RequestFrame();
        }
    }

    /// <summary>Starts towards a new position: gliding from wherever the caret is drawn now, or not.</summary>
    /// <remarks>
    /// An edit snaps. Word 2013 animated the caret while typing and users disliked it enough that
    /// turning it off became a how-to article; the caret has to be where the next character goes
    /// the moment the last one appears. A composition snaps for the same reason.
    /// </remarks>
    private void Retarget(Rect inDocument)
    {
        var typed = !ReferenceEquals(_doc, _owner.State.Doc) || _owner.Preedit is not null;
        var glide = _placed && !typed && _owner.SmoothCaret && !_owner.ReducesMotion;

        _from = glide ? Lerp(_from, _to, EaseOut(Progress())) : inDocument;
        _to = inDocument;
        _start = _owner.Time.GetTimestamp();
        _placed = true;
    }

    private double Progress() =>
        _from == _to
            ? 1
            : Math.Clamp(_owner.Time.GetElapsedTime(_start) / GlideDuration, 0, 1);

    private void RequestFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            InvalidateVisual();
        });
    }

    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    private static Rect Lerp(Rect a, Rect b, double t) =>
        new(
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t),
            b.Width,
            a.Height + ((b.Height - a.Height) * t));
}
