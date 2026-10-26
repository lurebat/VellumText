namespace VellumText;

/// <summary>
/// Counts of work done on the current thread, for tests that pin an algorithm's shape without a
/// clock.
/// </summary>
/// <remarks>
/// <para>
/// A scan over an array allocates nothing, so a test that measures allocation cannot see one,
/// and a test that measures time is noise on a shared machine. The audit's quadratic findings
/// were scans of exactly that kind. These counters are what lets a test say "this touched every
/// span" deterministically: reset, run the operation, read the count.
/// </para>
/// <para>
/// Thread-static so that tests running in parallel cannot see each other's work, and so that
/// counting costs an increment rather than an interlocked operation. Nothing in the library reads
/// them.
/// </para>
/// </remarks>
internal static class WorkCounters
{
    /// <summary>
    /// Recorded formatting spans a <see cref="RestoreMarksStep"/> has examined while applying
    /// itself.
    /// </summary>
    [ThreadStatic]
    internal static long RestoredSpanVisits;

    /// <summary>Calls to <see cref="InlineContent.MarkAt"/>, each a search of the mark spans.</summary>
    [ThreadStatic]
    internal static long MarkLookups;
}
