using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation.Peers;

namespace VellumText.Avalonia;

/// <summary>
/// The block list: realising a document's blocks, keeping their vertical positions, and turning
/// document positions into geometry and back.
/// </summary>
/// <remarks>
/// <para>
/// Two lists move together. <see cref="Slots"/> is what the document says — which block, where
/// it starts, how deep it is nested — and is patched on every change, only where the change was.
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
public abstract partial class DocumentPresenter
{
    private protected readonly BlockHeightIndex _heights = new();
    private protected readonly List<BlockView> _views = [];
    private protected readonly BlockSlotList _slots = new();

    /// <summary>The document <see cref="_slots"/> describes, or null when it describes none.</summary>
    /// <remarks>
    /// <para>
    /// Documents are immutable, so the same object is the same block list: reference identity is
    /// an exact key here rather than an approximation of one, and a caret move — which cannot
    /// have changed a block — costs nothing.
    /// </para>
    /// <para>
    /// A different document is compared with this one top-level block by top-level block, and
    /// only the slots of the blocks that differ are rebuilt. Flattening the whole document again
    /// on every change allocated a slot per block — 3.2 MB a keystroke at 50,000 paragraphs, and
    /// the gen-2 collections that came with it (F-14).
    /// </para>
    /// </remarks>
    private DocumentNode? _slotsFor;

    private DocumentNode? _leavesFor;
    private ImmutableArray<BlockSlot> _leaves = [];

    /// <summary>
    /// Every block a caret can be inside, in document order, however deeply nested.
    /// </summary>
    /// <remarks>
    /// Cached against the document it was built from, which is sound because a document is
    /// immutable — an edit produces a new one, so reference equality is exactly the right key and
    /// no invalidation hook can be forgotten. Worth caching rather than recomputing: the walk is
    /// linear in the document, a vertical caret move needs it, and measured on 2000 paragraphs it
    /// was **0.577 ms** of a **0.582 ms** keypress. It is the whole cost.
    /// </remarks>
    private protected ImmutableArray<BlockSlot> Leaves
    {
        get
        {
            var doc = PresentedDocument;

            if (!ReferenceEquals(_leavesFor, doc))
            {
                _leaves = DocumentBlocks.Leaves(doc);
                _leavesFor = doc;
            }

            return _leaves;
        }
    }

    /// <summary>The document whose blocks are presented.</summary>
    /// <remarks>
    /// The one thing a presenter cannot supply for itself. An editor reads it off its editor
    /// state; a viewer holds it directly.
    /// </remarks>
    protected internal abstract DocumentNode PresentedDocument { get; }

    /// <summary>Whether this presenter lets the user change the document.</summary>
    /// <remarks>
    /// <para>
    /// Exists for accessibility, which has to answer "is this read-only?" before it has any other
    /// reason to distinguish an editor from a viewer.
    /// </para>
    /// <para>
    /// <b>Corrected 2026-08-18.</b> These remarks used to say the property was "deliberately not a
    /// settable property: a viewer that could be made writable, or an editor that could be locked,
    /// is a feature nobody has asked for". An editor that can be locked is
    /// <see cref="RichTextView.IsReadOnly"/>, which by then existed, so the sentence described a
    /// world the code had left. It is still not settable — a presenter answers for itself — but an
    /// editor's answer now depends on whether it is locked, because a read-only editor reporting
    /// itself writable is the selection-toolbar defect aimed at the one user who has no way to see
    /// that nothing happened.
    /// </para>
    /// </remarks>
    protected internal virtual bool SupportsTextInput => false;

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new DocumentPresenterAutomationPeer(this);

    /// <summary>The block whose identity has to survive a reconcile, or null if none does.</summary>
    /// <remarks>
    /// Captured before the block list is rebuilt and handed back to
    /// <see cref="OnReconciled"/> afterwards, so a subclass can notice that the block it was
    /// tracking has been replaced. The editor tracks the caret's block this way.
    /// </remarks>
    private protected virtual BlockView? Anchor => null;

    /// <summary>Called once the block list matches the document again.</summary>
    /// <param name="before">Whatever <see cref="Anchor"/> was before the rebuild.</param>
    private protected virtual void OnReconciled(BlockView? before)
    {
    }

    /// <summary>Scrolls whatever the subclass wants kept on screen into view.</summary>
    /// <remarks>
    /// Called from <see cref="Realize"/>, because where the anchor sits depends on the heights
    /// of every block above it and those are not settled until the window has been laid out.
    /// A presenter with nothing to keep on screen — a viewer — does nothing and says so.
    /// </remarks>
    /// <returns>Whether the view moved.</returns>
    private protected virtual bool ScrollAnchorIntoView() => false;

    /// <summary>The blocks that have been laid out, in document order.</summary>
    internal IReadOnlyList<BlockView> Blocks => _views;

    /// <summary>
    /// How many blocks have been laid out — the range every block index on this type accepts.
    /// </summary>
    /// <remarks>
    /// The count without the views. <see cref="BlockOrigin"/>, <see cref="BlockIndexOf"/>,
    /// <see cref="BlockIndexAt"/> and <c>RichTextView.CaretBlockIndex</c> all speak in block
    /// indices, so a host needs to know how many there are; it does not need the layout objects
    /// behind them, which are rendering machinery.
    /// </remarks>
    public int BlockCount => _views.Count;

    /// <summary>What the document says about each block, in document order.</summary>
    /// <remarks>A copy, for tests and diagnostics; the presenter itself reads the live list.</remarks>
    internal ImmutableArray<BlockSlot> Slots => _slots.ToImmutableArray();

    /// <summary>The top-left corner of a block, in this control's coordinates.</summary>
    /// <remarks>
    /// One of the two places document space and viewport space meet: the block stack starts at
    /// zero and the control shows a window into it, so the scroll offset comes off here. See the
    /// Scroll part of this class.
    /// </remarks>
    /// <param name="index">The block's index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a block.</exception>
    public Point BlockOrigin(int index)
    {
        if (index < 0 || index >= _views.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Not a realised block.");
        }

        return new Point(0, _heights.OffsetOf(index) - _offset.Y);
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
    /// <remarks>The other place viewport space becomes document space; see <see cref="BlockOrigin"/>.</remarks>
    /// <param name="y">The offset from the top of the viewport.</param>
    public int BlockIndexAt(double y) => _views.Count == 0 ? 0 : _heights.IndexAt(y + _offset.Y);

    /// <summary>The leaf block a document position sits in.</summary>
    /// <param name="position">The document position.</param>
    /// <remarks>
    /// The <em>leaf</em>, not the drawn block. A table is one drawn block holding many
    /// paragraphs, so anything that reasons about "the block the caret is in" and reads the drawn
    /// list treats the whole table as the caret's block — and stepping out of it steps over every
    /// cell at once. Leaves are in document order, so this is a binary search.
    /// </remarks>
    private protected BlockSlot LeafContaining(int position)
    {
        var leaves = Leaves;
        var found = 0;
        var low = 0;
        var high = leaves.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (leaves[mid].Start <= position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return leaves[found];
    }

    /// <summary>
    /// The paragraph a vertical move from a line should land in, or null if there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Blocks a caret cannot enter are stepped over rather than stopped at. A rule between two
    /// paragraphs is one block on screen and no positions to a caret, so Down from the paragraph
    /// above it must reach the paragraph below rather than refusing to move.
    /// </para>
    /// <para>
    /// Searched over <see cref="DocumentBlocks.Leaves"/> rather than over the drawn blocks,
    /// because a table is one drawn block holding many paragraphs and vertical movement has to
    /// step between its rows. Stepping over the drawn blocks skipped every table whole.
    /// </para>
    /// <para>
    /// The choice is then geometric rather than ordinal, because document order and visual order
    /// are not the same thing: the paragraph after the one in a table's top-left cell is the one
    /// to its <em>right</em>, and Down must reach the cell <em>below</em>. So this takes the
    /// nearest row of paragraphs past <paramref name="fromY"/> and, within it, the one whose caret
    /// lands closest to the goal column.
    /// </para>
    /// <para>
    /// The scan stops at the first row past <paramref name="fromY"/> rather than reading the whole
    /// document, which matters because asking a paragraph where its lines are measures it.
    /// </para>
    /// </remarks>
    /// <param name="position">The document position to search from.</param>
    /// <param name="fromY">The bottom (moving down) or top (moving up) of the caret's line.</param>
    /// <param name="forward">Which way to search.</param>
    /// <param name="goalX">The column being aimed for, in this control's coordinates.</param>
    private protected TextAt? AdjacentParagraph(int position, double fromY, bool forward, double goalX)
    {
        // Half a pixel: enough that two cells in one row, whose tops are equal but for rounding,
        // count as the same row, and far too little to merge two real rows.
        const double Tolerance = 0.5;

        var leaves = Leaves;

        // Leaves are in document order, so the one the position sits in is a binary search rather
        // than a scan. It was a scan, and on 2000 paragraphs that alone was most of a keypress.
        var from = 0;
        var low = 0;
        var high = leaves.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (leaves[mid].Start <= position)
            {
                from = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        TextAt? best = null;
        var bestDistance = double.MaxValue;
        var rowY = double.NaN;

        for (var i = from + (forward ? 1 : -1); i >= 0 && i < leaves.Length; i += forward ? 1 : -1)
        {
            if (leaves[i].Node is not ParagraphNode || ParagraphAt(leaves[i].Start) is not { } at)
            {
                continue;
            }

            // Still level with the line being left — another cell in the same row.
            if (forward ? at.Origin.Y < fromY - Tolerance : at.Origin.Y > fromY - Tolerance)
            {
                continue;
            }

            if (double.IsNaN(rowY))
            {
                rowY = at.Origin.Y;
            }
            else if (Math.Abs(at.Origin.Y - rowY) > Tolerance)
            {
                // Purely a bound on the work: a paragraph in a later row is rejected by this same
                // test anyway, so continuing would give the same answer. It stays because asking a
                // paragraph where its lines are measures it, and without this a single arrow key
                // would measure every paragraph after the caret. No test can tell it from a
                // `continue`, and one is not invented to pretend otherwise.
                break;
            }

            var line = forward ? 0 : at.View.LineCount - 1;
            var landing = at.View.PositionAtLineX(line, goalX - at.Origin.X);
            var distance = Math.Abs(at.Origin.X + at.View.GetCaretRect(landing).X - goalX);

            if (distance < bestDistance)
            {
                best = at;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// The paragraph at a document position, where it starts, and where it is drawn in this
    /// control's coordinates, or null if no paragraph is there.
    /// </summary>
    /// <remarks>
    /// Measures the block on the way, because a paragraph that has never been laid out cannot say
    /// where its lines are, and vertical movement can land arbitrarily far outside the realized
    /// window — past a full-page image, say.
    /// </remarks>
    /// <param name="position">The document position.</param>
    private protected TextAt? ParagraphAt(int position)
    {
        if (_views.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(BlockIndexOf(position), 0, _views.Count - 1);
        var slot = _slots[index];
        var view = EnsureMeasured(index);
        var found = view.ParagraphAt(Math.Clamp(position - slot.Start, 0, view.ContentSize));

        RecordHeight(index);

        if (found is not { } at)
        {
            return null;
        }

        var origin = BlockOrigin(index);

        return new TextAt(
            at.View,
            slot.Start + at.Start,
            new Point(origin.X + at.Origin.X, origin.Y + at.Origin.Y));
    }

    /// <summary>
    /// The box the image at a document position is drawn in, in this control's coordinates, or
    /// null if there is no image there.
    /// </summary>
    /// <remarks>
    /// Measures the block on the way, for the same reason <see cref="ParagraphAt"/> does: an image
    /// far outside the realized window cannot say where it is until it has been laid out.
    /// </remarks>
    /// <param name="position">The document position the image occupies.</param>
    public Rect? ImageRect(int position)
    {
        if (_views.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(BlockIndexOf(position), 0, _views.Count - 1);
        var slot = _slots[index];
        var found = EnsureMeasured(index).GetImageRect(position - slot.Start);

        RecordHeight(index);

        if (found is not { } rect)
        {
            return null;
        }

        var origin = BlockOrigin(index);

        return rect.Translate(new Vector(origin.X, origin.Y));
    }

    /// <summary>
    /// The column boundary a point in this control's coordinates is on, or null if it is not on
    /// one.
    /// </summary>
    /// <remarks>
    /// Only the block under the point is asked. A boundary is a vertical line inside one table's
    /// own box, so a point that is on one is over that table by definition, and a search over
    /// every block would only find the same answer more slowly.
    /// </remarks>
    /// <param name="point">The point.</param>
    /// <param name="tolerance">How far from the line still counts as on it.</param>
    internal ColumnBoundary? ColumnBoundaryAt(Point point, double tolerance)
    {
        if (_views.Count == 0)
        {
            return null;
        }

        var index = BlockIndexAt(point.Y);

        if (EnsureMeasured(index) is not TableBlockView table)
        {
            return null;
        }

        var origin = BlockOrigin(index);
        var boundary = table.BoundaryAt(
            point, _slots[index].Start, new Vector(origin.X, origin.Y), tolerance);

        RecordHeight(index);

        return boundary;
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

        var hit = EnsureMeasured(index).HitTest(point - origin);

        RecordHeight(index);

        return slot.Start + hit;
    }

    /// <summary>
    /// Whether a point in this control's coordinates lies over a block's content.
    /// </summary>
    /// <remarks>
    /// What the pointer shape is decided from. The control is nearly always taller than the
    /// document it holds, and an I-beam over the empty space below the last paragraph claims
    /// there is text there to select. Clicking that space still places a caret — this only
    /// governs what the pointer looks like, not where a click may land.
    /// </remarks>
    /// <param name="point">The point.</param>
    public bool IsOverContent(Point point)
    {
        if (_views.Count == 0 || point.X < 0 || point.Y < 0)
        {
            return false;
        }

        var index = BlockIndexAt(point.Y);
        var origin = BlockOrigin(index);
        var size = EnsureMeasured(index).Size;

        return point.Y >= origin.Y
            && point.Y < origin.Y + size.Height
            && point.X < origin.X + size.Width;
    }

    /// <summary>
    /// Brings the slot list up to date with the document without laying anything out.
    /// </summary>
    /// <remarks>
    /// Cheaper than <see cref="Realize"/> and enough for anything that only needs to know which
    /// blocks the document has and where they start. Measuring needs a width, which is not known
    /// until the layout pass; asking a question about block structure does not.
    /// </remarks>
    private protected void EnsureSlots()
    {
        if (_slots.IsEmpty)
        {
            Reconcile();
        }
    }

    /// <summary>
    /// Brings the block list up to date with the document and lays out the blocks on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from the measure pass and from every state change, because the two happen in
    /// either order. A state change that only invalidated measure would leave every geometry
    /// question answering for the document as it was before the edit until the layout pass
    /// caught up, and typing then moving the caret in the same turn is enough to see it.
    /// </para>
    /// <para>
    /// Only the blocks intersecting the viewport, plus <c>Overscan</c> either side, are laid
    /// out; the rest contribute an estimated height. When nothing is scrolling the view the
    /// window is the whole document and this is what it always was. See the Scroll part of this
    /// class.
    /// </para>
    /// </remarks>
    /// <returns>The space the view asks for.</returns>
    private protected Size Realize()
    {
        var held = HeldRow();

        Reconcile();

        if (_views.Count == 0)
        {
            _first = 0;
            _last = -1;
            SetExtent(default);

            return default;
        }

        // Which block the top of the viewport is looking at. Measuring replaces estimates with
        // real heights, and a correction anywhere above the viewport moves everything below it;
        // Settle is what puts this block back so the reader never sees that happen.
        //
        // A block laid out for a different width is not the height it was, so every height on
        // record has become a guess again. Reset keeps what it has learned about how tall a
        // block in this document tends to be, which is the part still true.
        if (!_realizedWidth.Equals(_width))
        {
            _realizedWidth = _width;
            _heights.Reset(_views.Count);
        }

        var size = Settle(held);

        if (_pendingAnchor)
        {
            _pendingAnchor = false;

            // Laying out a screenful replaces estimates with real heights, which moves the
            // caret, which can put it back off the screen it was just scrolled onto. The bound
            // is there because a loop waiting on an estimate to converge must be allowed not to.
            for (var attempt = 0; attempt < 3 && ScrollAnchorIntoView(); attempt++)
            {
                size = Settle(null);
            }
        }

        return size;
    }

    /// <summary>
    /// Lays out the window and puts the block at the top of the viewport back where it was.
    /// </summary>
    /// <remarks>
    /// Measuring a block replaces the estimate for every block nobody has measured, because the
    /// estimate is drawn from the ones that have been (<see cref="BlockHeightIndex.Estimate"/>). So laying out a screenful moves the
    /// whole document under the viewport — and moves the window, which lays out more blocks,
    /// which moves it again. Repeating until the window stops changing is what settles it; the
    /// anchor is what stops the reader seeing any of it.
    /// </remarks>
    private Size Settle((TableBlockView Table, int Version, int Row, double Fraction)? held)
    {
        var size = default(Size);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var anchor = _heights.IndexAt(_offset.Y);
            var into = _offset.Y - _heights.OffsetOf(anchor);
            var first = _first;
            var last = _last;

            // Inside a table the anchor is a row, not the table: rows above the viewport are
            // measured as it is realized, and holding only the distance into the table would let
            // their corrections slide a different row under the top of the viewport.
            var row = _views[anchor] is TableBlockView table
                ? Anchored(table, into, attempt == 0 ? held : null)
                : default((int Row, double Fraction)?);

            size = RealizeWindow();

            if (row is { } kept && _views[anchor] is TableBlockView settled)
            {
                into = settled.RowOffset(kept.Row, kept.Fraction);
            }

            // Clamped against the height the block turned out to have, not the one it was
            // estimated to have. An anchor 40px tall that was guessed at 80 would otherwise keep a
            // 60px offset into it, which is not inside it at all — the viewport would land on the
            // block after the one the scroll bar pointed at.
            var height = _heights.HeightOf(anchor);

            ApplyOffset(
                new Vector(_offset.X, _heights.OffsetOf(anchor) + Math.Min(into, height)),
                relayout: false);

            if (_first == first && _last == last)
            {
                break;
            }
        }

        return size;
    }

    /// <summary>
    /// The table row at the top of the viewport as the last layout left it, before an edit is
    /// reconciled into the blocks.
    /// </summary>
    /// <remarks>
    /// Taken before <see cref="Reconcile"/> because reconciling can move the estimate a table
    /// prices its unmeasured rows at — deleting a measured row does, by taking its height out of the
    /// mean — and that moves every unmeasured row above the viewport. The offset is still where it
    /// was, so a row worked out from it afterwards is a different row, and the content slides.
    /// </remarks>
    private (TableBlockView Table, int Version, int Row, double Fraction)? HeldRow()
    {
        if (_views.Count == 0 || _heights.Count != _views.Count)
        {
            return null;
        }

        var anchor = _heights.IndexAt(_offset.Y);

        if (_views[anchor] is not TableBlockView table)
        {
            return null;
        }

        var (row, fraction) = table.RowAnchor(_offset.Y - _heights.OffsetOf(anchor));

        return (table, table.Version, row, fraction);
    }

    /// <summary>The row to keep at the top of the viewport inside a table at the anchor.</summary>
    /// <param name="table">The table at the top of the viewport.</param>
    /// <param name="into">How far into the table the top of the viewport is.</param>
    /// <param name="held">The row held from before the edit, if there was one.</param>
    private static (int Row, double Fraction) Anchored(
        TableBlockView table,
        double into,
        (TableBlockView Table, int Version, int Row, double Fraction)? held)
    {
        if (held is { } h
            && ReferenceEquals(h.Table, table)
            && table.RowAfterUpdate(h.Row, h.Version) is var row and >= 0)
        {
            return (row, h.Fraction);
        }

        return table.RowAnchor(into);
    }

    /// <summary>Lays out the blocks the viewport covers and reports what the view needs.</summary>
    private Size RealizeWindow()
    {
        var virtualizing = IsVirtualizing;
        var first = virtualizing ? _heights.IndexAt(Math.Max(0, _offset.Y - Overscan)) : 0;
        var limit = virtualizing ? _offset.Y + ViewportHeight + Overscan : double.PositiveInfinity;

        var width = 0d;
        var last = first;

        for (var i = first; i < _views.Count; i++)
        {
            // A block taller than the window — a long table — lays out only the part of itself
            // the window covers; told in its own coordinates, against where it sits now.
            if (virtualizing)
            {
                // Anchored at the top of the viewport if the block reaches above it, which is the
                // point Settle keeps where it is; otherwise at the block's own top.
                var top = _heights.OffsetOf(i);
                var anchor = Math.Max(0, _offset.Y - top);

                _views[i].SetWindow(anchor, anchor > 0 ? Overscan : 0, limit - top - anchor);
            }
            else
            {
                _views[i].SetWindow(0, 0, double.PositiveInfinity);
            }

            var size = _views[i].Measure(_width);

            _heights.SetHeight(i, size.Height);
            width = Math.Max(width, size.Width);
            last = i;

            // Asked after the measurement, not before: the block's own height is what decides
            // whether the window is full, and its offset is only correct once it is recorded.
            if (_heights.OffsetOf(i) + size.Height >= limit)
            {
                break;
            }
        }

        _first = first;
        _last = last;

        SetExtent(new Size(width, _heights.TotalHeight));

        return new Size(
            width,
            virtualizing ? Math.Min(_heights.TotalHeight, ViewportHeight) : _heights.TotalHeight);
    }

    /// <summary>Lays a block out if the realized window has not already.</summary>
    /// <remarks>
    /// A geometry question can be asked about a block nowhere near the viewport — the caret
    /// jumping to the end of the document is exactly that — and answering it needs a layout.
    /// Where the block <em>sits</em> is still derived from estimates for everything above it, so
    /// that answer is only as good as those estimates; laying the block out is what stops the
    /// answer about its inside from being nonsense.
    /// </remarks>
    /// <remarks>
    /// The height index is asked to record the result, not asked whether to bother. Its
    /// measurement flag is a fact about the index and the view's layout is a fact about the view,
    /// and reconciling an edit routinely separates the two: a block whose paragraph was replaced,
    /// or whose list marker moved, has thrown its layout away while the index still holds a height
    /// for it. Outside the realized window nothing repairs that, so guarding on the flag would
    /// answer "already measured" about a view that cannot answer a geometry question at all.
    /// <see cref="BlockView.Measure"/> is idempotent and returns immediately when the layout is
    /// current, so there is nothing to save by asking first.
    /// </remarks>
    /// <param name="index">The block's index.</param>
    private protected BlockView EnsureMeasured(int index)
    {
        _views[index].Measure(_width);
        RecordHeight(index);

        return _views[index];
    }

    /// <summary>Records the height a block has now in the height index and the extent.</summary>
    /// <remarks>
    /// Also wanted after a geometry question has been answered, not only after a measure: a long
    /// table realizes and measures the rows a question reaches outside its window, which changes
    /// its height — those rows, and the estimate every unmeasured row is priced at — after
    /// <see cref="EnsureMeasured"/> recorded it. Nothing else records it again until the block is
    /// measured in a layout pass, which for a block outside the realized window may never come.
    /// </remarks>
    /// <param name="index">The block's index.</param>
    private protected void RecordHeight(int index)
    {
        var height = _views[index].Size.Height;

        if (!_heights.Measured(index) || Math.Abs(_heights.HeightOf(index) - height) > 0.01)
        {
            _heights.SetHeight(index, height);
            SetExtent(_extent.WithHeight(_heights.TotalHeight));
        }
    }

    /// <summary>
    /// Brings the slot list and the block views up to date with the document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The top-level blocks the edit did not touch are the same objects in the new document as in
    /// the old one, so comparing the two block arrays by reference from each end finds the
    /// replaced run exactly. Only that run is flattened again, and every slot after it moves by
    /// the number of positions the edit added or removed. That is linear in the document, like
    /// the edit that caused it, but it allocates only the slots that are new.
    /// </para>
    /// <para>
    /// Within the replaced run the old and new slots are then matched by node reference from each
    /// end as well, so that a list whose one item changed relays out that item and not the list.
    /// </para>
    /// </remarks>
    private void Reconcile()
    {
        var previous = Anchor;
        var document = PresentedDocument;

        if (_slotsFor is null)
        {
            // Nothing to patch: the first realization, or the layout was dropped.
            _slots.Reset(DocumentBlocks.Flatten(document));
            _slotsFor = document;
            Rebuild(0, [], _slots.Length);
        }
        else if (!ReferenceEquals(document, _slotsFor))
        {
            var before = _slotsFor.Blocks;
            var after = document.Blocks;
            var limit = Math.Min(before.Length, after.Length);
            var prefix = 0;
            var position = 0;

            while (prefix < limit && ReferenceEquals(before[prefix], after[prefix]))
            {
                position += after[prefix].NodeSize;
                prefix++;
            }

            var suffix = 0;
            var tail = 0;

            while (suffix < limit - prefix
                && ReferenceEquals(before[^(suffix + 1)], after[^(suffix + 1)]))
            {
                tail += after[^(suffix + 1)].NodeSize;
                suffix++;
            }

            // Slot starts strictly increase, so the replaced blocks' slots are exactly the ones
            // starting between where the first replaced block begins and where the unchanged
            // tail begins in the old document.
            var first = _slots.FirstStartingAtOrAfter(position);
            var end = _slots.FirstStartingAtOrAfter(_slotsFor.ContentSize - tail);
            var replaced = DocumentBlocks.Flatten(
                ImmutableArray.Create(after, prefix, after.Length - prefix - suffix),
                position);
            var old = _slots.Copy(first, end - first);

            _slots.Splice(first, end - first, replaced, document.ContentSize - _slotsFor.ContentSize);
            _slotsFor = document;
            Rebuild(first, old, replaced.Length);
        }

        OnReconciled(previous);
    }

    /// <summary>Brings the views of a run of slots up to date with them.</summary>
    /// <param name="first">The index of the first slot in the run.</param>
    /// <param name="old">The slots the run replaced, which the views still show.</param>
    /// <param name="count">How many slots the run has now.</param>
    private void Rebuild(int first, BlockSlot[] old, int count)
    {
        var limit = Math.Min(old.Length, count);
        var prefix = 0;

        while (prefix < limit && ReferenceEquals(old[prefix].Node, _slots[first + prefix].Node))
        {
            prefix++;
        }

        var suffix = 0;

        while (suffix < limit - prefix
            && ReferenceEquals(old[^(suffix + 1)].Node, _slots[first + count - suffix - 1].Node))
        {
            suffix++;
        }

        var start = first + prefix;
        var oldMiddle = old.Length - prefix - suffix;
        var newMiddle = count - prefix - suffix;

        // The middle is what actually changed. Views in it are updated in place where they can
        // be — a paragraph whose text was edited keeps its view, and with it the run cache the
        // Increment 0 measurements said is worth keeping across a width change.
        for (var i = 0; i < Math.Min(oldMiddle, newMiddle); i++)
        {
            var index = start + i;

            if (_views[index] is ParagraphView view && _slots[index].Node is ParagraphNode paragraph)
            {
                // Highlighted here as Create does: an edit, or a viewer handed a document of the
                // same shape, updates the view in place, and the bare paragraph drew code plain.
                view.Update(Highlight(paragraph));
            }
            else if (_views[index] is TableBlockView table && _slots[index].Node is TableNode edited)
            {
                table.Update(edited);
            }
            else
            {
                _views[index] = Create(_slots[index].Node);
            }
        }

        if (newMiddle > oldMiddle)
        {
            var added = new BlockView[newMiddle - oldMiddle];

            for (var i = 0; i < added.Length; i++)
            {
                added[i] = Create(_slots[start + oldMiddle + i].Node);
            }

            _views.InsertRange(start + oldMiddle, added);
            _heights.Insert(start + oldMiddle, added.Length);
        }
        else if (oldMiddle > newMiddle)
        {
            _views.RemoveRange(start + newMiddle, oldMiddle - newMiddle);
            _heights.Remove(start + newMiddle, oldMiddle - newMiddle);
        }

        // Every block of the run, not just its changed middle. A marker is not part of a
        // paragraph, so an item's number can change while its paragraph node stays the very same
        // object — which is exactly what the matching above skipped. Outside the run nothing can
        // have moved: depth and number come from the lists inside one top-level block, and every
        // top-level block outside the run is the one it was.
        for (var i = first; i < first + count; i++)
        {
            if (_views[i] is ParagraphView paragraph)
            {
                paragraph.SetLead(_slots[i].Depth, _slots[i].Marker);
            }
        }
    }

    private BlockView Create(BlockNode node) => node switch
    {
        ParagraphNode paragraph => new ParagraphView(
            Highlight(paragraph),
            TextStyles,
            _embeds,
            _paragraphStyles),
        RuleNode => new RuleView(TextStyles.Resolve(MarkSet.Empty).ForegroundBrush ?? global::Avalonia.Media.Brushes.Black),
        BlockImageNode image => new BlockImageView(image, _embeds, TextStyles.Resolve(MarkSet.Empty)),
        TableNode table => new TableBlockView(table, Create, TableBorderBrush, TableHeaderBrush) { Prepare = Highlight },

        // Every block the document schema allows now has a view. This stays as an assertion
        // rather than a silent blank, because reaching it means the schema grew a block the view
        // was never taught to draw, and drawing nothing would hide that until someone noticed
        // missing text.
        _ => throw new NotSupportedException(
            $"No view for a '{node.TypeName}' block."),
    };

    /// <summary>A paragraph as it is drawn: with syntax colours, if it is code and there is a highlighter.</summary>
    private ParagraphNode Highlight(ParagraphNode paragraph) =>
        SyntaxHighlighting.Apply(paragraph, _highlighter, _syntaxTheme);

    /// <summary>Throws away every layout, keeping the document.</summary>
    /// <remarks>
    /// For a change that invalidates how text is drawn rather than what it says — a new style
    /// resolver, a new embed renderer, a font change on the control. The heights go with them,
    /// since a block laid out with a different font is not the height it was.
    /// </remarks>
    private protected void DropLayout()
    {
        _views.Clear();
        _slots.Clear();
        _slotsFor = null;
        _heights.Reset(0);
        _first = 0;
        _last = -1;
        _realizedWidth = double.NaN;

        OnLayoutDropped();
    }

    /// <summary>Called after every layout has been discarded, so a subclass can forget too.</summary>
    private protected virtual void OnLayoutDropped()
    {
    }
}
