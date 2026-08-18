using System.Collections.Immutable;

namespace VellumText;

/// <summary>
/// Which side of a position content is considered to belong to when an edit lands exactly on it.
/// </summary>
/// <remarks>
/// A caret sitting between two characters has to pick a side when text is inserted at exactly
/// that spot: either it stays put and the new text appears after it, or it moves along and the
/// new text appears before it. Neither is universally right, so callers say which they mean.
/// </remarks>
public enum Assoc
{
    /// <summary>Bind to the content before the position; insertions at it push content to the right.</summary>
    Before = -1,

    /// <summary>Bind to the content after the position; insertions at it move the position along.</summary>
    After = 1,
}

/// <summary>The outcome of mapping a position through an edit.</summary>
/// <param name="Pos">The position in the new document.</param>
/// <param name="Deleted">
/// Whether the content this position was bound to no longer exists. The position is still
/// usable — it collapses to the edge of the replaced range — but a caller that cares about
/// identity rather than location should notice.
/// </param>
public readonly record struct MapResult(int Pos, bool Deleted);

/// <summary>One stretch of the document that a step replaced.</summary>
/// <param name="Start">Where the replaced range begins, in the document before the step.</param>
/// <param name="OldSize">How many positions it covered before.</param>
/// <param name="NewSize">How many it covers after.</param>
public readonly record struct ReplacedRange(int Start, int OldSize, int NewSize)
{
    /// <summary>How much longer this range made the document. Negative for a deletion.</summary>
    public int SizeDelta => NewSize - OldSize;

    /// <summary>One past the last position the range covered, before the step.</summary>
    public int End => Start + OldSize;

    /// <inheritdoc/>
    public override string ToString() => $"[{Start}, +{OldSize}) -> +{NewSize}";
}

/// <summary>
/// How one step moved positions: the ranges of the document it replaced.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole reason positions are flat integers. Rewriting a stale position after an
/// edit is arithmetic on a handful of ranges rather than a walk of the tree, which is what makes
/// it affordable to route every externally-held position — selections, link anchors, later
/// collaborative cursors — through the mapping on every keystroke.
/// </para>
/// <para>
/// Almost every step replaces exactly one range, and that case is stored inline, so a keystroke
/// still allocates nothing to describe itself. Several ranges are what the edits that reach into
/// a table need: inserting a column adds a cell to every row, and merging cells takes content out
/// of several and puts it in one. Folding those into a single range spanning the whole table
/// would be sound arithmetic and a poor answer, because every position in every untouched row
/// would then collapse onto the table's edge.
/// </para>
/// <para>
/// The ranges are sorted, and no two of them touch: ranges that abut are merged when the map is
/// built, which is why a position can never be claimed by two of them and <see cref="Map"/> needs
/// no tie-break.
/// </para>
/// <para>
/// A step that does not move anything, such as applying a mark, has an <see cref="IsIdentity"/>
/// map. Recording it anyway keeps a mapping's map count equal to its step count, so a step's map
/// can always be found by index.
/// </para>
/// </remarks>
public readonly record struct StepMap
{
    private readonly ReplacedRange _first;

    // Default - and so unallocated - for the one-range case, which is every step on the edit path.
    private readonly ImmutableArray<ReplacedRange> _rest;

    /// <summary>Creates a map for a range replacement.</summary>
    /// <param name="start">Where the replaced range begins.</param>
    /// <param name="oldSize">How many positions the range covered before.</param>
    /// <param name="newSize">How many positions it covers after.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A value is negative, or the range described could not fit in a document.
    /// </exception>
    public StepMap(int start, int oldSize, int newSize)
    {
        var range = new ReplacedRange(start, oldSize, newSize);

        Check(range, 0, nameof(newSize));

        _first = range;
        _rest = default;
    }

    private StepMap(ReplacedRange first, ImmutableArray<ReplacedRange> rest)
    {
        _first = first;
        _rest = rest;
    }

    /// <summary>A map for a step that moves nothing.</summary>
    public static StepMap Identity { get; }

    /// <summary>
    /// Creates a map over several replaced ranges.
    /// </summary>
    /// <param name="ranges">The ranges, in ascending order of <see cref="ReplacedRange.Start"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ranges"/> is null.</exception>
    /// <exception cref="ArgumentException">Two ranges overlap, or they are out of order.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A range has a negative value, or the ranges together could not fit in a document.
    /// </exception>
    /// <remarks>
    /// Ranges that touch are merged, so a map's ranges are always strictly separated. Two adjacent
    /// replacements and the one replacement covering both move everything outside them
    /// identically; merging picks one of the two spellings, so the position exactly between them
    /// has one answer rather than two that disagree about which range deleted it.
    /// </remarks>
    public static StepMap Of(IEnumerable<ReplacedRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var merged = new List<ReplacedRange>();
        var delta = 0;

        foreach (var range in ranges)
        {
            Check(range, delta, nameof(ranges));

            if (merged.Count > 0)
            {
                var last = merged[^1];

                if (range.Start < last.End)
                {
                    throw new ArgumentException(
                        $"Range {range} overlaps or precedes {last}. A map's ranges are positions "
                        + "in one document, so no two of them may describe the same position.",
                        nameof(ranges));
                }

                if (range.Start == last.End)
                {
                    merged[^1] = new ReplacedRange(
                        last.Start, last.OldSize + range.OldSize, last.NewSize + range.NewSize);
                    delta += range.SizeDelta;

                    continue;
                }
            }

            merged.Add(range);
            delta += range.SizeDelta;
        }

        return merged.Count switch
        {
            0 => Identity,
            1 => new StepMap(merged[0], default),
            _ => new StepMap(merged[0], [.. merged.Skip(1)]),
        };
    }

    /// <summary>
    /// How many ranges this map covers. Never zero: a map that moves nothing has one empty range.
    /// </summary>
    public int RangeCount => _rest.IsDefaultOrEmpty ? 1 : _rest.Length + 1;

    /// <summary>One of the replaced ranges.</summary>
    /// <param name="index">Its index, in ascending order of start position.</param>
    /// <exception cref="ArgumentOutOfRangeException">There is no range with that index.</exception>
    public ReplacedRange RangeAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, RangeCount);

        return index == 0 ? _first : _rest[index - 1];
    }

    /// <summary>Whether this map leaves every position where it was.</summary>
    public bool IsIdentity
    {
        get
        {
            var count = RangeCount;

            for (var i = 0; i < count; i++)
            {
                var range = RangeAt(i);

                if (range.OldSize != 0 || range.NewSize != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>How much longer the document got. Negative for a deletion.</summary>
    public int SizeDelta
    {
        get
        {
            var count = RangeCount;
            var delta = 0;

            for (var i = 0; i < count; i++)
            {
                delta += RangeAt(i).SizeDelta;
            }

            return delta;
        }
    }

    /// <summary>Rewrites a position for the document after the edit.</summary>
    /// <param name="pos">A position in the document before the edit.</param>
    /// <param name="assoc">Which side of the position to bind to.</param>
    public int Map(int pos, Assoc assoc = Assoc.After) => MapWithResult(pos, assoc).Pos;

    /// <summary>
    /// Rewrites a position, also reporting whether what it pointed at survived.
    /// </summary>
    /// <param name="pos">A position in the document before the edit.</param>
    /// <param name="assoc">Which side of the position to bind to.</param>
    public MapResult MapWithResult(int pos, Assoc assoc = Assoc.After)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pos);

        var count = RangeCount;
        var delta = 0;

        for (var i = 0; i < count; i++)
        {
            var range = RangeAt(i);

            if (pos < range.Start)
            {
                return new MapResult(pos + delta, false);
            }

            if (pos <= range.End)
            {
                // The position is inside the replaced range, so it has to collapse to one edge of
                // the replacement. At the range's own edges the answer is forced regardless of
                // what the caller asked for: a position before the range cannot be pushed past the
                // new content, and one after it cannot be pulled in front of it. Only a pure
                // insertion, where both edges coincide, is genuinely the caller's choice.
                var side = range.OldSize == 0
                    ? assoc
                    : pos == range.Start ? Assoc.Before
                    : pos == range.End ? Assoc.After
                    : assoc;

                var mapped = side == Assoc.Before
                    ? range.Start + delta
                    : range.Start + delta + range.NewSize;

                var deleted = assoc == Assoc.Before ? pos != range.Start : pos != range.End;

                return new MapResult(mapped, deleted);
            }

            delta += range.SizeDelta;
        }

        return new MapResult(pos + delta, false);
    }

    /// <summary>The map that undoes this one.</summary>
    /// <remarks>
    /// Each range swaps its two sizes and moves to where this map left it, because the inverse is
    /// read against the document this map produced. The ranges keep their order and stay
    /// separated: a range's new start is its old start shifted by everything before it, which is
    /// the same shift the range after it gets.
    /// </remarks>
    public StepMap Invert()
    {
        var count = RangeCount;

        if (count == 1)
        {
            return new StepMap(_first.Start, _first.NewSize, _first.OldSize);
        }

        var inverted = new ReplacedRange[count];
        var delta = 0;

        for (var i = 0; i < count; i++)
        {
            var range = RangeAt(i);

            inverted[i] = new ReplacedRange(range.Start + delta, range.NewSize, range.OldSize);
            delta += range.SizeDelta;
        }

        return new StepMap(inverted[0], [.. inverted.Skip(1)]);
    }

    /// <summary>Whether two maps move every position the same way.</summary>
    /// <param name="other">The map to compare against.</param>
    /// <remarks>
    /// Written out rather than left to the compiler, which compares the extra ranges by array
    /// reference and would report two maps built separately from the same ranges as different.
    /// </remarks>
    public bool Equals(StepMap other)
    {
        var count = RangeCount;

        if (count != other.RangeCount)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (RangeAt(i) != other.RangeAt(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        var count = RangeCount;

        for (var i = 0; i < count; i++)
        {
            hash.Add(RangeAt(i));
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (IsIdentity)
        {
            return "identity";
        }

        var count = RangeCount;

        if (count == 1)
        {
            return _first.ToString();
        }

        var text = new System.Text.StringBuilder();

        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            text.Append(RangeAt(i));
        }

        return text.ToString();
    }

    /// <summary>
    /// Refuses a range that could not describe a real document.
    /// </summary>
    /// <param name="range">The range to check.</param>
    /// <param name="delta">How far the ranges before it have already moved the document.</param>
    /// <param name="parameterName">The parameter to blame.</param>
    /// <remarks>
    /// Mapping is addition, and it runs on every keystroke for every held position, so it cannot
    /// afford checked arithmetic. Refusing a map that could overflow costs nothing here and is the
    /// difference between a position landing past the end of the document and one landing at a
    /// large negative number, which reads as valid to everything.
    /// </remarks>
    private static void Check(ReplacedRange range, int delta, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(range.Start, parameterName);
        ArgumentOutOfRangeException.ThrowIfNegative(range.OldSize, parameterName);
        ArgumentOutOfRangeException.ThrowIfNegative(range.NewSize, parameterName);

        if ((long)range.Start + range.OldSize > int.MaxValue
            || (long)range.Start + delta + range.NewSize > int.MaxValue
            || (long)range.Start + delta < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                range,
                "A step map cannot describe a range that runs past the end of the position space.");
        }
    }
}
