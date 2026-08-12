using System.Collections.Immutable;
using Avalonia;

namespace Vellum.Avalonia;

/// <summary>
/// The block list: realising a document's blocks, keeping their vertical positions, and turning
/// document positions into geometry and back.
/// </summary>
/// <remarks>
/// <para>
/// Two lists move together. <see cref="Slots"/> is what the document says — which block, where
/// it starts, how deep it is nested — and is rebuilt from the tree on every change.
/// <see cref="Blocks"/> is what has been laid out, and is emphatically <em>not</em> rebuilt,
/// because a block view owns a text layout and a run cache that cost real time to produce. The
/// reconciliation between them is the whole point of this file.
/// </para>
/// <para>
/// Blocks are matched by node reference, which works because the model is immutable and an edit
/// rebuilds only the spine it touched: every block the edit did not reach comes back as the same
/// object. Matching a common prefix and a common suffix therefore identifies the changed middle
/// exactly, and inserting a paragraph at the top of a long document relays out one block rather
/// than all of them.
/// </para>
/// </remarks>
public partial class RichTextView
{
    private readonly BlockHeightIndex _heights = new();
    private readonly List<BlockView> _views = [];
    private ImmutableArray<BlockSlot> _slots = [];
    private int _caretBlock;

    /// <summary>The blocks that have been laid out, in document order.</summary>
    public IReadOnlyList<BlockView> Blocks => _views;

    /// <summary>What the document says about each block, in document order.</summary>
    public ImmutableArray<BlockSlot> Slots => _slots;

    /// <summary>The index of the block the caret is in.</summary>
    public int CaretBlockIndex => _caretBlock;

    /// <summary>
    /// The view of the block the caret is in, or null before the control has been measured.
    /// </summary>
    /// <remarks>
    /// A paragraph, or null when the caret is somewhere no text lives — which today means a
    /// document of nothing but rules, since <see cref="Selection.Near"/> puts a text caret
    /// wherever one can go.
    /// </remarks>
    public ParagraphView? Block =>
        _views.Count == 0 ? null : _views[_caretBlock] as ParagraphView;

    /// <summary>
    /// The document position at which the caret's block starts.
    /// </summary>
    /// <remarks>The one place a block-local position becomes a document position.</remarks>
    public int BlockStart => _slots.IsEmpty ? 0 : _slots[_caretBlock].Start;

    /// <summary>The top-left corner of a block, in this control's coordinates.</summary>
    /// <param name="index">The block's index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a block.</exception>
    public Point BlockOrigin(int index)
    {
        if (index < 0 || index >= _views.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Not a realised block.");
        }

        return new Point(0, _heights.OffsetOf(index));
    }

    /// <summary>
    /// The block a document position belongs to.
    /// </summary>
    /// <remarks>
    /// A position between two blocks — the gap where one closes and the next opens — belongs to
    /// the earlier one, so a caret that ends up there draws at the end of the block it was in
    /// rather than jumping ahead. A position before the first block belongs to the first.
    /// </remarks>
    /// <param name="position">A document position.</param>
    public int BlockIndexOf(int position)
    {
        if (_slots.Length <= 1)
        {
            return 0;
        }

        // Binary search for the last block that starts at or before the position.
        var low = 0;
        var high = _slots.Length - 1;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;

            if (_slots[mid].Start <= position)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    /// <summary>The block covering a vertical offset in this control's coordinates.</summary>
    /// <param name="y">The offset from the top of the content.</param>
    public int BlockIndexAt(double y) => _views.Count == 0 ? 0 : _heights.IndexAt(y);

    /// <summary>
    /// The nearest paragraph block on one side of another block, or -1 if there is none.
    /// </summary>
    /// <remarks>
    /// Blocks a caret cannot enter are stepped over rather than stopped at. A rule between two
    /// paragraphs is one block on screen and no positions to a caret, so Down from the paragraph
    /// above it must reach the paragraph below rather than refusing to move.
    /// </remarks>
    /// <param name="index">The block to search from, exclusive.</param>
    /// <param name="forward">Which way to search.</param>
    private int AdjacentParagraph(int index, bool forward)
    {
        for (var i = index + (forward ? 1 : -1); i >= 0 && i < _views.Count; i += forward ? 1 : -1)
        {
            if (_views[i] is ParagraphView)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The document position nearest a point in this control's coordinates.</summary>
    /// <param name="point">The point.</param>
    public int PositionAt(Point point)
    {
        if (_views.Count == 0)
        {
            return 0;
        }

        var index = BlockIndexAt(point.Y);
        var slot = _slots[index];
        var origin = BlockOrigin(index);

        return slot.Start + _views[index].HitTest(point - origin);
    }

    /// <summary>
    /// Brings the slot list up to date with the document without laying anything out.
    /// </summary>
    /// <remarks>
    /// Cheaper than <see cref="Realize"/> and enough for anything that only needs to know which
    /// blocks the document has and where they start. Measuring needs a width, which is not known
    /// until the layout pass; asking a question about block structure does not.
    /// </remarks>
    private void EnsureSlots()
    {
        if (_slots.IsEmpty)
        {
            Reconcile(DocumentBlocks.Flatten(_state.Doc));
        }
    }

    /// <summary>
    /// Brings the block list up to date with the document and lays it out at the last width the
    /// control was measured at.
    /// </summary>
    /// <remarks>
    /// Called from the measure pass and from every state change, because the two happen in
    /// either order. A state change that only invalidated measure would leave every geometry
    /// question answering for the document as it was before the edit until the layout pass
    /// caught up, and typing then moving the caret in the same turn is enough to see it.
    /// </remarks>
    /// <returns>The space the blocks need together.</returns>
    private Size Realize()
    {
        Reconcile(DocumentBlocks.Flatten(_state.Doc));

        var width = 0d;

        for (var i = 0; i < _views.Count; i++)
        {
            var size = _views[i].Measure(_width);

            _heights.SetHeight(i, size.Height);
            width = Math.Max(width, size.Width);
        }

        return new Size(width, _heights.TotalHeight);
    }

    private void Reconcile(ImmutableArray<BlockSlot> slots)
    {
        var previous = _caretBlock < _views.Count ? _views[_caretBlock] : null;

        var prefix = 0;

        while (prefix < _views.Count
            && prefix < slots.Length
            && ReferenceEquals(_slots[prefix].Node, slots[prefix].Node))
        {
            prefix++;
        }

        var suffix = 0;

        while (suffix < _views.Count - prefix
            && suffix < slots.Length - prefix
            && ReferenceEquals(_slots[^(suffix + 1)].Node, slots[^(suffix + 1)].Node))
        {
            suffix++;
        }

        var oldMiddle = _views.Count - prefix - suffix;
        var newMiddle = slots.Length - prefix - suffix;

        // The middle is what actually changed. Views in it are updated in place where they can
        // be — a paragraph whose text was edited keeps its view, and with it the run cache the
        // Increment 0 measurements said is worth keeping across a width change.
        for (var i = 0; i < Math.Min(oldMiddle, newMiddle); i++)
        {
            var index = prefix + i;

            if (_views[index] is ParagraphView view && slots[index].Node is ParagraphNode paragraph)
            {
                view.Update(paragraph);
            }
            else
            {
                _views[index] = Create(slots[index].Node);
            }
        }

        if (newMiddle > oldMiddle)
        {
            var added = Enumerable
                .Range(prefix + oldMiddle, newMiddle - oldMiddle)
                .Select(i => Create(slots[i].Node));

            _views.InsertRange(prefix + oldMiddle, added);
            _heights.Insert(prefix + oldMiddle, newMiddle - oldMiddle);
        }
        else if (oldMiddle > newMiddle)
        {
            _views.RemoveRange(prefix + newMiddle, oldMiddle - newMiddle);
            _heights.Remove(prefix + newMiddle, oldMiddle - newMiddle);
        }

        _slots = slots;
        _caretBlock = Math.Min(BlockIndexOf(_state.Selection.Head), Math.Max(0, _views.Count - 1));

        // Every block, not just the rebuilt middle. A marker is not part of a paragraph, so an
        // item's number can change while its paragraph node stays the very same object — which
        // is exactly what the prefix scan matched on and skipped.
        for (var i = 0; i < _views.Count; i++)
        {
            if (_views[i] is ParagraphView paragraph)
            {
                paragraph.SetLead(slots[i].Depth, slots[i].Marker);
            }
        }

        // A composition belongs to the block the caret was in. If the caret has left, the
        // composition has to go with it: leaving it behind would draw composed text in a
        // paragraph the input method is no longer talking about, and it would never be
        // committed there.
        if (previous is ParagraphView left
            && !ReferenceEquals(left, _views.Count == 0 ? null : _views[_caretBlock]))
        {
            left.SetPreedit(null);
        }
    }

    private BlockView Create(BlockNode node) => node switch
    {
        ParagraphNode paragraph => new ParagraphView(paragraph, TextStyles, _embeds, _paragraphStyles),
        RuleNode => new RuleView(TextStyles.Resolve(MarkSet.Empty).ForegroundBrush ?? global::Avalonia.Media.Brushes.Black),
        _ => throw new NotSupportedException(
            $"No view for a '{node.TypeName}' block; tables arrive with Increment 6."),
    };

    /// <summary>Throws away every layout, keeping the document.</summary>
    /// <remarks>
    /// For a change that invalidates how text is drawn rather than what it says — a new style
    /// resolver, a new embed renderer, a font change on the control. The heights go with them,
    /// since a block laid out with a different font is not the height it was.
    /// </remarks>
    private void DropLayout()
    {
        _views.Clear();
        _slots = [];
        _heights.Reset(0);
        _caretBlock = 0;
    }
}
