using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>
/// Moving the selected content by dragging it somewhere else in the same document.
/// </summary>
/// <remarks>
/// <para>
/// A press inside an existing selection is ambiguous until the pointer moves: it is either the
/// start of a new selection or the pickup of the selected content. The decision is deferred. The
/// press collapses nothing and begins nothing; past a small movement the gesture becomes a move,
/// and a release before that movement is a click and collapses the caret where it landed. Every
/// text surface on the platform behaves this way, and it is the only arrangement that keeps both
/// gestures. The cost is paid in <c>OnPointerPressed</c>, which now has an outcome it did not
/// have before — a press that has answered nothing yet.
/// </para>
/// <para>
/// The delete and the insert are two steps of one transaction. Two transactions would let an undo
/// stop between them, leaving the content in neither place, which is worse than either half being
/// wrong. The order within the transaction is the rest of the difficulty: the drop position is
/// stated against the document as it was, so it is mapped through the deletion before the insert
/// uses it. Getting that wrong moves the content by exactly the length of the selection and
/// corrupts silently.
/// </para>
/// <para>
/// This is a view-local gesture rather than a platform drag source. Dragging out to another
/// application and between two instances of the control are both out of scope, and
/// <c>Avalonia.Headless</c> 12.1.1 carries no <c>IPlatformDragSource</c> at all, so a
/// <c>DoDragDrop</c> implementation of the same feature could not be tested here. Drops arriving
/// from outside remain the business of the Drop part of this class.
/// </para>
/// </remarks>
public partial class RichTextView
{
    /// <summary>How far the pointer must travel before a press becomes a move, in pixels.</summary>
    /// <remarks>
    /// Four, which is what Windows reports for <c>SM_CXDRAG</c> and near enough what the other
    /// platforms use. Smaller and a click with an unsteady hand picks the selection up; larger and
    /// a short move inside a word needs a deliberate overshoot. Measured per axis rather than by
    /// distance, again matching the platform, so a diagonal wobble of three pixels each way is
    /// still a click.
    /// </remarks>
    private const double MoveThreshold = 4;

    /// <summary>The press whose meaning has not been decided, or null.</summary>
    private Pickup? _pickup;

    /// <summary>Whether that press has passed the threshold and become a move.</summary>
    private bool _moving;

    /// <summary>Where the content would land, in the document as it is now.</summary>
    private int _moveTarget;

    /// <summary>Whether the selected content is being dragged somewhere else.</summary>
    /// <remarks>
    /// Distinct from <see cref="IsDragging"/>, which means a drag that is <em>selecting</em>. The
    /// two are never both true: a press that picks the selection up returns before the selection
    /// drag is armed.
    /// </remarks>
    public bool IsMovingSelection => _moving;

    /// <summary>A press inside the selection, before it is known what it meant.</summary>
    /// <param name="At">Where the press landed, in this control's coordinates.</param>
    /// <param name="Origin">The document and selection the pickup was made against.</param>
    private sealed record Pickup(Point At, EditOrigin Origin);

    /// <summary>Defers the decision if the press landed on the selected content.</summary>
    private bool TryPickUpSelection(Point point)
    {
        if (SelectionUnder(point) is not { } origin)
        {
            return false;
        }

        _pickup = new Pickup(point, origin);
        _moving = false;

        return true;
    }

    /// <summary>What a press at a point would pick up, or null if it would pick up nothing.</summary>
    /// <remarks>
    /// Read-only is answered here, so that the deferred press, the capture, the drop caret and the
    /// commit are governed by one condition rather than by four that can drift apart. Architecture
    /// 10.2 records six defects in this codebase where a read-only control advertised, armed or
    /// prompted for an edit it then refused; every one of them gated the write and nothing else.
    /// Refusing at the press is what keeps a read-only drag an ordinary drag-select.
    /// </remarks>
    private EditOrigin? SelectionUnder(Point point)
    {
        if (IsReadOnly)
        {
            return null;
        }

        var selection = _state.Selection;

        // A rectangle of cells is refused for the reason RemoveSelection refuses it: its From..To
        // runs through cells that are not in it, so a move stated in those numbers would take
        // whole cells out of their rows rather than moving the rectangle.
        if (selection.IsEmpty || selection is CellSelection)
        {
            return null;
        }

        // A press on an image selects that image, which is what the branch below this one in
        // OnPointerPressed does and what it did before this existed. That also covers a picture
        // that is already the selection: moving one is the next increment.
        if (ImageUnder(point) is not null)
        {
            return null;
        }

        var position = PositionAt(point);

        // Inclusive at both ends, so that a one-character selection can be picked up at all -
        // there is no position strictly inside one. Claiming the boundary costs nothing: a press
        // there that turns out to be a click collapses the caret exactly where an ordinary press
        // would have put it.
        return position >= selection.From && position <= selection.To ? CurrentOrigin() : null;
    }

    /// <summary>Decides an undecided press, and steers a move that has begun.</summary>
    private void UpdateMove(Point point)
    {
        if (_pickup is not { } pickup)
        {
            return;
        }

        if (!_moving)
        {
            if (Math.Abs(point.X - pickup.At.X) <= MoveThreshold
                && Math.Abs(point.Y - pickup.At.Y) <= MoveThreshold)
            {
                return;
            }

            _moving = true;
        }

        _moveTarget = NearestTextPosition(PositionAt(point), forward: true);

        InvalidateVisual();
    }

    /// <summary>Ends the gesture, by committing the move or by resolving the click.</summary>
    /// <param name="dropped">
    /// Whether the pointer was released, as opposed to capture having been taken away.
    /// </param>
    private void EndMove(bool dropped)
    {
        if (_pickup is not { } pickup)
        {
            return;
        }

        var moving = _moving;

        _pickup = null;
        _moving = false;

        InvalidateVisual();

        if (!moving)
        {
            // The decision the press deferred, made now and made the way an ordinary press would
            // have made it.
            _goalX = null;
            MoveTo(PositionAt(pickup.At), extend: false);

            return;
        }

        // Capture taken away is not a drop. A popup opening or the window deactivating mid-drag
        // must not commit a move the user never released.
        if (dropped && StillAt(pickup.Origin))
        {
            MoveSelectionTo(_moveTarget);
        }
    }

    /// <summary>Moves the selected content to a position, in one transaction.</summary>
    /// <remarks>
    /// <para>
    /// The order is the whole of it. <paramref name="target"/> is stated against the document as
    /// it is now, and the deletion moves everything after the selection; mapping it through the
    /// transaction's own accumulated map is what puts the insert where the user aimed rather than
    /// as many positions past it as the selection was long.
    /// </para>
    /// <para>
    /// A refused insert abandons the whole edit. Applying the delete on its own would be the
    /// content vanishing, which is the one outcome worse than the gesture doing nothing.
    /// </para>
    /// <para>
    /// What was moved is left selected where it landed, rather than a caret left after it, so that
    /// a drag which came down a few characters out can be dragged again without hunting for the
    /// content first.
    /// </para>
    /// </remarks>
    /// <param name="target">Where to put it, in the document as it is now.</param>
    /// <returns>Whether the document changed.</returns>
    private bool MoveSelectionTo(int target)
    {
        var selection = _state.Selection;

        // Dropping inside what is being dragged is a no-op. Removing the range and putting it
        // back into the hole it left is an undo entry for nothing at best, and at the ends an
        // insert at a position the deletion took away.
        if (selection.IsEmpty || (target >= selection.From && target <= selection.To))
        {
            return false;
        }

        var slice = Slice.Cut(_state.Doc, selection.From, selection.To);
        var transaction = _state.Transaction().As(TransactionKind.Structure);

        if (!RemoveSelection(transaction, selection))
        {
            return false;
        }

        var at = transaction.Mapping.Map(target);

        transaction.Replace(at, at, slice);

        if (!transaction.Failures.IsEmpty)
        {
            return false;
        }

        transaction
            .SetSelection(TextSelection.Create(transaction.Doc, at, at + slice.Size))
            .SetStoredMarks(null);

        if (!Apply(transaction))
        {
            return false;
        }

        _goalX = null;

        return true;
    }

    /// <summary>Draws the caret at the position a drop would land on.</summary>
    private void RenderDropCaret(DrawingContext context)
    {
        if (!_moving || PositionRect(_moveTarget) is not { } rect)
        {
            return;
        }

        // The same fallback the ordinary caret uses, so the two are drawn in one colour.
        if ((CaretBrush ?? TextStyles.Resolve(MarkSet.Empty).ForegroundBrush) is not { } brush)
        {
            return;
        }

        context.FillRectangle(brush, SnapCaret(rect, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
    }
}
