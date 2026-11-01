namespace VellumText.Avalonia;

/// <summary>
/// The vertical position of every block in a document, whether or not it has been measured,
/// per architecture 4.6.
/// </summary>
/// <remarks>
/// <para>
/// Virtualization needs two answers that are cheap only if something keeps them: where does
/// block <c>i</c> start, and which block covers pixel <c>y</c>. Both are prefix sums over block
/// heights, so a Fenwick tree over the heights answers each in logarithmic time. Updating a
/// height re-sums its logarithmically many ancestors from their logarithmically many children,
/// avoiding edit-history roundoff without walking the prefix. A running array would answer the first in constant time and the second
/// in logarithmic, but would cost linear time on every measurement, which is the operation
/// that happens most.
/// </para>
/// <para>
/// The subtlety is not the tree, it is the blocks nobody has measured. Their height has to be
/// <em>guessed</em>, because scrolling to the end of a document may not measure everything on
/// the way. The guess used here is the mean of the heights actually measured so far, with any one block's share bounded (see <see cref="Estimate"/>), which
/// starts as a caller-supplied estimate and improves as the user scrolls. That means a block's
/// stored height changes when its neighbours are measured, and the total moves under the
/// scrollbar — unavoidable with estimates, and the reason
/// <see cref="Measured"/> is exposed so a caller can tell a real answer from a guess.
/// </para>
/// <para>
/// <b>The estimate is never stored.</b> An earlier version wrote the current estimate into every
/// unmeasured block whenever it moved, so that one tree of heights answered everything — correct,
/// and linear in the document on the operation that happens most. Instead there are two trees
/// over the same indices: measured heights, and the number of unmeasured blocks. A prefix sum is
/// then <c>measured + unmeasured × estimate</c>, so the estimate moving costs nothing at all and
/// a measurement touches only the changed height's ancestors and their child nodes.
/// </para>
/// </remarks>
internal sealed class BlockHeightIndex
{
    private readonly HeightPrefixIndex _prefix = new();
    private double[] _height = [];
    private double[] _sample = [];
    private bool[] _measured = [];
    private int _count;
    private readonly HeightEstimate _estimate;

    /// <summary>Creates an index with no blocks.</summary>
    /// <param name="estimate">
    /// The height to assume for a block before anything has been measured. Must be positive:
    /// a zero estimate would put every unmeasured block at the same pixel and make
    /// <see cref="IndexAt"/> meaningless.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="estimate"/> is not positive and finite.</exception>
    public BlockHeightIndex(double estimate = DefaultEstimate)
    {
        if (!(estimate > 0) || double.IsInfinity(estimate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimate), estimate, "An estimated height must be positive and finite.");
        }

        _estimate = new HeightEstimate(estimate);
    }

    /// <summary>The height assumed for a block before any block has been measured.</summary>
    public const double DefaultEstimate = 20;

    /// <summary>The number of blocks.</summary>
    public int Count => _count;

    /// <summary>The height currently assumed for a block nobody has measured.</summary>
    /// <remarks>
    /// Drawn from the measured heights with any one block's share bounded, so one giant
    /// paragraph cannot price all the others like itself; see <see cref="HeightEstimate"/>.
    /// </remarks>
    public double Estimate => _estimate.Value;
    /// <summary>The height of every block together, measured and estimated alike.</summary>
    public double TotalHeight => SumTo(_count);

    /// <summary>Whether a block's height is a measurement rather than an estimate.</summary>
    /// <param name="index">The block's index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a block.</exception>
    public bool Measured(int index)
    {
        CheckIndex(index);

        return _measured[index];
    }

    /// <summary>The height of one block.</summary>
    /// <param name="index">The block's index.</param>
    /// <remarks>The measured height, or the current estimate for a block nobody has measured.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a block.</exception>
    public double HeightOf(int index)
    {
        CheckIndex(index);

        return _measured[index] ? _height[index] : Estimate;
    }

    /// <summary>The distance from the top of the document to the top of a block.</summary>
    /// <param name="index">The block's index. <see cref="Count"/> is allowed and gives the total.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public double OffsetOf(int index)
    {
        if (index < 0 || index > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Not a block in this index.");
        }

        return SumTo(index);
    }

    /// <summary>
    /// The block covering a vertical position, clamped to the document.
    /// </summary>
    /// <remarks>
    /// The block whose half-open range <c>[offset, offset + height)</c> contains
    /// <paramref name="y"/>. A position past the end reports the last block rather than
    /// nothing, because a drag below the document selects to its end rather than to nowhere.
    /// </remarks>
    /// <param name="y">The distance from the top of the document.</param>
    /// <exception cref="InvalidOperationException">There are no blocks.</exception>
    public int IndexAt(double y)
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("An empty index covers no position.");
        }

        return _prefix.IndexAt(y, Estimate);
    }

    /// <summary>
    /// Records a block's measured height, which is all known: what the estimate learns from it.
    /// </summary>
    /// <param name="index">The block's index.</param>
    /// <param name="height">The measured height.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not a block, or <paramref name="height"/> is negative or not finite.
    /// </exception>
    public void SetHeight(int index, double height) => SetHeight(index, height, height);

    /// <summary>
    /// Records a block's measured height, and how much of it is known: what the estimate for the
    /// blocks nobody measured learns from it.
    /// </summary>
    /// <remarks>
    /// A table at the top of the document lays out only what the window reaches, and is as tall as
    /// its own estimate says of the rest. Learning from that height, every block nobody measured was
    /// priced like the table's estimate — a document 99 times its height, fuzz seed 10,045 — as a
    /// row learning from a table in its cell priced every row like it, before rows learned only what
    /// is known of the tables in them (<c>TableBlockView.ExactHeight</c>).
    /// </remarks>
    /// <param name="index">The block's index.</param>
    /// <param name="height">The measured height, where the block is placed.</param>
    /// <param name="sample">How much of it is known; no more than the height.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not a block, or a height is negative or not finite.
    /// </exception>
    public void SetHeight(int index, double height, double sample)
    {
        CheckIndex(index);

        if (height < 0 || double.IsNaN(height) || double.IsInfinity(height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(height), height, "A measured height must be zero or more, and finite.");
        }

        if (sample < 0 || double.IsNaN(sample) || double.IsInfinity(sample))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sample), sample, "What is known of a height must be zero or more, and finite.");
        }

        if (_measured[index])
        {
            _estimate.Remove(_sample[index]);
        }

        _prefix.SetHeight(index, _measured[index] ? _height[index] : double.NaN, height);
        _measured[index] = true;

        _estimate.Add(sample);
        _height[index] = height;
        _sample[index] = sample;

        // No rewrite of the other blocks. They never held the estimate, so the estimate moving
        // is already reflected everywhere it is used - which is what makes this indexed
        // rather than linear in the document.
    }

    /// <summary>Inserts blocks before an index, all of them unmeasured.</summary>
    /// <param name="index">Where the first new block goes. <see cref="Count"/> appends.</param>
    /// <param name="count">How many to insert.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is out of range or the count is negative.</exception>
    /// <remarks>
    /// In place, into spare room that doubles when it runs out. Enter inserts a block, and this
    /// used to allocate all four arrays afresh for it — about a megabyte at 50,000 blocks on every
    /// paragraph written. The tree is still rebuilt, because every node after the insertion
    /// covers different blocks now; that is linear, like the edit that caused it, but allocates
    /// nothing.
    /// </remarks>
    public void Insert(int index, int count)
    {
        if (index < 0 || index > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Not a place in this index.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0)
        {
            return;
        }

        EnsureCapacity(_count + count);

        Array.Copy(_height, index, _height, index + count, _count - index);
        Array.Copy(_sample, index, _sample, index + count, _count - index);
        Array.Copy(_measured, index, _measured, index + count, _count - index);

        var estimate = Estimate;

        for (var i = index; i < index + count; i++)
        {
            _height[i] = estimate;
            _measured[i] = false;
        }

        _count += count;
        RebuildTree();
    }

    /// <summary>Removes blocks.</summary>
    /// <param name="index">The first block to remove.</param>
    /// <param name="count">How many to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside the index.</exception>
    /// <remarks>In place, for the same reason as <see cref="Insert"/>.</remarks>
    public void Remove(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (index < 0 || index + count > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Not a range in this index.");
        }

        if (count == 0)
        {
            return;
        }

        for (var i = index; i < index + count; i++)
        {
            if (_measured[i])
            {
                _estimate.Remove(_sample[i]);
            }
        }

        Array.Copy(_height, index + count, _height, index, _count - index - count);
        Array.Copy(_sample, index + count, _sample, index, _count - index - count);
        Array.Copy(_measured, index + count, _measured, index, _count - index - count);

        _count -= count;
        RebuildTree();
    }

    /// <summary>Sets the number of blocks, forgetting every measurement.</summary>
    /// <remarks>
    /// For a document replaced outright, where nothing is known to correspond. The estimate
    /// learned so far is kept as the seed, because it is still the best guess available about
    /// how tall a block in this control tends to be.
    /// </remarks>
    /// <param name="count">The number of blocks.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public void Reset(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _estimate.Restart();

        EnsureCapacity(count);

        Array.Fill(_height, _estimate.Seed, 0, count);
        Array.Clear(_measured, 0, count);

        _count = count;
        RebuildTree();
    }

    /// <summary>Makes room for at least <paramref name="count"/> blocks, keeping the ones there are.</summary>
    private void EnsureCapacity(int count)
    {
        if (_height.Length >= count)
        {
            return;
        }

        var capacity = Math.Max(count, _height.Length * 2);
        var heights = new double[capacity];
        var samples = new double[capacity];
        var measured = new bool[capacity];

        Array.Copy(_height, heights, _count);
        Array.Copy(_sample, samples, _count);
        Array.Copy(_measured, measured, _count);

        _height = heights;
        _sample = samples;
        _measured = measured;
    }

    /// <summary>Builds both trees from the first <see cref="Count"/> heights.</summary>
    private void RebuildTree() => _prefix.Rebuild(_height.AsSpan(0, _count), _measured.AsSpan(0, _count));

    /// <summary>The height of the first <paramref name="count"/> blocks.</summary>
    /// <remarks>
    /// Measured heights are summed; unmeasured blocks are counted and priced at the current
    /// estimate. Keeping the two apart is what lets the estimate move without touching anything.
    /// </remarks>
    private double SumTo(int count) => _prefix.OffsetOf(count, Estimate);

    private void CheckIndex(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Not a block in this index.");
        }
    }

}
