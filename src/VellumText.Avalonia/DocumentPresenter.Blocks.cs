using System.Collections.Immutable;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation.Peers;

namespace VellumText.Avalonia;

/// <summary>
/// Content kept at the top of the viewport across a layout: a document position in
/// <paramref name="Basis"/>, and how to find the point from the content there.
/// </summary>
/// <param name="Position">The document position the anchor is at.</param>
/// <param name="Local">The anchor, local to the block it was taken in.</param>
/// <param name="Basis">The document whose layout it was read from.</param>
internal readonly record struct HeldAnchor(int Position, LocalAnchor Local, DocumentNode Basis);

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

    /// <summary>Content held at the top of the viewport for the next <see cref="Realize"/>, if any.</summary>
    private HeldAnchor? _heldAnchor;

    /// <summary>Whether a <see cref="Realize"/> is running.</summary>
    private bool _realizing;

    /// <summary>
    /// The content the last layout pass kept at the top of the viewport, and the offset it left
    /// the viewport at.
    /// </summary>
    private (HeldAnchor Anchor, double Offset)? _settled;

    /// <summary>The content the last settle held, when the offset ended clamped short of putting it at the top.</summary>
    private (int Block, LocalAnchor Local)? _clamped;

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

        PreparePosition(position);

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

        PreparePosition(position);

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

        PreparePoint(point.Y);

        var prepared = PrepareAnswer(point);

        BeforeAnswer?.Invoke();

        var index = BlockIndexAt(point.Y);
        ColumnBoundary? boundary = null;

        using (LayoutGuard.Answering(prepared))
        {
            if ((prepared ? _views[index] : EnsureMeasured(index)) is TableBlockView table)
            {
                var origin = BlockOrigin(index);

                boundary = table.BoundaryAt(point, _slots[index].Start, new Vector(origin.X, origin.Y), tolerance);
            }
        }

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

        PreparePoint(point.Y);

        var prepared = PrepareAnswer(point);

        BeforeAnswer?.Invoke();

        var index = BlockIndexAt(point.Y);
        var slot = _slots[index];
        int hit;

        using (LayoutGuard.Answering(prepared))
        {
            var view = prepared ? _views[index] : EnsureMeasured(index);

            hit = view.HitTest(point - BlockOrigin(index));
        }

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
        // Taken before the change is reconciled, while the views still show what the reader saw;
        // a composition that was about to drop a layout took its own first. See ViewportAnchor.
        var held = _heldAnchor ?? ViewportAnchor();
        var mapping = held is { } h && !ReferenceEquals(h.Basis, PresentedDocument)
            ? MappingFrom(h.Basis)
            : null;

        _heldAnchor = null;
        _realizing = true;

        // Taken whatever happens below, so a request that finds nothing to scroll does not linger
        // and fire on some later pass.
        var toEnd = _endRequested;

        _endRequested = false;

        try
        {
            Reconcile();

            if (_views.Count == 0)
            {
                _first = 0;
                _last = -1;
                SetExtent(default);

                return default;
            }

            // A block laid out for a different width is not the height it was, so every height on
            // record has become a guess again. Reset keeps what it has learned about how tall a
            // block in this document tends to be, which is the part still true.
            if (!_realizedWidth.Equals(_width))
            {
                _realizedWidth = _width;
                _heights.Reset(_views.Count);
            }

            var size = Settle(Resolve(held, mapping));

            // An explicit request for the end (RichTextView.ScrollToEnd, the scroll bar taken to
            // its end) is settled from the end up, as Ctrl+End is below, and for the same reason:
            // an offset at the end of a range built from estimates stops wherever they stop.
            if (toEnd)
            {
                size = SettleAtEnd();
            }

            if (_pendingAnchor)
            {
                _pendingAnchor = false;

                // The caret sent to the document's end — Ctrl+End — means the end: laid out from
                // the end up, with the end at the bottom of the viewport. Aimed at by the caret's
                // position instead, it is wherever the estimates above it put it, and when the rows
                // nobody has seen hold most of the document that can be screens short of the end,
                // with the offset clamped at an extent that is too small to reach it. The caret is
                // then brought into view as ever: the last place it can sit is not always at the
                // bottom — beside a long table in the last row it is at the row's top.
                if (IsAnchorAtDocumentEnd)
                {
                    size = SettleAtEnd();
                }

                // Laying out a screenful replaces estimates with real heights, which moves the
                // caret, which can put it back off the screen it was just scrolled onto. The bound
                // is there because a loop waiting on an estimate to converge must be allowed not to.
                for (var attempt = 0; attempt < 3 && ScrollAnchorIntoView(); attempt++)
                {
                    size = Settle(null);
                }
            }

            // Whatever painting will ask about — the caret, the composition, a drop target far
            // off screen — is laid out now, while laying out is allowed, and the viewport put back.
            PreparePaintDependencies();

            // A settle that ended with the offset clamped at the end of the document left the
            // content it held below the top. Laying out what painting needs can move the end — an
            // estimate replaced — so the held content is put back as far toward the top as the end
            // now allows, rather than the content the clamp happened to leave there.
            if (_clamped is { } clamped)
            {
                size = Settle(clamped);
            }

            return Finish(size);
        }
        finally
        {
            _realizing = false;
        }
    }

    /// <summary>Lays out every position painting is going to ask about.</summary>
    /// <remarks>
    /// Painting lays nothing out (<see cref="LayoutGuard"/>), so a position it asks about has to be
    /// prepared before the frame: at the end of every layout, and wherever something painting
    /// depends on changes without one. The default paints nothing that needs it.
    /// </remarks>
    private protected virtual void PreparePaintDependencies()
    {
    }

    /// <summary>Whether a question about a document position can be answered without laying anything out.</summary>
    /// <param name="position">The document position.</param>
    private protected bool IsPositionPrepared(int position)
    {
        if (_views.Count == 0)
        {
            return true;
        }

        var index = Math.Clamp(BlockIndexOf(position), 0, _views.Count - 1);

        return IsPrepared(index, Math.Clamp(position - _slots[index].Start, 0, _views[index].ContentSize));
    }

    private bool IsPrepared(int index, int local) =>
        _heights.Count == _views.Count
            && _heights.Measured(index)
            && _views[index].Size.Height.Equals(_heights.HeightOf(index))
            && _views[index].IsPreparedAt(local);

    /// <summary>
    /// Lays out what a question about a document position needs, keeping the content at the top
    /// of the viewport where it is.
    /// </summary>
    /// <remarks>
    /// A question about a far position — the caret after Ctrl+End, a find match, a drop target —
    /// lays out the row or block it is in. That replaces estimates with real heights, and when the
    /// estimate it replaces prices content above the viewport, the content on screen moves unless
    /// the viewport moves with it. So the question is answered in two steps: this lays out and puts
    /// the content at the top back, and the question then reads what is laid out, against the
    /// positions as they finally are.
    /// </remarks>
    /// <param name="position">The document position.</param>
    private protected void PreparePosition(int position)
    {
        if (_views.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(BlockIndexOf(position), 0, _views.Count - 1);
        var local = Math.Clamp(position - _slots[index].Start, 0, _views[index].ContentSize);

        if (IsPrepared(index, local))
        {
            return;
        }

        Transact(() =>
        {
            _views[index].Measure(_width);
            _views[index].PrepareAt(local);
            RecordHeight(index);
        });
    }

    /// <summary>
    /// Lays out everything between the content at the top of the viewport and a point, so that the
    /// question about the point is answered against real heights.
    /// </summary>
    /// <remarks>
    /// A point far off screen — a drag past the edge — names whatever is that far from what the
    /// reader sees. Where that is depends on the heights of everything in between, and an answer
    /// read from estimates names whatever the estimates put there, which laying the answer out then
    /// moves. So the window is widened from the content at the top to the point, laid out with real
    /// heights, and the content at the top put back; the point then lands on what is really there.
    /// The cost is the distance to the point, as scrolling there would be.
    /// </remarks>
    /// <param name="y">The point's vertical position, in this control's coordinates.</param>
    /// <param name="tolerance">How far either side of it must be laid out too.</param>
    private protected void PreparePoint(double y, double tolerance = 0)
    {
        if (_views.Count == 0
            || !IsVirtualizing
            || _heights.Count != _views.Count
            || (y - tolerance >= -Overscan && y + tolerance <= ViewportHeight + Overscan))
        {
            return;
        }

        Transact(() =>
        {
            var (index, local) = AnchorAtOffset();
            var point = _heights.OffsetOf(index) + PointOf(index, local);
            var top = _offset.Y - point;

            RealizeWindow(
                index,
                local,
                Math.Max(Overscan - top, -(top + y - tolerance)),
                Math.Max(top + ViewportHeight + Overscan, top + y + tolerance));

            // Back to the window the viewport needs; everything in it is laid out by now.
            RealizeWindow(index, local, Overscan - top, top + ViewportHeight + Overscan);
        });
    }

    /// <summary>
    /// Lays out everything a point question reads to answer — the block at the point and, in a
    /// table, every row whose closed band touches the point's height, so the row starting exactly
    /// there as well as the one ending there, and the same in the tables inside them — keeping the
    /// content at the top of the viewport where it is.
    /// </summary>
    /// <remarks>
    /// Putting the content back moves the block under the point, so what the point names is
    /// chosen again against the positions as they then are, until it is laid out; a few rounds
    /// are allowed, as each lays out more and nothing laid out is undone. If they do not settle,
    /// the answer lays out what it still needs, as it always did, and records the height after.
    /// </remarks>
    /// <param name="point">The point, in this control's coordinates.</param>
    /// <returns>Whether the answer can read everything it needs without laying anything out.</returns>
    private bool PrepareAnswer(Point point)
    {
        if (_heights.Count != _views.Count)
        {
            return false;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var index = BlockIndexAt(point.Y);

            if (IsAnswerPrepared(index, point))
            {
                return true;
            }

            Transact(() =>
            {
                EnsureMeasured(index);

                if (_views[index] is TableBlockView table)
                {
                    table.PreparePoint(point - BlockOrigin(index));
                    RecordHeight(index);
                }
            });
        }

        return IsAnswerPrepared(BlockIndexAt(point.Y), point);
    }

    private bool IsAnswerPrepared(int index, Point point) =>
        _heights.Measured(index)
            && _views[index].Size.Height.Equals(_heights.HeightOf(index))
            && (_views[index] is TableBlockView table
                ? table.IsPreparedForPoint(point - BlockOrigin(index))
                : _views[index].IsPreparedAt(0));

    /// <summary>
    /// Runs something that lays out outside the window, then puts the content at the top of the
    /// viewport back where it was and makes sure the viewport is still covered.
    /// </summary>
    /// <remarks>
    /// The content put back is the content the last layout held at the top while nothing has
    /// scrolled since, and it stays held at the offset the viewport moved to: read afresh, a line
    /// that a narrower width folded into an earlier one would be named by that earlier start, and
    /// the next resize would hold the wrong line.
    /// </remarks>
    private void Transact(Action layOut)
    {
        LayoutGuard.Changing("a geometry question laid something out");

        var settled = _settled is { } s && s.Offset.Equals(_offset.Y) && ReferenceEquals(s.Anchor.Basis, _slotsFor)
            ? s.Anchor
            : default(HeldAnchor?);
        var held = _heights.Count != _views.Count ? null
            : settled is { } kept ? Resolve(kept, null)
            : AnchorAtOffset();
        var before = held is { } h ? _heights.OffsetOf(h.Block) + PointOf(h.Block, h.Local) : 0;

        layOut();
        AfterPrepare?.Invoke();

        if (held is { } anchor && anchor.Block < _views.Count)
        {
            var after = _heights.OffsetOf(anchor.Block) + PointOf(anchor.Block, anchor.Local);

            var wanted = _offset.Y + (after - before);

            if (!after.Equals(before))
            {
                ApplyOffset(new Vector(_offset.X, wanted), relayout: false);
            }

            // Not when the offset was clamped: the held content is no longer at the top.
            if (settled is { } stillHeld)
            {
                _settled = _offset.Y.Equals(wanted) ? (stillHeld, _offset.Y) : null;
            }
        }

        // Outside a layout pass nothing else checks: a hole now would be drawn blank by the next
        // frame, so the next frame is made to wait for a layout pass that fills it.
        if (!_realizing && !IsWindowCovered())
        {
            WorkCounters.CoverageRepairs++;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Makes sure everything the viewport and its overscan meet is laid out, whatever the passes
    /// before left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drawing lays nothing out, so a row or a block on screen that is not laid out when this
    /// returns is a blank on screen. The passes before keep the content at the top of the viewport
    /// where it was and lay out outward from it until real heights reach the window's edges, so
    /// the window is covered when they finish; this is the check that it is, and what happens if
    /// it is not.
    /// </para>
    /// <para>
    /// The check is the same in every build: the window is laid out again from the content at
    /// its top, twice at most, and if that still leaves a hole — which would be a defect in the
    /// passes above, and is counted and traced so a test sees it — every block is laid out whole.
    /// That is slow, but nothing is left unlaid, so nothing can be drawn blank.
    /// </para>
    /// </remarks>
    private Size Finish(Size size)
    {
        if (IsWindowCovered())
        {
            return size;
        }

        WorkCounters.CoverageRepairs++;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            size = Settle(null);

            if (IsWindowCovered())
            {
                return size;
            }
        }

        WorkCounters.FullLayoutBackstops++;
        Trace.TraceWarning("VellumText: the realized window did not cover the viewport; every block was laid out.");

        size = LayOutEverything();

        Invariant.That(IsWindowCovered(), "the viewport is covered once every block is laid out");

        return size;
    }

    /// <summary>
    /// The content at the top of the viewport, as the last layout drew it, or null when that is a
    /// distance into a block rather than content in one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inside a table the viewport is held by what is there — the line of text at its top, through
    /// any table nested in a cell — rather than by a distance from the table's top. Measuring rows
    /// above the viewport replaces their estimates, an edit far up the table changes their sizes,
    /// and a composition reflows the paragraph it is in, and each of those moves the content under
    /// a fixed distance. A document position moves with the content, and the transaction that
    /// moved it says where to (<see cref="MappingFrom"/>), so the line at the top stays at the top
    /// (owner decision Q3).
    /// </para>
    /// <para>
    /// Read from the last layout, without laying anything out, because it is taken before an
    /// edit is reconciled or a composition invalidates a paragraph: after either, the views no
    /// longer show what the reader saw. Other blocks keep the distance they always had.
    /// </para>
    /// </remarks>
    private protected HeldAnchor? ViewportAnchor()
    {
        // Not scrolled at all, nothing is held, as a browser selects no scroll anchor there (CSS
        // Scroll Anchoring, anchor node selection step 2): text typed or inserted at the very top
        // pushes what follows down rather than scrolling the new text out of sight.
        if (_views.Count == 0 || _heights.Count != _views.Count || _slotsFor is null || _offset.Y <= 0)
        {
            return null;
        }

        // The content the last layout put at the top, while nothing has scrolled since: read from
        // the layout again, a line that reflowed would be named by wherever it now starts, and a
        // run of compositions or edits would walk the anchor up the paragraph one reflow at a time.
        if (_settled is { } settled
            && settled.Offset.Equals(_offset.Y)
            && ReferenceEquals(settled.Anchor.Basis, _slotsFor))
        {
            return settled.Anchor;
        }

        var index = _heights.IndexAt(_offset.Y);

        if (_views[index] is not TableBlockView table)
        {
            return null;
        }

        var local = table.CaptureAnchor(_offset.Y - _heights.OffsetOf(index));

        return new HeldAnchor(_slots[index].Start + local.Position, local, _slotsFor);
    }

    /// <summary>
    /// Holds the content at the top of the viewport before something drops a layout without the
    /// document changing — a composition does — so the next <see cref="Realize"/> keeps that
    /// content there rather than reading a layout that is no longer there.
    /// </summary>
    /// <remarks>
    /// Not while a realize is running: it has already held what was on screen before it began,
    /// and a composition it clears while reconciling must not replace that.
    /// </remarks>
    private protected void HoldViewportAnchor()
    {
        if (!_realizing && _heldAnchor is null)
        {
            _heldAnchor = ViewportAnchor();
        }
    }

    /// <summary>Forgets a held anchor that no realize is going to consume.</summary>
    private protected void ReleaseViewportAnchor() => _heldAnchor = null;

    /// <summary>
    /// How positions in a document the view last laid out moved to reach the one it presents now,
    /// or null if that is not known.
    /// </summary>
    /// <remarks>
    /// Null whenever the new document did not come from the old one by one transaction — a
    /// document handed over whole, or two edits applied before the view saw either — and then the
    /// viewport keeps its distance from the top, as it always did.
    /// </remarks>
    /// <param name="basis">The document the view last laid out.</param>
    private protected virtual Mapping? MappingFrom(DocumentNode basis) => null;

    /// <summary>A held anchor brought into the document as it is now, or null if it cannot be.</summary>
    /// <remarks>
    /// An anchor whose content the edit deleted lands where the content was, and is moved on to
    /// the first leaf that survives there — the next one, or the last if none follows — with the
    /// top of its first line at the top of the viewport.
    /// </remarks>
    private (int Block, LocalAnchor Local)? Resolve(HeldAnchor? held, Mapping? mapping)
    {
        if (held is not { } anchor || _views.Count == 0)
        {
            return null;
        }

        var position = anchor.Position;
        var local = anchor.Local;

        if (!ReferenceEquals(anchor.Basis, PresentedDocument))
        {
            if (mapping is null)
            {
                return null;
            }

            var mapped = mapping.MapWithResult(position, Assoc.After);

            position = mapped.Pos;

            if (mapped.Deleted)
            {
                position = SurvivingLeaf(position);
                local = new LocalAnchor(0, 0, AnchorKind.Line);
            }
        }

        var index = Math.Clamp(BlockIndexOf(position), 0, _views.Count - 1);

        return (index, local with { Position = Math.Clamp(position - _slots[index].Start, 0, _views[index].ContentSize) });
    }

    /// <summary>The position itself if a leaf holds it, else the start of the next leaf, else the end of the last.</summary>
    private int SurvivingLeaf(int position)
    {
        var leaves = Leaves;

        if (leaves.IsEmpty)
        {
            return position;
        }

        var low = 0;
        var high = leaves.Length - 1;
        var found = -1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);

            if (leaves[middle].Start <= position)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (found >= 0 && position <= End(leaves[found]))
        {
            return position;
        }

        return found + 1 < leaves.Length ? leaves[found + 1].Start : End(leaves[^1]);

        static int End(BlockSlot leaf) => leaf.Node.IsLeaf ? leaf.Start : leaf.Start + leaf.Node.ContentSize;
    }

    /// <summary>
    /// Lays out the window and puts the content at the top of the viewport back where it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measuring a block replaces the estimate for every block nobody has measured, because the
    /// estimate is drawn from the ones that have been (<see cref="BlockHeightIndex.Estimate"/>). So
    /// laying out a screenful moves the whole document under the viewport — and moves the window,
    /// which lays out more blocks, which moves it again. Repeating until the window stops changing
    /// and covers the viewport is what settles it; the anchor is what stops the reader seeing any
    /// of it.
    /// </para>
    /// <para>
    /// The anchor is held across every attempt, and the window is laid out outward from it, so the
    /// content measured from is the content put back. Near the end of the document the offset can
    /// be clamped above the anchor; the next attempt lays the window out from where the viewport's
    /// top then is, relative to the anchor, so the content the clamp brought on screen is laid out
    /// too.
    /// </para>
    /// </remarks>
    /// <param name="held">The content to keep at the top, or null for what is there now.</param>
    private Size Settle((int Block, LocalAnchor Local)? held)
    {
        var size = default(Size);
        var anchor = held;
        var top = 0.0;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var first = _first;
            var last = _last;
            var (index, local) = anchor ??= AnchorAtOffset();

            size = RealizeWindow(index, local, Overscan - top, top + ViewportHeight + Overscan);

            // A share of a row nobody had laid out names no content, and the point it names moves
            // as the row's height does. A table holding tables turns it into the content it found
            // there once that row was laid out, and measured around that; it is what is held now.
            if (local.Kind == AnchorKind.RowShare && _views[index] is TableBlockView settling && settling.WindowAnchor != local)
            {
                local = settling.WindowAnchor;
                anchor = (index, local);
            }

            var point = _heights.OffsetOf(index) + PointOf(index, local);


            ApplyOffset(new Vector(_offset.X, point), relayout: false);

            // Zero unless the offset was clamped: where the viewport's top is, from the anchor.
            top = _offset.Y - point;

            if (_first == first && _last == last && IsWindowCovered())
            {
                break;
            }
        }

        // A share of a row nobody had laid out names no content. The row is laid out now, so what
        // is held from here on is the text the reader is shown at the top (owner decision Q3):
        // a resize keeps that line still, not a share of a row that changed height.
        if (top == 0
            && anchor is ({ } shared, { Kind: AnchorKind.RowShare })
            && _views[shared] is TableBlockView sharedTable
            && _heights.IndexAt(_offset.Y) == shared)
        {
            anchor = (shared, sharedTable.CaptureAnchor(_offset.Y - _heights.OffsetOf(shared)));
        }

        _clamped = top == 0 ? null : anchor;

        // Not when the offset was clamped: then the anchor is not at the top of the viewport, and
        // holding it would name content the reader cannot see there.
        _settled = top == 0 && anchor is var (block, content) && _views[block] is TableBlockView && _slotsFor is { } basis
            ? (new HeldAnchor(_slots[block].Start + content.Position, content, basis), _offset.Y)
            : null;

        AfterSettle?.Invoke();

        return size;
    }

    /// <summary>
    /// Lays the window out from the end of the document up and puts the end at the bottom of the
    /// viewport.
    /// </summary>
    /// <remarks>
    /// Held by the last block's bottom — a table's by its last row's — so each attempt measures up
    /// from the end itself, whatever the estimates above it say; nothing is held at the top after,
    /// so the next pass reads the content the end left there.
    /// </remarks>
    private Size SettleAtEnd()
    {
        var size = default(Size);

        for (var attempt = 0; attempt < 3 && _views.Count > 0; attempt++)
        {
            var first = _first;
            var last = _last;
            var index = _views.Count - 1;
            var local = _views[index] is TableBlockView table ? table.BottomAnchor : new LocalAnchor(0, 0, AnchorKind.BoxBottom);

            size = RealizeWindow(index, local, Overscan + ViewportHeight, Overscan);
            ApplyOffset(new Vector(_offset.X, _heights.OffsetOf(index) + PointOf(index, local) - ViewportHeight), relayout: false);

            if (_first == first && _last == last && IsWindowCovered())
            {
                break;
            }
        }

        _clamped = null;
        _settled = null;

        return size;
    }

    /// <summary>Whether what the view keeps in view — the caret — is at the very end of the document.</summary>
    private protected virtual bool IsAnchorAtDocumentEnd => false;

    /// <summary>Called at the end of every layout pass, for tests that break what a pass left.</summary>
    internal Action? AfterSettle { get; set; }

    /// <summary>Called after a geometry question lays something out, for tests that break what it left.</summary>
    internal Action? AfterPrepare { get; set; }

    /// <summary>
    /// For tests: every block's height as the index holds it — measured, or the estimate — added
    /// up one by one, and whether each block laid out in the window is the height recorded for it.
    /// </summary>
    internal (double Sum, bool Agree) HeightLedger()
    {
        var sum = 0.0;
        var agree = _heights.Count == _views.Count;

        for (var i = 0; i < _heights.Count; i++)
        {
            sum += _heights.HeightOf(i);

            if (agree && i >= _first && i <= _last && _heights.Measured(i))
            {
                agree = Math.Abs(_views[i].Size.Height - _heights.HeightOf(i)) <= 0.01;
            }
        }

        return (sum, agree);
    }

    /// <summary>
    /// For tests: the document position of the text line held at the top of the viewport, while
    /// one is held and nothing has scrolled since.
    /// </summary>
    internal int? HeldLine =>
        _settled is { } settled && settled.Offset.Equals(_offset.Y) && settled.Anchor.Local.Kind == AnchorKind.Line
            ? settled.Anchor.Position
            : null;

    /// <summary>
    /// Called when a point question has laid out what it needs and starts answering, for tests
    /// that check the answer itself lays nothing out.
    /// </summary>
    internal Action? BeforeAnswer { get; set; }

    /// <summary>The block at the top of the viewport and the content there.</summary>
    private (int Block, LocalAnchor Local) AnchorAtOffset()
    {
        var index = _heights.IndexAt(_offset.Y);
        var into = _offset.Y - _heights.OffsetOf(index);

        return (index, _views[index] is TableBlockView table
            ? table.CaptureAnchor(into)
            : new LocalAnchor(0, into, AnchorKind.Box));
    }

    /// <summary>Where an anchor's point is in its block now.</summary>
    /// <remarks>
    /// Clamped against the height the block turned out to have, not the one it was estimated to
    /// have. An anchor 40px tall that was guessed at 80 would otherwise keep a 60px offset into
    /// it, which is not inside it at all — the viewport would land on the block after the one the
    /// scroll bar pointed at.
    /// </remarks>
    private double PointOf(int index, LocalAnchor local) =>
        _views[index].AnchorPoint(local) is { } point && double.IsFinite(point)
            ? Math.Clamp(point, 0, _heights.HeightOf(index))
            : Math.Clamp(local.Dy, 0, _heights.HeightOf(index));

    /// <summary>
    /// Whether every block, and every row of every table, that the viewport and its overscan meet
    /// is laid out.
    /// </summary>
    private protected bool IsWindowCovered()
    {
        if (!IsVirtualizing || _views.Count == 0)
        {
            return true;
        }

        var top = Math.Max(0, _offset.Y - Overscan);
        var bottom = Math.Min(_heights.TotalHeight, _offset.Y + ViewportHeight + Overscan);

        if (_last < _first || _heights.IndexAt(top) < _first)
        {
            return false;
        }

        if (bottom > top && _heights.IndexAt(Math.Max(top, bottom - 0.5)) > _last)
        {
            return false;
        }

        for (var i = _first; i <= _last; i++)
        {
            if (!_heights.Measured(i))
            {
                return false;
            }

            if (_views[i] is TableBlockView table)
            {
                var at = _heights.OffsetOf(i);

                if (!table.Covers(top - at, bottom - at))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Lays every block out whole, keeping the content at the top of the viewport.</summary>
    /// <remarks>The last resort of <see cref="Finish"/>: slow, and leaves nothing unlaid.</remarks>
    private Size LayOutEverything()
    {
        var (index, local) = AnchorAtOffset();
        var width = 0d;

        for (var i = 0; i < _views.Count; i++)
        {
            _views[i].SetWindow(0, 0, double.PositiveInfinity);

            var measured = _views[i].Measure(_width);

            _heights.SetHeight(i, measured.Height);
            width = Math.Max(width, measured.Width);
        }

        _first = 0;
        _last = _views.Count - 1;
        SetExtent(new Size(width, _heights.TotalHeight));
        ApplyOffset(new Vector(_offset.X, _heights.OffsetOf(index) + PointOf(index, local)), relayout: false);

        return new Size(width, IsVirtualizing ? Math.Min(_heights.TotalHeight, ViewportHeight) : _heights.TotalHeight);
    }

    /// <summary>Lays out the blocks the viewport covers and reports what the view needs.</summary>
    /// <remarks>
    /// Outward from the anchor: the anchor's block first, with its window set around the anchor's
    /// content, then down and up from it, adding the real heights of what has been laid out until
    /// they reach the viewport and its overscan. Walking forward from a block that an estimate said
    /// was at the top of the window measured the wrong blocks whenever the estimate was wrong, and
    /// the blocks above the anchor are exactly the ones priced at estimates.
    /// </remarks>
    /// <param name="index">The anchor's block.</param>
    /// <param name="local">The anchor's content in it.</param>
    /// <param name="above">How far above the anchor's point to lay out.</param>
    /// <param name="below">How far below it to lay out.</param>
    private Size RealizeWindow(int index, LocalAnchor local, double above, double below)
    {
        var width = 0d;

        if (!IsVirtualizing)
        {
            for (var i = 0; i < _views.Count; i++)
            {
                _views[i].SetWindow(0, 0, double.PositiveInfinity);

                var measured = _views[i].Measure(_width);

                _heights.SetHeight(i, measured.Height);
                width = Math.Max(width, measured.Width);
            }

            _first = 0;
            _last = _views.Count - 1;
            SetExtent(new Size(width, _heights.TotalHeight));

            return new Size(width, _heights.TotalHeight);
        }

        var anchor = _views[index];

        if (anchor is TableBlockView table)
        {
            table.SetWindow(local, above, below);
        }
        else
        {
            anchor.SetWindow(Math.Max(0, local.Dy), above, below);
        }

        var size = anchor.Measure(_width);

        _heights.SetHeight(index, size.Height);
        width = size.Width;

        var point = PointOf(index, local);
        var reach = size.Height - point;
        var last = index;

        for (var i = index + 1; i < _views.Count && reach < below; i++)
        {
            _views[i].SetWindow(0, 0, below - reach);

            var measured = _views[i].Measure(_width);

            _heights.SetHeight(i, measured.Height);
            width = Math.Max(width, measured.Width);
            reach += measured.Height;
            last = i;
        }

        reach = point;

        var first = index;

        for (var i = index - 1; i >= 0 && reach < above; i--)
        {
            // From its bottom, which is the edge facing the anchor.
            _views[i].SetWindow(_views[i].Size.Height, above - reach, 0);

            var measured = _views[i].Measure(_width);

            _heights.SetHeight(i, measured.Height);
            width = Math.Max(width, measured.Width);
            reach += measured.Height;
            first = i;
        }

        _first = first;
        _last = last;

        SetExtent(new Size(width, _heights.TotalHeight));

        return new Size(width, Math.Min(_heights.TotalHeight, ViewportHeight));
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
            LayoutGuard.Changing("a block's recorded height");
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
