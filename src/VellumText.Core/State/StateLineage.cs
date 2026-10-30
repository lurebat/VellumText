using System.Runtime.CompilerServices;

namespace VellumText;

/// <summary>
/// Which state each state was applied from, and how that application moved positions.
/// </summary>
/// <remarks>
/// <para>
/// For a view that keeps something fixed across an edit — the line at the top of the viewport —
/// and so has to follow it through the edit that moved it. Mapping a position needs the
/// transaction's <see cref="Mapping"/>, and a view is handed only the state the transaction
/// produced, so the mapping is recorded here as the state is made.
/// </para>
/// <para>
/// Kept beside <see cref="EditorState"/> rather than on it. The state is a record, so a field on
/// it would join its value equality: two states holding the same document and selection would
/// stop comparing equal because they came about by different routes, and a mapping compares by
/// reference. A weak table keyed by the state adds nothing to its equality and keeps nothing
/// alive — not the state it was applied from, which is held weakly, and not a chain of mappings,
/// since each entry holds only its own transaction's.
/// </para>
/// </remarks>
internal static class StateLineage
{
    private static readonly ConditionalWeakTable<EditorState, Entry> Entries = new();

    /// <summary>Records that <paramref name="after"/> was applied from <paramref name="before"/>.</summary>
    /// <param name="after">The state the transaction produced.</param>
    /// <param name="before">The state it was applied to.</param>
    /// <param name="mapping">How the transaction moved positions.</param>
    internal static void Record(EditorState after, EditorState before, Mapping mapping) =>
        Entries.AddOrUpdate(after, new Entry(new WeakReference<EditorState>(before), mapping));

    /// <summary>
    /// How positions in <paramref name="before"/> moved to reach <paramref name="after"/>, or null
    /// if <paramref name="after"/> was not applied directly from <paramref name="before"/>.
    /// </summary>
    /// <param name="before">The earlier state.</param>
    /// <param name="after">The later state.</param>
    internal static Mapping? MappingFrom(EditorState before, EditorState after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return Entries.TryGetValue(after, out var entry)
            && entry.Before.TryGetTarget(out var recorded)
            && ReferenceEquals(recorded, before)
                ? entry.Mapping
                : null;
    }

    private sealed class Entry(WeakReference<EditorState> before, Mapping mapping)
    {
        public WeakReference<EditorState> Before { get; } = before;

        public Mapping Mapping { get; } = mapping;
    }
}
