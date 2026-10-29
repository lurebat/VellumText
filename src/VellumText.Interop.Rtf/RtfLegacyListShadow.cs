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
/// following paragraph then reads as a legacy-only bullet. In Word's clipboard RTF that paragraph
/// is a table's header row, and every header cell came out as a bulleted list.
/// </para>
/// <para>
/// A document with a list table states every list paragraph with <c>\ls</c>, so in such a
/// document a paragraph with <c>\pn</c> and no <c>\ls</c> is a shadow out of place, and is made
/// plain. A document without one (WordPad, older writers) numbers with <c>\pn</c> alone, and is
/// left as it is.
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

        var pending = new Stack<IReadOnlyList<IRtfBlock>>();

        pending.Push(document.Blocks);

        while (pending.TryPop(out var blocks))
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case RtfParagraph { ListId: null, LegacyNumbering.Enabled: true } paragraph:
                        paragraph.LegacyNumbering.Clear();
                        paragraph.ListKind = RtfListKind.None;
                        break;

                    case RtfTable table:
                        foreach (var row in table.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                pending.Push(cell.Blocks);
                            }
                        }

                        break;
                }
            }
        }
    }
}
