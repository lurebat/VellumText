namespace VellumText.Avalonia;

/// <summary>Letting go of the rows of a table in a cell that scrolling has left far behind.</summary>
/// <remarks>
/// <para>
/// A table at any depth lays out only what its window reaches, but kept every row it had ever laid
/// out: scrolling through a 10,000-row table in a cell kept 116 MB, every row of it, and three deep
/// beside a line of text 562 MB (rubber-duck review finding 10.4, scope (b)). A table in a cell now
/// lets the views of rows far outside its window go at the end of a layout pass, keeping their
/// heights, so what scrolling through it keeps is a few screenfuls (owner decision: tables in cells
/// only; a table at the top of the document keeps what it lays out, as it always did).
/// </para>
/// <para>
/// <b>When.</b> Once, at the end of the part of the presenter's layout pass that lays the viewport
/// out (<see cref="LettingGo"/>), against the windows the pass settled on — before it lays out what
/// painting will ask about — bands let go whole only in a table holding more rows realized than
/// <c>max(64, 4 × the rows its window reached)</c>. Questions about far points and positions lay
/// out what they need between passes and are answered before the next one, so nothing they
/// prepared is let go under them.
/// </para>
/// <para>
/// <b>What.</b> Whole bands — the rows cells spanning rows tie together — none of whose rows is
/// within the window or a window's height either side of it, that hold no pinned position (the
/// caret, the composition, the selected picture and the drop target of a move: the presenter's
/// <c>PinnedPositions</c>), and that can come back exactly as they are
/// (<see cref="CanComeBackAsItWas"/>): bands of text, and bands whose tables are wholly laid out
/// and no taller than twice the window. Letting a band go drops its rows' views, and with them
/// every table in its cells; its heights stay, so nothing moves and the extent is unchanged. A far
/// band that cannot come back as it is keeps its views, and the tables in it let go of their rows
/// instead, at every depth, keeping the tables and their heights.
/// </para>
/// <para>
/// <b>Coming back.</b> A band let go is realized and laid out again when a window or a question
/// reaches it: a band of text at the height it had, a band holding tables with those tables laid
/// out whole again, as they were let go, so it too comes back at the height it had and letting go
/// never moves anything. (Owner decision Q2 accepted that a row holding a table could come back
/// another height; laid out only as far as the window reached, it did, and that moved text hanging
/// from the row's top in a cell beside the one the viewport was held in, and made a window held by
/// a share of the row chase it — found by the fuzz, so it is not allowed to happen.) Exactly as
/// it was assumes content measures the same each time: a picture whose source the host gave new
/// content (<see cref="BitmapEmbedRenderer.Invalidate"/>) comes back at its new size, measured and
/// settled as any row that changes height is (Q2) — as everything laid out again after that call is.
/// </para>
/// <para>
/// <b>What is known.</b> A band let go keeps what it taught the estimate and
/// <see cref="ExactHeight"/>, which is still true of its content; an edit to it takes that back,
/// as an edit to any row does. A band let go while it was wholly laid out stays wholly known
/// (<see cref="Row.Kept"/>), so a table it completes is not walked again for it; it comes back
/// wholly laid out, so what is known is never less for having let it go.
/// </para>
/// </remarks>
internal sealed partial class TableBlockView
{
    /// <summary>
    /// The document positions the current layout pass must keep laid out, or null outside a pass
    /// that may let rows go.
    /// </summary>
    [ThreadStatic]
    private static int[]? s_pins;

    /// <summary>The tables in cells measured during the current scope, to let rows go at its end.</summary>
    [ThreadStatic]
    private static HashSet<TableBlockView>? s_measured;

    /// <summary>The rows that have views, by index.</summary>
    private readonly HashSet<int> _live = [];

    /// <summary>Whether this table was given its window by a table it is in, rather than by the presenter.</summary>
    private bool _inCell;

    /// <summary>
    /// Where this table's first inner position is in the document, as of the last time the block
    /// or table holding it said; what the pinned positions are compared with.
    /// </summary>
    internal int DocumentStart { get; set; }

    /// <summary>
    /// Lets the tables in cells laid out during the layout pass this scope covers let go of far
    /// rows when it ends, keeping the rows that hold <paramref name="pins"/> laid out.
    /// </summary>
    /// <remarks>
    /// At the end, once, against the windows the pass settled on: let go at the end of each measure,
    /// the passes of the table holding one — each giving it a window a little different from the
    /// last — let rows go and laid them out again, a row holding a table came back another height
    /// each time, and the windows did not settle.
    /// </remarks>
    /// <param name="pins">Document positions whose rows must not be let go.</param>
    /// <returns>A scope that ends it.</returns>
    internal static LettingGoScope LettingGo(int[] pins)
    {
        var scope = new LettingGoScope(s_pins, s_measured);

        s_pins = pins;
        s_measured = [];

        return scope;
    }

    /// <summary>Ends a <see cref="LettingGo"/> scope, letting the far rows go.</summary>
    internal readonly struct LettingGoScope(int[]? outerPins, HashSet<TableBlockView>? outerMeasured) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose()
        {
            var pins = s_pins;
            var measured = s_measured;

            s_pins = outerPins;
            s_measured = outerMeasured;

            if (pins is null || measured is null)
            {
                return;
            }

            foreach (var table in measured)
            {
                table.LetGoFarRows(pins);
            }
        }
    }

    /// <summary>Notes that this table was measured in a scope that lets rows go.</summary>
    private void Measured()
    {
        if (_inCell)
        {
            s_measured?.Add(this);
        }
    }

    /// <summary>Passes the pinned positions' frame of reference down to a table in a cell.</summary>
    private void Adopt(TableBlockView nested, int start)
    {
        nested._inCell = true;
        nested.DocumentStart = DocumentStart + start;
    }

    /// <summary>
    /// Lets go of the bands far outside the window, if this is a table in a cell holding more rows
    /// than its window warrants and the layout pass allows it.
    /// </summary>
    private void LetGoFarRows(int[] pins)
    {
        if (!_inCell
            || double.IsNaN(_width)
            || !double.IsFinite(_windowAbove)
            || !double.IsFinite(_windowBelow))
        {
            return;
        }

        var reached = 0;

        foreach (var start in _visited)
        {
            reached += _bandEnd[start] - start;
        }

        // Bands are let go whole only in a table holding more rows than its window warrants, so a
        // short table is not laid out again and again as its window moves. The tables in far bands
        // that keep their views let their rows go in any table: a few rows each holding tables
        // several screens tall kept every row of every one of them.
        var many = _live.Count > Math.Max(64, 4 * reached);

        // The window and its height again either side: scrolling a screenful back finds its rows
        // still there.
        var anchor = AnchorY();
        var span = _windowAbove + _windowBelow;
        var (first, last) = RowsMeeting(anchor - _windowAbove - span, anchor + _windowBelow + span);
        var keepFrom = first <= last ? _bandStart[first] : _rows.Length;
        var keepTo = first <= last ? _bandEnd[last] : _rows.Length;
        var pinned = new HashSet<int>();

        foreach (var pin in pins)
        {
            var local = pin - DocumentStart;

            if (local >= 0 && local <= ContentSize && _rows.Length > 0)
            {
                pinned.Add(_bandStart[Math.Clamp(RowAt(local), 0, _rows.Length - 1)]);
            }
        }

        var far = new HashSet<int>();
        var kept = new HashSet<int>();

        foreach (var row in _live)
        {
            var start = _bandStart[row];

            if ((start < keepFrom || start >= keepTo)
                && !_visited.Contains(start)
                && !pinned.Contains(start)
                && !far.Contains(start))
            {
                if (CanComeBackAsItWas(start, 2 * span))
                {
                    if (many)
                    {
                        far.Add(start);
                    }
                }
                else
                {
                    kept.Add(start);
                }
            }
        }

        var pinnedLive = pinned.Where(BandRealized).ToList();

        foreach (var start in far)
        {
            LetGo(start);
        }

        foreach (var start in kept)
        {
            foreach (var nested in NestedInBand(start))
            {
                nested.LetGoEverything(2 * span);
            }
        }

        CheckLetGo(pinnedLive);
    }

    /// <summary>
    /// Lets go of every band of this table that can come back as it was, and of the rows of the
    /// tables in the rest: it is in a band far outside the window of the table holding it that
    /// keeps its views, being too tall to lay out whole again or holding a table only partly laid
    /// out.
    /// </summary>
    /// <remarks>
    /// Such a band is never let go itself, and the tables in it, no longer measured, never let
    /// their own far rows go: a table of tables each a few screens tall kept every row of every one
    /// of them (rubber-duck review of scope (b), finding 1). Each band let go here keeps its height,
    /// and the table keeps the heights it had and the estimate for the rest, so it stays its
    /// height and the row holding it does not move; the table itself is kept, so a table partly
    /// laid out comes back the very table it was. Pins need no check: a pinned position anywhere
    /// in the band holding this table keeps the whole band, so none is in here.
    /// </remarks>
    /// <param name="limit">How tall a band holding tables may be to be let go whole.</param>
    private void LetGoEverything(double limit)
    {
        if (double.IsNaN(_width))
        {
            return;
        }

        foreach (var start in _live.Select(row => _bandStart[row]).Distinct().ToList())
        {
            if (CanComeBackAsItWas(start, limit))
            {
                LetGo(start);
            }
            else
            {
                foreach (var nested in NestedInBand(start))
                {
                    nested.LetGoEverything(limit);
                }
            }
        }

        // What its window reached when it was last measured is let go too: forgotten, so that if it
        // was measured earlier in this pass — the windows of the tables holding it move between
        // the passes of one measure — letting its own rows go at the end of it neither keeps them
        // for that window nor finds them gone. And asked for the same window again, it walks it.
        _visited.Clear();
        _measured = null;
    }

    /// <summary>
    /// Whether a band can be let go and laid out again later exactly as it is: one holding no
    /// table, or one whose tables are wholly laid out and short enough to lay out whole again.
    /// </summary>
    /// <remarks>
    /// A band comes back with the tables in it laid out whole (<see cref="Row.Kept"/>), so letting
    /// it go never moves anything: laid out afresh as far as the window reached, a row holding a
    /// table came back another height, which moved what hung from the row's top in a cell beside
    /// the one the viewport was held in, and a window anchored by a share of such a row chased the
    /// share from pass to pass. A band holding a table only partly laid out keeps its views — the
    /// table in it lets its own far rows go — and so does one whose tables are taller than
    /// <paramref name="limit"/>, whose laying out again whole would cost more than a few screens;
    /// the tables in either let their rows go instead (<see cref="LetGoEverything"/>).
    /// </remarks>
    private bool CanComeBackAsItWas(int start, double limit) =>
        !BandHostsTables(start)
            || (BandComplete(start) && RowTop(_bandEnd[start]) - RowTop(start) <= limit);

    /// <summary>Whether any row of a band has views.</summary>
    private bool BandRealized(int start)
    {
        for (var r = start; r < _bandEnd[start]; r++)
        {
            if (_rows[r].Cells is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Drops the views of a band's rows, keeping their heights and what they taught.</summary>
    private void LetGo(int start)
    {
        LayoutGuard.Changing("rows of a table in a cell let go");

        var complete = BandComplete(start);

        for (var r = start; r < _bandEnd[start]; r++)
        {
            _rows[r].Cells = null;
            _rows[r].Kept = complete;
            _live.Remove(r);
        }

        WorkCounters.TableRowsLetGo += _bandEnd[start] - start;
    }

    /// <summary>Takes back that a band is wholly known without being laid out: it was edited.</summary>
    private void Unkeep(int row)
    {
        for (var r = _bandStart[row]; r < _bandEnd[row]; r++)
        {
            _rows[r].Kept = false;
        }
    }

    /// <summary>The rows that have views, worked out again after a rebuild.</summary>
    private void Recount()
    {
        _live.Clear();

        for (var r = 0; r < _rows.Length; r++)
        {
            if (_rows[r].Cells is not null)
            {
                _live.Add(r);
            }
        }
    }

    /// <summary>
    /// In debug builds, after rows are let go: every band the window reached is laid out, every
    /// band holding a pinned position that had views has them still, and the rows counted as having
    /// views are the rows that have them.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void CheckLetGo(List<int> pinnedLive)
    {
        foreach (var start in _visited)
        {
            Invariant.That(IsLaidOut(start), "a band the window reached is laid out after rows are let go");
        }

        foreach (var start in pinnedLive)
        {
            Invariant.That(BandRealized(start), "a band holding a pinned position keeps its views");
        }

        if (_rows.Length <= 4_096)
        {
            Invariant.That(
                _live.Count == _rows.Count(row => row.Cells is not null) && _live.All(row => _rows[row].Cells is not null),
                "the rows counted as having views are the rows that have them");
        }
    }
}
