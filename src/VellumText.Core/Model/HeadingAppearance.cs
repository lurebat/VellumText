namespace VellumText;

/// <summary>
/// What a heading looks like, expressed against body text: how much larger it is drawn, whether
/// it is emphasised, and which outline level it occupies.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the model rather than in the view because it is not only the view that needs it.
/// A heading has no representation in RTF, so an exporter writing to the clipboard can carry only
/// the heading's visual consequence — size, weight, outline level — and that has to be the same
/// appearance the user was looking at when they pressed copy. Two independent tables meant the
/// editor and the clipboard disagreed about every level from 2 down.
/// </para>
/// <para>
/// Internal because it is a default, not a contract: a host that supplies its own
/// <c>IParagraphStyleResolver</c> replaces what the editor draws, and only the editor's own
/// resolver is bound to these numbers.
/// </para>
/// </remarks>
internal static class HeadingAppearance
{
    private static readonly double[] Scales = [2.0, 1.6, 1.35, 1.2, 1.1, 1.0];

    /// <summary>Whether <paramref name="kind"/> is one of the six heading levels.</summary>
    /// <param name="kind">The paragraph kind.</param>
    /// <returns><see langword="true"/> for <c>Heading1</c> through <c>Heading6</c>.</returns>
    internal static bool IsHeading(ParagraphKind kind) =>
        kind is >= ParagraphKind.Heading1 and <= ParagraphKind.Heading6;

    /// <summary>
    /// The zero-based outline level of <paramref name="kind"/>, or <see langword="null"/> when it
    /// is not a heading.
    /// </summary>
    /// <param name="kind">The paragraph kind.</param>
    /// <returns>0 for <c>Heading1</c> through 5 for <c>Heading6</c>; otherwise <see langword="null"/>.</returns>
    internal static int? OutlineLevel(ParagraphKind kind) =>
        IsHeading(kind) ? kind - ParagraphKind.Heading1 : null;

    /// <summary>How much larger than body text <paramref name="kind"/> is drawn.</summary>
    /// <param name="kind">The paragraph kind.</param>
    /// <returns>The multiplier on the body text size, or 1 for anything that is not a heading.</returns>
    internal static double Scale(ParagraphKind kind) =>
        OutlineLevel(kind) is { } level ? Scales[level] : 1.0;
}
