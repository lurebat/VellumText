using System.Globalization;
using System.Text;

namespace VellumText.Interop.Rtf;

/// <summary>
/// Writes a document as Rich Text Format, the shape formatted text has to be in to reach a
/// Windows application through the clipboard.
/// </summary>
/// <remarks>
/// <para>
/// Written by hand rather than by inverting <see cref="RtfImporter"/>, because that import runs
/// through RtfPipe and RtfPipe only reads. Going out through HTML and asking something to convert
/// it back would put a second, unrelated translator between the user's document and the
/// clipboard.
/// </para>
/// <para>
/// The dialect is deliberately conservative: the control words here are the ones that have been
/// understood since Word 97, because the receiving application is not known and the ones that
/// matter — Word, Outlook, WordPad, LibreOffice — disagree about everything newer. Where a
/// construct has a modern spelling and a legacy spelling, both are written when they can coexist,
/// which is why a list item carries its marker as literal <c>\listtext</c> as well as naming its
/// list and level in the list table.
/// </para>
/// <para>
/// RTF has no heading, so one is written as its visual consequence — bold, and at the size the
/// editor draws it — plus <c>\outlinelevel</c>, which is the one part of a heading RTF can state
/// outright. That is enough for the level to come back: a heading exported and re-imported is a
/// heading again, not body text that resembles one. The size and weight travel separately, as
/// appearance, and at level 6 the size is body size, so only the weight and the outline level
/// distinguish it.
/// </para>
/// <para>
/// Round-tripping is proved for every heading level, <c>Heading1</c> through <c>Heading6</c>:
/// both the kind and the exported size. The kind theory covered only the first three until the
/// rest were added and passed; the levels worked, nothing had asserted it. The size was asserted
/// later still, and it was wrong — the exporter had its own ramp that agreed with the editor at
/// level 1 and nowhere else.
/// </para>
/// </remarks>
public sealed class RtfExporter : IDocumentExporter
{
    /// <summary>Twips per pixel, at the 96dpi the rest of the model measures in.</summary>
    /// <remarks>A twip is a twentieth of a point, so an inch is 1440 of them and 96 pixels.</remarks>
    private const double TwipsPerPixel = 1440.0 / 96.0;

    /// <summary>The width of one indent level in twips, which is Word's half inch.</summary>
    private const int IndentTwips = 720;

    /// <summary>How far a list item's marker hangs to the left of its text.</summary>
    private const int HangingIndentTwips = 360;

    /// <summary>The default text size in half-points, matching the model's default of 16px.</summary>
    private const int DefaultHalfPoints = 24;

    /// <summary>A shared instance. The exporter holds no state.</summary>
    public static RtfExporter Instance { get; } = new();

    /// <inheritdoc/>
    public string Format => "rtf";

    /// <inheritdoc/>
    public string MediaType => "text/rtf";

    /// <inheritdoc/>
    public Encoding? TextEncoding => Encoding.Latin1;

    /// <inheritdoc/>
    public void Export(Stream destination, DocumentNode doc) =>
        DocumentPayload.WriteAllText(destination, Encoding.Latin1, Export(doc));

    /// <inheritdoc/>
    public string Export(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        // The tables have to be written before the body but are only known after walking it, so
        // the body is built first and the header is prepended.
        var fonts = new FontTable();
        var colours = new ColourTable();
        var lists = new ListTable();
        var body = new StringBuilder();

        var writer = new Writer(body, fonts, colours, lists);

        writer.WriteBlocks(doc.Blocks);

        var rtf = new StringBuilder();

        rtf.Append(@"{\rtf1\ansi\ansicpg1252\deff0\uc1");
        fonts.Write(rtf);
        colours.Write(rtf);
        lists.Write(rtf);
        rtf.Append(body);
        rtf.Append('}');

        return rtf.ToString();
    }

    /// <summary>The fonts used by a document, in the order they were first asked for.</summary>
    /// <remarks>
    /// Index 0 is always present and is the document default, because <c>\deff0</c> in the header
    /// names it and a reader that finds no such font is entitled to do anything it likes.
    /// </remarks>
    private sealed class FontTable
    {
        private readonly List<string> _fonts = ["Segoe UI"];

        // The list defines the order the table is written in; the dictionary answers "have we
        // seen this one". Searching the list instead made a run's font cost a scan of every
        // distinct font before it, which is quadratic in a document that uses many.
        private readonly Dictionary<string, int> _index =
            new(StringComparer.Ordinal) { ["Segoe UI"] = 0 };

        internal int IndexOf(string? family)
        {
            if (string.IsNullOrWhiteSpace(family))
            {
                return 0;
            }

            if (_index.TryGetValue(family, out var index))
            {
                return index;
            }

            _fonts.Add(family);
            _index[family] = _fonts.Count - 1;

            return _fonts.Count - 1;
        }

        /// <summary>What a level's definition records of a list: its kind, and its start if numbered.</summary>
        private static (ListKind Kind, int Start) Definition(ListNode list) =>
            (list.Kind, list.Kind == ListKind.Ordered ? list.Start : 1);

        internal void Write(StringBuilder rtf)
        {
            rtf.Append(@"{\fonttbl");

            for (var i = 0; i < _fonts.Count; i++)
            {
                rtf.Append(@"{\f")
                    .Append(i.ToString(CultureInfo.InvariantCulture))
                    .Append(@"\fnil ");

                // A font name is text like any other: it can contain a brace, a backslash or a
                // character outside the code page, and Word's own output regularly does. A
                // semicolon is the exception: it ends the entry, and no escape un-ends it, so
                // "Weird;Font" was the font "Weird" followed by stray text. It is left out.
                Text.Write(rtf, _fonts[i].Replace(";", string.Empty, StringComparison.Ordinal));

                rtf.Append(";}");
            }

            rtf.Append('}');
        }
    }

    /// <summary>The colours used by a document, in the order they were first asked for.</summary>
    /// <remarks>
    /// Index 0 is the "auto" colour and has no definition of its own, which is what the empty
    /// first entry in the table means. Writing <c>\cf0</c> is how a run says it wants whatever
    /// the reader considers normal, rather than naming black and being wrong on a dark theme.
    /// </remarks>
    private sealed class ColourTable
    {
        private readonly List<Rgba> _colours = [];

        // As with the fonts: the list is the written order, the dictionary is the lookup. A
        // document that colours every run its own shade was scanning the whole table per run.
        private readonly Dictionary<Rgba, int> _index = [];

        internal int IndexOf(Rgba? colour)
        {
            if (colour is not { } value)
            {
                return 0;
            }

            if (!_index.TryGetValue(value, out var index))
            {
                _colours.Add(value);
                index = _colours.Count - 1;
                _index[value] = index;
            }

            // Offset by the auto entry, which occupies index 0 without appearing in the list.
            return index + 1;
        }

        /// <summary>What a level's definition records of a list: its kind, and its start if numbered.</summary>
        private static (ListKind Kind, int Start) Definition(ListNode list) =>
            (list.Kind, list.Kind == ListKind.Ordered ? list.Start : 1);

        internal void Write(StringBuilder rtf)
        {
            rtf.Append(@"{\colortbl;");

            foreach (var colour in _colours)
            {
                rtf.Append(@"\red").Append(colour.R.ToString(CultureInfo.InvariantCulture))
                    .Append(@"\green").Append(colour.G.ToString(CultureInfo.InvariantCulture))
                    .Append(@"\blue").Append(colour.B.ToString(CultureInfo.InvariantCulture))
                    .Append(';');
            }

            rtf.Append('}');
        }
    }

    /// <summary>The numbering definitions of a document's lists, one per top-level list.</summary>
    /// <remarks>
    /// <para>
    /// A list's nesting and numbering live here, in <c>\listtable</c> and
    /// <c>\listoverridetable</c>, and each paragraph only names its list (<c>\ls</c>) and level
    /// (<c>\ilvl</c>). The old per-paragraph <c>\pn</c> carried no level, so readers flattened
    /// nested lists, and it carried no identity, so two lists written back to back merged.
    /// </para>
    /// <para>
    /// A level's kind and start come from the first list at that depth. RTF numbers a level the
    /// same way throughout its list, so a nested list whose kind or start differs from what its
    /// level already says gets a list definition of its own, copied from its ancestors' levels,
    /// and its own <c>\ls</c>. Before, it was written with the first sibling's numbering, and a
    /// reader that honours <c>\listtable</c> read a numbered sublist starting at 3 back as
    /// bullets from 1 (rubber-duck review 5.2).
    /// </para>
    /// </remarks>
    private sealed class ListTable
    {
        /// <summary>The deepest level RTF defines; deeper nesting shares it.</summary>
        private const int MaxLevel = 8;

        private readonly List<(ListKind Kind, int Start)?[]> _lists = [];

        /// <summary>The <c>\ls</c> of each nested list that needed a definition of its own.</summary>
        private readonly Dictionary<ListNode, int> _own = new(ReferenceEqualityComparer.Instance);

        /// <summary>The <c>\ilvl</c> for a nesting depth.</summary>
        internal static int Level(int depth) => Math.Min(depth, MaxLevel);

        /// <summary>Defines a top-level list, and returns its <c>\ls</c> number.</summary>
        internal int Add(ListNode list)
        {
            var levels = new (ListKind Kind, int Start)?[MaxLevel + 1];

            _lists.Add(levels);

            var number = _lists.Count;

            Collect(list, 0, levels);

            return number;
        }

        /// <summary>The <c>\ls</c> a nested list is written with: its own, or its parent's.</summary>
        internal int NumberOf(ListNode list, int parent) => _own.TryGetValue(list, out var own) ? own : parent;

        private void Collect(ListNode list, int depth, (ListKind Kind, int Start)?[] levels)
        {
            if (depth > MaxLevel)
            {
                return;
            }

            var definition = Definition(list);

            if (levels[depth] is { } recorded && recorded != definition)
            {
                // Its ancestors' levels as they are, its own at its depth, and its descendants'
                // to come; nothing a sibling put deeper.
                var own = new (ListKind Kind, int Start)?[MaxLevel + 1];

                Array.Copy(levels, own, depth);
                levels = own;
                _lists.Add(levels);
                _own[list] = _lists.Count;
            }

            levels[depth] = definition;

            foreach (var item in list.Items)
            {
                foreach (var block in item.Blocks)
                {
                    if (block is ListNode nested)
                    {
                        Collect(nested, depth + 1, levels);
                    }
                }
            }
        }

        /// <summary>What a level's definition records of a list: its kind, and its start if numbered.</summary>
        private static (ListKind Kind, int Start) Definition(ListNode list) =>
            (list.Kind, list.Kind == ListKind.Ordered ? list.Start : 1);

        internal void Write(StringBuilder rtf)
        {
            if (_lists.Count == 0)
            {
                return;
            }

            rtf.Append(@"{\*\listtable");

            for (var i = 0; i < _lists.Count; i++)
            {
                var id = (i + 1).ToString(CultureInfo.InvariantCulture);

                rtf.Append(@"{\list\listtemplateid").Append(id);

                for (var level = 0; level <= MaxLevel; level++)
                {
                    WriteLevel(rtf, level, _lists[i][level] ?? (ListKind.Unordered, 1));
                }

                rtf.Append(@"{\listname ;}\listid").Append(id).Append('}');
            }

            rtf.Append(@"}{\*\listoverridetable");

            for (var i = 0; i < _lists.Count; i++)
            {
                var id = (i + 1).ToString(CultureInfo.InvariantCulture);

                rtf.Append(@"{\listoverride\listid").Append(id)
                    .Append(@"\listoverridecount0\ls").Append(id).Append('}');
            }

            rtf.Append('}');
        }

        /// <summary>One level's definition: its numbering, start and indent.</summary>
        /// <remarks>
        /// <c>\leveltext</c> is a length byte then the text, in which a number placeholder is the
        /// index of the level it shows: <c>\'02\'00.</c> is "level 0's number, then a full stop".
        /// The bullet is <c>\'95</c>, which is "•" in the code page the header names.
        /// </remarks>
        private static void WriteLevel(StringBuilder rtf, int level, (ListKind Kind, int Start) definition)
        {
            var ordered = definition.Kind == ListKind.Ordered;
            var format = ordered ? "0" : "23";

            rtf.Append(@"{\listlevel\levelnfc").Append(format)
                .Append(@"\levelnfcn").Append(format)
                .Append(@"\leveljc0\leveljcn0\levelfollow0\levelstartat")
                .Append((ordered ? definition.Start : 1).ToString(CultureInfo.InvariantCulture))
                .Append(@"\levelspace0\levelindent0");

            rtf.Append(ordered
                ? $@"{{\leveltext\'02\'0{level.ToString(CultureInfo.InvariantCulture)}.;}}{{\levelnumbers\'01;}}"
                : @"{\leveltext\'01\'95;}{\levelnumbers;}");

            rtf.Append(@"\fi-").Append(HangingIndentTwips.ToString(CultureInfo.InvariantCulture))
                .Append(@"\li").Append(((level + 1) * IndentTwips).ToString(CultureInfo.InvariantCulture))
                .Append('}');
        }
    }

    /// <summary>Escaping, which is the same everywhere text appears.</summary>
    private static class Text
    {
        internal static void Write(StringBuilder rtf, string text)
        {
            foreach (var c in text)
            {
                Write(rtf, c);
            }
        }

        /// <summary>Writes a quoted argument of a field instruction.</summary>
        /// <param name="rtf">The output.</param>
        /// <param name="value">The argument.</param>
        /// <param name="address">
        /// Whether it is a URL, where a quote is percent-encoded rather than escaped.
        /// </param>
        /// <remarks>
        /// A field instruction has its own quoting inside RTF's: a quote ends the argument and a
        /// backslash escapes the next character. Written as plain text, <c>a\b</c> came back as
        /// <c>ab</c>, and a quote ended the address — dropping the link, or, for
        /// <c>" "javascript:…</c>, starting a second argument that RtfPipe-based readers take as
        /// the target. A backslash is doubled for the field and again for RTF. A quote in an address
        /// is <c>%22</c>, which every URL parser reads as the same address and no field reader can
        /// mistake; a quote in other text has to stay a quote, so it is escaped.
        /// </remarks>
        internal static void WriteFieldArgument(StringBuilder rtf, string value, bool address)
        {
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"' when address:
                        rtf.Append("%22");
                        break;

                    case '"':
                        rtf.Append(@"\\""");
                        break;

                    case '\\':
                        rtf.Append(@"\\\\");
                        break;

                    default:
                        Write(rtf, c);
                        break;
                }
            }
        }

        internal static void Write(StringBuilder rtf, char c)
        {
            switch (c)
            {
                case '\\':
                    rtf.Append(@"\\");
                    return;

                case '{':
                    rtf.Append(@"\{");
                    return;

                case '}':
                    rtf.Append(@"\}");
                    return;

                case '\n':
                    rtf.Append(@"\line ");
                    return;

                case '\t':
                    rtf.Append(@"\tab ");
                    return;

                case '\r':
                    // A stray carriage return is not a second line break; the newline beside it
                    // already was one.
                    return;
            }

            if (c is >= ' ' and <= '~')
            {
                rtf.Append(c);
                return;
            }

            // \u takes a signed 16-bit integer, so anything above U+7FFF is written negative --
            // and a surrogate pair is written as its two halves, which is what a reader that
            // understands \u at all expects. The trailing "?" is the replacement for readers too
            // old to understand it, and \uc1 in the header is what says there is exactly one.
            rtf.Append(@"\u")
                .Append(((short)c).ToString(CultureInfo.InvariantCulture))
                .Append('?');
        }
    }

    /// <summary>Walks the document, holding the state RTF needs to be told about explicitly.</summary>
    private sealed class Writer(StringBuilder rtf, FontTable fonts, ColourTable colours, ListTable lists)
    {
        /// <summary>How many table cells the block being written is inside.</summary>
        /// <remarks>
        /// Every paragraph in a cell has to say so with <c>\intbl</c>, and a reader that meets one
        /// without it ends the table there: a list or a second paragraph in a cell used to be
        /// written as ordinary paragraphs and split the table in two.
        /// </remarks>
        private int _cellDepth;

        /// <summary>Whether the cell being written is a header cell, whose text is bold.</summary>
        private bool _headerCell;

        /// <summary>Whether the last block written was a table, which the next table cannot touch.</summary>
        private bool _afterTable;

        internal void WriteBlocks(IEnumerable<BlockNode> blocks)
        {
            foreach (var block in blocks)
            {
                WriteBlock(block);
            }
        }

        /// <summary>Starts a paragraph, inside a table if one is being written.</summary>
        private void Pard()
        {
            rtf.Append(@"\pard");

            if (_cellDepth > 0)
            {
                rtf.Append(@"\intbl");
            }

            _afterTable = false;
        }

        private void WriteBlock(BlockNode block)
        {
            switch (block)
            {
                case ParagraphNode paragraph:
                    WriteParagraph(paragraph);
                    return;

                case ListNode list:
                    WriteList(list, depth: 0);
                    return;

                case TableNode table when _cellDepth > 0:
                    FlattenTable(table);
                    return;

                case TableNode table:
                    WriteTable(table);
                    return;

                case RuleNode:
                    // There is no horizontal rule in RTF, only a paragraph with a rule drawn under
                    // it. \pard afterwards is what keeps the border off the paragraph that follows;
                    // inside a cell the next paragraph or the end of the cell resets it anyway.
                    Pard();
                    rtf.Append(@"\brdrb\brdrs\brdrw10\brsp20\par");

                    if (_cellDepth == 0)
                    {
                        rtf.Append(@"\pard");
                    }

                    return;

                case BlockImageNode image:
                    Pard();
                    WriteAlignment(image.Align);
                    WriteImage(image.Image);
                    rtf.Append(@"\par");
                    return;

                default:
                    // Reachable only by growing the schema without extending this exporter, which
                    // is exactly when silence would lose a user's content without a trace.
                    throw new NotSupportedException(
                        $"The RTF exporter does not know how to write a '{block.TypeName}' block.");
            }
        }

        private void WriteParagraph(ParagraphNode paragraph)
        {
            Pard();
            WriteParagraphProperties(paragraph);
            WriteContent(
                paragraph.Content,
                HeadingScale(paragraph.Kind),
                Emphasised(paragraph.Kind) || _headerCell);
            rtf.Append(@"\par");
        }

        private void WriteParagraphProperties(ParagraphNode paragraph)
        {
            WriteAlignment(paragraph.Align);

            var indent = paragraph.IndentLevel;

            // A quote is an indent as far as RTF is concerned. Saying so keeps a quoted paragraph
            // looking quoted in an application that has no notion of one.
            if (paragraph.Kind == ParagraphKind.Quote)
            {
                indent++;
            }

            if (indent > 0)
            {
                rtf.Append(@"\li").Append((indent * IndentTwips).ToString(CultureInfo.InvariantCulture));
            }

            if (OutlineLevel(paragraph.Kind) is { } level)
            {
                rtf.Append(@"\outlinelevel").Append(level.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void WriteAlignment(TextAlign align)
        {
            rtf.Append(align switch
            {
                TextAlign.Center => @"\qc",
                TextAlign.Right => @"\qr",
                TextAlign.Justify => @"\qj",
                TextAlign.Left => @"\ql",
                _ => string.Empty,
            });
        }

        /// <summary>Writes inline content as a sequence of runs, each in its own group.</summary>
        /// <param name="content">The content.</param>
        /// <param name="scale">A multiplier on the default text size, for headings; an explicit size is not scaled.</param>
        /// <param name="alwaysBold">Whether the whole run is bold regardless of its marks.</param>
        /// <remarks>
        /// A group is used per run rather than turning each property off again afterwards.
        /// Switching off by hand means every new property needs a matching reset in every other
        /// branch, and the one that gets forgotten leaks formatting across the rest of the
        /// document. A group cannot leak.
        /// </remarks>
        private void WriteContent(InlineContent content, double scale, bool alwaysBold)
        {
            var embed = 0;

            foreach (var run in content.Runs())
            {
                var mark = run.Value;
                var link = mark.Link;

                if (link is not null)
                {
                    rtf.Append(@"{\field{\*\fldinst{HYPERLINK """);
                    Text.WriteFieldArgument(rtf, link.Href, address: true);
                    rtf.Append('"');

                    // The \o switch is the link's tooltip, which is what a title is.
                    if (link.Title is { } title)
                    {
                        rtf.Append(@" \\o """);
                        Text.WriteFieldArgument(rtf, title, address: false);
                        rtf.Append('"');
                    }

                    rtf.Append(@"}}{\fldrslt");
                }

                rtf.Append('{');
                WriteRunProperties(mark, scale, alwaysBold, underlined: link is not null);

                for (var i = run.Start; i < run.End; i++)
                {
                    var c = content.Text[i];

                    if (c == InlineContent.Placeholder)
                    {
                        if (embed < content.Embeds.Length && content.Embeds[embed] is ImageEmbed image)
                        {
                            WriteImage(image);
                        }

                        embed++;
                        continue;
                    }

                    Text.Write(rtf, c);
                }

                rtf.Append('}');

                if (link is not null)
                {
                    rtf.Append("}}");
                }
            }
        }

        private void WriteRunProperties(MarkSet mark, double scale, bool alwaysBold, bool underlined)
        {
            var start = rtf.Length;

            if (mark.FontFamily is { } family)
            {
                rtf.Append(@"\f").Append(fonts.IndexOf(family).ToString(CultureInfo.InvariantCulture));
            }

            // The heading's scale is the size of text that asked for none. Text that asked for a
            // size is that size: scaling it too wrote 12px in a Heading1 as 24, which reads back as
            // an explicit 24 and doubled again on every copy and paste.
            var halfPoints = (int)Math.Round(
                mark.FontSize is { } size ? size * 1.5 : DefaultHalfPoints * scale,
                MidpointRounding.AwayFromZero);

            if (halfPoints != DefaultHalfPoints)
            {
                rtf.Append(@"\fs").Append(halfPoints.ToString(CultureInfo.InvariantCulture));
            }

            if (mark.Foreground is not null)
            {
                rtf.Append(@"\cf").Append(colours.IndexOf(mark.Foreground).ToString(CultureInfo.InvariantCulture));
            }

            if (mark.Highlight is not null)
            {
                rtf.Append(@"\highlight").Append(colours.IndexOf(mark.Highlight).ToString(CultureInfo.InvariantCulture));
            }

            if (alwaysBold || mark.Has(TextStyle.Bold))
            {
                rtf.Append(@"\b");
            }

            if (mark.Has(TextStyle.Italic))
            {
                rtf.Append(@"\i");
            }

            // A link is underlined because that is what a link looks like, not because the model
            // said so -- and a reader that drops the field keeps the appearance.
            if (underlined || mark.Has(TextStyle.Underline))
            {
                rtf.Append(@"\ul");
            }

            if (mark.Has(TextStyle.Strikethrough))
            {
                rtf.Append(@"\strike");
            }

            if (mark.Has(TextStyle.Super))
            {
                rtf.Append(@"\super");
            }

            if (mark.Has(TextStyle.Sub))
            {
                rtf.Append(@"\sub");
            }

            // Monospace is the whole of what inline code means visually, and RTF has no way to
            // say "code" other than to name a font that looks like it.
            if (mark.Has(TextStyle.Code) && mark.FontFamily is null)
            {
                rtf.Append(@"\f").Append(fonts.IndexOf("Consolas").ToString(CultureInfo.InvariantCulture));
            }

            // Separates the last control word from the text, and is consumed as part of it. Only
            // when there was a control word: a group with no properties needs no delimiter, and a
            // space written anyway is not a delimiter but a space in the user's text.
            if (rtf.Length > start)
            {
                rtf.Append(' ');
            }
        }

        private void WriteList(ListNode list, int depth, int? listNumber = null)
        {
            var ls = listNumber ?? lists.Add(list);
            var number = list.Start;

            foreach (var item in list.Items)
            {
                var marker = list.Kind == ListKind.Ordered
                    ? $"{number.ToString(CultureInfo.InvariantCulture)}."
                    : "\u2022";

                WriteListItem(item, marker, depth, ls);
                number++;
            }
        }

        /// <summary>Writes one list item: its first paragraph numbered, the rest under it.</summary>
        /// <remarks>
        /// <para>
        /// The first paragraph names its list and level with <c>\ls</c> and <c>\ilvl</c>, which is
        /// what readers nest and number by. Depth carried by the indent alone, as it used to be, is
        /// not nesting to any reader — a nested list came back flat — and two lists that share a
        /// numbering definition are one list, which merged lists written back to back. The marker
        /// is also written as literal text in <c>\listtext</c>, for readers that know no list
        /// table and print it instead.
        /// </para>
        /// <para>
        /// The item's later paragraphs are written as indented paragraphs outside the numbering,
        /// which is how Word writes them. RTF has no multi-paragraph list item, so a reader either
        /// ends the list there or takes the paragraph for a new item; neither is this writer's to
        /// fix.
        /// </para>
        /// </remarks>
        private void WriteListItem(ListItemNode item, string marker, int depth, int ls)
        {
            var first = true;

            foreach (var block in item.Blocks)
            {
                // A nested list is the next level of this list, not a list of its own: one \ls
                // with a deeper \ilvl is what a reader nests.
                if (block is ListNode nested)
                {
                    WriteList(nested, depth + 1, lists.NumberOf(nested, ls));
                    continue;
                }

                if (block is not ParagraphNode paragraph)
                {
                    WriteBlock(block);
                    continue;
                }

                var indent = ((depth + 1) * IndentTwips) + (paragraph.IndentLevel * IndentTwips);

                Pard();
                WriteAlignment(paragraph.Align);

                if (first)
                {
                    rtf.Append(@"\ls").Append(ls.ToString(CultureInfo.InvariantCulture))
                        .Append(@"\ilvl").Append(ListTable.Level(depth).ToString(CultureInfo.InvariantCulture));
                }

                rtf.Append(@"\li").Append(indent.ToString(CultureInfo.InvariantCulture));

                if (first)
                {
                    // The marker hangs to the left of the text, which is what a list looks like.
                    rtf.Append(@"\fi-").Append(HangingIndentTwips.ToString(CultureInfo.InvariantCulture));
                }

                if (OutlineLevel(paragraph.Kind) is { } level)
                {
                    rtf.Append(@"\outlinelevel").Append(level.ToString(CultureInfo.InvariantCulture));
                }

                if (first)
                {
                    rtf.Append(@"{\listtext ");
                    Text.Write(rtf, marker);
                    rtf.Append(@"\tab}");
                }

                WriteContent(
                    paragraph.Content,
                    HeadingScale(paragraph.Kind),
                    Emphasised(paragraph.Kind) || _headerCell);
                rtf.Append(@"\par");
                first = false;
            }
        }

        /// <summary>Writes a table as rows of cells, each row declaring its own geometry.</summary>
        /// <remarks>
        /// <para>
        /// RTF has no table element. A row is a run of paragraphs that happens to be preceded by
        /// a row definition and punctuated by <c>\cell</c>, and the definition has to be repeated
        /// in front of every row because a reader keeps no memory of the last one.
        /// </para>
        /// <para>
        /// The rows are written from the grid <see cref="TableGeometry.Place"/> works out, never from
        /// each row's cell array. A cell spanning rows leaves the rows below it holding fewer cells
        /// than the table is wide; written in order, every one of them landed a column too far left.
        /// From the grid, the first row of a span is <c>\clvmgf</c> and each covered slot below it is
        /// an empty <c>\clvmrg</c> cell of the same width, so everything after it stays in its
        /// column. A column span is the width of its <c>\cellx</c>, which readers line up across
        /// rows; <c>\clmgf</c> is not used, because RtfPipe reads it as a span of the wrong cell.
        /// </para>
        /// <para>
        /// A table inside a cell is flattened into that cell's paragraphs, row by row: nested
        /// tables need RTF 1.8's <c>\itap</c> and <c>\nesttableprops</c>, which readers handle
        /// inconsistently — RtfPipe turns one into an extra row of the outer table. The text and
        /// its formatting survive; the inner grid does not.
        /// </para>
        /// </remarks>
        private void WriteTable(TableNode table)
        {
            const int DefaultCellTwips = 1440;

            // Two tables with nothing between them are one table to every reader: the second's
            // rows simply continue the first. An empty paragraph is the separator Word itself uses.
            if (_afterTable)
            {
                rtf.Append(@"\pard\par");
            }

            var placements = TableGeometry.Place(table, out var width);
            var rows = table.Rows.Length;
            var owner = new (TableCellNode Cell, CellPlacement Place)?[rows, width];
            var index = 0;

            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    var place = placements[index++];

                    for (var r = place.Top; r < Math.Min(place.Bottom, rows); r++)
                    {
                        for (var c = place.Left; c < Math.Min(place.Right, width); c++)
                        {
                            owner[r, c] = (cell, place);
                        }
                    }
                }
            }

            for (var r = 0; r < rows; r++)
            {
                rtf.Append(@"\trowd\trgaph108\trleft0");

                var slots = new List<(TableCellNode Cell, bool Continued)>();

                for (var c = 0; c < width;)
                {
                    if (owner[r, c] is not { } slot)
                    {
                        // A row shorter than the table, with nothing below a span to fill it. The
                        // row simply ends; padding it would invent cells the document never had.
                        break;
                    }

                    var (cell, place) = slot;
                    var continued = place.Top < r;
                    var spansRows = Math.Min(place.Bottom, rows) - place.Top > 1;

                    if (spansRows)
                    {
                        rtf.Append(continued ? @"\clvmrg" : @"\clvmgf");
                    }

                    if (cell.Background is { } background)
                    {
                        rtf.Append(@"\clcbpat")
                            .Append(colours.IndexOf(background).ToString(CultureInfo.InvariantCulture));
                    }

                    var right = Math.Min(place.Right, width);

                    rtf.Append(@"\clbrdrt\brdrs\clbrdrl\brdrs\clbrdrb\brdrs\clbrdrr\brdrs");
                    rtf.Append(@"\cellx").Append((right * DefaultCellTwips).ToString(CultureInfo.InvariantCulture));

                    slots.Add((cell, continued));
                    c = right;
                }

                foreach (var (cell, continued) in slots)
                {
                    if (continued)
                    {
                        rtf.Append(@"\pard\intbl\cell");
                        continue;
                    }

                    WriteCell(cell);
                }

                rtf.Append(@"\row");
            }

            rtf.Append(@"\pard");
            _afterTable = true;
        }

        /// <summary>Writes one cell's blocks, each one a paragraph inside the table.</summary>
        /// <remarks>
        /// Paragraphs inside a cell are separated by <c>\par</c> and the last one is ended by
        /// <c>\cell</c> instead — a cell's text written without either ran its paragraphs together,
        /// "Hello" and "World" arriving as "HelloWorld".
        /// </remarks>
        private void WriteCell(TableCellNode cell)
        {
            var header = _headerCell;
            var start = rtf.Length;

            _cellDepth++;
            _headerCell = cell.IsHeader;

            try
            {
                WriteBlocks(cell.Blocks);
            }
            finally
            {
                _cellDepth--;
                _headerCell = header;
            }

            const string Par = @"\par";

            if (rtf.Length - start >= Par.Length
                && rtf.ToString(rtf.Length - Par.Length, Par.Length) == Par)
            {
                rtf.Length -= Par.Length;
                rtf.Append(@"\cell");
            }
            else
            {
                rtf.Append(@"\pard\intbl\cell");
            }
        }

        /// <summary>Writes a table that is inside a cell as the cell's own paragraphs.</summary>
        private void FlattenTable(TableNode table)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    var header = _headerCell;

                    _headerCell = header || cell.IsHeader;

                    try
                    {
                        WriteBlocks(cell.Blocks);
                    }
                    finally
                    {
                        _headerCell = header;
                    }
                }
            }
        }

        /// <summary>Writes an image, when its bytes are something RTF can be given.</summary>
        /// <remarks>
        /// Only a data URI can be embedded. RTF carries picture data inline and has no way to
        /// reference one, so an image held as an <c>http</c> URL would have to be fetched — and an
        /// exporter that reaches out to the network to serialise a document is an exporter that
        /// hangs, leaks the fact that the document was copied, and cannot be used offline. Its
        /// alternative text is written instead, so the reader is told what is missing.
        /// </remarks>
        private void WriteImage(ImageEmbed image)
        {
            if (!TryReadPicture(image.Source, out var bytes, out var kind))
            {
                if (image.AltText is { Length: > 0 } alt)
                {
                    Text.Write(rtf, alt);
                }

                return;
            }

            rtf.Append(@"{\pict");

            // Word reads a picture's alternative text from its wzDescription shape property.
            // Without it the alt text was simply not in the RTF.
            if (image.AltText is { Length: > 0 } description)
            {
                rtf.Append(@"{\*\picprop{\sp{\sn wzDescription}{\sv ");
                Text.Write(rtf, description);
                rtf.Append("}}}");
            }

            rtf.Append(kind);

            if (image.Width is { } width)
            {
                rtf.Append(@"\picwgoal")
                    .Append(((int)Math.Round(width * TwipsPerPixel)).ToString(CultureInfo.InvariantCulture));
            }

            if (image.Height is { } height)
            {
                rtf.Append(@"\pichgoal")
                    .Append(((int)Math.Round(height * TwipsPerPixel)).ToString(CultureInfo.InvariantCulture));
            }

            rtf.Append(' ');

            foreach (var b in bytes)
            {
                rtf.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            rtf.Append('}');
        }
    }

    /// <summary>How much larger than body text a heading is drawn.</summary>
    /// <remarks>
    /// RTF cannot say "heading", so the size is the only part of the meaning that can be carried,
    /// and it has to agree with what the user was looking at when they pressed copy. That is why
    /// the ratios come from the model rather than being restated here: a second table drifted from
    /// the editor's at every level below the first.
    /// </remarks>
    private static double HeadingScale(ParagraphKind kind) => HeadingAppearance.Scale(kind);

    private static bool Emphasised(ParagraphKind kind) => HeadingAppearance.IsHeading(kind);

    private static int? OutlineLevel(ParagraphKind kind) => HeadingAppearance.OutlineLevel(kind);

    /// <summary>Reads the picture bytes, and the control word naming their format, out of a <c>data:</c> URI.</summary>
    private static bool TryReadPicture(string source, out byte[] bytes, out string kind)
    {
        bytes = [];
        kind = string.Empty;

        if (!DataUri.TryParse(source, out var mediaType, out var base64))
        {
            return false;
        }

        // Only the two formats every RTF reader has a control word for. A GIF or an SVG would
        // have to be re-encoded, and an exporter is the wrong place to be decoding images.
        kind = mediaType.ToLowerInvariant() switch
        {
            "image/png" => @"\pngblip",
            "image/jpeg" or "image/jpg" => @"\jpegblip",
            _ => string.Empty,
        };

        if (kind.Length == 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(base64.ToString());
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
