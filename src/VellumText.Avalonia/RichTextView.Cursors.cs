using Avalonia;
using Avalonia.Input;

namespace VellumText.Avalonia;

/// <summary>Which pointer the view shows, and when it changes.</summary>
public partial class RichTextView
{
    /// <summary>Over selected text: the content can be dragged, as in Word and TextEdit.</summary>
    internal static readonly Cursor DraggableCursor = ArrowCursor;

    /// <summary>Over a selected picture, which a press picks up and moves.</summary>
    internal static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

    /// <summary>While selected content is being dragged somewhere else in the document.</summary>
    internal static readonly Cursor MovingCursor = new(StandardCursorType.DragMove);

    /// <summary>Where the pointer last hovered, or null when it is not over the view.</summary>
    private Point? _hoverAt;

    /// <summary>Picks the pointer for a hover at <paramref name="point"/>.</summary>
    /// <remarks>
    /// <para>
    /// The resize handles and column boundaries first, because they are small targets sitting on
    /// top of content. Then a link Ctrl would follow, then the selection — an arrow says the
    /// selection can be picked up — and only then the I-beam over text. An I-beam everywhere says
    /// every part of the control is text, which is wrong for the space below the last block and
    /// to the right of a short line.
    /// </para>
    /// <para>
    /// Only assigned when it changes: assigning <c>Cursor</c> raises a property change and
    /// re-reads the pointer on every move otherwise.
    /// </para>
    /// </remarks>
    private void UpdateHoverCursor(Point point, bool control)
    {
        UpdateLinkAffordance(point, control);

        var wanted = ResizeCursor(point)
            ?? ColumnResizeCursor(point)
            ?? (_linkCursor ? HandCursor : null)
            ?? SelectionCursor(point)
            ?? (IsOverContent(point) ? TextCursor : ArrowCursor);

        if (!ReferenceEquals(Cursor, wanted))
        {
            Cursor = wanted;
        }
    }

    /// <summary>The pointer over selected content that a press would pick up, or null.</summary>
    /// <remarks>
    /// <see cref="SelectionUnder"/> is the same test the press makes, so the pointer promises a
    /// drag exactly where a drag will happen: never in a read-only view, never over a rectangle of
    /// cells.
    /// </remarks>
    private Cursor? SelectionCursor(Point point) =>
        SelectionUnder(point) is null ? null
            : SelectedImage is not null ? MoveCursor
            : DraggableCursor;

    /// <summary>Recomputes the hover pointer where the pointer already is.</summary>
    /// <remarks>
    /// Pressing or releasing Ctrl over a link changes what a click would do without the pointer
    /// moving, so it has to change the pointer without waiting for a move either.
    /// </remarks>
    /// <param name="control">Whether Ctrl is now held.</param>
    private void RefreshHoverCursor(bool control)
    {
        if (_hoverAt is { } at && !_dragging && _pickup is null)
        {
            UpdateHoverCursor(at, control);
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnKeyUp(e);

        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            RefreshHoverCursor(control: false);
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverAt = null;
    }
}
