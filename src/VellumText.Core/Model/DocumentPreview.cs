using System.Collections.Immutable;
using System.Globalization;

namespace VellumText;

/// <summary>
/// Shortening a document to a preview of it.
/// </summary>
/// <remarks>
/// <para>
/// For lists of documents — a clipboard history, a search result, a revision list — where each row
/// shows the first line or two of something that may be enormous. Rendering the whole document
/// into a small box and clipping it works until the document is a hundred pages, at which point
/// the layout cost is paid in full for a result nobody sees.
/// </para>
/// <para>
/// The formatting is kept. A preview that dropped it would misrepresent what the row holds, which
/// for a clipboard history is the one thing the row is for.
/// </para>
/// </remarks>
public static class DocumentPreview
{
    /// <summary>Shortens a document to roughly the first <paramref name="maxCharacters"/> of text.</summary>
    /// <param name="doc">The document to shorten.</param>
    /// <param name="maxCharacters">How much text to keep, counted across every block.</param>
    /// <returns>
    /// The shortened document, or <paramref name="doc"/> itself when it was already short enough.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCharacters"/> is negative.</exception>
    /// <remarks>
    /// <para>
    /// <b>Roughly</b>, in two directions. The cut lands on a grapheme boundary rather than a code
    /// unit, so a family emoji or a combining mark is kept whole or dropped whole and never halved
    /// into replacement glyphs — which means the result can be a character or two short. And a
    /// block that is not a paragraph is kept whole or not at all, so a preview never contains half
    /// a table.
    /// </para>
    /// <para>
    /// The result always has at least one block, because a document with none is not one every
    /// consumer will accept. A budget of zero therefore yields one empty paragraph rather than
    /// nothing.
    /// </para>
    /// </remarks>
    public static DocumentNode Truncate(DocumentNode doc, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);

        if (!IsLongerThan(doc, maxCharacters))
        {
            return doc;
        }

        var kept = ImmutableArray.CreateBuilder<BlockNode>();
        var budget = maxCharacters;

        foreach (var block in doc.Blocks)
        {
            if (budget == 0)
            {
                break;
            }

            if (block is ParagraphNode paragraph)
            {
                var length = paragraph.Content.Length;

                if (length <= budget)
                {
                    kept.Add(paragraph);
                    budget -= length;

                    continue;
                }

                var cut = BoundaryAtOrBefore(paragraph.Content.Text, budget);

                if (cut > 0)
                {
                    kept.Add(paragraph.WithContent(paragraph.Content.Substring(0, cut)));
                }

                break;
            }

            // Anything else is kept whole or not at all. Half a table is not a preview of one, and
            // the alternative - recursing into every container to trim it - buys detail that a row
            // two lines high cannot show anyway. Its text is counted rather than built, because
            // images sharing one alt text can make it longer than any string can be.
            var text = 0L;
            var any = false;

            Count(block.Children, budget, ref text, ref any);

            if (text > budget)
            {
                break;
            }

            kept.Add(block);
            budget -= (int)text;
        }

        return new DocumentNode(kept.Count > 0 ? kept.ToImmutable() : [ParagraphNode.Empty]);
    }

    /// <summary>Whether a document holds more text than a budget, without measuring all of it.</summary>
    /// <param name="doc">The document to measure.</param>
    /// <param name="maxCharacters">The budget.</param>
    /// <returns><see langword="true"/> if the document is longer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCharacters"/> is negative.</exception>
    /// <remarks>
    /// <para>
    /// Stops as soon as the budget is exceeded, so asking whether a hundred-page document is
    /// longer than eighty characters costs eighty characters rather than a hundred pages. That is
    /// the whole reason this is not <c>DocumentText.Of(doc).Length &gt; max</c>.
    /// </para>
    /// <para>
    /// Including inside a container. A table or list counts as its plain text, and that used to
    /// be built whole to take its length — 17 MB to measure a 50,000-row table that comes first,
    /// for an answer the first row already gave. It is counted paragraph by paragraph now, the
    /// way <see cref="DocumentText"/> would lay it out, and the count stops where the budget does.
    /// </para>
    /// </remarks>
    public static bool IsLongerThan(DocumentNode doc, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);

        var seen = 0L;

        foreach (var block in doc.Blocks)
        {
            if (block is ParagraphNode paragraph)
            {
                seen += paragraph.Content.Length;
            }
            else
            {
                var any = false;

                Count(block.Children, maxCharacters, ref seen, ref any);
            }

            if (seen > maxCharacters)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds the length of the plain text of <paramref name="nodes"/> to <paramref name="seen"/>,
    /// stopping once it passes <paramref name="maxCharacters"/>.
    /// </summary>
    /// <remarks>
    /// The same count <see cref="DocumentText.Of(IEnumerable{Node})"/> would give the container:
    /// each paragraph's text with every embed as its fallback text, and a newline between
    /// paragraphs.
    /// </remarks>
    private static void Count(ImmutableArray<Node> nodes, int maxCharacters, ref long seen, ref bool any)
    {
        foreach (var node in nodes)
        {
            if (node is ParagraphNode paragraph)
            {
                WorkCounters.PreviewParagraphsMeasured++;

                var content = paragraph.Content;

                seen += (any ? 1 : 0) + content.Length - content.Embeds.Length;
                any = true;

                // Images can share one alt text, so a paragraph's count is bounded by nothing in
                // memory: it can pass int.MaxValue, and it is checked per image rather than per
                // paragraph so that the walk stops where the budget does.
                foreach (var embed in content.Embeds)
                {
                    WorkCounters.PreviewEmbedsMeasured++;

                    seen += embed.PlainTextFallback.Length;

                    if (seen > maxCharacters)
                    {
                        return;
                    }
                }
            }
            else
            {
                Count(node.Children, maxCharacters, ref seen, ref any);
            }

            if (seen > maxCharacters)
            {
                return;
            }
        }
    }

    /// <summary>The last grapheme boundary at or before an offset.</summary>
    /// <remarks>
    /// Code units are the wrong unit to cut on twice over: a surrogate pair halves into a
    /// replacement glyph, and a ZWJ sequence or a combining mark halves into something worse,
    /// because each half is a perfectly valid character that renders as the wrong thing. Walking
    /// the text elements is the only way to know where one actually ends.
    /// </remarks>
    private static int BoundaryAtOrBefore(string text, int offset)
    {
        if (offset >= text.Length)
        {
            return text.Length;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var boundary = 0;

        while (enumerator.MoveNext())
        {
            var next = enumerator.ElementIndex + enumerator.GetTextElement().Length;

            if (next > offset)
            {
                break;
            }

            boundary = next;
        }

        return boundary;
    }
}
