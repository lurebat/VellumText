using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;

namespace VellumText.Avalonia;

/// <summary>When a paragraph is laid out in chunks, and how long a chunk is.</summary>
/// <param name="Threshold">
/// The length, in UTF-16 code units of what is laid out, above which a paragraph is chunked. At or
/// below it the paragraph is one <see cref="TextLayout"/>, as it always was.
/// </param>
/// <param name="Window">
/// How much text one chunk's layout is given. A chunk keeps the lines of that layout that cannot
/// depend on where the window was cut, so it covers a little less than this.
/// </param>
internal readonly record struct ParagraphChunking(int Threshold, int Window)
{
    /// <summary>What the editor uses: chunk past 4,096 characters, about 2,048 at a time.</summary>
    /// <remarks>
    /// Sized from the 9.0 profile: one 2,048-character window of formatted text is well under a
    /// millisecond to lay out, and a keystroke lays out one or two of them.
    /// </remarks>
    public static ParagraphChunking Default { get; } = new(4_096, 2_048);

    /// <summary>Never chunk: every paragraph is one layout.</summary>
    public static ParagraphChunking Never { get; } = new(int.MaxValue, int.MaxValue);
}

/// <summary>
/// A line of a paragraph and where it sits, whichever layout it came from.
/// </summary>
/// <param name="Line">The line.</param>
/// <param name="Offset">
/// Where the line's layout starts in the paragraph's layout positions. A line's own indices are
/// relative to its layout, so this is added to every one of them.
/// </param>
/// <param name="Top">The distance from the top of the paragraph's text to the top of the line.</param>
internal readonly record struct PlacedLine(TextLine Line, int Offset, double Top)
{
    /// <summary>The first layout position on the line.</summary>
    public int Start => Offset + Line.FirstTextSourceIndex;

    /// <summary>One past the last layout position on the line.</summary>
    public int End => Offset + Line.FirstTextSourceIndex + Line.Length;
}

/// <summary>
/// A long paragraph held as consecutive independent layouts, each over a range of its text that
/// starts and ends at a line boundary (architecture §4.6, F-33).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Avalonia's wrapping copies the remaining run list once per line, so one layout
/// costs lines × runs, and a formatted paragraph has runs in proportion to its length: quadratic.
/// Nothing outside the formatter can merge the runs. What can be bounded is how much one layout is
/// given, so each chunk is a <see cref="TextLayout"/> over a window of the paragraph, and an edit
/// lays out again only from the chunk it lands in until the lines meet the old ones.
/// </para>
/// <para>
/// <b>Why it is exact.</b> A line the formatter starts afresh at a line boundary is the line it
/// would have produced continuing from the line before, provided nothing it resolves depends on
/// text before that boundary. Line breaking does not: a break is decided by what fits from the
/// start of the line and by the text just past the overflow. Shaping does not either, at a break
/// opportunity. Bidirectional resolution does — a neutral or a number at the start of a line can
/// take its direction from strong text on the line before, and a bracket can pair across any
/// distance — so a paragraph that holds any right-to-left, Arabic-number or explicit embedding
/// character is never chunked (<see cref="CanChunk"/>). Nor is a justified one: Avalonia
/// measures a justified layout's width before stretching it, which a chunk cannot reproduce.
/// </para>
/// <para>
/// <b>Where a chunk ends.</b> A chunk's layout is given a window of text that is cut off with an
/// end of paragraph wherever the window ends, so its last lines are wrong. Every line before the
/// last two is exact: the line after it is complete, so whatever decided the break was inside the
/// window. The chunk keeps those lines and the next chunk starts where they end. The two lines it
/// drops are laid out again by the next chunk, which is the whole of the overlap.
/// </para>
/// <para>
/// <b>Never <c>TextRunCache.InvalidateFrom</c>.</b> Increment 0 measured it returning stale
/// layouts for edits (A1). Chunks are separate layouts, so nothing here invalidates a cache: a
/// chunk is either kept as it is, because its text and everything it looked at are unchanged, or
/// built again.
/// </para>
/// </remarks>
internal sealed class ParagraphChunks
{
    private readonly Chunk[] _chunks;
    private PlacedLine[]? _lines;

    private ParagraphChunks(Chunk[] chunks, Key key)
    {
        _chunks = chunks;
        LayoutKey = key;

        var width = 0.0;
        var height = 0.0;

        foreach (var chunk in chunks)
        {
            var lines = chunk.Layout.TextLines;

            for (var i = 0; i < chunk.Lines; i++)
            {
                // The same accumulation, in the same order, as TextLayout's own metrics, so that a
                // chunked paragraph and an unchunked one agree to the last bit.
                height += lines[i].Height;
                width = Math.Max(width, lines[i].Width);
            }
        }

        Width = width;
        Height = height;
    }

    /// <summary>The number of chunks.</summary>
    public int Count => _chunks.Length;

    /// <summary>Where each chunk starts, in layout positions.</summary>
    internal IEnumerable<int> Starts => _chunks.Select(chunk => chunk.Start);

    /// <summary>The widest line, as <see cref="TextLayout.Width"/> would report it.</summary>
    public double Width { get; }

    /// <summary>All the lines' heights together, as <see cref="TextLayout.Height"/> would report it.</summary>
    public double Height { get; }

    /// <summary>What these chunks were laid out from, so a later layout can tell what changed.</summary>
    public Key LayoutKey { get; }

    /// <summary>Every line of the paragraph, in order, with where it sits.</summary>
    public PlacedLine[] Lines => _lines ??= Place();

    /// <summary>
    /// Whether a paragraph's text may be chunked at all.
    /// </summary>
    /// <remarks>
    /// False for anything holding a character whose bidirectional class is right-to-left, Arabic
    /// letter, Arabic number, or an explicit embedding, override or isolate. Without them every
    /// character resolves to the paragraph's own level whichever layout it is in; with them the
    /// level can depend on text arbitrarily far away. See the class remarks. Only characters from
    /// U+0590 up can be any of those, so the common case is one vectorized search.
    /// </remarks>
    /// <param name="text">The paragraph's text.</param>
    /// <param name="preedit">Text being composed in it, or null.</param>
    public static bool CanChunk(string text, Preedit? preedit) =>
        IsLeftToRightOnly(text) && (preedit is null || IsLeftToRightOnly(preedit.Text));

    private static bool IsLeftToRightOnly(ReadOnlySpan<char> text)
    {
        var at = 0;

        while (true)
        {
            var next = text[at..].IndexOfAnyInRange('\u0590', char.MaxValue);

            if (next < 0)
            {
                return true;
            }

            at += next;

            var codepoint = Codepoint.ReadAt(text, at, out var count);

            if (codepoint.BiDiClass is BidiClass.RightToLeft
                or BidiClass.ArabicLetter
                or BidiClass.ArabicNumber
                or BidiClass.LeftToRightEmbedding
                or BidiClass.LeftToRightOverride
                or BidiClass.LeftToRightIsolate
                or BidiClass.RightToLeftEmbedding
                or BidiClass.RightToLeftOverride
                or BidiClass.RightToLeftIsolate
                or BidiClass.FirstStrongIsolate
                or BidiClass.PopDirectionalFormat
                or BidiClass.PopDirectionalIsolate)
            {
                return false;
            }

            at += count;
        }
    }

    /// <summary>
    /// Lays a paragraph out in chunks, reusing whatever of <paramref name="previous"/> the change
    /// since it cannot have affected.
    /// </summary>
    /// <param name="source">The whole paragraph's text source, composition included.</param>
    /// <param name="key">What the paragraph is being laid out from.</param>
    /// <param name="properties">The paragraph's properties.</param>
    /// <param name="window">How much text to give one chunk's layout.</param>
    /// <param name="previous">The chunks as they were, or null to lay out from scratch.</param>
    public static ParagraphChunks Build(
        ITextSource source,
        Key key,
        TextParagraphProperties properties,
        int window,
        ParagraphChunks? previous)
    {
        var total = key.LayoutLength;
        var chunks = new List<Chunk>();
        var position = 0;
        var delta = 0;
        var reusable = int.MaxValue;
        var old = previous is not null && previous.LayoutKey.SameShape(key) ? previous._chunks : null;

        if (old is not null)
        {
            if (Change.Between(previous!.LayoutKey, key) is not { } change)
            {
                return previous;
            }

            var first = ChunkAt(old, change.Start);

            // The first line of a chunk was broken where it was because of what fitted on the line
            // before it, which belongs to the previous chunk. An edit there can move that break.
            if (first > 0 && change.Start <= old[first].Start + old[first].FirstLineEnd)
            {
                first--;
            }

            chunks.AddRange(old.AsSpan(0, first));
            position = old[first].Start;
            delta = change.NewEnd - change.OldEnd;

            // A chunk is only as unchanged as everything it was laid out from, which is all of its
            // window; the first one that started past the change qualifies, and every one after it.
            reusable = first + 1;

            while (reusable < old.Length && old[reusable].Start < change.OldEnd)
            {
                reusable++;
            }
        }

        while (position < total || chunks.Count == 0)
        {
            var target = -1;

            if (old is not null)
            {
                while (reusable < old.Length && old[reusable].Start + delta < position)
                {
                    reusable++;
                }

                if (reusable < old.Length)
                {
                    target = old[reusable].Start + delta;

                    if (target == position)
                    {
                        // The lines have met the old ones at the start of a chunk that the change
                        // did not reach: it and everything after it stand, moved by the change.
                        for (var i = reusable; i < old.Length; i++)
                        {
                            chunks.Add(old[i] with { Start = old[i].Start + delta });
                        }

                        break;
                    }
                }
            }

            var chunk = Next(source, total, position, properties, key.TextWidth, window, target);

            chunks.Add(chunk);
            position += chunk.Length;
        }

        return new ParagraphChunks([.. chunks], key);
    }

    /// <summary>The index of the chunk holding a layout position; the last for the end.</summary>
    private static int ChunkAt(Chunk[] chunks, int position)
    {
        var low = 0;
        var high = chunks.Length - 1;

        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);

            if (chunks[middle].Start <= position)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>Lays out the chunk starting at <paramref name="start"/>.</summary>
    /// <param name="source">The whole paragraph's text source.</param>
    /// <param name="total">The paragraph's length in layout positions.</param>
    /// <param name="start">The layout position the chunk starts at, which is a line boundary.</param>
    /// <param name="properties">The paragraph's properties.</param>
    /// <param name="width">The width to wrap to.</param>
    /// <param name="window">How much text to give the layout.</param>
    /// <param name="target">
    /// A position the chunk would rather end at, because an unchanged chunk starts there, or -1.
    /// </param>
    private static Chunk Next(
        ITextSource source,
        int total,
        int start,
        TextParagraphProperties properties,
        double width,
        int window,
        int target)
    {
        var size = Math.Max(1, window);

        while (true)
        {
            var end = (int)Math.Min(total, (long)start + size);
            var fetched = WorkCounters.RunsFetched;
            var layout = new TextLayout(
                new WindowSource(source, start, end, total),
                properties,
                maxWidth: width);

            WorkCounters.CharactersLaidOut += end - start;
            CountWrapping(layout, fetched);

            var lines = layout.TextLines;

            if (end >= total)
            {
                return new Chunk(layout, start, total - start, lines.Count);
            }

            // The last line ends where the window was cut and the one before it was broken against
            // that cut; every line before those is exact. See the class remarks.
            if (lines.Count >= 3)
            {
                var keep = lines.Count - 2;

                for (var i = 1; i < keep && target > start; i++)
                {
                    if (start + lines[i].FirstTextSourceIndex == target)
                    {
                        keep = i;
                        break;
                    }
                }

                return new Chunk(layout, start, lines[keep].FirstTextSourceIndex, keep);
            }

            // Too few lines to keep any: a line longer than the window. Give it more.
            size *= 2;
        }
    }

    /// <summary>Adds a layout's lines × fetched runs to <see cref="WorkCounters.WrappingWork"/>.</summary>
    /// <param name="layout">The layout just built.</param>
    /// <param name="fetchedBefore"><see cref="WorkCounters.RunsFetched"/> before it was built.</param>
    internal static void CountWrapping(TextLayout layout, long fetchedBefore) =>
        WorkCounters.WrappingWork += layout.TextLines.Count * (WorkCounters.RunsFetched - fetchedBefore);

    private PlacedLine[] Place()
    {
        var count = 0;

        foreach (var chunk in _chunks)
        {
            count += chunk.Lines;
        }

        var placed = new PlacedLine[count];
        var top = 0.0;
        var at = 0;

        foreach (var chunk in _chunks)
        {
            var lines = chunk.Layout.TextLines;

            for (var i = 0; i < chunk.Lines; i++)
            {
                placed[at++] = new PlacedLine(lines[i], chunk.Start, top);
                top += lines[i].Height;
            }
        }

        return placed;
    }

    /// <summary>One chunk: a layout, where it starts, and how many of its lines are kept.</summary>
    /// <param name="Layout">The chunk's layout, whose positions start at zero.</param>
    /// <param name="Start">The layout position the chunk starts at.</param>
    /// <param name="Length">How many layout positions the kept lines cover.</param>
    /// <param name="Lines">How many of the layout's lines are kept.</param>
    private sealed record Chunk(TextLayout Layout, int Start, int Length, int Lines)
    {
        /// <summary>The end of the chunk's first line, relative to the chunk.</summary>
        public int FirstLineEnd =>
            Layout.TextLines[0].FirstTextSourceIndex + Layout.TextLines[0].Length;
    }

    /// <summary>What a paragraph was laid out from.</summary>
    /// <param name="Content">The paragraph's content.</param>
    /// <param name="Preedit">The composition spliced into it, or null.</param>
    /// <param name="TextWidth">The width the text wrapped to.</param>
    /// <param name="Alignment">The paragraph's alignment.</param>
    /// <param name="LineHeight">The forced line height, or zero.</param>
    /// <param name="Styles">The resolver the runs' properties came from.</param>
    internal sealed record Key(
        InlineContent Content,
        Preedit? Preedit,
        double TextWidth,
        TextAlignment Alignment,
        double LineHeight,
        ITextStyleResolver Styles)
    {
        /// <summary>The number of positions laid out, which counts the composition.</summary>
        public int LayoutLength => Content.Length + (Preedit?.Length ?? 0);

        /// <summary>
        /// Whether everything but the text is the same, so that unchanged text lays out the same.
        /// </summary>
        public bool SameShape(Key other) =>
            TextWidth.Equals(other.TextWidth)
            && Alignment == other.Alignment
            && LineHeight.Equals(other.LineHeight)
            && ReferenceEquals(Styles, other.Styles);
    }

    /// <summary>
    /// The stretch of layout positions that differs between two layouts of a paragraph.
    /// </summary>
    /// <param name="Start">The first position that may differ; everything before it is the same.</param>
    /// <param name="OldEnd">Where the unchanged tail starts in the old layout.</param>
    /// <param name="NewEnd">Where the same tail starts in the new one.</param>
    internal readonly record struct Change(int Start, int OldEnd, int NewEnd)
    {
        /// <summary>
        /// What changed between two keys, or null if nothing did.
        /// </summary>
        /// <remarks>
        /// Conservative rather than minimal: the text, the formatting spans and the embeds are each
        /// matched from both ends, and the change covers everything any of them disagrees about. A
        /// composition is always treated as changed, since it is styled from the text around it.
        /// </remarks>
        public static Change? Between(Key before, Key after)
        {
            var a = before.Content;
            var b = after.Content;

            if (ReferenceEquals(a, b) && before.Preedit == after.Preedit)
            {
                return null;
            }

            var shorter = Math.Min(a.Length, b.Length);
            var prefix = a.Text.AsSpan().CommonPrefixLength(b.Text);

            // Not limited by the prefix yet: when only formatting or an embed changed, the text
            // matches end to end, and the tail is wherever the formatting stops differing.
            var suffix = CommonSuffixLength(a.Text, b.Text, shorter);

            (prefix, suffix) = Marks(a, b, prefix, suffix);
            (prefix, suffix) = Embeds(a, b, prefix, suffix);

            if (before.Preedit is { } was)
            {
                prefix = Math.Min(prefix, was.Offset);
                suffix = Math.Min(suffix, a.Length - was.Offset);
            }

            if (after.Preedit is { } now)
            {
                prefix = Math.Min(prefix, now.Offset);
                suffix = Math.Min(suffix, b.Length - now.Offset);
            }

            // The two must not overlap, or the stretch between them would be a negative length.
            suffix = Math.Min(suffix, shorter - prefix);

            // Every composition now starts inside the change, so it sits wholly before the tail.
            return new Change(
                prefix,
                a.Length - suffix + (before.Preedit?.Length ?? 0),
                b.Length - suffix + (after.Preedit?.Length ?? 0));
        }

        private static int CommonSuffixLength(string a, string b, int limit)
        {
            var count = 0;

            while (count < limit && a[a.Length - 1 - count] == b[b.Length - 1 - count])
            {
                count++;
            }

            return count;
        }

        private static (int Prefix, int Suffix) Marks(InlineContent a, InlineContent b, int prefix, int suffix)
        {
            var x = a.Marks;
            var y = b.Marks;
            var i = 0;

            while (i < x.Length && i < y.Length && x[i].Equals(y[i]))
            {
                i++;
            }

            if (i < x.Length)
            {
                prefix = Math.Min(prefix, x[i].Start);
            }

            if (i < y.Length)
            {
                prefix = Math.Min(prefix, y[i].Start);
            }

            var j = 1;

            while (j <= x.Length && j <= y.Length
                && x[^j].Length == y[^j].Length
                && a.Length - x[^j].End == b.Length - y[^j].End
                && x[^j].Value.Equals(y[^j].Value))
            {
                j++;
            }

            if (j <= x.Length)
            {
                suffix = Math.Min(suffix, a.Length - x[^j].End);
            }

            if (j <= y.Length)
            {
                suffix = Math.Min(suffix, b.Length - y[^j].End);
            }

            return (prefix, suffix);
        }

        private static (int Prefix, int Suffix) Embeds(InlineContent a, InlineContent b, int prefix, int suffix)
        {
            if (a.Embeds.IsEmpty && b.Embeds.IsEmpty)
            {
                return (prefix, suffix);
            }

            // Placeholders inside the common text are at the same offsets in both, so the embeds
            // there pair up by index; one that differs is a change at its placeholder.
            var x = Offsets(a);
            var y = Offsets(b);
            var i = 0;

            while (i < x.Length && i < y.Length && x[i] < prefix && y[i] == x[i])
            {
                if (!a.Embeds[i].Equals(b.Embeds[i]))
                {
                    prefix = Math.Min(prefix, x[i]);
                    break;
                }

                i++;
            }

            for (var j = 1; j <= x.Length && j <= y.Length; j++)
            {
                if (a.Length - x[^j] > suffix || a.Length - x[^j] != b.Length - y[^j])
                {
                    break;
                }

                if (!a.Embeds[^j].Equals(b.Embeds[^j]))
                {
                    suffix = Math.Min(suffix, a.Length - x[^j] - 1);
                    break;
                }
            }

            return (prefix, suffix);
        }

        private static int[] Offsets(InlineContent content)
        {
            var offsets = new int[content.Embeds.Length];
            var at = 0;

            for (var i = 0; i < offsets.Length; i++)
            {
                at = content.NextPlaceholderFrom(at);
                offsets[i] = at;
                at++;
            }

            return offsets;
        }
    }

    /// <summary>
    /// A window onto a paragraph's text source: positions start at zero, and the text ends where
    /// the window does.
    /// </summary>
    private sealed class WindowSource(ITextSource inner, int start, int end, int total) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex < 0)
            {
                return null;
            }

            var at = start + textSourceIndex;

            if (at >= end)
            {
                // The paragraph's own end where the window reaches it; a stand-in elsewhere, so the
                // formatter stops fetching and shaping at the window.
                return end >= total ? inner.GetTextRun(at) : new TextEndOfParagraph();
            }

            var run = inner.GetTextRun(at);

            return run is TextCharacters characters && at + characters.Length > end
                ? new TextCharacters(characters.Text[..(end - at)], characters.Properties!)
                : run;
        }
    }
}
