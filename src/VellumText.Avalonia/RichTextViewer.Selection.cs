using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>
/// Selection and copy on the read-only viewer.
/// </summary>
/// <remarks>
/// <para>
/// A reader who cannot copy out of a viewer pastes the whole entry somewhere else and deletes the
/// rest, which is why this closes a gap the control's own documentation used to record. The
/// driving consumer is a clipboard manager: "look at this clip but you may not take anything from
/// it" is precisely the wrong behaviour for it.
/// </para>
/// <para>
/// <b>Read-only is structural here, not a flag.</b> The editor enforces
/// <c>IsReadOnly</c> at one choke point, <c>RichTextView.Apply</c>; this control has no
/// equivalent because it has no transaction, no history, no text input client and no editing
/// method at all. The selection is two integers and the copy is a read of the document. There is
/// nothing to switch off, so nothing can be switched back on by mistake.
/// </para>
/// <para>
/// It also does not acquire the editor's per-instance cost, which was the measured justification
/// for the two controls existing separately: no blink timer, no input-method client, no context
/// flyout. What it shares — the range geometry, the hit testing, the block stack — it shares
/// through <see cref="DocumentPresenter"/> rather than by copying.
/// </para>
/// </remarks>
public partial class RichTextViewer
{
    /// <summary>Defines the <see cref="SelectionBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<RichTextViewer, IBrush?>(
            nameof(SelectionBrush), new SolidColorBrush(Color.FromArgb(80, 0, 120, 215)));

    private int _anchor;
    private int _head;
    private bool _dragging;

    static RichTextViewer()
    {
        // The selection is filled in Render, so a new brush is only seen if it asks for a render.
        AffectsRender<RichTextViewer>(SelectionBrushProperty);
    }

    /// <summary>The fill drawn behind selected text.</summary>
    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>The lower end of the selection.</summary>
    public int SelectionStart => Math.Min(_anchor, _head);

    /// <summary>The higher end of the selection.</summary>
    public int SelectionEnd => Math.Max(_anchor, _head);

    /// <summary>The plain text covered by the selection, empty when nothing is selected.</summary>
    public string SelectedText() =>
        SelectionStart == SelectionEnd
            ? string.Empty
            : DocumentText.Of(Slice.Cut(Document, SelectionStart, SelectionEnd).Content);

    /// <summary>Selects the whole document.</summary>
    /// <returns>Whether the selection changed.</returns>
    public bool SelectAll() =>
        SetSelection(NearestText(0, forward: true), NearestText(Document.ContentSize, forward: false));

    /// <summary>The position nearest a given one at which text lives.</summary>
    /// <remarks>
    /// The viewer's counterpart to the editor's private version, and needed for the same reason:
    /// position 0 and <c>ContentSize</c> are the outsides of the first and last block, not
    /// positions in their text, so a selection anchored there covers nothing.
    /// </remarks>
    private int NearestText(int position, bool forward)
    {
        var size = Document.ContentSize;
        var start = Math.Clamp(position, 0, size);

        for (var distance = 0; distance <= size; distance++)
        {
            var first = forward ? start + distance : start - distance;

            if (first >= 0 && first <= size && Document.Resolve(first).IsInText)
            {
                return first;
            }

            var second = forward ? start - distance : start + distance;

            if (distance > 0 && second >= 0 && second <= size && Document.Resolve(second).IsInText)
            {
                return second;
            }
        }

        // Nothing but rules. The selection machinery will make what it can of the raw position.
        return start;
    }

    /// <summary>Drops the selection.</summary>
    /// <returns>Whether the selection changed.</returns>
    public bool ClearSelection() => SetSelection(_head, _head);

    /// <summary>Puts the selection on the clipboard as text and in every registered format.</summary>
    /// <remarks>
    /// The flavours come from the same exporters the editor uses, so a clip copied out of a
    /// viewer pastes into a word processor with its formatting rather than as flat text. That is
    /// the whole point for the clipboard manager this exists for.
    /// </remarks>
    /// <returns><see langword="true"/> if anything was written.</returns>
    public async Task<bool> CopyAsync()
    {
        var text = SelectedText();

        if (text.Length == 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return false;
        }

        return await RichTextClipboard
            .WriteAsync(clipboard, SelectedDocument(), Formats.OfType<IDocumentExporter>(), text)
            .ConfigureAwait(true);
    }

    /// <summary>The clipboard payload for the current selection.</summary>
    /// <remarks>
    /// Separate from <see cref="CopyAsync"/> for the reason the editor's counterpart is: this is
    /// the whole of what a copy decides, and what a real clipboard does with it afterwards is the
    /// platform's business rather than this control's.
    /// </remarks>
    internal DataTransfer BuildCopyData(string text) =>
        RichTextClipboard.Build(SelectedDocument(), Formats.OfType<IDocumentExporter>(), text);

    /// <summary>The selection as a document in its own right.</summary>
    /// <remarks>
    /// The selection, not the whole document: copying one paragraph out of a clip must not paste
    /// the clip back in.
    /// </remarks>
    internal DocumentNode SelectedDocument() =>
        new(Slice.Cut(Document, SelectionStart, SelectionEnd).Content.OfType<BlockNode>());

    /// <summary>The formats a copy is offered in.</summary>
    /// <remarks>
    /// Read-only in spirit as well as in name: only the exporters are ever consulted, because
    /// there is no path by which this control could import anything.
    /// </remarks>
    public ICollection<IDocumentFormat> Formats { get; } = new DocumentFormats();

    /// <summary>Puts the caret at the first position where text actually lives.</summary>
    /// <remarks>
    /// Position 0 is outside the first paragraph's content, so an unsnapped caret there steps from
    /// the wrong place — the first Shift+Right selected the *second* character. Called both at
    /// construction and whenever the document is replaced.
    /// </remarks>
    private void ResetSelection()
    {
        var start = TextSelection.Create(Document, NearestText(0, forward: true), NearestText(0, forward: true));

        _anchor = start.Anchor;
        _head = start.Head;

        InvalidateVisual();
    }

    /// <summary>Moves the selection, snapping to positions where text lives.</summary>
    private bool SetSelection(int anchor, int head)
    {
        var wanted = TextSelection.Create(Document, anchor, head);
        if (wanted.Anchor == _anchor && wanted.Head == _head)
        {
            return false;
        }

        _anchor = wanted.Anchor;
        _head = wanted.Head;

        InvalidateVisual();

        return true;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();

        var point = e.GetPosition(this);
        var position = PositionAt(point);

        _dragging = true;
        e.Pointer.Capture(this);

        // Double for a word, triple for a paragraph, exactly as in the editor. A reader who can
        // select at all expects the conventions that go with selecting.
        if (e.ClickCount >= 2 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (e.ClickCount % 3 == 0 ? SelectParagraphAt(position) : SelectWordAt(position))
            {
                e.Handled = true;

                return;
            }
        }

        SetSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? _anchor : position, position);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerMoved(e);

        var point = e.GetPosition(this);

        if (!_dragging)
        {
            // The pointer says what a click will do, so it may only claim text where selecting
            // is possible at all. This is why the arrow is not simply set once in the
            // constructor any more.
            var wanted = IsOverContent(point) ? TextCursor : ArrowCursor;

            if (!ReferenceEquals(Cursor, wanted))
            {
                Cursor = wanted;
            }

            return;
        }

        SetSelection(_anchor, PositionAt(point));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _dragging = false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The keys a reader has: move and extend, select all, copy. Nothing that could change the
    /// document is reachable, because no such method exists on this control.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Left:
                Step(forward: false, extend);
                break;

            case Key.Right:
                Step(forward: true, extend);
                break;

            case Key.Home:
                SetSelection(extend ? _anchor : NearestText(0, forward: true), NearestText(0, forward: true));
                break;

            case Key.End:
                SetSelection(extend ? _anchor : NearestText(Document.ContentSize, forward: false), NearestText(Document.ContentSize, forward: false));
                break;

            case Key.A when control:
                SelectAll();
                break;

            case Key.C when control:
                // Started and not awaited, as the editor does: the clipboard is asynchronous and
                // the key must be marked handled now either way.
                _ = CopyAsync();
                break;

            case Key.Escape:
                ClearSelection();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Moves the head one grapheme cluster, never landing inside one.</summary>
    /// <remarks>
    /// Through the paragraph view rather than by arithmetic on the position, for the reason
    /// Increment 0 established for the editor: stepping by one integer position walks into the
    /// middle of a surrogate pair or between a base character and its combining marks.
    /// </remarks>
    private void Step(bool forward, bool extend)
    {
        if (ParagraphAt(_head) is not { } at)
        {
            return;
        }

        var local = Math.Clamp(_head - at.Start, 0, at.View.ContentSize);

        var moved = forward
            ? at.View.NextCaretPosition(local)
            : at.View.PreviousCaretPosition(local);

        // At the edge of the paragraph, step one position outside it and let TextSelection.Create
        // repair to wherever text actually is next.
        var head = moved == local
            ? _head + (forward ? 1 : -1)
            : at.Start + moved;

        SetSelection(extend ? _anchor : head, Math.Clamp(head, 0, Document.ContentSize));
    }

    /// <summary>Selects the word around a document position.</summary>
    /// <param name="position">A position inside the word.</param>
    /// <returns>Whether a word was found and selected.</returns>
    public bool SelectWordAt(int position)
    {
        if (ParagraphAt(position) is not { } at)
        {
            return false;
        }

        var (start, end) = TextBoundaries.WordAt(
            at.View.Paragraph.Content.Text,
            Math.Clamp(position - at.Start, 0, at.View.ContentSize));

        return start != end && SetSelection(at.Start + start, at.Start + end);
    }

    /// <summary>Selects the whole paragraph around a document position.</summary>
    /// <param name="position">A position inside the paragraph.</param>
    /// <returns>Whether a paragraph was found and selected.</returns>
    public bool SelectParagraphAt(int position)
    {
        if (ParagraphAt(position) is not { } at || at.View.ContentSize == 0)
        {
            return false;
        }

        return SetSelection(at.Start, at.Start + at.View.ContentSize);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Under the text, so glyphs stay legible through the highlight. There is no caret to draw
    /// over it — a viewer has no insertion point — which is the one place this differs from the
    /// editor's order.
    /// </remarks>
    private protected override void RenderContent(DrawingContext context)
    {
        if (SelectionBrush is { } brush && SelectionStart != SelectionEnd)
        {
            EnumerateRange(SelectionStart, SelectionEnd, rect => context.FillRectangle(brush, rect));
        }

        RenderBlocks(context);
    }
}
