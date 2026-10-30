using Avalonia.Input;

namespace VellumText.Avalonia;

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

        // The one place read-only is enforced. See RichTextView.Options.cs: a check at each
        // command is a check the next command will forget, and forgetting it is silent.
        if (IsReadOnly)
        {
            return false;
        }

        var before = HistoryFlags;

        // Recorded before the state is installed, and unconditionally: every edit that goes
        // through the control is undoable by construction rather than because each call site
        // remembered. History itself refuses to record a replay, so this cannot make Ctrl+Z
        // undo itself.
        _history = _history.Record(transaction);

        // Installing the state resets the caret blink; see OnStateChanged.
        SetState(_state.Apply(transaction), derived: true);

        // Announced last, so that a listener woken by either signal sees both settled.
        RaiseHistoryFlags(before);

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

        // A rectangle of cells has no one place a character could go, and its From..To spans
        // cells outside it — replacing that range would take whole cells out of their rows.
        if (Rectangle is not null)
        {
            return false;
        }

        var selection = _state.Selection;
        var transaction = _state.Transaction().As(TransactionKind.Typing);

        if (!RemoveSelection(transaction, selection))
        {
            return false;
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
    /// <remarks>
    /// At the very start of a list item's first paragraph it takes the bullet off instead —
    /// lifting the item one level, out of the list at the top — and keeps the words, as word
    /// processors do; joining the words onto the item above is the next Backspace. In an empty
    /// code block it turns the block into an ordinary paragraph, which is the way out of a code
    /// block that has nothing in it yet.
    /// </remarks>
    /// <returns>Whether anything changed.</returns>
    public bool Backspace()
    {
        if (Rectangle is null
            && _state.Selection.IsEmpty
            && _state.Doc.Resolve(_state.Selection.From) is
                { Parent: ParagraphNode { Kind: ParagraphKind.Code, Content.Text.Length: 0 } } code)
        {
            var transaction = _state.Transaction()
                .As(TransactionKind.Format)
                .Step(new SetBlockAttrStep(
                    code.Before(code.Depth), BlockAttr.ParagraphKind, (int)ParagraphKind.Body));

            return Apply(transaction);
        }

        if (Rectangle is null
            && _state.Selection.IsEmpty
            && _state.Doc.Resolve(_state.Selection.From) is { ParentOffset: 0 } at
            && at.Depth >= 2
            && at.Parent is ParagraphNode
            && at.NodeAt(at.Depth - 1) is ListItemNode
            && at.IndexAt(at.Depth - 1) == 0
            && LiftListItem())
        {
            return true;
        }

        return DeleteAround(forward: false);
    }

    /// <summary>Whether a position is inside a paragraph that is directly a list item's.</summary>
    private static bool InListItemParagraph(DocumentNode doc, int position)
    {
        var at = doc.Resolve(position);

        return at.Depth >= 2
            && at.Parent is ParagraphNode
            && at.NodeAt(at.Depth - 1) is ListItemNode;
    }

    /// <summary>Deletes the selection, or one cluster forward from the caret.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Delete() => DeleteAround(forward: true);

    /// <summary>Splits the block at the selection, replacing it if it is not empty.</summary>
    /// <remarks>
    /// <para>
    /// The half that follows the caret keeps the block's presentation — its kind, alignment and
    /// indent — because pressing Enter in the middle of a quote should give two quotes rather
    /// than a quote and a body paragraph. Enter at the <em>end</em> of a heading is the case
    /// where users usually want a body paragraph instead, and that is a deliberate omission
    /// here: it is a policy about heading kinds, and kinds arrive in the next piece of work.
    /// </para>
    /// <para>
    /// A code block is the exception: its lines are newlines in its text, so Enter there breaks
    /// the line and the block stays one block. Enter on an empty last line under another empty
    /// line leaves it instead, dropping the empty lines at its end.
    /// </para>
    /// </remarks>
    /// <returns>Whether anything changed.</returns>
    public bool Split()
    {
        if (Block is null || Rectangle is not null || !AcceptsReturn)
        {
            return false;
        }

        // Splitting a code block made every line of code a block of its own, with no way out of
        // the last one but the toolbar.
        if (InCodeBlock(_state.Doc, _state.Selection.From))
        {
            return OnEmptyLinesAtEndOfCode() ? LeaveCodeBlock(trim: true) : InsertLineBreak();
        }

        // Enter in an empty bullet leaves the list rather than adding another empty one. It is
        // the only way out of a list that does not require knowing a shortcut, and every editor
        // a user has met behaves this way.
        if (InEmptyListItem() && LiftListItem())
        {
            return true;
        }

        var selection = _state.Selection;
        var transaction = _state.Transaction().As(TransactionKind.Structure);

        if (!RemoveSelection(transaction, selection))
        {
            return false;
        }

        var at = selection.From;
        var tail = Block.Paragraph.WithContent(InlineContent.Empty);

        _goalX = null;

        // In a list item, Enter starts the next item, carrying what follows the caret with it,
        // as every word processor does. Splitting only the paragraph left the new line inside the
        // same item: it had no bullet, Tab could not nest it, and Shift+Tab lifted both lines.
        if (InListItemParagraph(transaction.Doc, at))
        {
            transaction
                .Replace(
                    at,
                    at,
                    new Slice([new ListItemNode([ParagraphNode.Empty]), new ListItemNode([tail])], 2, 2))

                // Four positions: the paragraph and its item close, the next item and its
                // paragraph open.
                .SetSelection(TextSelection.Cursor(at + 4))
                .SetStoredMarks(null);

            return Apply(transaction);
        }

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

    /// <summary>Inserts a line break inside the paragraph, without splitting it.</summary>
    /// <remarks>
    /// <para>
    /// A newline in a paragraph's own text, which is what the model has always meant by a soft
    /// break: the HTML importer turns <c>&lt;br&gt;</c> into one and the exporter turns it back,
    /// <c>&lt;pre&gt;</c> produces them, and Avalonia's formatter already breaks a line on one.
    /// So there is no new node kind here and nothing in the model changed — measured with the
    /// text formatter directly, "one\ntwo" lays out as two lines with the newline counted as the
    /// first line's terminator rather than drawn.
    /// </para>
    /// <para>
    /// Deliberately routed through <see cref="InsertText"/> rather than built as its own step, so
    /// that a soft break replaces a selection, carries the marks at the caret and joins the
    /// typing transaction exactly as a typed character does. <see cref="AcceptsReturn"/> governs
    /// it for the same reason it governs <see cref="Split"/>: a single-line field should not gain
    /// a second line by a different route.
    /// </para>
    /// </remarks>
    /// <returns>Whether anything changed.</returns>
    public bool InsertLineBreak() => AcceptsReturn && InsertText("\n");

    /// <summary>Whether a position is inside a code block.</summary>
    private static bool InCodeBlock(DocumentNode doc, int position) =>
        doc.Resolve(position).Parent is ParagraphNode { Kind: ParagraphKind.Code };

    /// <summary>
    /// Whether the caret is at the end of a code block whose last line is empty and whose line
    /// before that is empty too — the third Enter of the three that leave the block.
    /// </summary>
    /// <remarks>
    /// Only from a caret at the very end: two empty lines in the middle of the code are content,
    /// and Enter over a selection replaces it with a line break as it does anywhere else.
    /// </remarks>
    private bool OnEmptyLinesAtEndOfCode()
    {
        var selection = _state.Selection;

        if (!selection.IsEmpty
            || _state.Doc.Resolve(selection.Head) is not
                { Parent: ParagraphNode { Kind: ParagraphKind.Code } code } at
            || at.ParentOffset != code.ContentSize)
        {
            return false;
        }

        var text = code.Content.Text;

        return text.EndsWith('\n') && (text.Length == 1 || text[^2] == '\n');
    }

    /// <summary>
    /// Leaves the code block the caret is in for a new body paragraph directly below it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every way out of a code block ends here — the third Enter, Ctrl+Enter, and ↓ from its last
    /// line when there is nothing below — so they all land in the same place: an ordinary
    /// paragraph straight after the block, inside whatever list item or cell holds it.
    /// </para>
    /// <para>
    /// <paramref name="trim"/> drops the empty lines at the end of the block, which is what the
    /// Enters that asked to leave typed. Removing them and adding the paragraph are one
    /// transaction, so one undo puts the empty lines and the caret back.
    /// </para>
    /// </remarks>
    /// <param name="trim">Whether to drop the empty lines at the end of the block.</param>
    /// <returns>Whether anything changed.</returns>
    private bool LeaveCodeBlock(bool trim)
    {
        if (Rectangle is not null
            || !AcceptsReturn
            || _state.Doc.Resolve(_state.Selection.Head) is not
                { Parent: ParagraphNode { Kind: ParagraphKind.Code } code } at)
        {
            return false;
        }

        var text = code.Content.Text;
        var removed = trim ? text.Length - text.TrimEnd('\n').Length : 0;
        var end = at.End(at.Depth);
        var after = at.After(at.Depth) - removed;
        var transaction = _state.Transaction().As(TransactionKind.Structure);

        if (removed > 0)
        {
            transaction.Delete(end - removed, end);
        }

        transaction
            .Replace(after, after, new Slice([ParagraphNode.Empty], 0, 0))
            .SetSelection(TextSelection.Cursor(after + 1))
            .SetStoredMarks(null);

        _goalX = null;

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
        // Before the Block guard: a rectangle's caret is not inside any one block, so asking
        // whether there is a current block would refuse the delete before it was considered.
        if (Rectangle is { } rectangle)
        {
            return ClearCells(rectangle);
        }

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
            .DeleteRange(from, to)
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
    /// Joining two parts of a table is one of those refusals: the step will not close a range
    /// back up over a cell boundary, because a single-row table merged into one cell is still a
    /// legal table and is not the one the user had.
    /// </para>
    /// </remarks>
    private bool DeleteAcrossBoundary(bool forward, Transaction transaction)
    {
        // Adjacency in the document, not in the drawn run: two paragraphs inside one table cell
        // are neighbours the user can join, and the drawn run sees only the table around them.
        var leaves = Leaves;
        var head = _state.Selection.Head;
        var index = -1;

        for (var i = 0; i < leaves.Length; i++)
        {
            if (head >= leaves[i].Start && head <= leaves[i].End)
            {
                index = i;
                break;
            }
        }

        var neighbour = index + (forward ? 1 : -1);

        if (index < 0 || neighbour < 0 || neighbour >= leaves.Length)
        {
            return false;
        }

        var slot = leaves[index];
        var other = leaves[neighbour];

        var (from, to) = other.Node.IsLeaf
            ? (other.Start, other.Start + other.Node.NodeSize)
            : forward ? (slot.End, other.Start) : (other.End, slot.Start);

        // Everything removed lies before the caret when deleting backwards, so the caret moves
        // back by exactly that much; deleting forwards removes what is after it, so it stays.
        var caret = forward ? head : slot.Start - (to - from);

        transaction
            .Delete(from, to)
            .SetSelection(TextSelection.Cursor(caret))
            .SetStoredMarks(null);

        return Apply(transaction);
    }


    /// <summary>Clears the selection so that something can be put in its place.</summary>
    /// <remarks>
    /// The caller has a second step to add — the typed character, the split, the pasted slice —
    /// and that step is written against the document the delete was supposed to leave. A dropped
    /// delete is recorded on the transaction and nowhere else, so a caller that does not ask ends
    /// up inserting <em>into</em> the selection it believed it had removed: type over a select-all
    /// and the document grows. Reporting the refusal is the caller's cue to abandon the whole
    /// edit, which is the difference between a key that did nothing and a document that is wrong.
    /// </remarks>
    /// <param name="transaction">The transaction to delete on.</param>
    /// <param name="selection">The selection to remove.</param>
    /// <returns>Whether the selection is gone.</returns>
    private static bool RemoveSelection(Transaction transaction, Selection selection)
    {
        if (selection.IsEmpty)
        {
            return true;
        }

        // The one place every "replace what is selected" command passes through, so it is where
        // a rectangle is stopped for good. Its From..To runs through cells outside it, and
        // deleting that range takes whole cells out of their rows rather than emptying them.
        // A caller that wants to empty cells asks ClearCells; one that cannot express a
        // rectangle gets a refusal here even if it forgot to check.
        if (selection is CellSelection)
        {
            return false;
        }

        transaction.DeleteRange(selection.From, selection.To);

        return transaction.Failures.IsEmpty;
    }

    /// <summary>The formatting a character typed at a position should take.</summary>    /// <remarks>
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
