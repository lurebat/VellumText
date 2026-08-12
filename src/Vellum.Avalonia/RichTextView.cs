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
/// Increment 2 realises exactly one paragraph. The block list, the height index and the
/// virtualization that <c>ILogicalScrollable</c> exists for arrive with the increment
/// that has more than one block to scroll. What is settled here is the division of labour:
/// blocks own layout and answer for geometry, and this control owns the caret, the selection
/// highlight and everything that has to be true across blocks.
/// </para>
/// <para>
/// The caret and the highlight are drawn here rather than by the block precisely because a
/// selection spans blocks and the caret is a single concern with one blink timer. A block that
/// drew its own caret would need to know whether it held the focused end of the selection,
/// which is knowledge it has no business having.
/// </para>
/// </remarks>
public class RichTextView : Control
{
    /// <summary>Defines the <see cref="State"/> property.</summary>
    public static readonly DirectProperty<RichTextView, EditorState> StateProperty =
        AvaloniaProperty.RegisterDirect<RichTextView, EditorState>(
            nameof(State), o => o.State, (o, v) => o.State = v);

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
    private IEmbedRenderer _embeds = new PlaceholderEmbedRenderer();
    private ParagraphView? _block;
    private bool _caretVisible;

    /// <summary>Creates the view.</summary>
    public RichTextView()
    {
        Focusable = true;

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

            SetAndRaise(StateProperty, ref _state, value);
            OnStateChanged();
        }
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
            _block = null;
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
            _block = null;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// The document position at which the realised block's content starts.
    /// </summary>
    /// <remarks>
    /// The one place where a block-local position becomes a document position. Increment 2 has
    /// a single block so the answer is constant, but it is derived rather than written as 1 so
    /// that the block list arriving later changes this method and nothing else.
    /// </remarks>
    public int BlockStart => _state.Doc.Resolve(1).Start(1);

    /// <summary>The view of the realised block, laying it out if needed.</summary>
    /// <remarks>Null until the control has been measured.</remarks>
    public ParagraphView? Block => _block;

    /// <summary>Whether the caret is currently painted, for tests and for the blink timer.</summary>
    public bool IsCaretVisible => _caretVisible;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var paragraph = _state.Doc.Blocks[0] as ParagraphNode
            ?? throw new NotSupportedException(
                "Increment 2 realises a single paragraph; the block list arrives with Increment 3.");

        if (_block is null)
        {
            _block = new ParagraphView(paragraph, TextStyles, _embeds);
        }
        else
        {
            _block.Update(paragraph);
        }

        // An unconstrained width has no wrap point, so the text would lay out on one endless
        // line and the control would ask for it. A finite fallback keeps that from happening
        // when the control is measured outside a width-constraining parent.
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? DefaultWidth
            : availableSize.Width;

        var size = _block.Measure(width);

        // Room for the caret at the end of the longest line, which otherwise sits exactly on
        // the edge and is clipped to a half-pixel sliver.
        return size.WithWidth(size.Width + CaretWidth);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_block is null)
        {
            return;
        }

        var origin = new Point(0, 0);

        RenderSelection(context);
        _block.Render(context, origin);
        RenderCaret(context);
    }

    /// <summary>The document position nearest a point in this control's coordinates.</summary>
    /// <param name="point">The point.</param>
    public int PositionAt(Point point) =>
        _block is null ? BlockStart : BlockStart + _block.HitTest(point);

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
            _block = null;
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
        InvalidateMeasure();
        ResetCaretBlink();
    }

    private void RenderSelection(DrawingContext context)
    {
        if (_state.Selection.IsEmpty || SelectionBrush is null || _block is null)
        {
            return;
        }

        var from = _state.Selection.From - BlockStart;
        var to = _state.Selection.To - BlockStart;

        // A selection is a document range and the block holds only part of the document, so it
        // is clipped before being asked about. With one block this is a formality; with a
        // block list it is the whole of how a multi-block selection is drawn.
        from = Math.Clamp(from, 0, _block.ContentSize);
        to = Math.Clamp(to, 0, _block.ContentSize);

        foreach (var rect in _block.GetSelectionRects(from, to))
        {
            context.FillRectangle(SelectionBrush, rect);
        }
    }

    private void RenderCaret(DrawingContext context)
    {
        if (!_caretVisible || CaretBrush is null || _block is null || !_state.Selection.IsEmpty)
        {
            return;
        }

        var local = Math.Clamp(_state.Selection.Head - BlockStart, 0, _block.ContentSize);
        var caret = _block.GetCaretRect(local);
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        context.FillRectangle(CaretBrush, SnapCaret(caret, scale));
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
