using System.Collections.Immutable;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace VellumText.Avalonia;

/// <summary>
/// Resizing a table column by dragging the boundary between it and the next, per architecture
/// 10.4.3 AC5.
/// </summary>
/// <remarks>
/// <para>
/// A drag redistributes the two columns either side of the boundary and leaves everything else
/// alone. Their sum is held constant, which is what keeps the table's total width unchanged and
/// what leaves a cell spanning the boundary exactly as wide as it was — the boundary being
/// dragged is a boundary of the <em>grid</em>, which is what the width vector indexes, and a
/// merged cell tiling it is not evidence that the column underneath has stopped existing.
/// </para>
/// <para>
/// The drag draws a guide line and applies nothing until the pointer is released, matching the
/// image resize in the Images part of this class. One transaction per gesture, so undo takes the
/// whole drag back at once; relying on transaction coalescing to hide a hundred of them would
/// leave the undo stack correct only by accident. Re-laying-out on every move is the other
/// tempting option and is worse here than it is for an image: a table remeasures every cell in
/// it, so the document would reflow under a pointer that is still aiming.
/// </para>
/// <para>
/// The written vector is normalised to sum 1. The stored numbers are ratios, so their magnitudes
/// never meant anything and rescaling them changes no layout; saying so in the numbers themselves
/// is what makes them impossible to read as pixels — which <c>TableNode.ColumnWidths</c>' own
/// documentation invited until 2026-08-19, when it still called them device-independent pixels.
/// </para>
/// </remarks>
public partial class RichTextView
{
    /// <summary>How near a column boundary the pointer must be to grab it, in pixels.</summary>
    /// <remarks>
    /// The band is centred on the line and so is twice this wide. Four is about the smallest that
    /// can be hit reliably with a mouse and stays well inside a cell's six pixels of padding, so
    /// the band never reaches the text and a click aimed at the first character of a cell is not
    /// taken for a resize.
    /// </remarks>
    public const double ColumnBoundaryTolerance = 4;

    /// <summary>The smallest share of the table's width a drag may leave a column at.</summary>
    /// <remarks>
    /// Five per cent: on a 400 pixel table that is 20 pixels, comfortably wider than the grab band
    /// so a column cannot be squeezed to where its own boundary is unreachable, and narrow enough
    /// not to fight a deliberately thin column. A floor is needed at all because a column dragged
    /// to nothing has no boundary left to grab and cannot be recovered by the same gesture. It is
    /// capped against the pair either side of the boundary — see <see cref="Clamped"/> — since a
    /// table wide enough has every column below this share already.
    /// </remarks>
    public const double MinimumColumnShare = 0.05;

    /// <summary>The cursor shown on a column boundary.</summary>
    internal static readonly Cursor ColumnResizeCursorShape = new(StandardCursorType.SizeWestEast);

    /// <summary>The boundary being dragged, or null.</summary>
    private ColumnBoundary? _columnDrag;

    /// <summary>Where the boundary would land, in the control's coordinates.</summary>
    private double _columnDragTo;

    /// <summary>Where the boundary was when the drag began, in the control's coordinates.</summary>
    private double _columnDragFrom;

    /// <summary>Whether a column boundary is being dragged.</summary>
    public bool IsResizingColumn => _columnDrag is not null;

    /// <summary>Starts a column resize if the pointer went down on a boundary.</summary>
    /// <remarks>
    /// The read-only gate is here rather than at the commit, so a read-only view shows no cursor,
    /// takes no capture and draws no guide. Gating only the write would advertise an edit the
    /// control then refuses, which architecture 10.2 records this codebase getting wrong five
    /// times.
    /// </remarks>
    private bool TryBeginColumnResize(Point point)
    {
        if (ColumnBoundaryUnder(point) is not { } boundary)
        {
            return false;
        }

        _columnDrag = boundary;
        _columnDragFrom = boundary.Left + Sum(boundary.Widths, boundary.Boundary);
        _columnDragTo = _columnDragFrom;

        InvalidateVisual();

        return true;
    }

    /// <summary>Moves the guide the drag would leave the boundary at.</summary>
    private void UpdateColumnResize(Point point)
    {
        if (_columnDrag is not { } boundary)
        {
            return;
        }

        _columnDragTo = Clamped(boundary, point.X);

        InvalidateVisual();
    }

    /// <summary>Writes the drag into the document, if there was one.</summary>
    private void EndColumnResize()
    {
        if (_columnDrag is not { } boundary)
        {
            return;
        }

        var to = _columnDragTo;

        _columnDrag = null;

        InvalidateVisual();

        // A press and release on a boundary is a click, not a resize, and must not put a
        // transaction on the undo stack for a gesture that changed nothing.
        if (to == _columnDragFrom)
        {
            return;
        }

        var widths = boundary.Widths.ToArray();
        var moved = to - _columnDragFrom;

        widths[boundary.Boundary - 1] += moved;
        widths[boundary.Boundary] -= moved;

        SetColumnWidths(boundary.TablePos, widths);
    }

    /// <summary>The cursor for a point, or null if the point is not on a column boundary.</summary>
    private Cursor? ColumnResizeCursor(Point point) =>
        ColumnBoundaryUnder(point) is null ? null : ColumnResizeCursorShape;

    /// <summary>The boundary a point may grab, or null.</summary>
    /// <remarks>
    /// Read-only is answered here so that every part of the gesture — cursor, capture, preview and
    /// commit — is governed by one condition rather than by four that can drift apart.
    /// </remarks>
    private ColumnBoundary? ColumnBoundaryUnder(Point point) =>
        IsReadOnly ? null : ColumnBoundaryAt(point, ColumnBoundaryTolerance);

    /// <summary>
    /// Writes a table's column widths as proportions summing to one.
    /// </summary>
    /// <remarks>
    /// Normalising here rather than in <see cref="TableEdits.SetColumnWidths"/>, which takes the
    /// caller at its word: a host with pixel measurements from somewhere else should not have them
    /// silently rewritten, whereas what this control has is a set of laid-out widths whose
    /// magnitudes are an artefact of the window it happened to be drawn in.
    /// </remarks>
    private void SetColumnWidths(int tablePos, IReadOnlyList<double> widths)
    {
        var total = 0d;

        foreach (var width in widths)
        {
            total += width;
        }

        if (total <= 0 || !double.IsFinite(total))
        {
            return;
        }

        var shares = new double[widths.Count];

        for (var i = 0; i < shares.Length; i++)
        {
            shares[i] = widths[i] / total;
        }

        if (TableEdits.SetColumnWidths(_state.Doc, tablePos, shares) is not { } step)
        {
            return;
        }

        Apply(_state.Transaction().Step(step).As(TransactionKind.Structure));
    }

    /// <summary>Where a boundary dragged to <paramref name="x"/> is allowed to land.</summary>
    /// <remarks>
    /// The pair either side is all that moves, so the limits are its own edges pulled in by the
    /// minimum. The minimum is a share of the whole table, which means the same thing at any window
    /// size — but it is capped at a quarter of the pair, because a table of more than twenty
    /// columns already has every column below that share and an uncapped floor would then exceed
    /// half the pair. At exactly half the boundary is pinned to the pair's midpoint and the gesture
    /// silently does nothing; above half the lower limit passes the upper one and
    /// <see cref="Math.Clamp(double, double, double)"/> throws rather than picking a side. A
    /// quarter is the round number below half, and leaves half the pair as travel either way.
    /// </remarks>
    private static double Clamped(ColumnBoundary boundary, double x)
    {
        var start = Sum(boundary.Widths, boundary.Boundary - 1);
        var pair = boundary.Widths[boundary.Boundary - 1] + boundary.Widths[boundary.Boundary];
        var total = Sum(boundary.Widths, boundary.Widths.Length);
        var floor = Math.Min(MinimumColumnShare * total, pair / 4);

        return Math.Clamp(x, boundary.Left + start + floor, boundary.Left + start + pair - floor);
    }

    /// <summary>The first <paramref name="count"/> widths added up.</summary>
    private static double Sum(ImmutableArray<double> widths, int count)
    {
        var total = 0d;

        for (var i = 0; i < count; i++)
        {
            total += widths[i];
        }

        return total;
    }

    private void RenderColumnGuide(DrawingContext context)
    {
        if (_columnDrag is null || CaretBrush is not { } brush)
        {
            return;
        }

        // A line the height of the viewport rather than of the table: the table's own box is not
        // known here, and a guide that stopped short of the pointer would be harder to aim with
        // than one that runs past it.
        context.DrawLine(
            new ImmutablePen(brush.ToImmutable(), 1),
            new Point(_columnDragTo, 0),
            new Point(_columnDragTo, Bounds.Height));
    }
}
