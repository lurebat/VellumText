using Avalonia;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>How the caret blinks.</summary>
public enum CaretBlinkStyle
{
    /// <summary>
    /// On and off at the operating system's rate, and not at all where the user has turned
    /// blinking off. Windows reports both; elsewhere the rate is
    /// <see cref="RichTextView.BlinkInterval"/>.
    /// </summary>
    System,

    /// <summary>
    /// Fades out and back in at the operating system's rate, like VS Code's <c>smooth</c>. Still
    /// solid where the user has turned blinking off, and an ordinary blink under reduced motion.
    /// </summary>
    Smooth,

    /// <summary>Never blinks.</summary>
    Solid,
}

/// <summary>How the caret blinks and moves.</summary>
public partial class RichTextView
{
    /// <summary>Defines the <see cref="CaretBlink"/> property.</summary>
    public static readonly StyledProperty<CaretBlinkStyle> CaretBlinkProperty =
        AvaloniaProperty.Register<RichTextView, CaretBlinkStyle>(nameof(CaretBlink));

    /// <summary>Defines the <see cref="SmoothCaret"/> property.</summary>
    public static readonly StyledProperty<bool> SmoothCaretProperty =
        AvaloniaProperty.Register<RichTextView, bool>(nameof(SmoothCaret));

    private readonly CaretLayer _caretLayer;

    /// <summary>How the caret blinks.</summary>
    /// <remarks>
    /// Defaults to <see cref="CaretBlinkStyle.System"/>, which is what a platform text box does.
    /// Every style keeps the caret solid while it is moving: each move restarts the blink.
    /// </remarks>
    public CaretBlinkStyle CaretBlink
    {
        get => GetValue(CaretBlinkProperty);
        set => SetValue(CaretBlinkProperty, value);
    }

    /// <summary>Whether the caret glides to where it moves instead of jumping.</summary>
    /// <remarks>
    /// <para>
    /// An 80 ms ease-out when the caret is moved by the arrow keys, a click or the host —
    /// VS Code's <c>cursorSmoothCaretAnimation</c>. Typing never glides: the caret has to be
    /// where the next character will appear the moment the last one does. Off by default, as it
    /// is in VS Code, because it is a matter of taste.
    /// </para>
    /// <para>
    /// Under reduced motion (see <see cref="DocumentPresenter.Motion"/>) the caret jumps.
    /// </para>
    /// </remarks>
    public bool SmoothCaret
    {
        get => GetValue(SmoothCaretProperty);
        set => SetValue(SmoothCaretProperty, value);
    }

    /// <summary>Asks the operating system for its blink interval, or null for none. Replaced by tests.</summary>
    internal Func<TimeSpan?> SystemCaretBlinkInterval { get; set; } = SystemMotion.CaretBlinkInterval;

    /// <summary>One tick of the hard blink.</summary>
    /// <remarks>
    /// The layer's opacity, not a repaint: the compositor shows or hides what it already has.
    /// </remarks>
    internal void BlinkTick()
    {
        _caretVisible = !_caretVisible;
        _caretLayer.Show(_caretVisible);
    }

    /// <summary>Restarts the blink so the caret is solid immediately after it moves.</summary>
    /// <remarks>
    /// Without this a caret moved just before the timer ticks vanishes on arrival, which reads
    /// as a dropped keystroke. It is also where the blink style is put into effect, so a change
    /// of style, of the system's rate or of the motion setting is picked up at the next move.
    /// </remarks>
    internal void ResetCaretBlink()
    {
        _caretVisible = IsFocused;
        _blink.Stop();
        _caretLayer.InvalidateVisual();

        // The view too: a move is also a selection change, and the highlight is the view's to draw.
        InvalidateVisual();

        if (!IsFocused)
        {
            _caretLayer.Show(false);

            return;
        }

        if (CaretBlink == CaretBlinkStyle.Solid || SystemCaretBlinkInterval() is not { } interval)
        {
            _caretLayer.Show(true);

            return;
        }

        if (CaretBlink == CaretBlinkStyle.Smooth && !ReducesMotion)
        {
            _caretLayer.Fade(interval);

            return;
        }

        _caretLayer.Show(true);
        _blink.Interval = interval;
        _blink.Start();
    }

    /// <summary>Where the caret layer draws the caret, or null when it draws nothing.</summary>
    /// <remarks>The conditions the view applied when it drew the caret itself.</remarks>
    internal Rect? CaretLayerRect()
    {
        if (!IsFocused || _views.Count == 0 || !PaintReady(_state.Selection.Head, _state.Selection.From))
        {
            return null;
        }

        // A non-empty selection hides the caret, except while composing: then the caret belongs
        // to the input method and sits inside text the selection knows nothing about.
        if (Preedit is null && !_state.Selection.IsEmpty)
        {
            return null;
        }

        return CaretRect();
    }

    /// <summary>
    /// The caret's colour: the one set for it, or else the colour the text is painted in, so the
    /// caret follows the theme. See <see cref="CaretBrushProperty"/>.
    /// </summary>
    internal IBrush? CaretLayerBrush() => CaretBrush ?? TextStyles.Resolve(MarkSet.Empty).ForegroundBrush;

    private protected override void OnOffsetMoved() => _caretLayer.InvalidateVisual();

    private protected override void OnArranged(Size finalSize)
    {
        _caretLayer.Measure(finalSize);
        _caretLayer.Arrange(new Rect(finalSize));

        // Anything that moves text moves the caret, and every such change arranges the view.
        _caretLayer.InvalidateVisual();
    }
}
