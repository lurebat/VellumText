using Avalonia.Media.TextFormatting.Unicode;

namespace VellumText.Avalonia;

/// <summary>
/// Where a word starts and ends, for double-click selection.
/// </summary>
/// <remarks>
/// <para>
/// Built on Avalonia's <see cref="WordBreakEnumerator"/>, which implements UAX #29, rather than on
/// the letter-or-digit run <c>RichTextView.WordBoundary</c> uses for Ctrl+Arrow. The two are
/// deliberately different and both are deliberately kept. Ctrl+Arrow walks caret positions and
/// lands <em>between</em> words, so an approximation there is a matter of taste; a double-click
/// names a range and a wrong answer visibly slices a grapheme cluster in half. Measured against
/// the approximation: "don't" came out as two words, "3.14" as two numbers, and a family emoji
/// ZWJ sequence split into its component people.
/// </para>
/// <para>
/// Not used for Ctrl+Arrow, which is left alone: UAX #29 puts a boundary either side of every
/// punctuation mark, so Ctrl+Right through "a, b" would stop three times where Windows stops
/// once. That is a change to established behaviour, not a bug fix, and is out of scope here.
/// </para>
/// </remarks>
internal static class TextBoundaries
{
    /// <summary>The word containing an offset, as a half-open range.</summary>
    /// <remarks>
    /// An offset sitting exactly on a boundary belongs to the segment that <em>starts</em> there,
    /// which is what makes a double-click at the left edge of a word select that word rather than
    /// the space in front of it. The end of the text is the one place there is no such segment,
    /// and there the last one is taken instead.
    /// </remarks>
    /// <param name="text">The paragraph's text.</param>
    /// <param name="offset">An offset into <paramref name="text"/>.</param>
    public static (int Start, int End) WordAt(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return (0, 0);
        }

        var at = Math.Clamp(offset, 0, text.Length);
        var enumerator = new WordBreakEnumerator(text.AsSpan());
        var start = 0;
        var end = text.Length;

        while (enumerator.MoveNext(out var segment))
        {
            start = segment.Offset;
            end = segment.Offset + segment.Length;

            if (at < end)
            {
                break;
            }
        }

        return (start, end);
    }
}
