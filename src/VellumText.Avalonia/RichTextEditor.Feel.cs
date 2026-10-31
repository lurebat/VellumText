using Avalonia;

namespace VellumText.Avalonia;

/// <summary>The view's motion, caret and drawing options, settable on the shell before it is templated.</summary>
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

    /// <summary>Defines the <see cref="IsContrastAdaptationEnabled"/> property.</summary>
    public static readonly StyledProperty<bool> IsContrastAdaptationEnabledProperty =
        DocumentPresenter.IsContrastAdaptationEnabledProperty.AddOwner<RichTextEditor>();

    /// <summary>Whether text the document colours explicitly is drawn readably on the background.</summary>
    /// <remarks>Forwarded to <see cref="DocumentPresenter.IsContrastAdaptationEnabled"/>, which documents it.</remarks>
    public bool IsContrastAdaptationEnabled
    {
        get => GetValue(IsContrastAdaptationEnabledProperty);
        set => SetValue(IsContrastAdaptationEnabledProperty, value);
    }

    /// <summary>Defines the <see cref="SelectionCornerRadius"/> property.</summary>
    public static readonly StyledProperty<double> SelectionCornerRadiusProperty =
        DocumentPresenter.SelectionCornerRadiusProperty.AddOwner<RichTextEditor>();

    /// <summary>How far the outer corners of a selection highlight are rounded, in pixels.</summary>
    /// <remarks>Forwarded to <see cref="DocumentPresenter.SelectionCornerRadius"/>, which documents it.</remarks>
    public double SelectionCornerRadius
    {
        get => GetValue(SelectionCornerRadiusProperty);
        set => SetValue(SelectionCornerRadiusProperty, value);
    }

    /// <summary>Defines the <see cref="SmoothScrolling"/> property.</summary>
    public static readonly StyledProperty<bool> SmoothScrollingProperty =
        DocumentPresenter.SmoothScrollingProperty.AddOwner<RichTextEditor>();

    /// <summary>Whether a wheel notch, Page Up or Down and a far jump to the caret ease.</summary>
    /// <remarks>Forwarded to <see cref="DocumentPresenter.SmoothScrolling"/>, which documents it.</remarks>
    public bool SmoothScrolling
    {
        get => GetValue(SmoothScrollingProperty);
        set => SetValue(SmoothScrollingProperty, value);
    }

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
        || property == CaretBlinkProperty
        || property == IsContrastAdaptationEnabledProperty
        || property == SelectionCornerRadiusProperty
        || property == SmoothScrollingProperty;

    private void PushFeel(RichTextView view)
    {
        view.Motion = Motion;
        view.SmoothCaret = SmoothCaret;
        view.CaretBlink = CaretBlink;
        view.IsContrastAdaptationEnabled = IsContrastAdaptationEnabled;
        view.SelectionCornerRadius = SelectionCornerRadius;
        view.SmoothScrolling = SmoothScrolling;
    }
}
