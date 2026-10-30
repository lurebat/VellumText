using System.Text;

namespace VellumText.Interop.Html;

/// <summary>
/// Collects the blocks an import produces, and the run of inline content not yet closed into one.
/// </summary>
/// <remarks>
/// HTML draws no line between the two: text, an <c>&lt;em&gt;</c> and a <c>&lt;table&gt;</c> can be
/// siblings. So the walker pours everything into here, and a paragraph appears whenever something
/// arrives that cannot be part of one.
/// </remarks>
internal sealed class BlockSink
{
    private readonly Action<string, string> _downgrade;
    private readonly List<BlockNode> _blocks = [];
    private readonly StringBuilder _text = new();
    private readonly List<ValueSpan<MarkSet>> _marks = [];
    private readonly List<InlineEmbed> _embeds = [];
    private bool _pendingSpace;
    private MarkSet _pendingSpaceMark;

    /// <summary>Whether the last character written is a newline that came from a <c>&lt;br&gt;</c>.</summary>
    private bool _endsWithBreak;

    /// <summary>Creates a sink.</summary>
    /// <param name="downgrade">
    /// Reports a repair made to content the model would otherwise refuse, as a message and a
    /// context.
    /// </param>
    /// <param name="keepSpaceRuns">See <see cref="HtmlImportOptions.KeepSpaceRuns"/>.</param>
    internal BlockSink(Action<string, string> downgrade, bool keepSpaceRuns = false)
    {
        _downgrade = downgrade;
        _keepSpaceRuns = keepSpaceRuns;
    }

    private readonly bool _keepSpaceRuns;

    /// <summary>The blocks produced so far.</summary>
    internal IReadOnlyList<BlockNode> Blocks => _blocks;

    /// <summary>Whether any inline content is waiting to be closed into a paragraph.</summary>
    internal bool HasPending => _text.Length > 0;

    /// <summary>Adds text.</summary>
    /// <param name="text">The characters.</param>
    /// <param name="mark">Their formatting.</param>
    /// <param name="preformatted">Whether whitespace in it is content.</param>
    internal void Add(string text, MarkSet mark, bool preformatted)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (preformatted)
        {
            Append(text, mark);
            return;
        }

        if (_keepSpaceRuns)
        {
            text = KeepSpaceRuns(text);
        }

        // Outside <pre>, any run of whitespace is one space, and a run at the start of a paragraph
        // is nothing at all. Deferring the space rather than appending it is what makes the run at
        // the end disappear too: nothing ever asks for it.
        var start = -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (IsCollapsible(text[i]))
            {
                if (start >= 0)
                {
                    Append(text[start..i], mark);
                    start = -1;
                }

                DeferSpace(mark);
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            Append(text[start..], mark);
        }
    }

    /// <summary>
    /// Records that a space is owed, carrying the formatting in force where it occurred.
    /// </summary>
    /// <param name="mark">The formatting of the whitespace itself.</param>
    /// <remarks>
    /// <para>
    /// The mark has to be captured here rather than taken from whatever text arrives next. In
    /// <c>x &lt;b&gt;bold&lt;/b&gt;</c> the space is outside the element, so charging it to the
    /// following run makes the bold span one character too wide — and formatting then creeps a
    /// character to the left every time a document is imported.
    /// </para>
    /// <para>
    /// The first of two adjacent whitespace runs wins, because that is the one that survives the
    /// collapse. In <c>&lt;b&gt;bold &lt;/b&gt; text</c> the surviving space is inside the
    /// element and is therefore bold, which is what HTML itself says.
    /// </para>
    /// </remarks>
    private void DeferSpace(MarkSet mark)
    {
        if (_pendingSpace)
        {
            return;
        }

        _pendingSpace = true;
        _pendingSpaceMark = mark;
    }

    /// <summary>Adds an embedded object, which occupies exactly one position.</summary>
    /// <param name="embed">The embed.</param>
    /// <param name="mark">Its formatting.</param>
    internal void AddEmbed(InlineEmbed embed, MarkSet mark)
    {
        FlushPendingSpace();
        AppendRaw(InlineContent.Placeholder.ToString(), mark);
        _embeds.Add(embed);
    }

    /// <summary>Adds a line break inside the paragraph being built.</summary>
    /// <param name="mark">The formatting in force where the break occurred.</param>
    /// <remarks>
    /// A break swallows the whitespace on either side of it, which is why this is not simply a
    /// preformatted newline: "a &lt;br&gt; b" is two lines reading "a" and "b", not "a " and " b".
    /// </remarks>
    internal void AddLineBreak(MarkSet mark)
    {
        _pendingSpace = false;
        Append("\n", mark);
        _pendingSpace = false;
        _endsWithBreak = true;
    }

    /// <summary>
    /// Drops tabs, and writes each run of spaces as one space followed by non-breaking spaces,
    /// so that the collapse after it leaves the run's length intact.
    /// </summary>
    private static string KeepSpaceRuns(string text)
    {
        if (!text.Contains('\t', StringComparison.Ordinal) && !text.Contains("  ", StringComparison.Ordinal))
        {
            return text;
        }

        var kept = new StringBuilder(text.Length);
        var previousWasSpace = false;

        foreach (var c in text)
        {
            if (c == '\t')
            {
                continue;
            }

            if (c == ' ')
            {
                kept.Append(previousWasSpace ? '\u00A0' : ' ');
                previousWasSpace = true;
            }
            else
            {
                kept.Append(c);
                previousWasSpace = false;
            }
        }

        return kept.ToString();
    }

    /// <summary>Whether a character is whitespace that HTML collapses.</summary>
    /// <param name="c">The character.</param>
    /// <returns>True if a run of it becomes a single space.</returns>
    /// <remarks>
    /// Deliberately not <see cref="char.IsWhiteSpace(char)"/>, which is true of U+00A0. A
    /// non-breaking space is content — it is exactly what a source writes when it wants a space
    /// that survives — and Word fills its output with them.
    /// </remarks>
    private static bool IsCollapsible(char c) =>
        c is ' ' or '\t' or '\n' or '\r' or '\f';

    private void Append(string text, MarkSet mark)
    {
        FlushPendingSpace();

        // U+FFFC belongs to the model: it marks where an embed sits, and the model rejects content
        // whose placeholder count does not match its embed count. Source text is entitled to
        // contain the character anyway — Word writes it for objects it cannot otherwise express,
        // and anything that has already round-tripped through a rich text model will carry it — so
        // it has to be taken out here. Letting one through discards the whole import.
        if (text.Contains(InlineContent.Placeholder, StringComparison.Ordinal))
        {
            text = text.Replace(InlineContent.Placeholder.ToString(), string.Empty, StringComparison.Ordinal);

            if (text.Length == 0)
            {
                return;
            }
        }

        AppendRaw(text, mark);
    }

    private void FlushPendingSpace()
    {
        if (!_pendingSpace)
        {
            return;
        }

        _pendingSpace = false;

        // A space only separates things. There is nothing to the left of the first one, and
        // nothing to the left of the first one on a line either.
        if (_text.Length > 0 && _text[^1] != '\n')
        {
            AppendRaw(" ", _pendingSpaceMark);
        }
    }

    private void AppendRaw(string text, MarkSet mark)
    {
        var start = _text.Length;
        _text.Append(text);
        _endsWithBreak = false;

        if (mark.IsEmpty)
        {
            return;
        }

        // Runs that carry the same formatting are merged as they arrive. The model would reject
        // overlapping spans and merge equal neighbours anyway, but doing it here keeps a paragraph
        // built from a thousand nested spans from allocating a thousand of them first.
        if (_marks.Count > 0)
        {
            var last = _marks[^1];

            if (last.End == start && last.Value == mark)
            {
                _marks[^1] = new ValueSpan<MarkSet>(last.Start, last.Length + text.Length, mark);
                return;
            }
        }

        _marks.Add(new ValueSpan<MarkSet>(start, text.Length, mark));
    }

    /// <summary>Closes any pending inline content into a paragraph.</summary>
    /// <param name="style">The formatting for the paragraph.</param>
    /// <param name="force">
    /// Whether to produce a paragraph even with nothing in it, which is what an explicit line break
    /// asks for.
    /// </param>
    internal void Flush(BlockStyle style, bool force = false)
    {
        // A break that ends a block only gives its line a height, which the paragraph has anyway:
        // <p>a<br></p> is one line and <p><br></p> is one blank line. Kept as a character, every
        // blank line arrived two lines tall. The paragraph it was in is still a paragraph.
        if (_endsWithBreak)
        {
            DropTrailingBreak();
            force = true;
        }

        if (_text.Length == 0 && !force)
        {
            Reset();
            return;
        }

        var text = WellFormed(_text.ToString());

        SnapMarksToCharacters(text);

        var content = InlineContent.Create(text, _marks, _embeds);

        _blocks.Add(new ParagraphNode(content, style.Kind, style.Align, style.Indent));
        Reset();
    }

    /// <summary>The text with every unpaired surrogate replaced by U+FFFD.</summary>
    /// <remarks>
    /// The model refuses text that is not well-formed UTF-16, and a refusal here used to cost the
    /// whole import, not the half character. A raw lone surrogate reaches this far from a .NET
    /// string or an RTF <c>\u</c>; the HTML tokenizer already replaces one written as a character
    /// reference. The replacement is one code unit for one, so no span offset moves.
    /// </remarks>
    private string WellFormed(string text)
    {
        char[]? repaired = null;

        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                repaired ??= text.ToCharArray();
                repaired[i] = '\uFFFD';
            }
        }

        if (repaired is null)
        {
            return text;
        }

        _downgrade(
            "Text contained half a character (an unpaired UTF-16 surrogate); it was replaced with U+FFFD.",
            "unpaired-surrogate");

        return new string(repaired);
    }

    /// <summary>Moves span edges that fall inside a surrogate pair to the start of the pair.</summary>
    /// <remarks>
    /// A pair written as two escapes with formatting between its halves puts an edge inside the
    /// character, which the model refuses. Moving every edge the same way keeps spans that did not
    /// overlap from overlapping; one left empty is dropped by <see cref="InlineContent.Create"/>.
    /// </remarks>
    private void SnapMarksToCharacters(string text)
    {
        var moved = false;

        for (var i = 0; i < _marks.Count; i++)
        {
            var span = _marks[i];
            var start = CharacterStart(text, span.Start);
            var end = CharacterStart(text, span.End);

            if (start != span.Start || end != span.End)
            {
                _marks[i] = new ValueSpan<MarkSet>(start, end - start, span.Value);
                moved = true;
            }
        }

        if (moved)
        {
            _downgrade(
                "Formatting began or ended inside a character and was moved to the character's start.",
                "split-character");
        }
    }

    // Only called on well-formed text, where a low surrogate always completes a pair.
    private static int CharacterStart(string text, int offset) =>
        offset > 0 && offset < text.Length && char.IsLowSurrogate(text[offset]) ? offset - 1 : offset;

    private void DropTrailingBreak()
    {
        _text.Length--;

        // Spans arrive in order and merge as they go, so only the last can cover the break. One
        // left empty is dropped by InlineContent.Create.
        if (_marks.Count > 0 && _marks[^1].End > _text.Length)
        {
            var last = _marks[^1];

            _marks[^1] = new ValueSpan<MarkSet>(last.Start, last.Length - 1, last.Value);
        }
    }

    /// <summary>Closes any pending content, then adds a block of its own.</summary>
    /// <param name="style">The formatting for the paragraph being closed.</param>
    /// <param name="block">The block to add.</param>
    internal void AddBlock(BlockStyle style, BlockNode block)
    {
        Flush(style);
        _blocks.Add(block);
    }

    private void Reset()
    {
        _text.Clear();
        _marks.Clear();
        _embeds.Clear();
        _pendingSpace = false;
        _endsWithBreak = false;
    }
}
