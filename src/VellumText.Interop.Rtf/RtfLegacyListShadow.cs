using OfficeIMO.Rtf;

namespace VellumText.Interop.Rtf;

/// <summary>
/// Drops a legacy <c>\pn</c> numbering the reader carried past the paragraph that declared it.
/// </summary>
/// <remarks>
/// <para>
/// Word writes each list paragraph twice over: <c>\ls</c> for its list table and a
/// <c>{\*\pn …}</c> group as a shadow for readers that predate list tables. When the
/// <c>\par</c> that ends such a paragraph sits inside a group, closing the group restores the
/// <c>\pn</c> state, and the reader's next <c>\pard</c> clears <c>\ls</c> but not <c>\pn</c>. The
/// following paragraphs then read as legacy-only bullets. In Word's clipboard RTF they are a
/// table's header row, and every header cell came out as a bulleted list.
/// </para>
/// <para>
/// So in a document with a list table, a paragraph with <c>\pn</c> and no <c>\ls</c> whose
/// numbering is the one the last <c>\ls</c> paragraph shadowed, with no paragraph between that
/// reset it, is a shadow out of place, and is made plain. A paragraph that numbers itself with a
/// <c>\pn</c> of its own is a list as its writer meant (rubber-duck review finding 9.5), and so
/// is every one in a document without a list table (WordPad, older writers).
/// </para>
/// <para>
/// A correction on the reader's model, not a parser: it runs after reading and before the
/// model is written as HTML, like <see cref="RtfFormattingHoist"/>.
/// </para>
/// </remarks>
internal static class RtfLegacyListShadow
{
    /// <summary>Makes plain every paragraph numbered only by a stray <c>\pn</c>.</summary>
    /// <param name="document">The reader's model, corrected in place.</param>
    internal static void Drop(RtfDocument document)
    {
        if (document.ListOverrides.Count == 0)
        {
            return;
        }

        // The shadow is carried in reading order, so the walk is in reading order too: a table's
        // cells, row by row, before what follows the table.
        RtfLegacyNumbering? shadow = null;
        var pending = new Stack<(IReadOnlyList<IRtfBlock> Blocks, int Next)>();

        pending.Push((document.Blocks, 0));

        while (pending.TryPop(out var frame))
        {
            var (blocks, next) = frame;

            while (next < blocks.Count)
            {
                var block = blocks[next++];

                if (block is RtfParagraph paragraph)
                {
                    shadow = Correct(paragraph, shadow);
                }
                else if (block is RtfTable table)
                {
                    pending.Push((blocks, next));
                    pending.Push(([.. table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Blocks)], 0));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="paragraph"/> plain if it only inherits <paramref name="shadow"/>, and
    /// returns the shadow the next paragraph may inherit.
    /// </summary>
    private static RtfLegacyNumbering? Correct(RtfParagraph paragraph, RtfLegacyNumbering? shadow)
    {
        var numbering = paragraph.LegacyNumbering;

        if (paragraph.ListId is not null)
        {
            return numbering.Enabled ? numbering : null;
        }

        if (!numbering.Enabled || shadow is null || !Same(numbering, shadow))
        {
            return null;
        }

        numbering.Clear();
        paragraph.ListKind = RtfListKind.None;

        return shadow;
    }

    private static bool Same(RtfLegacyNumbering a, RtfLegacyNumbering b) =>
        a.LevelKind == b.LevelKind
        && a.Level == b.Level
        && a.NumberStyle == b.NumberStyle
        && a.StartAt == b.StartAt
        && a.IndentTwips == b.IndentTwips
        && a.FontId == b.FontId
        && a.TextBefore == b.TextBefore
        && a.TextAfter == b.TextAfter;
}
