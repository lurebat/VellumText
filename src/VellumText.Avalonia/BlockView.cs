using Avalonia;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>
/// The view of one block, per architecture 4.6.
/// </summary>
/// <remarks>
/// <para>
/// A block view owns its own layout and reports geometry, but it draws neither the caret nor
/// the selection highlight. Those belong to the view that hosts the blocks, because a selection
/// can span several of them and the caret is one concern with one blink timer. Blocks answer
/// <see cref="GetCaretRect"/> and <see cref="GetSelectionRects(int, int)"/> and stop there.
/// </para>
/// <para>
/// Every position in this API is <em>local</em> to the block's content: 0 is the first position
/// inside the block, and <see cref="ContentSize"/> is the last. The host adds the block's
/// document offset. Keeping the conversion in one place is what stops position arithmetic from
/// leaking into every block type.
/// </para>
/// </remarks>
internal abstract class BlockView
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
    /// Draws the block, knowing that only <paramref name="visible"/> of it can be seen.
    /// </summary>
    /// <remarks>
    /// A hint, not a clip: drawing more than this is correct and only wasted. The default draws
    /// everything; a block whose cost is in parts that can be off screen — a table's rows — skips
    /// the ones that are.
    /// </remarks>
    /// <param name="context">The drawing context.</param>
    /// <param name="origin">Where the block's top-left corner sits.</param>
    /// <param name="visible">The part of the block that can be seen, in its own coordinates.</param>
    public virtual void Render(DrawingContext context, Point origin, Rect visible) => Render(context, origin);

    /// <summary>
    /// Tells the block which part of it, in its own coordinates, the next <see cref="Measure"/>
    /// must lay out: <paramref name="below"/> pixels down from <paramref name="anchor"/> and
    /// <paramref name="above"/> pixels up from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The presenter's realized window, so that a block taller than the viewport — a table of ten
    /// thousand rows — can lay out the part about to be seen and estimate the rest. The default
    /// ignores it and lays out everything, which is what every block but a table does. An infinite
    /// <paramref name="below"/> from zero means everything.
    /// </para>
    /// <para>
    /// An anchor and two distances, not two positions, because the distances are in real pixels
    /// and the positions of parts nobody has laid out are estimates: the anchor is the point the
    /// presenter keeps at the top of the viewport, and what is on screen is what lies below it
    /// once laid out, however wrong the estimate that placed it was.
    /// </para>
    /// </remarks>
    /// <param name="anchor">The point the viewport's top is kept at, or the block's top.</param>
    /// <param name="above">How far above the anchor to lay out.</param>
    /// <param name="below">How far below the anchor to lay out.</param>
    public virtual void SetWindow(double anchor, double above, double below)
    {
    }

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

    /// <summary>
    /// The highlight rectangles for a local range, as far as they fall in <paramref name="visible"/>.
    /// </summary>
    /// <remarks>
    /// For painting: rectangles outside <paramref name="visible"/> may be left out, and are, by a
    /// block that would otherwise have to lay out parts of itself nobody can see. The default
    /// returns them all.
    /// </remarks>
    /// <param name="from">The local start position.</param>
    /// <param name="to">The local end position.</param>
    /// <param name="visible">What of the block can be seen, in its own coordinates.</param>
    public virtual IReadOnlyList<Rect> GetSelectionRects(int from, int to, Rect visible) =>
        GetSelectionRects(from, to);

    /// <summary>
    /// The paragraph at a local position, or null if there is no editable text there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seam between the editing layer and block nesting. Almost every editing command is
    /// written against one paragraph — insert here, walk a cluster, split, find the word around
    /// the caret — and it needs the paragraph the caret is actually in, which for a table is
    /// several levels down inside a block the flat run treats as one. Asking the block rather
    /// than assuming it <em>is</em> the paragraph is what lets a table be one slot without every
    /// command having to learn about cells.
    /// </para>
    /// <para>
    /// The default is null, which is the right answer for a rule and for an image: the caret can
    /// sit on them but there is no text to edit.
    /// </para>
    /// </remarks>
    /// <param name="localPosition">A local position in <c>[0, <see cref="ContentSize"/>]</c>.</param>
    public virtual TextAt? ParagraphAt(int localPosition) => null;

    /// <summary>
    /// Tells the block that the paragraph at a local position, one <see cref="ParagraphAt"/>
    /// handed out, has dropped its layout without the block being updated — a composition does.
    /// </summary>
    /// <remarks>
    /// The default does nothing, which is right for a block that lays its paragraphs out whenever
    /// it is measured. A block that measures only the parts it knows have changed has to count
    /// this as a change, or it goes on answering for a paragraph that cannot answer.
    /// </remarks>
    /// <param name="localPosition">A local position inside the paragraph.</param>
    public virtual void ParagraphChanged(int localPosition)
    {
    }

    /// <summary>
    /// The box an image at a local position is drawn in, or null if there is no image there.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="GetSelectionRects(int, int)"/>, which for an inline image returns the
    /// <em>line</em> box: as tall as the line and reaching below the picture by the descent of the
    /// text beside it. Resize handles drawn on that rectangle float visibly off the image, so the
    /// block is asked where the picture itself is rather than where its position is.
    /// </remarks>
    /// <param name="localPosition">A local position in <c>[0, <see cref="ContentSize"/>]</c>.</param>
    public virtual Rect? GetImageRect(int localPosition) => null;
}

/// <summary>Where a paragraph found inside a block lives.</summary>
/// <param name="View">The paragraph's view.</param>
/// <param name="Start">
/// The paragraph's first inner position, relative to the containing block's content.
/// </param>
/// <param name="Origin">
/// The paragraph's top-left corner, relative to the containing block's top-left corner.
/// </param>
internal readonly record struct TextAt(ParagraphView View, int Start, Point Origin);
