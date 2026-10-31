using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using ImeSelection = Avalonia.Input.TextInput.TextSelection;

namespace VellumText.Avalonia;

/// <summary>
/// Composition through a platform input method, per architecture 4.8.
/// </summary>
/// <remarks>
/// <para>
/// <b>A composition never becomes a transaction.</b> While the input method is composing, the
/// text lives only in <see cref="ParagraphView.Preedit"/>, which is spliced into the layout and
/// drawn underlined. The document is untouched and so is the undo stack. When the input method
/// finalizes, the platform sends the result as ordinary text input and it becomes one typing
/// transaction like any other keystroke. Ctrl+Z therefore takes back a committed word, never a
/// half-composed one.
/// </para>
/// <para>
/// The surrounding text handed to the input method is <em>paragraph-local</em>. Platform input
/// methods want a sentence or two of context to make conversion decisions; handing them a whole
/// document would be useless to them and expensive for us.
/// </para>
/// </remarks>
public partial class RichTextView
{
    private readonly RichTextInputMethodClient _inputMethod;

    /// <summary>The paragraph a composition is drawn in, or null if nothing is composing.</summary>
    /// <remarks>
    /// Kept rather than looked up again, because the lookup answers where a composition would go
    /// now, not where the one on screen is. Once the selection has moved the two differ, and
    /// clearing the paragraph the lookup found left the composed text drawn in the other.
    /// </remarks>
    private ParagraphView? _composing;

    /// <summary>Where <see cref="_composing"/> started when it was composed in.</summary>
    private int _composingStart;

    /// <summary>Text currently being composed, or null if nothing is.</summary>
    internal Preedit? Preedit => _composing?.Preedit;

    /// <summary>
    /// The paragraph a composition is drawn in: the one holding the start of the selection,
    /// which is where the commit will replace it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Block"/>, which is the paragraph holding the head. Over a selection that
    /// crosses paragraphs the two differ, and anchoring in the head's paragraph drew the composed
    /// text at the start of the last paragraph while the commit landed in the first.
    /// </remarks>
    /// <para>
    /// Resolved through <see cref="DocumentPresenter.ParagraphAt"/>, whose start and origin are
    /// in document and control coordinates; <see cref="CaretText"/>'s are relative to its block.
    /// </para>
    private TextAt? CompositionText => ParagraphAt(_state.Selection.From);

    /// <summary>Whether an input method is composing into this view.</summary>
    public bool IsComposing => Preedit is not null;

    /// <summary>Sets or clears the text being composed at the caret.</summary>
    /// <remarks>
    /// <para>
    /// Anchored at the start of the selection rather than at its head, so composing over a
    /// selection draws the composed text where the replacement will land. The selection itself
    /// stays put: it is replaced by the commit, in that one transaction, not now.
    /// </para>
    /// <para>
    /// A locked view refuses to start composing at all. The commit was always refused — it
    /// arrives as ordinary text input and the edit path declines it — but refusing only at the
    /// end meant the user watched composed characters appear underlined and then vanish, with the
    /// input method's own candidate window still open over them. Clearing a composition is
    /// allowed whatever the state, so a view locked mid-composition can still be tidied up.
    /// </para>
    /// </remarks>
    /// <param name="text">The composed text, or null or empty to stop composing.</param>
    /// <param name="cursorPosition">Where the input method wants the caret within the text.</param>
    /// <returns>Whether anything changed.</returns>
    internal bool SetPreedit(string? text, int? cursorPosition = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Recomposed(ClearComposition());
        }

        if (CompositionText is not { } at)
        {
            return false;
        }

        var block = at.View;

        if (IsReadOnly)
        {
            return false;
        }

        // Composing needs a caret, and a rectangle of cells has none — the position its bounds
        // begin at is in a row, outside any text. The commit would land in the wrong cell.
        if (Rectangle is not null)
        {
            return false;
        }

        var anchor = Math.Clamp(_state.Selection.From - at.Start, 0, block.ContentSize);

        // The cursor is clamped rather than validated: it comes from a platform input method,
        // and a client that throws at one is a client that crashes the application on a stray
        // report from somebody else's IME.
        var preedit = new Preedit(
            text,
            anchor,
            cursorPosition is { } cursor ? Math.Clamp(cursor, 0, text.Length) : null);

        // Before anything drops a layout: the line at the top of the viewport is read from it.
        HoldViewportAnchor();

        var moved = !ReferenceEquals(_composing, block) && ClearComposition();
        var changed = block.SetPreedit(preedit);

        _composing = block;
        _composingStart = at.Start;

        if (changed)
        {
            Unmeasured(block, at.Start);
        }

        return Recomposed(moved | changed);
    }

    /// <summary>Removes the composition from the paragraph it is drawn in.</summary>
    /// <returns>Whether there was one to remove.</returns>
    private bool ClearComposition()
    {
        if (_composing is not { } composing)
        {
            return false;
        }

        _composing = null;
        HoldViewportAnchor();

        if (!composing.SetPreedit(null))
        {
            return false;
        }

        Unmeasured(composing, _composingStart);

        return true;
    }

    /// <summary>Tells the block holding a paragraph that the paragraph dropped its layout.</summary>
    /// <remarks>
    /// The paragraph dropped its layout without the block holding it being updated, and a table
    /// measures only the rows it knows have changed: told nothing, it would answer for and draw
    /// this paragraph unmeasured. The block is found from the paragraph itself, not from the
    /// selection — a composition is cleared after the selection has left it.
    /// </remarks>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="start">Where the paragraph started when it was composed in.</param>
    private void Unmeasured(ParagraphView paragraph, int start)
    {
        if (_views.Count == 0)
        {
            return;
        }

        var hinted = BlockIndexOf(start);

        if (Holds(hinted))
        {
            return;
        }

        for (var i = 0; i < _views.Count; i++)
        {
            if (i != hinted && Holds(i))
            {
                return;
            }
        }

        bool Holds(int i) =>
            ReferenceEquals(_views[i], paragraph)
            || _views[i].ParagraphChanged(paragraph, start - _slots[i].Start);
    }

    /// <summary>Brings the view up to date after the composition changed, if it did.</summary>
    /// <param name="changed">Whether it did.</param>
    /// <returns><paramref name="changed"/>.</returns>
    private bool Recomposed(bool changed)
    {
        if (!changed)
        {
            ReleaseViewportAnchor();

            return false;
        }

        // Laid out eagerly, for the same reason a state change is: the composition dropped the
        // layout, and the input method asks for CursorRectangle immediately after handing over
        // the composed text — well before the layout pass. Deferring here is not a stale
        // answer, it is an exception.
        Realize();

        // Composed text changes what is laid out, so the view re-measures exactly as it does
        // for an edit — a composition can wrap the paragraph onto another line.
        InvalidateMeasure();
        ResetCaretBlink();

        return true;
    }

    /// <summary>The plain text of the paragraph the caret is in, without any composition.</summary>
    internal string ParagraphText => Block?.Paragraph.Content.Text ?? string.Empty;

    /// <summary>The selection in paragraph-local offsets, as the input method wants it.</summary>
    internal ImeSelection InputMethodSelection
    {
        get
        {
            if (Block is not { } block)
            {
                return new ImeSelection(0, 0);
            }

            var size = block.ContentSize;

            return new ImeSelection(
                Math.Clamp(_state.Selection.Anchor - BlockStart, 0, size),
                Math.Clamp(_state.Selection.Head - BlockStart, 0, size));
        }

        set
        {
            if (Block is null)
            {
                return;
            }

            // Through MoveTo in both steps, so a selection the input method asks for is clamped
            // and normalized by exactly the same code as one the user drags out.
            MoveTo(BlockStart + value.Start, extend: false);
            MoveTo(BlockStart + value.End, extend: true);
        }
    }

    /// <summary>Where the caret is drawn, in this control's coordinates.</summary>
    /// <remarks>
    /// While composing, the caret belongs to the input method and sits at an offset into the
    /// composed text, which is a position the document does not have. This is the rectangle
    /// before it is snapped to the pixel grid; see <see cref="SnapCaret"/>.
    /// </remarks>
    public Rect CaretRect()
    {
        // While composing, the caret is inside the composed text, wherever that is drawn.
        if (CompositionText is { View.Preedit: not null } composing)
        {
            return composing.View.GetPreeditCaretRect().Translate(new Vector(composing.Origin.X, composing.Origin.Y));
        }

        if (Block is not { } block)
        {
            return default;
        }

        var local = block.GetCaretRect(Math.Clamp(_state.Selection.Head - BlockStart, 0, block.ContentSize));

        // Translated by the block's own origin: a caret in the fourth paragraph is drawn where
        // that paragraph is, and the block has no idea where that is.
        return local.Translate(BlockOffset);
    }

    /// <summary>Tells the input method that what it is composing against has moved.</summary>
    private void NotifyInputMethod() => _inputMethod.NotifyChanged();
}

/// <summary>
/// The <see cref="TextInputMethodClient"/> a <see cref="RichTextView"/> hands to the platform.
/// </summary>
/// <remarks>
/// Everything here is in paragraph-local offsets, which is what an input method wants and all it
/// can usefully be given. See <see cref="RichTextView"/>'s IME remarks for why a preedit never
/// reaches the model.
/// </remarks>
internal sealed class RichTextInputMethodClient : TextInputMethodClient
{
    private readonly RichTextView _view;

    internal RichTextInputMethodClient(RichTextView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _view = view;
    }

    /// <inheritdoc/>
    public override Visual TextViewVisual => _view;

    /// <inheritdoc/>
    public override bool SupportsPreedit => true;

    /// <inheritdoc/>
    public override bool SupportsSurroundingText => true;

    /// <inheritdoc/>
    public override string SurroundingText => _view.ParagraphText;

    /// <inheritdoc/>
    public override Rect CursorRectangle => _view.CaretRect();

    /// <inheritdoc/>
    public override ImeSelection Selection
    {
        get => _view.InputMethodSelection;
        set => _view.InputMethodSelection = value;
    }

    /// <inheritdoc/>
    public override void SetPreeditText(string? preeditText, int? cursorPos) =>
        _view.SetPreedit(preeditText, cursorPos);

    /// <summary>Tells the platform that the text, selection or caret it composes against moved.</summary>
    internal void NotifyChanged()
    {
        RaiseSurroundingTextChanged();
        RaiseSelectionChanged();
        RaiseCursorRectangleChanged();
    }
}
