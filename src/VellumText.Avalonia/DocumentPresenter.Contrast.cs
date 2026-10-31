using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace VellumText.Avalonia;

/// <summary>Drawing a document's own text colours readably on whatever the control sits on.</summary>
public abstract partial class DocumentPresenter
{
    /// <summary>Defines the <see cref="IsContrastAdaptationEnabled"/> property.</summary>
    public static readonly StyledProperty<bool> IsContrastAdaptationEnabledProperty =
        AvaloniaProperty.Register<DocumentPresenter, bool>(nameof(IsContrastAdaptationEnabled), defaultValue: true);

    private ITextStyleResolver? _renderStyles;
    private Color? _backdrop;
    private Visual[] _watched = [];

    /// <summary>
    /// Whether text the document colours explicitly is drawn in a readable colour when, as written,
    /// it would barely show against the background.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Black text pasted from a word processor into a dark-themed editor is the case: correct, and
    /// invisible. With this on it is drawn light — the same hue, mirrored in lightness — and only
    /// drawn so. The document keeps the colour it was given, so copying, exporting and saving
    /// carry the author's colour, and the same document shown on a light background is drawn as
    /// written.
    /// </para>
    /// <para>
    /// A colour is adapted when its contrast with what is behind it falls below 3:1, and is then
    /// brought up to 4.5:1. What is behind it is the run's highlight if it has one, else the fill
    /// of its code block or table cell, else the first solid background found on this control or
    /// its ancestors. With no background anywhere nothing is adapted, because an unknown
    /// background is not a dark one. Text in the theme's own colour is never adapted.
    /// </para>
    /// <para>On by default.</para>
    /// </remarks>
    public bool IsContrastAdaptationEnabled
    {
        get => GetValue(IsContrastAdaptationEnabledProperty);
        set => SetValue(IsContrastAdaptationEnabledProperty, value);
    }

    /// <summary>The resolver blocks are built with: <see cref="TextStyles"/>, adapted when that is on.</summary>
    private protected ITextStyleResolver RenderStyles =>
        _renderStyles ??= IsContrastAdaptationEnabled && _backdrop is { } backdrop
            ? new ContrastStyles(TextStyles, backdrop)
            : TextStyles;

    /// <summary>Forgets the render resolver, so the next block built gets a current one.</summary>
    private void ResetRenderStyles() => _renderStyles = null;

    /// <summary>Looks for the background again, and redraws every block if it moved.</summary>
    /// <remarks>
    /// Called when the control joins a tree, when the theme changes and when its own background
    /// does: the moments the colour behind the text can change without the document changing.
    /// </remarks>
    private void RefreshBackdrop()
    {
        Unwatch();

        var found = FindBackdrop();

        if (found == _backdrop)
        {
            return;
        }

        _backdrop = found;

        if (IsContrastAdaptationEnabled)
        {
            ResetRenderStyles();
            DropLayout();
            InvalidateMeasure();
        }
    }

    /// <summary>The opaque colour behind the control, or null when nothing solid is.</summary>
    /// <remarks>
    /// Walks outwards from the control itself, compositing translucent fills over whatever is
    /// found beneath them, and stops at the first opaque one.
    /// </remarks>
    private Color? FindBackdrop()
    {
        var layers = new List<Color>();
        var walked = new List<Visual>();

        // Every ancestor passed on the way is watched, so a background that changes above the
        // control (an editor's template paints it on a Border) is looked for again.
        try
        {
            return Walk(layers, walked);
        }
        finally
        {
            _watched = [.. walked];

            foreach (var visual in _watched)
            {
                visual.PropertyChanged += OnBackdropChanged;
            }
        }
    }

    private Color? Walk(List<Color> layers, List<Visual> walked)
    {
        for (Visual? visual = this; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual != this)
            {
                walked.Add(visual);
            }

            if (BackgroundOf(visual) is not ISolidColorBrush { Color: var colour } brush)
            {
                continue;
            }

            var alpha = colour.A * brush.Opacity;

            if (alpha <= 0)
            {
                continue;
            }

            layers.Add(Color.FromArgb((byte)Math.Round(alpha), colour.R, colour.G, colour.B));

            if (alpha >= 255)
            {
                var result = layers[^1];

                for (var i = layers.Count - 2; i >= 0; i--)
                {
                    result = TextContrast.Over(layers[i], result);
                }

                return result;
            }
        }

        return null;
    }

    private static IBrush? BackgroundOf(Visual visual) => visual switch
    {
        DocumentPresenter presenter => presenter.Background,
        Border border => border.Background,
        Panel panel => panel.Background,
        ContentPresenter content => content.Background,
        TemplatedControl templated => templated.Background,
        _ => null,
    };

    private void Unwatch()
    {
        foreach (var visual in _watched)
        {
            visual.PropertyChanged -= OnBackdropChanged;
        }

        _watched = [];
    }

    private void OnBackdropChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // By name: Border, Panel, ContentPresenter and TemplatedControl each register their own.
        if (e.Property.Name == nameof(Background))
        {
            RefreshBackdrop();
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshBackdrop();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unwatch();

        // Nothing is on screen to ease, and an animation left running would move the view the
        // moment it was shown somewhere else.
        StopScrollAnimation();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Applies a change that bears on contrast adaptation.</summary>
    private void OnContrastPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsContrastAdaptationEnabledProperty)
        {
            ResetRenderStyles();
            DropLayout();
            InvalidateMeasure();
        }
        else if (change.Property == BackgroundProperty)
        {
            RefreshBackdrop();
        }
    }
}
