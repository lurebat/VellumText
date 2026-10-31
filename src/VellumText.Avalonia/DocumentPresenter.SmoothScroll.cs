using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace VellumText.Avalonia;

/// <summary>Scrolling that eases over a few frames instead of jumping.</summary>
/// <remarks>
/// <para>
/// Avalonia's <see cref="ScrollViewer"/> eases only touch gestures; a wheel notch or Page Down
/// jumps in one frame. The view is the scroll viewer's <see cref="ILogicalScrollable"/>, so it owns
/// the offset and can move it over several frames itself.
/// </para>
/// <para>
/// VS Code's <c>editor.smoothScrolling</c> is the model: 125 ms with a cubic ease-out, and a jump
/// of more than a couple of viewports cut short so that only the last viewport is animated —
/// animating through the whole distance would lay out every block on the way.
/// </para>
/// </remarks>
public abstract partial class DocumentPresenter
{
    /// <summary>Defines the <see cref="SmoothScrolling"/> property.</summary>
    public static readonly StyledProperty<bool> SmoothScrollingProperty =
        AvaloniaProperty.Register<DocumentPresenter, bool>(nameof(SmoothScrolling), defaultValue: true);

    /// <summary>How long an eased scroll takes.</summary>
    internal static readonly TimeSpan ScrollDuration = TimeSpan.FromMilliseconds(125);

    private double? _scrollTarget;
    private double _scrollFrom;
    private long _scrollStart;
    private bool _scrollFrameRequested;
    private bool _animatingOffset;

    /// <summary>Whether a wheel notch, Page Up or Down and a far jump to the caret ease.</summary>
    /// <remarks>
    /// <para>
    /// On by default: browsers ease the wheel on every platform, so an editor in a web page does,
    /// and one that jumps reads as stiff beside it. A precision touchpad is not eased again, because
    /// its stream of small deltas is smooth already, and an arrow key or typing at the edge of the
    /// view still scrolls at once, so the view never trails the caret.
    /// </para>
    /// <para>
    /// Under reduced motion (see <see cref="Motion"/>) every scroll is immediate. A wheel turned
    /// at either end of the document is not handled, so a page the editor sits in scrolls instead.
    /// </para>
    /// </remarks>
    public bool SmoothScrolling
    {
        get => GetValue(SmoothScrollingProperty);
        set => SetValue(SmoothScrollingProperty, value);
    }

    /// <summary>Whether an eased scroll is in progress.</summary>
    internal bool IsScrollAnimating => _scrollTarget is not null;

    /// <summary>Whether scrolls should ease now.</summary>
    private protected bool EasesScrolling => SmoothScrolling && !ReducesMotion && IsVirtualizing;

    /// <summary>Where scrolling is heading: an eased scroll's target, or the offset when none is running.</summary>
    private protected double ScrollDestination => _scrollTarget ?? _offset.Y;

    /// <summary>The largest offset the document allows.</summary>
    private protected double MaxOffsetY => Math.Max(0, _extent.Height - _viewport.Height);

    /// <summary>Eases the view to a vertical offset, or moves it at once if scrolling does not ease.</summary>
    /// <param name="y">Where to go, clamped to the document.</param>
    private protected void ScrollTo(double y)
    {
        y = Math.Clamp(y, 0, MaxOffsetY);

        if (!EasesScrolling)
        {
            StopScrollAnimation();
            ApplyOffset(new Vector(_offset.X, y), relayout: true);

            return;
        }

        // A jump of more than two viewports: cut to one viewport short and ease the rest.
        if (Math.Abs(y - _offset.Y) > 2 * _viewport.Height)
        {
            MoveOffset(y - (Math.Sign(y - _offset.Y) * _viewport.Height));
        }

        _scrollFrom = _offset.Y;
        _scrollTarget = y;
        _scrollStart = Time.GetTimestamp();
        RequestScrollFrame();
    }

    /// <summary>Stops an eased scroll where it is.</summary>
    private protected void StopScrollAnimation()
    {
        _scrollTarget = null;
    }

    /// <summary>
    /// Whether the caret's latest move may be eased into view: a move within the same document,
    /// not an edit and not a document the host has just loaded.
    /// </summary>
    private protected virtual bool RevealMayEase => false;

    /// <summary>
    /// Turns a reveal the layout has just carried out into an eased one, when it was far enough to
    /// see and the move allows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reveal is worked out exactly first — Ctrl+End laid out from the end up, the caret
    /// brought into view — and only then is the view put back where it was and eased there, so the
    /// animation heads for the place the reveal would have jumped to and nowhere else. More than a
    /// viewport away, it is put back only one viewport short. Heights learned on the way are carried
    /// (<see cref="CarryScrollAnimation"/>), so it still lands on that place.
    /// </para>
    /// <para>
    /// A move of three blocks or less is never eased: an arrow key or typing at the edge has to
    /// keep pace with the keyboard, auto-repeat included.
    /// </para>
    /// <para>
    /// Called from inside the measure pass, so the offset moves without asking for another layout;
    /// the caller settles the window there.
    /// </para>
    /// </remarks>
    /// <param name="from">Where the view was before the reveal.</param>
    /// <returns>Whether the view was moved back to start an animation.</returns>
    private protected bool BeginEasedReveal(double from)
    {
        var to = _offset.Y;

        if (Math.Abs(to - from) < 0.5)
        {
            return false;
        }

        // A reveal is an instruction: whatever was easing before is superseded by it.
        StopScrollAnimation();

        if (!RevealMayEase || !EasesScrolling || _viewport.Height <= 0
            || Math.Abs(to - from) <= 3 * Math.Max(1, _heights.Estimate))
        {
            return false;
        }

        var start = Math.Abs(to - from) > _viewport.Height
            ? to - (Math.Sign(to - from) * _viewport.Height)
            : from;

        _animatingOffset = true;

        try
        {
            ApplyOffset(new Vector(_offset.X, start), relayout: false);
        }
        finally
        {
            _animatingOffset = false;
        }

        _scrollFrom = _offset.Y;
        _scrollTarget = to;
        _scrollStart = Time.GetTimestamp();
        RequestScrollFrame();

        return true;
    }

    /// <summary>Moves the offset as the animation itself, which an outside offset change would cancel.</summary>
    private void MoveOffset(double y)
    {
        _animatingOffset = true;

        try
        {
            ApplyOffset(new Vector(_offset.X, y), relayout: true);
        }
        finally
        {
            _animatingOffset = false;
        }
    }

    /// <summary>Moves an eased scroll by a correction the layout made to the offset.</summary>
    /// <remarks>
    /// Laying out blocks nobody had measured replaces estimated heights with real ones, and the
    /// layout moves the offset to keep the content at the top of the viewport where it was. An
    /// animation going from and to absolute offsets would undo that on its next frame and the text
    /// would jump; carrying the same correction keeps it heading for the same content. Moves that are
    /// instructions rather than corrections stop the animation before they get here.
    /// </remarks>
    /// <param name="moved">How far the offset is about to move.</param>
    private void CarryScrollAnimation(double moved)
    {
        if (_animatingOffset || _scrollTarget is not { } target)
        {
            return;
        }

        _scrollFrom += moved;
        _scrollTarget = target + moved;
    }

    private void RequestScrollFrame()
    {
        if (_scrollFrameRequested || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _scrollFrameRequested = true;
        top.RequestAnimationFrame(_ => DeliverScrollFrame());
    }

    /// <summary>Delivers the frame the animation asked for, if it asked for one. For tests.</summary>
    /// <remarks>
    /// The headless platform services a frame request made outside a frame, but not one made from
    /// inside the frame callback itself - which is how every frame after the first is asked for.
    /// This stands in for the compositor's next frame, and does nothing unless one was requested,
    /// so an animation that stopped asking stops here too.
    /// </remarks>
    internal void DeliverScrollFrame()
    {
        if (!_scrollFrameRequested)
        {
            return;
        }

        _scrollFrameRequested = false;
        ScrollFrame();
    }

    private void ScrollFrame()
    {
        if (_scrollTarget is not { } target)
        {
            return;
        }

        var t = Math.Clamp(Time.GetElapsedTime(_scrollStart) / ScrollDuration, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);

        MoveOffset(_scrollFrom + ((target - _scrollFrom) * eased));

        if (t < 1)
        {
            RequestScrollFrame();

            return;
        }

        _scrollTarget = null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only a plain notched wheel is taken over: a modified wheel belongs to the host (zoom,
    /// sideways), a precision touchpad is smooth already, and a wheel turned at an end of the
    /// document is left unhandled so that whatever the editor sits in scrolls instead.
    /// </remarks>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerWheelChanged(e);

        if (e.Handled || e.Delta.Y == 0 || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var notched = Math.Abs(e.Delta.Y - Math.Round(e.Delta.Y)) < 1e-6;

        if (!notched || !EasesScrolling)
        {
            // The scroll viewer moves the offset, which stops anything this started.
            return;
        }

        var target = Math.Clamp(ScrollDestination - (e.Delta.Y * ((ILogicalScrollable)this).ScrollSize.Height), 0, MaxOffsetY);

        if (Math.Abs(target - ScrollDestination) < 0.5)
        {
            // Already on the way to the end: the notch is spent here, not handed to the scroll
            // viewer, which would jump there, or to an outer one, which would scroll the page.
            // Only a view resting at its limit passes the wheel on.
            e.Handled = _scrollTarget is not null;

            return;
        }

        ScrollTo(target);
        e.Handled = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Page Up and Down, eased. They move the view and not the caret, which is what the scroll
    /// viewer did with them and what it still does when scrolling does not ease.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnKeyDown(e);

        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.PageDown or Key.PageUp) || !EasesScrolling)
        {
            return;
        }

        ScrollTo(ScrollDestination + ((e.Key == Key.PageDown ? 1 : -1) * _viewport.Height));
        e.Handled = true;
    }

    /// <summary>Applies a change that bears on smooth scrolling.</summary>
    private void OnScrollPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if ((change.Property == SmoothScrollingProperty || change.Property == MotionProperty) && !EasesScrolling)
        {
            // Turned off mid-scroll: land now rather than leave the view part of the way there.
            if (_scrollTarget is { } target)
            {
                StopScrollAnimation();
                ApplyOffset(new Vector(_offset.X, target), relayout: true);
            }
        }
    }
}
