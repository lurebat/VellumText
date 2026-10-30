using System.Collections.Immutable;

namespace VellumText.Avalonia;

/// <summary>
/// A presenter's block list, patched in place as the document changes.
/// </summary>
/// <remarks>
/// <para>
/// Not an <see cref="ImmutableArray{T}"/>, which is what it was. An immutable list has to be
/// rebuilt to change, and rebuilding it from the document on every keystroke was 3.2 MB at
/// 50,000 paragraphs — most of what a keystroke allocated, and the gen-2 collections that came
/// with it. An edit replaces a few top-level blocks, so this replaces their slots and moves the
/// rest by the number of positions the edit added or removed: linear, like the edit, but with
/// nothing allocated beyond the slots that are new.
/// </para>
/// <para>
/// Slots are kept in document order and their starts strictly increase, which is what makes the
/// binary searches over them sound.
/// </para>
/// </remarks>
internal sealed class BlockSlotList
{
    private BlockSlot[] _items = [];
    private int _count;

    /// <summary>How many slots there are.</summary>
    public int Length => _count;

    /// <summary>Whether there are none.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>One slot.</summary>
    /// <param name="index">Its index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a slot.</exception>
    public BlockSlot this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Not a slot in this list.");
            }

            return _items[index];
        }
    }

    /// <summary>Replaces every slot.</summary>
    /// <param name="slots">The new slots.</param>
    public void Reset(ImmutableArray<BlockSlot> slots)
    {
        Clear();
        EnsureCapacity(slots.Length);
        slots.CopyTo(_items);
        _count = slots.Length;
    }

    /// <summary>Removes every slot.</summary>
    public void Clear()
    {
        // Cleared rather than abandoned: a slot holds its block, and a list that kept the blocks
        // of a document nobody shows any more would keep that document alive.
        Array.Clear(_items, 0, _count);
        _count = 0;
    }

    /// <summary>
    /// Replaces a run of slots, moving every slot after it by <paramref name="shift"/> positions.
    /// </summary>
    /// <param name="index">The first slot replaced.</param>
    /// <param name="removed">How many are replaced.</param>
    /// <param name="inserted">What replaces them.</param>
    /// <param name="shift">How far the edit moved everything after the replaced blocks.</param>
    public void Splice(int index, int removed, ImmutableArray<BlockSlot> inserted, int shift)
    {
        var after = _count - index - removed;
        var count = _count - removed + inserted.Length;

        EnsureCapacity(count);

        Array.Copy(_items, index + removed, _items, index + inserted.Length, after);
        inserted.CopyTo(_items, index);

        for (var i = index + inserted.Length; i < count; i++)
        {
            _items[i] = _items[i] with { Start = _items[i].Start + shift };
        }

        // A shrinking list leaves its old tail behind; drop the blocks it holds.
        if (count < _count)
        {
            Array.Clear(_items, count, _count - count);
        }

        _count = count;
    }

    /// <summary>A copy of a run of slots.</summary>
    /// <param name="index">The first slot.</param>
    /// <param name="count">How many.</param>
    public BlockSlot[] Copy(int index, int count) => _items.AsSpan(index, count).ToArray();

    /// <summary>The index of the first slot starting at or after a position, or <see cref="Length"/>.</summary>
    /// <param name="position">A document position.</param>
    public int FirstStartingAtOrAfter(int position)
    {
        var low = 0;
        var high = _count;

        while (low < high)
        {
            var mid = low + ((high - low) / 2);

            if (_items[mid].Start >= position)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    /// <summary>The slots as an immutable array: a copy, for callers outside the hot path.</summary>
    public ImmutableArray<BlockSlot> ToImmutableArray() => ImmutableArray.Create(_items, 0, _count);

    private void EnsureCapacity(int count)
    {
        if (_items.Length >= count)
        {
            return;
        }

        var items = new BlockSlot[Math.Max(count, _items.Length * 2)];

        Array.Copy(_items, items, _count);
        _items = items;
    }
}
