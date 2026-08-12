using Avalonia;
using Avalonia.Input;

namespace Vellum.Avalonia;

/// <summary>
/// Pointer and keyboard selection.
/// </summary>
/// <remarks>
/// <para>
/// Everything here goes through <see cref="MoveTo"/>, which is the only place a caret movement
/// turns into a new selection. Input handlers decide <em>where</em>; they never decide how a
/// selection is built, so extend-with-Shift and drag-with-the-mouse cannot drift apart.
/// </para>
/// <para>
/// No handler here edits the document. Typing arrives in the next piece of work; selection has
/// to be right first, because every edit is expressed relative to it.
/// </para>
/// </remarks>
public partial class RichTextView
{
    private bool _dragging;
    private double? _goalX;

    /// <summary>
    /// The horizontal offset a vertical move is trying to keep, if one is in progress.
    /// </summary>
    /// <remarks>
    /// Exposed for tests. A run of Up/Down keeps the column the caret started from rather than
    /// the column it reached, so passing through a short line does not permanently pull the
    /// caret left. Any other movement clears it.
    /// </remarks>
    public double? GoalX => _goalX;

    /// <summary>Whether a pointer drag is currently extending the selection.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Places the caret, or extends the selection to reach it.</summary>
    /// <remarks>
    /// The clamp is to the <em>block</em>, which while there is one block coincides exactly with
    /// the document-wide clamp <c>TextSelection.Create</c> already applies — measured: removing
    /// it changes no observable behaviour today. It is kept because the block boundary is what
    /// this method means, and the two stop coinciding as soon as there is a second block.
    /// </remarks>
    /// <param name="position">The document position to move to.</param>
    /// <param name="extend">Whether to keep the current anchor.</param>
    public void MoveTo(int position, bool extend)
    {
        var clamped = Math.Clamp(position, BlockStart, BlockStart + ContentSize);
        var anchor = extend ? _state.Selection.Anchor : clamped;
        var selection = TextSelection.Create(_state.Doc, anchor, clamped);

        if (selection == _state.Selection)
        {
            // Still reset the blink: a caret that will not move should be solid, not caught
            // mid-blink, or the key reads as dropped.
            ResetCaretBlink();
            return;
        }

        State = EditorState.Create(_state.Doc, selection);
        ResetCaretBlink();
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

        _goalX = null;
        _dragging = true;

        // Explicit, although Avalonia also captures implicitly on press — measured: deleting
        // this line changes nothing the headless harness can observe. It is kept because a drag
        // that leaves the control is exactly the case the harness cannot exercise, and that is
        // the case capture exists for.
        e.Pointer.Capture(this);

        MoveTo(PositionAt(e.GetPosition(this)), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerMoved(e);

        if (!_dragging)
        {
            return;
        }

        // extend: true unconditionally. Once a drag has started the anchor is where it went
        // down, whether or not Shift is still held.
        MoveTo(PositionAt(e.GetPosition(this)), extend: true);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        // The single place a drag ends. Releasing the button drops capture and arrives here, and
        // so does having capture taken away — a popup opening, the window deactivating. Clearing
        // the flag in a pointer-released handler as well would be duplicate bookkeeping that no
        // test could distinguish from this.
        _dragging = false;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnKeyDown(e);

        if (e.Handled || _block is null)
        {
            return;
        }

        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var word = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Left:
                Horizontal(forward: false, word, extend);
                break;

            case Key.Right:
                Horizontal(forward: true, word, extend);
                break;

            case Key.Up:
                Vertical(down: false, extend);
                break;

            case Key.Down:
                Vertical(down: true, extend);
                break;

            case Key.Home:
                _goalX = null;
                MoveTo(word ? BlockStart : BlockStart + _block.LineStart(CaretLine), extend);
                break;

            case Key.End:
                _goalX = null;
                MoveTo(
                    word ? BlockStart + ContentSize : BlockStart + _block.LineEnd(CaretLine),
                    extend);
                break;

            case Key.Back:
                if (!Backspace())
                {
                    return;
                }

                break;

            case Key.Delete:
                if (!Delete())
                {
                    return;
                }

                break;

            case Key.Z when word && extend:
            case Key.Y when word:
                if (!Redo())
                {
                    return;
                }

                break;

            case Key.Z when word:
                if (!Undo())
                {
                    return;
                }

                break;

            case Key.A when word:
                _goalX = null;
                State = EditorState.Create(
                    _state.Doc, TextSelection.Create(_state.Doc, BlockStart, BlockStart + ContentSize));
                ResetCaretBlink();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>The number of positions in the realised block.</summary>
    private int ContentSize => _block?.ContentSize ?? 0;

    /// <summary>The caret's position expressed relative to the block.</summary>
    private int CaretLocal =>
        Math.Clamp(_state.Selection.Head - BlockStart, 0, ContentSize);

    /// <summary>
    /// The visual line the caret is <em>drawn</em> on.
    /// </summary>
    /// <remarks>
    /// A wrap boundary is one position on two lines, and the two answers are not equally good
    /// here. Measured on a paragraph wrapping at 72: the caret for position 72 is drawn at
    /// y = 15.96, which is the second line. So Home, End and vertical movement must all treat it
    /// as belonging to the line that <em>starts</em> there, or they disagree with what the user
    /// can see — pressing Up at a wrap boundary would leave for the top of the document, and
    /// Home would jump to the start of the previous line.
    /// </remarks>
    private int CaretLine => _block!.LineIndexAt(CaretLocal, forward: true);

    private void Horizontal(bool forward, bool word, bool extend)
    {
        _goalX = null;

        // A collapsing move — no Shift, and something is selected — goes to the edge of the
        // selection rather than one step from its head. Stepping instead would swallow a
        // character at one end, which is a bug users notice immediately.
        if (!extend && !_state.Selection.IsEmpty && !word)
        {
            MoveTo(forward ? _state.Selection.To : _state.Selection.From, extend: false);
            return;
        }

        var local = CaretLocal;

        var moved = word
            ? WordBoundary(local, forward)
            : forward ? _block!.NextCaretPosition(local) : _block!.PreviousCaretPosition(local);

        MoveTo(BlockStart + moved, extend);
    }

    private void Vertical(bool down, bool extend)
    {
        var local = CaretLocal;
        var line = CaretLine;

        // Read the goal before moving, and keep whatever a previous vertical move established.
        _goalX ??= _block!.GetCaretRect(local).X;

        var target = down ? line + 1 : line - 1;

        if (target < 0 || target >= _block!.LineCount)
        {
            // Off the end of the paragraph: go to the document edge, the way every editor does,
            // rather than refusing to move. The goal column is deliberately kept, so Up then
            // Down returns to the column the caret started in.
            MoveTo(down ? BlockStart + ContentSize : BlockStart, extend);
            return;
        }

        MoveTo(BlockStart + _block.PositionAtLineX(target, _goalX.Value), extend);
    }

    /// <summary>
    /// The next word boundary in a direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A deliberate approximation, not UAX #29: a word is a run of letters, digits, marks and
    /// underscores, and moving skips any separators before the run. Moving forward lands after
    /// the word, moving back lands before it, which is what Windows does.
    /// </para>
    /// <para>
    /// It walks text elements rather than chars so it cannot stop inside a surrogate pair or
    /// between a base character and its combining marks.
    /// </para>
    /// </remarks>
    private int WordBoundary(int local, bool forward)
    {
        var text = _block!.Paragraph.Content.Text;

        if (forward)
        {
            var i = local;

            while (i < text.Length && !IsWordChar(text, i))
            {
                i = _block.NextCaretPosition(i);
            }

            while (i < text.Length && IsWordChar(text, i))
            {
                i = _block.NextCaretPosition(i);
            }

            return i;
        }

        var j = local;

        while (j > 0 && !IsWordChar(text, _block.PreviousCaretPosition(j)))
        {
            j = _block.PreviousCaretPosition(j);
        }

        while (j > 0 && IsWordChar(text, _block.PreviousCaretPosition(j)))
        {
            j = _block.PreviousCaretPosition(j);
        }

        return j;
    }

    private static bool IsWordChar(string text, int index)
    {
        if (index < 0 || index >= text.Length)
        {
            return false;
        }

        var category = char.GetUnicodeCategory(text, index);

        return category is System.Globalization.UnicodeCategory.UppercaseLetter
            or System.Globalization.UnicodeCategory.LowercaseLetter
            or System.Globalization.UnicodeCategory.TitlecaseLetter
            or System.Globalization.UnicodeCategory.ModifierLetter
            or System.Globalization.UnicodeCategory.OtherLetter
            or System.Globalization.UnicodeCategory.DecimalDigitNumber
            or System.Globalization.UnicodeCategory.LetterNumber
            or System.Globalization.UnicodeCategory.OtherNumber
            or System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark
            or System.Globalization.UnicodeCategory.EnclosingMark
            or System.Globalization.UnicodeCategory.ConnectorPunctuation;
    }
}
