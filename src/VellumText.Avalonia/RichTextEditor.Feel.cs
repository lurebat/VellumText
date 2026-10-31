using Avalonia;

namespace VellumText.Avalonia;

/// <summary>The view's motion and caret options, settable on the shell before it is templated.</summary>
public sealed partial class RichTextEditor
{
    /// <summary>Defines the <see cref="Motion"/> property.</summary>
    /// <remarks>
    /// Added owners of the view's properties, like <see cref="IsReadOnlyProperty"/>, and pushed
    /// into the view the same way.
    /// </remarks>
    public static readonly StyledProperty<MotionPreference> MotionProperty =
        DocumentPresenter.MotionProperty.AddOwner<RichTextEditor>();

    /// <summary>Defines the <see cref="SmoothCaret"/> property.</summary>
    public static readonly StyledProperty<bool> SmoothCaretProperty =
        RichTextView.SmoothCaretProperty.AddOwner<RichTextEditor>();

    /// <summary>Defines the <see cref="CaretBlink"/> property.</summary>
    public static readonly StyledProperty<CaretBlinkStyle> CaretBlinkProperty =
        RichTextView.CaretBlinkProperty.AddOwner<RichTextEditor>();

    /// <summary>How much the control animates.</summary>
    /// <remarks>Forwarded to <see cref="DocumentPresenter.Motion"/>, which documents it.</remarks>
    public MotionPreference Motion
    {
        get => GetValue(MotionProperty);
        set => SetValue(MotionProperty, value);
    }

    /// <summary>Whether the caret glides to where it moves instead of jumping.</summary>
    /// <remarks>Forwarded to <see cref="RichTextView.SmoothCaret"/>, which documents it.</remarks>
    public bool SmoothCaret
    {
        get => GetValue(SmoothCaretProperty);
        set => SetValue(SmoothCaretProperty, value);
    }

    /// <summary>How the caret blinks.</summary>
    /// <remarks>Forwarded to <see cref="RichTextView.CaretBlink"/>, which documents it.</remarks>
    public CaretBlinkStyle CaretBlink
    {
        get => GetValue(CaretBlinkProperty);
        set => SetValue(CaretBlinkProperty, value);
    }

    private static bool IsFeelProperty(AvaloniaProperty property) =>
        property == MotionProperty
        || property == SmoothCaretProperty
        || property == CaretBlinkProperty;

    private void PushFeel(RichTextView view)
    {
        view.Motion = Motion;
        view.SmoothCaret = SmoothCaret;
        view.CaretBlink = CaretBlink;
    }
}
