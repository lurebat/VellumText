namespace VellumText.Avalonia;

/// <summary>
/// Measured height prefixes and counts of unmeasured entries, priced only when read.
/// </summary>
internal sealed class HeightPrefixIndex
{
    private double[] _tree = [0];
    private int[] _pending = [0];
    private double[] _height = [];
    private int _count;

    /// <summary>Builds both Fenwick trees in linear time.</summary>
    internal void Rebuild(ReadOnlySpan<double> heights, ReadOnlySpan<bool> measured)
    {
        Reset(heights.Length);

        for (var i = 1; i <= _count; i++)
        {
            _height[i - 1] = measured[i - 1] ? heights[i - 1] : 0;
            _pending[i] = measured[i - 1] ? 0 : 1;
        }

        for (var i = 1; i <= _count; i++)
        {
            SumNode(i);

            var parent = i + (i & -i);

            if (parent <= _count)
            {
                _pending[parent] += _pending[i];
            }
        }
    }

    /// <summary>Forgets all measurements.</summary>
    internal void Reset(int count)
    {
        _count = count;

        if (_tree.Length <= count)
        {
            var capacity = Math.Max(count + 1, _tree.Length * 2);

            _tree = new double[capacity];
            _pending = new int[capacity];
            _height = new double[capacity - 1];
        }
        else
        {
            Array.Clear(_tree, 0, count + 1);
            Array.Clear(_height, 0, count);
        }

        for (var i = 1; i <= count; i++)
        {
            _pending[i] = i & -i;
        }
    }

    /// <summary>Replaces a measured height, or a NaN standing for an unmeasured entry.</summary>
    internal void SetHeight(int index, double previous, double height)
    {
        var pending = double.IsNaN(previous) ? -1 : 0;

        if (pending == 0 && height.Equals(previous))
        {
            return;
        }

        _height[index] = height;

        for (var i = index + 1; i <= _count; i += i & -i)
        {
            WorkCounters.HeightPrefixSteps++;
            SumNode(i);
            _pending[i] += pending;
        }
    }

    // Recompute from unchanged child nodes rather than adding a height delta: subtracting a
    // formerly tall row leaves rounding behind, so identical final heights otherwise produce
    // different geometry depending on the edit history. Rebuild uses exactly the same grouping.
    private void SumNode(int index)
    {
        var sum = _height[index - 1];
        var first = index - (index & -index);

        for (var child = index - 1; child > first; child -= child & -child)
        {
            WorkCounters.HeightPrefixSteps++;
            sum += _tree[child];
        }

        _tree[index] = sum;
    }

    /// <summary>The height of the first count entries at the caller's current estimate.</summary>
    internal double OffsetOf(int count, double estimate)
    {
        var sum = 0d;
        var pending = 0;

        for (var i = count; i > 0; i -= i & -i)
        {
            WorkCounters.HeightPrefixSteps++;
            sum += _tree[i];
            pending += _pending[i];
        }

        return sum + (pending * estimate);
    }

    /// <summary>The entry containing y, clamped to a nonempty index.</summary>
    internal int IndexAt(double y, double estimate)
    {
        if (!(y > 0))
        {
            return 0;
        }

        var power = 1;

        while (power << 1 < _count + 1)
        {
            power <<= 1;
        }

        var index = 0;
        var remaining = y;

        for (var step = power; step > 0; step >>= 1)
        {
            var next = index + step;

            if (next > _count)
            {
                continue;
            }

            WorkCounters.HeightPrefixSteps++;
            var covered = _tree[next] + (_pending[next] * estimate);

            if (covered <= remaining)
            {
                index = next;
                remaining -= covered;
            }
        }

        return Math.Min(index, _count - 1);
    }
}
