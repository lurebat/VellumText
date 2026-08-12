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

    /// <summary>Splits the block at the selection, replacing it if it is not empty.</summary>
    /// <remarks>
    /// The half that follows the caret keeps the block's presentation — its kind, alignment and
    /// indent — because pressing Enter in the middle of a quote should give two quotes rather
    /// than a quote and a body paragraph. Enter at the <em>end</em> of a heading is the case
    /// where users usually want a body paragraph instead, and that is a deliberate omission
    /// here: it is a policy about heading kinds, and kinds arrive in the next piece of work.
    /// </remarks>
    /// <returns>Whether anything changed.</returns>
    public bool Split()
    {
        if (Block is null)
        {
            return false;
        }

        var selection = _state.Selection;
        var transaction = _state.Transaction().As(TransactionKind.Structure);

        if (!selection.IsEmpty)
        {
            transaction.Delete(selection.From, selection.To);
        }

        var at = selection.From;
        var tail = Block.Paragraph.WithContent(InlineContent.Empty);

        _goalX = null;

        transaction
            .Replace(at, at, new Slice([ParagraphNode.Empty, tail], 1, 1))

            // Two positions are inserted, not one: the first block closes and the second opens.
            // Aiming at the second of them is exact rather than relying on repair — a cursor
            // built on the boundary between them is silently moved here by Selection.Near, so
            // getting this wrong is invisible until something depends on the anchor.
            .SetSelection(TextSelection.Cursor(at + 2))
            .SetStoredMarks(null);

        return Apply(transaction);
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnTextInput(e);

        if (e.Handled || Block is null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Control characters arrive here as text on some backends — Enter as "\r", Escape as
        // "\u001b", Backspace as "\b". None of them are characters the document should hold;
        // Enter is a paragraph split and is handled as a key, so it must not also arrive as
        // text or one press would split twice.
        if (e.Text.Any(char.IsControl))
        {
            return;
        }

        e.Handled = InsertText(e.Text);
    }

    private bool DeleteAround(bool forward)
    {
        if (Block is null)
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
            var local = Math.Clamp(selection.Head - BlockStart, 0, Block.ContentSize);

            // Backspace is not the reverse of Delete. It peels one combining mark off a cluster
            // where the arrow keys and Delete step over the whole thing — Increment 0 measured
            // the difference and it was kept deliberately.
            var other = forward
                ? Block.NextCaretPosition(local)
                : Block.BackspacePosition(local);

            if (other == local)
            {
                return DeleteAcrossBoundary(forward, transaction);
            }

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

    /// <summary>
    /// Backspace at the very start of a block, or Delete at its very end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is nothing left in the block to delete, so what goes is what separates it from its
    /// neighbour. Against another paragraph that is the pair of positions where one closes and
    /// the next opens, and removing them joins the two. Against a block a caret cannot enter — a
    /// rule — the block itself goes, which is what every editor does and what makes a rule
    /// removable at all before there is a gesture that selects one.
    /// </para>
    /// <para>
    /// A join the schema will not allow is left to fail on its own: the step reports it, the
    /// transaction changed nothing, and the key reads as ignored rather than corrupting the tree.
    /// </para>
    /// </remarks>
    private bool DeleteAcrossBoundary(bool forward, Transaction transaction)
    {
        var neighbour = _caretBlock + (forward ? 1 : -1);

        if (neighbour < 0 || neighbour >= _slots.Length)
        {
            return false;
        }

        var slot = _slots[_caretBlock];
        var other = _slots[neighbour];

        var (from, to) = other.Node.IsLeaf
            ? (other.Start, other.Start + other.Node.NodeSize)
            : forward ? (slot.End, other.Start) : (other.End, slot.Start);

        // Everything removed lies before the caret when deleting backwards, so the caret moves
        // back by exactly that much; deleting forwards removes what is after it, so it stays.
        var caret = forward ? _state.Selection.Head : slot.Start - (to - from);

        transaction
            .Delete(from, to)
            .SetSelection(TextSelection.Cursor(caret))
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
