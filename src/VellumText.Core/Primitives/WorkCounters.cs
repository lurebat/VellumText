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

    /// <summary>Calls to <see cref="TableGeometry.Place"/>, each a pass over every cell of a table.</summary>
    [ThreadStatic]
    internal static long TablePlacements;

    /// <summary>Calls to <see cref="TableGrid.Build"/>, each a placement plus a dictionary of every cell.</summary>
    [ThreadStatic]
    internal static long TableGridsBuilt;

    /// <summary>Calls to <see cref="InlineContent.MarkAt"/>, each a search of the mark spans.</summary>
    [ThreadStatic]
    internal static long MarkLookups;

    /// <summary>Paragraphs inside containers <see cref="DocumentPreview"/> has counted.</summary>
    [ThreadStatic]
    internal static long PreviewParagraphsMeasured;

    /// <summary>
    /// UTF-16 code units of paragraph text handed to the text formatter, summed over every
    /// <c>TextLayout</c> a paragraph view builds.
    /// </summary>
    /// <remarks>
    /// Avalonia's line wrapping costs lines × runs for each layout it is given, so the size of
    /// what is laid out per keystroke is the deterministic stand-in for the keystroke's cost.
    /// </remarks>
    [ThreadStatic]
    internal static long CharactersLaidOut;

    /// <summary>
    /// Lines × text runs fetched, summed over every <c>TextLayout</c> a paragraph view builds.
    /// </summary>
    /// <remarks>
    /// The superlinear term the 9.0 profile found: Avalonia's wrapping copies the list of fetched
    /// runs that remains once per line, so one layout does work in proportion to its lines times
    /// the runs its source handed over. Unformatted text is one run, so it stays linear.
    /// </remarks>
    [ThreadStatic]
    internal static long WrappingWork;

    /// <summary>Runs a paragraph's text source has handed to the text formatter.</summary>
    [ThreadStatic]
    internal static long RunsFetched;

    /// <summary>Table cells a table view has drawn.</summary>
    [ThreadStatic]
    internal static long TableCellsRendered;

    /// <summary>Table cells a table view has looked at while drawing, drawn or not.</summary>
    [ThreadStatic]
    internal static long TableCellsExamined;

    /// <summary>
    /// Table rows a table view has checked are laid out before drawing them, one per row of each
    /// band checked.
    /// </summary>
    [ThreadStatic]
    internal static long TableRowsChecked;

    /// <summary>Rows or index nodes visited to update or read a table's height prefixes.</summary>
    [ThreadStatic]
    internal static long TableRowPrefixSteps;

    /// <summary>Table cells a table view has walked to build or update their views.</summary>
    [ThreadStatic]
    internal static long TableCellsBuilt;

    /// <summary>Table cells a table view has measured the contents of.</summary>
    [ThreadStatic]
    internal static long TableCellsMeasured;

    /// <summary>
    /// Layout passes whose realized window turned out not to cover the viewport and had to be laid
    /// out again. A defect in the windowing when it is not zero.
    /// </summary>
    [ThreadStatic]
    internal static long CoverageRepairs;

    /// <summary>
    /// Times a presenter laid every block out whole because laying out the window again did not
    /// cover the viewport either.
    /// </summary>
    [ThreadStatic]
    internal static long FullLayoutBackstops;

    /// <summary>
    /// Tables in cells laid out whole because their part of the window could not be worked out or
    /// did not settle. Slow, never blank, and a defect in the windowing when it is not zero.
    /// </summary>
    [ThreadStatic]
    internal static long NestedWindowFallbacks;

    /// <summary>Times a table gave a table in one of its cells its part of the window and measured it.</summary>
    [ThreadStatic]
    internal static long NestedTablesRefreshed;

    /// <summary>
    /// Bands of a table examined to bound where a window's anchor is in it — how deep, how far
    /// above its bottom, whether all of it is laid out — when deciding what in a row beside it
    /// must be exact. A screenful per decision, not the table's length.
    /// </summary>
    [ThreadStatic]
    internal static long NestedBoundBands;

    /// <summary>
    /// Rows of tables in cells whose views were let go, far outside the window, at the end of a
    /// layout pass.
    /// </summary>
    [ThreadStatic]
    internal static long TableRowsLetGo;

    /// <summary>
    /// Tables in cells walked to let go of their rows because a band far outside the window keeps
    /// its views; one already let go, with nothing laid out in it since, is not walked again.
    /// </summary>
    [ThreadStatic]
    internal static long NestedTablesDrained;

    /// <summary>
    /// Embeds inside containers whose fallback text <see cref="DocumentPreview"/> has counted.
    /// </summary>
    [ThreadStatic]
    internal static long PreviewEmbedsMeasured;
}
