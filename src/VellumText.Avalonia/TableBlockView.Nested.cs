using System.Diagnostics;

namespace VellumText.Avalonia;

/// <summary>Row virtualization for tables inside table cells.</summary>
/// <remarks>
/// <para>
/// A table in a cell is laid out only as far as this table's window reaches into it, as a table at
/// the top of the document is (rubber-duck review finding 10.4, owner decision: full nested
/// virtualization). Without this, every row of it was laid out whenever the row holding it was:
/// 135 MB and 1.5 s for a 10,000 × 3 table in a cell, against 5 MB and 37 ms for the same table
/// at the top.
/// </para>
/// <para>
/// <b>Windows.</b> This table's window is the anchor point — the content the presenter keeps at the
/// top of the viewport — and the distances above and below it. A table in a cell is given the part
/// of that window that falls in it (<see cref="WindowNested"/>): the anchor itself when the anchor
/// is inside it, measured outward the same way; otherwise the distance from its top down to the
/// window's bottom, or from its bottom up to the window's top. Both are measured from the edge
/// facing the anchor, so what is measured never depends on the inner table's own estimate.
/// </para>
/// <para>
/// <b>Heights.</b> A row holding a table is settled against the height the table measured to, and
/// is settled again whenever that moves — the inner table measured more of itself
/// (<see cref="RefreshNested"/>), or a question laid out a far row of it (<see cref="PrepareAt"/>).
/// The change goes up through every table to the presenter, which keeps the content at the top of
/// the viewport where it was.
/// </para>
/// <para>
/// <b>What must be exact</b> (<see cref="Close"/>). The anchor keeps still what is at the top of the
/// viewport and everything placed from it with real heights. Content placed across an estimate
/// that is not on that path — text in the cell beside a long inner table, the bottom of a row a
/// sibling table might outgrow — would move as estimates converge. Each such estimate is made
/// exact before it can move anything on screen: by proving from lower bounds that what it places
/// cannot reach the window, or by laying out the table that carries it (owner decision Q1: exact
/// and still, at the cost of laying out that table).
/// </para>
/// <para>
/// <b>Fallback.</b> A window that cannot be translated — a coordinate that is not a number — or
/// windows that do not settle within <see cref="RefreshLimit"/> passes lay the inner table out in
/// full, which is slow and never blank. It is counted and traced.
/// </para>
/// </remarks>
internal sealed partial class TableBlockView
{
    private readonly List<int> _visited = [];
    private readonly Dictionary<TableBlockView, Exactness> _exact = [];

    /// <summary>
    /// How many times one measure may settle rows again because a table in a cell changed height,
    /// before every table in the window is laid out in full instead.
    /// </summary>
    internal int RefreshLimit { get; init; } = 4;

    /// <summary>How much of a table in a cell has to be laid out, beyond what the window reaches.</summary>
    private enum Coverage
    {
        /// <summary>What the window reaches.</summary>
        Window,

        /// <summary>From its top down to <see cref="Exactness.Below"/>, to find out how tall it is.</summary>
        Probe,

        /// <summary>From its top down to the window's bottom, so it is placed by real heights.</summary>
        TopToWindow,

        /// <summary>Everything above the anchor inside it, so the anchor's depth is exact.</summary>
        Prefix,

        /// <summary>All of it.</summary>
        Full,
    }

    /// <summary>What a table in a cell must have laid out this measure.</summary>
    private readonly record struct Exactness(Coverage Kind, double Below = 0);

    /// <summary>
    /// Measures the window, then the tables inside its rows, until the rows holding them are
    /// settled against what those tables measured to.
    /// </summary>
    private void MeasureWindow()
    {
        _exact.Clear();

        for (var pass = 0; ; pass++)
        {
            Walk(_visited);

            if (!HostsTables(_visited))
            {
                break;
            }

            // The tables in the window are given it first: what is exact is decided from where
            // they are laid out for this window, not the last one.
            var changed = RefreshNested(_visited);

            changed |= Close();

            if (!changed)
            {
                break;
            }

            if (pass + 1 >= RefreshLimit)
            {
                // The windows did not settle. Every table the window reaches is laid out whole:
                // slow, and nothing it holds is left unlaid.
                foreach (var nested in NestedIn(_visited))
                {
                    if (_exact.GetValueOrDefault(nested).Kind != Coverage.Full)
                    {
                        Fallback(nested, $"the windows of tables in cells did not settle in {RefreshLimit} passes");
                    }
                }

                Walk(_visited);
                RefreshNested(_visited);
                Walk(_visited);

                break;
            }
        }

        CheckWindow();
    }

    /// <summary>
    /// Tells a table in a cell which part of it this table's window reaches, in its own
    /// coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owned: the anchor is inside it, so it gets the anchor itself and the same distances, and
    /// measures outward from the same content the presenter is about to put back. Above the anchor:
    /// from its bottom, the edge facing the anchor, up as far as the window's top. Otherwise: from
    /// its top down as far as the window's bottom. Every distance is clamped to be at least zero,
    /// and a coordinate that is not a number — which nothing should produce — lays it out whole.
    /// </para>
    /// <para>
    /// An infinite window — the presenter is not virtualizing — reaches all of it, which is no
    /// fallback.
    /// </para>
    /// </remarks>
    private void WindowNested(int row, Cell cell, Child child, TableBlockView nested)
    {
        var exact = _exact.GetValueOrDefault(nested);

        if (exact.Kind == Coverage.Full || double.IsPositiveInfinity(_windowBelow))
        {
            nested.SetWindow(0, 0, double.PositiveInfinity);

            return;
        }

        var start = _rowStarts[row] + child.Start;

        if (Owns(start, nested))
        {
            nested.SetWindow(
                _windowAnchor.Shift(-start),
                exact.Kind == Coverage.Prefix ? double.PositiveInfinity : _windowAbove,
                _windowBelow);

            return;
        }

        var anchor = AnchorY();
        var top = ChildOrigin(cell, child).Y;
        var height = nested.Size.Height;

        if (!double.IsFinite(anchor) || !double.IsFinite(top) || !double.IsFinite(height))
        {
            Fallback(nested, "a window coordinate was not a number");
            nested.SetWindow(0, 0, double.PositiveInfinity);

            return;
        }

        var band = _bandStart[row];
        var anchorBand = _bandStart[WindowRow];

        switch (exact.Kind)
        {
            case Coverage.Probe:
                nested.SetWindow(0, 0, Math.Max(0, exact.Below));
                break;
            case Coverage.TopToWindow:
                nested.SetWindow(0, 0, Math.Max(0, anchor + _windowBelow - top));
                break;
            default:
                if (band < anchorBand)
                {
                    // In a row above the anchor's: from its bottom up to the window's top. Its
                    // bottom is no lower than its row's, so the distance from the row's bottom
                    // is the most it can need — whatever it now measures to, which this row has
                    // not been settled against yet.
                    nested.SetWindow(height, Math.Max(0, _windowAbove - (anchor - RowTop(_bandEnd[row]))), 0);
                }
                else if (band > anchorBand)
                {
                    // In a row below the anchor's: from its top down to the window's bottom.
                    nested.SetWindow(0, 0, Math.Max(0, anchor + _windowBelow - top));
                }
                else if (top + height <= anchor)
                {
                    // Beside the anchor, wholly above it.
                    nested.SetWindow(height, Math.Max(0, _windowAbove - (anchor - (top + height))), 0);
                }
                else if (top >= anchor)
                {
                    // Beside the anchor, wholly below it.
                    nested.SetWindow(0, 0, Math.Max(0, anchor + _windowBelow - top));
                }
                else if (!HoldsContent())
                {
                    // The anchor point is inside it and no content is held — a row nobody had laid
                    // out was scrolled to — so it is measured outward from that point, as this
                    // table is.
                    nested.SetWindow(anchor - top, _windowAbove, _windowBelow);
                }
                else
                {
                    // Beside the content held at the top of the viewport, and reaching it: placed
                    // by real heights from its top, so nothing in it can move (owner decision Q1).
                    nested.SetWindow(0, 0, Math.Max(0, anchor + _windowBelow - top));
                }

                break;
        }
    }

    /// <summary>Whether the window's anchor names content, rather than a share of a row nobody laid out.</summary>
    private bool HoldsContent() =>
        !(_windowAnchor.Kind == AnchorKind.RowShare && _windowAnchor.Position == _rowStarts[WindowRow]);

    /// <summary>Whether the window's anchor is inside a table in a cell that starts at a position.</summary>
    private bool Owns(int start, TableBlockView nested)
    {
        var position = _windowAnchor.Position;

        // A box at the table's own first position is held from the table's edge, by this table;
        // anything else from there to its last position is inside it.
        return position >= start && position <= start + nested.ContentSize
            && !(position == start && _windowAnchor.Kind is AnchorKind.Box or AnchorKind.BoxBottom);
    }

    /// <summary>Where the window's anchor point is now, in this table's coordinates.</summary>
    private double AnchorY() =>
        AnchorPoint(_windowAnchor) is { } y && double.IsFinite(y) ? y : RowTop(WindowRow);

    /// <summary>
    /// Gives every table in the bands the window reached its part of the window, and settles again
    /// every band holding one whose height moved.
    /// </summary>
    /// <returns>Whether any band was settled again.</returns>
    private bool RefreshNested(List<int> bands)
    {
        var changed = false;

        foreach (var start in bands)
        {
            var moved = false;

            for (var r = start; r < _bandEnd[start]; r++)
            {
                foreach (var cell in _rows[r].Cells!)
                {
                    var inner = Math.Max(1, CellWidth(cell.Placement) - (2 * CellPadding));

                    foreach (var child in cell.Blocks)
                    {
                        if (child.View is not TableBlockView nested)
                        {
                            continue;
                        }

                        WindowNested(r, cell, child, nested);
                        WorkCounters.NestedTablesRefreshed++;

                        var height = nested.Measure(inner).Height;

                        if (!height.Equals(child.Height))
                        {
                            child.Height = height;
                            moved = true;
                        }
                    }
                }
            }

            if (moved)
            {
                SettleBand(start);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Makes exact every estimate that places content the viewport could show other than through
    /// the anchor, in the anchor's row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The anchor holds still what is at the top of the viewport and everything placed from it by
    /// real heights. Inside the anchor's row two things are placed across estimates that are not
    /// on that path, and the rest of the window is placed from real heights already (rows below
    /// the anchor's row hang from its bottom and measure their tables from the top; rows above it
    /// end above the viewport).
    /// </para>
    /// <para>
    /// <b>The row's top</b> is the anchor less the anchor's depth in the row — exact only if what is
    /// above the anchor in its cell is (an inner table's rows above its anchor, tables above it in
    /// the cell). Content in the other cells hangs from that top. A cell whose content provably
    /// ends above the window's top, using lower bounds for the depth, cannot be seen; one that is
    /// exactly measured and might reach the window makes the depth exact; one whose height is not
    /// known yet — an inner table not wholly laid out — is first measured from its top until it
    /// ends or until it is tall enough that it might reach the window, which also makes the depth
    /// exact, and is then laid out down to the window's bottom. A table that looked short by its
    /// estimate and holds one enormous row is found this way.
    /// </para>
    /// <para>
    /// <b>The row's bottom</b> is the tallest cell's. If it could be inside the window, every other
    /// cell's tables are laid out whole, so their heights are known; if the anchor's cell is then
    /// provably at least as tall as each of them, the bottom hangs from the anchor by real heights;
    /// otherwise the anchor's cell is laid out whole too.
    /// </para>
    /// <para>
    /// A band of rows that cells spanning rows tie together settles every row from every cell, so
    /// a band of more than one row holding a table not wholly laid out lays all of them out whole.
    /// </para>
    /// <para>
    /// Every decision only ever lays out more, so the passes end.
    /// </para>
    /// </remarks>
    /// <returns>Whether anything was made exact that was not.</returns>
    private bool Close()
    {
        if (!HoldsContent())
        {
            // No content is held — a row nobody has laid out — so there is nothing to keep still.
            return false;
        }

        var row = WindowRow;
        var start = _bandStart[row];
        var end = _bandEnd[row];
        var changed = false;

        if (!BandHostsTables(start))
        {
            return false;
        }

        if (end - start > 1)
        {
            foreach (var nested in NestedInBand(start))
            {
                if (!nested.IsComplete())
                {
                    changed |= Require(nested, new Exactness(Coverage.Full));
                }
            }

            return changed;
        }

        if (Locate(row, _windowAnchor.Position) is not { } at)
        {
            return false;
        }

        var (cell, anchorChild, rowStart) = at;
        var owner = anchorChild.View as TableBlockView;

        if (owner is not null && !Owns(rowStart + anchorChild.Start, owner))
        {
            owner = null;
        }

        // Alone in its row, the anchor places everything in the row below it from real heights,
        // and what is above it in its cell is above the viewport, placed upward from it: nothing
        // here moves on screen with an estimate. The bounds below walk tables in the cell, so they
        // are not worked out for nothing.
        if (_rows[row].Cells!.Length == 1)
        {
            return false;
        }

        // What the decisions below need of the anchor's depth in the row. Each bound walks a table
        // in the cell only as far as the question needs: a lower bound that has already passed a
        // threshold is as good as the whole one, and walking the whole of a long table on every
        // pass made scrolling through it cost its length on every page.
        var anchor = AnchorY();
        var into = owner is null ? Math.Max(0, anchor - ChildOrigin(cell, anchorChild).Y) : double.NaN;
        var others = _rows[row].Cells!.Where(other => !ReferenceEquals(other, cell)).ToArray();
        var uppers = others.Select(Upper).ToArray();

        // The row's bottom below the anchor, as far as the window's bottom.
        var below = (owner?.BelowAnchorLower(_windowBelow) ?? Math.Max(0, anchorChild.Height - into)) + CellPadding;
        var after = false;

        foreach (var child in cell.Blocks)
        {
            if (after)
            {
                below += Lower(child, _windowBelow);
            }

            after |= ReferenceEquals(child, anchorChild);
        }

        var reach = 0.0;

        foreach (var upper in uppers)
        {
            reach = Math.Max(reach, double.IsFinite(upper)
                ? upper + CellPadding + _windowAbove + Math.Max(0, (below < _windowBelow ? CellPadding - below : 0) - _windowAbove) + 1
                : double.PositiveInfinity);
        }

        if (owner is not null)
        {
            into = owner.AnchorDepthLower(reach);
        }

        var depth = CellPadding + into;

        foreach (var child in cell.Blocks)
        {
            if (ReferenceEquals(child, anchorChild))
            {
                break;
            }

            depth += Lower(child, reach);
        }

        // Whether all of it is exact, asked only when a decision turns on it: it walks everything
        // above the anchor, which is short whenever the depth is.
        bool? exactness = null;

        bool Exact() => exactness ??= (owner?.AnchorDepthExact() ?? true)
            && cell.Blocks.TakeWhile(child => !ReferenceEquals(child, anchorChild)).All(IsExact);

        // The other cells, hanging from the row's top.
        for (var i = 0; i < others.Length; i++)
        {
            var other = others[i];
            var upper = uppers[i];

            if (-depth + CellPadding + upper < -_windowAbove)
            {
                continue;
            }

            if (double.IsFinite(upper))
            {
                // Measured, and might be on screen: only the depth can move it, so the depth is
                // made exact.
                if (!Exact())
                {
                    changed |= MakeDepthExact(cell, anchorChild, owner);
                }

                continue;
            }

            var top = 0.0;

            foreach (var child in other.Blocks)
            {
                if (child.View is TableBlockView nested && !nested.IsComplete())
                {
                    var now = _exact.GetValueOrDefault(nested);

                    if (now.Kind is Coverage.Window)
                    {
                        // As tall as it can be and still end above the window, from its top.
                        changed |= Require(nested, new Exactness(Coverage.Probe, depth - CellPadding - top - _windowAbove));
                    }
                    else if (now.Kind is Coverage.Probe)
                    {
                        // Still not all laid out at that height: it might reach the window.
                        changed |= Require(nested, new Exactness(Coverage.TopToWindow));

                        if (!Exact())
                        {
                            changed |= MakeDepthExact(cell, anchorChild, owner);
                        }
                    }
                }

                top += Lower(child, depth);
            }
        }

        // The row's bottom, if it could be inside the window.
        if (below >= _windowBelow)
        {
            return changed;
        }

        var robust = true;

        foreach (var other in others)
        {
            foreach (var child in other.Blocks)
            {
                if (child.View is TableBlockView nested && !nested.IsComplete())
                {
                    changed |= Require(nested, new Exactness(Coverage.Full));
                }
            }

            robust &= -depth + CellPadding + Upper(other) + CellPadding <= below;
        }

        // Every other cell is now known; if one could still be the tallest, the anchor's cell
        // decides nothing on its own and is laid out whole too.
        if (!robust && !changed)
        {
            changed |= MakeDepthExact(cell, anchorChild, owner);

            if (owner is not null)
            {
                changed |= Require(owner, new Exactness(Coverage.Full));
            }

            foreach (var child in cell.Blocks)
            {
                if (child.View is TableBlockView nested && !nested.IsComplete())
                {
                    changed |= Require(nested, new Exactness(Coverage.Full));
                }
            }
        }

        return changed;
    }

    /// <summary>Lays out everything above the anchor in its cell, so the anchor's depth is exact.</summary>
    private bool MakeDepthExact(Cell cell, Child anchorChild, TableBlockView? owner)
    {
        var changed = false;

        foreach (var child in cell.Blocks)
        {
            if (ReferenceEquals(child, anchorChild))
            {
                break;
            }

            if (child.View is TableBlockView nested && !nested.IsComplete())
            {
                changed |= Require(nested, new Exactness(Coverage.Full));
            }
        }

        if (owner is not null && _exact.GetValueOrDefault(owner).Kind is not (Coverage.Prefix or Coverage.Full))
        {
            changed |= Require(owner, new Exactness(Coverage.Prefix));
        }

        return changed;
    }

    /// <summary>Raises what a table in a cell must have laid out, never lowering it.</summary>
    private bool Require(TableBlockView nested, Exactness wanted)
    {
        var now = _exact.GetValueOrDefault(nested);

        if (now.Kind >= wanted.Kind && !(now.Kind == Coverage.Probe && wanted.Kind == Coverage.Probe && wanted.Below > now.Below))
        {
            return false;
        }

        _exact[nested] = wanted;

        return true;
    }

    /// <summary>Lays a table in a cell out whole because its window could not be worked out, and says so.</summary>
    private void Fallback(TableBlockView nested, string reason)
    {
        if (_exact.GetValueOrDefault(nested).Kind == Coverage.Full)
        {
            return;
        }

        _exact[nested] = new Exactness(Coverage.Full);
        WorkCounters.NestedWindowFallbacks++;
        Trace.TraceWarning(
            "VellumText: a table in a cell was laid out in full ({0} rows): {1}.",
            nested.RowCount,
            reason);
    }

    /// <summary>
    /// A lower bound on a block's height, exact unless it is a table not wholly laid out, worked
    /// out only until it reaches <paramref name="cap"/>.
    /// </summary>
    private static double Lower(Child child, double cap) =>
        child.View is TableBlockView nested ? nested.LowerHeight(cap) : child.Height;

    /// <summary>Whether a block's height is known exactly.</summary>
    private static bool IsExact(Child child) => child.View is not TableBlockView nested || nested.IsComplete();

    /// <summary>An upper bound on a cell's content height: infinite while a table in it is not wholly laid out.</summary>
    private static double Upper(Cell cell)
    {
        var total = 0.0;

        foreach (var child in cell.Blocks)
        {
            if (!IsExact(child))
            {
                return double.PositiveInfinity;
            }

            total += child.Height;
        }

        return total;
    }

    /// <summary>
    /// A lower bound on this table's height: the bands laid out exactly at what they measured, and
    /// any other band at the least a band can be, its padding.
    /// </summary>
    /// <remarks>
    /// Not a row's least height: a row under a cell spanning rows can be no height at all. Added
    /// up only until it reaches <paramref name="cap"/>, which is all a caller comparing it with a
    /// threshold needs.
    /// </remarks>
    /// <param name="cap">Where to stop adding.</param>
    internal double LowerHeight(double cap = double.PositiveInfinity)
    {
        var total = 0.0;

        for (var start = 0; start < _rows.Length && total < cap; start = _bandEnd[start])
        {
            total += BandLower(start);
        }

        return total;
    }

    private double BandLower(int start)
    {
        if (!BandComplete(start))
        {
            return 2 * CellPadding;
        }

        var total = 0.0;

        for (var r = start; r < _bandEnd[start]; r++)
        {
            total += _rowHeights[r];
        }

        return total;
    }

    /// <summary>Whether every row is laid out at the current width, and so is every table in them.</summary>
    internal bool IsComplete()
    {
        // Every measured row is in the estimate, so fewer of them than rows is a row unmeasured,
        // known without walking the table.
        if (double.IsNaN(_width) || _estimate.Count < _rows.Length)
        {
            return false;
        }

        for (var start = 0; start < _rows.Length; start = _bandEnd[start])
        {
            if (!BandComplete(start))
            {
                return false;
            }
        }

        return true;
    }

    private bool BandComplete(int start)
    {
        WorkCounters.NestedBoundBands++;

        if (!IsLaidOut(start))
        {
            return false;
        }

        for (var r = start; r < _bandEnd[start]; r++)
        {
            foreach (var cell in _rows[r].Cells!)
            {
                foreach (var child in cell.Blocks)
                {
                    if (child.View is TableBlockView nested && !nested.IsComplete())
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>A lower bound on how far below this table's top its window's anchor point is.</summary>
    /// <remarks>
    /// The anchor's own band first, then the bands above it nearest first, until the bound reaches
    /// <paramref name="cap"/>: near the anchor the rows are laid out, so a threshold a decision
    /// turns on is passed within a screenful, however far down a long table the anchor is.
    /// </remarks>
    /// <param name="cap">Where to stop adding.</param>
    internal double AnchorDepthLower(double cap = double.PositiveInfinity)
    {
        if (_rows.Length == 0)
        {
            return 0;
        }

        var row = WindowRow;
        var start = _bandStart[row];
        var total = 0.0;

        if (BandComplete(start))
        {
            total = Math.Max(0, AnchorY() - RowTop(start));
        }
        else if (start == row && Locate(row, _windowAnchor.Position) is { } at)
        {
            var (cell, anchorChild, rowStart) = at;

            total = CellPadding;

            foreach (var child in cell.Blocks)
            {
                if (ReferenceEquals(child, anchorChild))
                {
                    break;
                }

                total += Lower(child, cap);
            }

            total += anchorChild.View is TableBlockView owner && Owns(rowStart + anchorChild.Start, owner)
                ? owner.AnchorDepthLower(cap)
                : Math.Max(0, AnchorY() - ChildOrigin(cell, anchorChild).Y);
        }

        for (var b = start - 1; b >= 0 && total < cap; b = _bandStart[b] - 1)
        {
            total += BandLower(_bandStart[b]);
        }

        return total;
    }

    /// <summary>Whether everything above the window's anchor point in this table is laid out exactly.</summary>
    internal bool AnchorDepthExact()
    {
        if (_rows.Length == 0)
        {
            return true;
        }

        var row = WindowRow;
        var start = _bandStart[row];

        for (var b = 0; b < start; b = _bandEnd[b])
        {
            if (!BandComplete(b))
            {
                return false;
            }
        }

        if (BandComplete(start))
        {
            return true;
        }

        if (start != row || Locate(row, _windowAnchor.Position) is not { } at)
        {
            return false;
        }

        var (cell, anchorChild, rowStart) = at;

        foreach (var child in cell.Blocks)
        {
            if (ReferenceEquals(child, anchorChild))
            {
                break;
            }

            if (!IsExact(child))
            {
                return false;
            }
        }

        return anchorChild.View is not TableBlockView owner
            || !Owns(rowStart + anchorChild.Start, owner)
            || owner.AnchorDepthExact();
    }

    /// <summary>A lower bound on how far below the window's anchor point this table's bottom is.</summary>
    /// <remarks>The anchor's band, then the bands below it nearest first, until it reaches <paramref name="cap"/>.</remarks>
    /// <param name="cap">Where to stop adding.</param>
    internal double BelowAnchorLower(double cap = double.PositiveInfinity)
    {
        if (_rows.Length == 0)
        {
            return 0;
        }

        var row = WindowRow;
        var start = _bandStart[row];
        var total = 0.0;

        if (BandComplete(start))
        {
            total = Math.Max(0, RowTop(_bandEnd[row]) - AnchorY());
        }
        else if (Locate(row, _windowAnchor.Position) is { } at)
        {
            var (cell, anchorChild, rowStart) = at;

            total = (anchorChild.View is TableBlockView owner && Owns(rowStart + anchorChild.Start, owner)
                ? owner.BelowAnchorLower(cap)
                : Math.Max(0, ChildOrigin(cell, anchorChild).Y + anchorChild.Height - AnchorY())) + CellPadding;

            var after = false;

            foreach (var child in cell.Blocks)
            {
                if (after)
                {
                    total += Lower(child, cap);
                }

                after |= ReferenceEquals(child, anchorChild);
            }
        }

        for (var b = _bandEnd[row]; b < _rows.Length && total < cap; b = _bandEnd[b])
        {
            total += BandLower(b);
        }

        return total;
    }

    /// <summary>Whether any row of the bands holds a table in a cell.</summary>
    private bool HostsTables(List<int> bands)
    {
        foreach (var start in bands)
        {
            if (BandHostsTables(start))
            {
                return true;
            }
        }

        return false;
    }

    private bool BandHostsTables(int start)
    {
        for (var r = start; r < _bandEnd[start]; r++)
        {
            if (_rows[r].Cells is not { } cells)
            {
                continue;
            }

            foreach (var cell in cells)
            {
                foreach (var child in cell.Blocks)
                {
                    if (child.View is TableBlockView)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private IEnumerable<TableBlockView> NestedIn(List<int> bands) => bands.SelectMany(NestedInBand);

    private IEnumerable<TableBlockView> NestedInBand(int start)
    {
        for (var r = start; r < _bandEnd[start]; r++)
        {
            if (_rows[r].Cells is not { } cells)
            {
                continue;
            }

            foreach (var cell in cells)
            {
                foreach (var child in cell.Blocks)
                {
                    if (child.View is TableBlockView nested)
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    /// <summary>
    /// In debug builds: every row the window meets is laid out, in this table and the tables in
    /// it, and every row holding a table was settled against what that table measured to.
    /// </summary>
    [Conditional("DEBUG")]
    private void CheckWindow()
    {
        if (double.IsNaN(_width) || _rows.Length == 0)
        {
            return;
        }

        var anchor = AnchorY();

        Invariant.That(
            Covers(anchor - _windowAbove, anchor + _windowBelow),
            "every row a table's window meets is laid out, in it and in the tables in its cells");

        foreach (var start in _visited)
        {
            for (var r = start; r < _bandEnd[start]; r++)
            {
                foreach (var cell in _rows[r].Cells!)
                {
                    foreach (var child in cell.Blocks)
                    {
                        Invariant.That(
                            child.View is not TableBlockView nested || nested.Size.Height.Equals(child.Height),
                            "a row holding a table was settled against the height that table has");
                    }
                }
            }
        }
    }
}
