using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace Vellum.Avalonia;

/// <summary>
/// The control that presents a document and draws the caret and the selection, per
/// architecture 4.6.
/// </summary>
/// <remarks>
/// <para>
/// The division of labour: blocks own layout and answer for geometry in their own coordinates,
/// and this control owns the block list, the caret, the selection highlight and everything
/// that has to be true across blocks. The block list and the reconciliation that keeps it
/// cheap live in the Blocks part of this class.
/// </para>
/// <para>
/// The caret and the highlight are drawn here rather than by the block precisely because a
/// selection spans blocks and the caret is a single concern with one blink timer. A block that
/// drew its own caret would need to know whether it held the focused end of the selection,
/// which is knowledge it has no business having.
/// </para>
/// </remarks>
public partial class RichTextView : Control
{
    /// <summary>Defines the <see cref="State"/> property.</summary>
    public static readonly DirectProperty<RichTextView, EditorState> StateProperty =
        AvaloniaProperty.RegisterDirect<RichTextView, EditorState>(
            nameof(State), o => o.State, (o, v) => o.State = v);

    /// <summary>Defines the <see cref="Background"/> property.</summary>
    /// <remarks>
    /// Transparent rather than null, and this matters: Avalonia hit-tests a plain
    /// <see cref="Control"/> against what it actually drew, so a view that painted only glyphs
    /// would not receive a click in the leading above a line, in the gap below one, or to the
    /// right of the last character. Measured: a click 2px from the top of the control reported
    /// the parent <c>ContentPresenter</c> as its source and never reached the view. Filling the
    /// bounds with a transparent brush is what makes the whole control clickable, and it costs
    /// nothing visually.
    /// </remarks>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<RichTextView, IBrush?>(nameof(Background), Brushes.Transparent);

    /// <summary>Defines the <see cref="SelectionBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<RichTextView, IBrush?>(
            nameof(SelectionBrush), new SolidColorBrush(Color.FromArgb(80, 0, 120, 215)));

    /// <summary>Defines the <see cref="CaretBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> CaretBrushProperty =
        AvaloniaProperty.Register<RichTextView, IBrush?>(nameof(CaretBrush), Brushes.Black);

    /// <summary>The interval at which the caret changes state.</summary>
    public static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _blink;
    private EditorState _state = EditorState.Create(DocumentNode.Empty);
    private ITextStyleResolver? _styles;
    private IParagraphStyleResolver _paragraphStyles = new ParagraphStyleResolver();
    private IEmbedRenderer _embeds = new PlaceholderEmbedRenderer();
    private bool _caretVisible;
    private double _width = DefaultWidth;

    /// <summary>Creates the view.</summary>
    public RichTextView()
    {
        Focusable = true;

        _inputMethod = new RichTextInputMethodClient(this);

        // How a control offers itself to the platform input method: the request bubbles from
        // the focused element, and answering it is what makes composition arrive here at all.
        AddHandler(
            global::Avalonia.Input.InputElement.TextInputMethodClientRequestedEvent,
            (_, e) => e.Client = _inputMethod);

        _blink = new DispatcherTimer { Interval = BlinkInterval };
        _blink.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
    }

    /// <summary>The document and selection being presented.</summary>
    public EditorState State
    {
        get => _state;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_state, value))
            {
                return;
            }

            // The view reconciles before the change is announced, not after. A consumer handling
            // StateProperty asks the view questions -- which block the caret is in, what the caret
            // block's style is -- and those are answered from state that OnStateChanged rebuilds.
            // Raising first hands every listener the previous document's answers.
            var previous = _state;
            _state = value;
            OnStateChanged();
            RaisePropertyChanged(StateProperty, previous, value);
        }
    }

    /// <summary>The fill behind the whole control.</summary>
    /// <remarks>See <see cref="BackgroundProperty"/> — this is load-bearing for hit testing.</remarks>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>The fill behind selected text.</summary>
    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>The caret's colour.</summary>
    public IBrush? CaretBrush
    {
        get => GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>Resolves mark sets to run properties.</summary>
    /// <remarks>
    /// <para>
    /// Not called <c>Styles</c>: <see cref="StyledElement.Styles"/> already is.
    /// </para>
    /// <para>
    /// Left unset it is derived from the control's own inherited text properties, so a document
    /// put inside a themed panel picks up that theme's font the way any other control would.
    /// Setting it explicitly opts out of that, and clearing it back to null opts in again.
    /// Either way every layout is discarded, because every run's properties have changed.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public ITextStyleResolver TextStyles
    {
        get => _styles ??= BuildStyles();
        set
        {
            _styles = value;
            _derivedStyles = value is null;
            DropLayout();
            InvalidateMeasure();
        }
    }

    /// <summary>Measures and draws inline embeds.</summary>
    public IEmbedRenderer Embeds
    {
        get => _embeds;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _embeds = value;
            DropLayout();
            InvalidateMeasure();
        }
    }

    /// <summary>Decides how each paragraph kind is presented.</summary>
    /// <remarks>
    /// Block-level presentation — heading sizes, the bar down a quote, the fill behind code —
    /// as opposed to the character-level presentation <see cref="TextStyles"/> resolves. A host
    /// replaces this to restyle headings without touching the document.
    /// </remarks>
    public IParagraphStyleResolver ParagraphStyles
    {
        get => _paragraphStyles;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _paragraphStyles = value;
            DropLayout();
            InvalidateMeasure();
        }
    }

    /// <summary>Whether the caret is currently painted, for tests and for the blink timer.</summary>
    public bool IsCaretVisible => _caretVisible;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        // An unconstrained width has no wrap point, so the text would lay out on one endless
        // line and the control would ask for it. A finite fallback keeps that from happening
        // when the control is measured outside a width-constraining parent.
        _width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? DefaultWidth
            : availableSize.Width;

        var size = Realize();

        // Room for the caret at the end of the longest line, which otherwise sits exactly on
        // the edge and is clipped to a half-pixel sliver.
        return size.WithWidth(size.Width + CaretWidth);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Background is { } background)
        {
            context.FillRectangle(background, new Rect(Bounds.Size));
        }

        RenderSelection(context);

        for (var i = 0; i < _views.Count; i++)
        {
            _views[i].Render(context, BlockOrigin(i));
        }

        RenderCaret(context);
    }

    /// <summary>Restarts the blink so the caret is solid immediately after it moves.</summary>
    /// <remarks>
    /// Without this a caret moved just before the timer ticks vanishes on arrival, which reads
    /// as a dropped keystroke.
    /// </remarks>
    public void ResetCaretBlink()
    {
        _caretVisible = IsFocused;
        _blink.Stop();

        if (IsFocused)
        {
            _blink.Start();
        }

        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnGotFocus(global::Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        ResetCaretBlink();
    }

    /// <inheritdoc/>
    protected override void OnLostFocus(global::Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ResetCaretBlink();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // A dispatcher timer holds the control alive through its Tick handler, so a view that
        // leaves the tree without stopping it keeps repainting nothing forever.
        _blink.Stop();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // A derived resolver is only as current as the properties it was derived from, so a
        // font change has to invalidate it. An explicitly set one is the caller's to manage.
        if (_derivedStyles && TextProperties.Contains(change.Property))
        {
            _styles = null;
            DropLayout();
            InvalidateMeasure();
        }
        else if (change.Property == SelectionBrushProperty || change.Property == CaretBrushProperty)
        {
            InvalidateVisual();
        }
    }

    private static readonly HashSet<AvaloniaProperty> TextProperties =
    [
        TextElement.FontFamilyProperty,
        TextElement.FontSizeProperty,
        TextElement.FontStyleProperty,
        TextElement.FontWeightProperty,
        TextElement.FontStretchProperty,
        TextElement.ForegroundProperty,
    ];

    private bool _derivedStyles = true;

    private ITextStyleResolver BuildStyles()
    {
        _derivedStyles = true;

        return new TextStyleResolver(
            new Typeface(
                TextElement.GetFontFamily(this),
                TextElement.GetFontStyle(this),
                TextElement.GetFontWeight(this),
                TextElement.GetFontStretch(this)),
            TextElement.GetFontSize(this),
            TextElement.GetForeground(this) ?? Brushes.Black);
    }

    private const double DefaultWidth = 400;
    private const double CaretWidth = 1;

    private void OnStateChanged()
    {
        if (_views.Count > 0)
        {
            // Realised eagerly rather than waiting for the layout pass: see Realize.
            Realize();
        }

        InvalidateMeasure();
        ResetCaretBlink();
        NotifyInputMethod();
    }

    private void RenderSelection(DrawingContext context)
    {
        if (_state.Selection.IsEmpty || SelectionBrush is null)
        {
            return;
        }

        var from = _state.Selection.From;
        var to = _state.Selection.To;

        for (var i = 0; i < _views.Count; i++)
        {
            var slot = _slots[i];
            var origin = BlockOrigin(i);

            if (slot.Node.IsLeaf)
            {
                // A leaf has no inside, so there is no part of it to select: either the
                // selection reaches across it and it is covered whole, or it is not touched.
                // The host answers this because only the host can see that the selection
                // reaches past the block on both sides.
                if (from <= slot.Start && to >= slot.Start + slot.Node.NodeSize)
                {
                    context.FillRectangle(SelectionBrush, new Rect(origin, _views[i].Size));
                }

                continue;
            }

            // Blocks the selection does not reach into at all. The comparisons are strict at
            // both ends so that a selection merely touching a boundary is skipped here rather
            // than asked for a zero-length range; GetSelectionRects answers that with nothing,
            // so this is a short-circuit, not the thing that keeps an empty sliver off screen.
            if (to <= slot.Start || from >= slot.End)
            {
                continue;
            }

            var view = _views[i];

            foreach (var rect in view.GetSelectionRects(
                Math.Max(0, from - slot.Start),
                Math.Min(view.ContentSize, to - slot.Start)))
            {
                context.FillRectangle(SelectionBrush, rect.Translate(origin));
            }
        }
    }

    private void RenderCaret(DrawingContext context)
    {
        if (!_caretVisible || CaretBrush is null || _views.Count == 0)
        {
            return;
        }

        // A non-empty selection hides the caret, except while composing: then the caret belongs
        // to the input method and sits inside text the selection knows nothing about.
        if (Block?.Preedit is null && !_state.Selection.IsEmpty)
        {
            return;
        }

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        context.FillRectangle(CaretBrush, SnapCaret(CaretRect(), scale));
    }

    /// <summary>
    /// Snaps a caret rectangle to the device pixel grid and gives it a width of exactly one
    /// device pixel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Architecture 4.6 says to follow <c>TextPresenter</c> with
    /// <c>PixelRect.FromRect(rect, scale).ToRect(scale)</c>. That does not do what is wanted
    /// here, and it was measured: <c>PixelRect.FromRect</c> is a <em>covering</em> conversion,
    /// flooring the left edge and ceiling the right, so a one-wide rectangle at a fractional x
    /// comes back two pixels wide — precisely the blurred double-width caret the snapping was
    /// meant to prevent. <c>FromRect(new Rect(10.7, 4.2, 1, 18.6), 1)</c> returns
    /// <c>10, 4, 2, 19</c>.
    /// </para>
    /// <para>
    /// So the origin is rounded to the grid and the width is set to one device pixel outright.
    /// The height is snapped by rounding both edges, which keeps the caret the same height as
    /// the line rather than letting rounding shave it.
    /// </para>
    /// </remarks>
    /// <param name="caret">The caret rectangle in device-independent pixels.</param>
    /// <param name="scale">The render scaling of the surface being drawn on.</param>
    public static Rect SnapCaret(Rect caret, double scale)
    {
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "Render scaling must be a positive, finite number.");
        }

        var x = Math.Round(caret.X * scale) / scale;
        var top = Math.Round(caret.Y * scale) / scale;
        var bottom = Math.Round((caret.Y + caret.Height) * scale) / scale;

        return new Rect(x, top, CaretWidth / scale, bottom - top);
    }
}
