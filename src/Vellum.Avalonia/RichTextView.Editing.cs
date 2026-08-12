using Avalonia.Input;

namespace Vellum.Avalonia;

/// <summary>
/// Typing, Backspace and Delete.
/// </summary>
/// <remarks>
/// <para>
/// Every edit goes through <see cref="Apply"/>, which is the only place a transaction becomes
/// the control's new state. Nothing here builds a document directly; the model decides whether
/// a step is legal and reports it, and a transaction that changed nothing is dropped rather
/// than installed.
/// </para>
/// <para>
/// Formatting for a typed character comes from the mark stored on the state if there is one,
/// and otherwise from the character to the left of the caret. That is the rule that makes
/// typing at the end of a bold word stay bold.
/// </para>
/// </remarks>
public partial class RichTextView
{
    /// <summary>Applies a transaction, if it changed anything.</summary>
    /// <remarks>
    /// A transaction whose steps all failed leaves the state alone. Installing it anyway would
    /// replace the state with an identical one and move the selection to wherever the caller
    /// guessed, which is worse than doing nothing.
    /// </remarks>
    /// <param name="transaction">The transaction to apply.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Apply(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (!transaction.ChangedDoc)
        {
            return false;
        }

        // Recorded before the state is installed, and unconditionally: every edit that goes
        // through the control is undoable by construction rather than because each call site
        // remembered. History itself refuses to record a replay, so this cannot make Ctrl+Z
        // undo itself.
        _history = _history.Record(transaction);

        // Installing the state resets the caret blink; see OnStateChanged.
        State = _state.Apply(transaction);

        return true;
    }

    /// <summary>Inserts text at the selection, replacing it if it is not empty.</summary>
    /// <param name="text">The text to insert.</param>
    /// <returns>Whether anything changed.</returns>
    public bool InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return false;
        }

        var selection = _state.Selection;
        var transaction = _state.Transaction().As(TransactionKind.Typing);

        if (!selection.IsEmpty)
        {
            transaction.Delete(selection.From, selection.To);
        }

        var at = selection.From;

        transaction
            .InsertText(at, text, MarkForInsertion(at))
            .SetSelection(TextSelection.Cursor(at + text.Length))
            .SetStoredMarks(null);

        _goalX = null;

        return Apply(transaction);
    }

    /// <summary>Deletes the selection, or one cluster back from the caret.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Backspace() => DeleteAround(forward: false);

    /// <summary>Deletes the selection, or one cluster forward from the caret.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Delete() => DeleteAround(forward: true);

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnTextInput(e);

        if (e.Handled || _block is null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Control characters arrive here as text on some backends — Enter as "\r", Escape as
        // "\u001b", Backspace as "\b". None of them are characters the document should hold, and
        // Enter in particular is a paragraph split, which is structure and not this increment.
        if (e.Text.Any(char.IsControl))
        {
            return;
        }

        e.Handled = InsertText(e.Text);
    }

    private bool DeleteAround(bool forward)
    {
        if (_block is null)
        {
            return false;
        }

        _goalX = null;

        var selection = _state.Selection;
        var transaction = _state.Transaction().As(TransactionKind.Delete);

        int from;
        int to;

        if (!selection.IsEmpty)
        {
            from = selection.From;
            to = selection.To;
        }
        else
        {
            var local = Math.Clamp(selection.Head - BlockStart, 0, _block.ContentSize);

            // Backspace is not the reverse of Delete. It peels one combining mark off a cluster
            // where the arrow keys and Delete step over the whole thing — Increment 0 measured
            // the difference and it was kept deliberately.
            var other = forward
                ? _block.NextCaretPosition(local)
                : _block.BackspacePosition(local);

            from = BlockStart + Math.Min(local, other);
            to = BlockStart + Math.Max(local, other);
        }

        if (from == to)
        {
            return false;
        }

        transaction
            .Delete(from, to)
            .SetSelection(TextSelection.Cursor(from))
            .SetStoredMarks(null);

        return Apply(transaction);
    }

    /// <summary>The formatting a character typed at a position should take.</summary>
    /// <remarks>
    /// A stored mark wins: it is what a user asking for bold before typing anything set. With no
    /// stored mark the formatting is inherited from the character to the left, which is what
    /// keeps typing at the end of a bold word bold.
    /// </remarks>
    private MarkSet MarkForInsertion(int position)
    {
        if (_state.StoredMarks is { } stored)
        {
            return stored;
        }

        var resolved = _state.Doc.Resolve(position);

        return resolved.Paragraph is { } paragraph
            ? paragraph.Content.MarkForInsertionAt(resolved.ParentOffset)
            : MarkSet.Empty;
    }
}
