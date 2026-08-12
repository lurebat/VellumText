using Avalonia;
using Avalonia.Media;

namespace Vellum.Avalonia;

/// <summary>
/// The view of one block, per architecture 4.6.
/// </summary>
/// <remarks>
/// <para>
/// A block view owns its own layout and reports geometry, but it draws neither the caret nor
/// the selection highlight. Those belong to the view that hosts the blocks, because a selection
/// can span several of them and the caret is one concern with one blink timer. Blocks answer
/// <see cref="GetCaretRect"/> and <see cref="GetSelectionRects"/> and stop there.
/// </para>
/// <para>
/// Every position in this API is <em>local</em> to the block's content: 0 is the first position
/// inside the block, and <see cref="ContentSize"/> is the last. The host adds the block's
/// document offset. Keeping the conversion in one place is what stops position arithmetic from
/// leaking into every block type.
/// </para>
/// </remarks>
public abstract class BlockView
{
    /// <summary>The number of positions inside this block.</summary>
    public abstract int ContentSize { get; }

    /// <summary>The size the block last measured to, or a default size if never measured.</summary>
    public abstract Size Size { get; }

    /// <summary>
    /// Lays the block out for a given width and returns the space it needs.
    /// </summary>
    /// <param name="availableWidth">The width to wrap to.</param>
    public abstract Size Measure(double availableWidth);

    /// <summary>Draws the block with its top-left corner at <paramref name="origin"/>.</summary>
    /// <param name="context">The drawing context.</param>
    /// <param name="origin">Where the block's top-left corner sits.</param>
    public abstract void Render(DrawingContext context, Point origin);

    /// <summary>
    /// Finds the position nearest a point in the block's own coordinates.
    /// </summary>
    /// <param name="local">The point, relative to the block's top-left corner.</param>
    /// <returns>A local position in <c>[0, <see cref="ContentSize"/>]</c>.</returns>
    public abstract int HitTest(Point local);

    /// <summary>The caret rectangle at a local position, in the block's own coordinates.</summary>
    /// <param name="localPosition">A local position in <c>[0, <see cref="ContentSize"/>]</c>.</param>
    public abstract Rect GetCaretRect(int localPosition);

    /// <summary>
    /// The highlight rectangles for a local range, in the block's own coordinates.
    /// </summary>
    /// <remarks>
    /// More than one rectangle is normal and not an edge case: a range that wraps produces one
    /// per line, and a range crossing a bidi boundary produces disjoint rectangles on a single
    /// line. Increment 0 measured both.
    /// </remarks>
    /// <param name="from">The local start position.</param>
    /// <param name="to">The local end position.</param>
    public abstract IReadOnlyList<Rect> GetSelectionRects(int from, int to);
}
