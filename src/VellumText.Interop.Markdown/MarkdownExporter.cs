using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace VellumText.Interop.Markdown;

/// <summary>
/// Writes a document as CommonMark with GitHub-flavoured pipe tables, in the dialect
/// <see cref="MarkdownImporter"/> reads back.
/// </summary>
/// <remarks>
/// <para>
/// Every choice here is made against the importer rather than against a style guide: this is the
/// output half of a round trip, and a construct written in a form the importer does not read is a
/// construct silently lost the moment a user saves and reopens. Where the two disagree the
/// round-trip tests are the arbiter, not this comment.
/// </para>
/// <para>
/// <b>Markdown is smaller than the document model</b>, which no other format here is. Colour,
/// highlight, font family, font size, underline, subscript, superscript, paragraph alignment,
/// indent, merged cells and cell backgrounds have no notation at all. They are reported through
/// <see cref="ExportWithDiagnostics"/> rather than dropped in silence — see
/// <see cref="MarkdownExportResult"/> for why that is a method of its own instead of a change to
/// <see cref="IDocumentExporter"/>.
/// </para>
/// <para>
/// <b>Escaping is deliberately literal-minded.</b> Every character that can begin an inline
/// construct is escaped wherever it appears, rather than only where it would actually be read as
/// one. The output is uglier — <c>AT&amp;T</c> is written <c>AT\&amp;T</c> — and it is exact. A
/// writer that guesses when a character is inert is a writer that is wrong on somebody else's text,
/// and the failure shows up as a user's asterisks turning into italics.
/// </para>
/// </remarks>
public sealed class MarkdownExporter : IDocumentExporter
{
    /// <summary>A shared instance. The exporter holds no state.</summary>
    public static MarkdownExporter Instance { get; } = new();

    /// <inheritdoc/>
    public string Format => "markdown";

    /// <inheritdoc/>
    /// <remarks>
    /// <c>text/markdown</c> is the registered type (RFC 7763). <c>text/x-markdown</c> is still seen
    /// in the wild but was never registered, and a reader that only knows one of them is far more
    /// likely to know the registered one.
    /// </remarks>
    public string MediaType => "text/markdown";

    /// <inheritdoc/>
    public Encoding? TextEncoding => Encoding.UTF8;

    /// <inheritdoc/>
    public string Export(DocumentNode doc) => Write(doc).Text;

    /// <inheritdoc/>
    public void Export(Stream destination, DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(destination);

        DocumentPayload.WriteAllText(destination, Encoding.UTF8, Write(doc).Text);
    }

    /// <summary>Writes the document and reports everything Markdown could not express.</summary>
    /// <param name="doc">The document.</param>
    /// <returns>The Markdown, and the list of compromises.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
    /// <remarks>
    /// The text is identical to what <see cref="Export(DocumentNode)"/> returns. The only
    /// difference is that the caller finds out what it cost.
    /// </remarks>
    public MarkdownExportResult ExportWithDiagnostics(DocumentNode doc) => Write(doc);

    private static MarkdownExportResult Write(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var writer = new Writer();

        writer.WriteBlocks(doc.Blocks);

        return writer.Result;
    }

    /// <summary>
    /// One export in progress: the text so far, the current list indentation, and the losses.
    /// </summary>
    /// <remarks>
    /// An instance per export rather than static methods with an accumulator parameter, because the
    /// indentation is a stack and the diagnostics are a set, and threading both through every
    /// method would bury the actual mapping under plumbing.
    /// </remarks>
    private sealed class Writer
    {
        private readonly StringBuilder _text = new();
        private readonly DiagnosticLog _log = new();

        /// <summary>What every line is prefixed with, which is how nested lists indent.</summary>
        private string _indent = string.Empty;

        /// <summary>
        /// A prefix that replaces <see cref="_indent"/> on the next line written, then clears.
        /// </summary>
        /// <remarks>
        /// This is how a list item's first line gets <c>- </c> while its remaining lines get two
        /// spaces, without every block type needing to know it is the first thing in an item.
        /// </remarks>
        private string? _marker;

        public MarkdownExportResult Result => new(_text.ToString(), _log.Items);

        public void WriteBlocks(IEnumerable<BlockNode> blocks, bool insideListItem = false)
        {
            var first = true;

            foreach (var block in blocks)
            {
                // An empty body paragraph has no notation: a blank line in Markdown is the
                // separator between blocks, so writing one produces nothing to read back. This is
                // the one loss not reported, because a document being edited routinely ends with
                // an empty paragraph and a warning on every save would train the user to ignore
                // the whole list.
                if (block is ParagraphNode { Kind: ParagraphKind.Body, Content.Length: 0 })
                {
                    continue;
                }

                // A list nested in an item follows its item's paragraph directly. A blank line
                // there is legal but makes the outer list "loose", which changes nothing in the
                // model and everything in how the source reads. At the top level the blank line is
                // required rather than cosmetic: an ordered list that does not start at 1 cannot
                // interrupt a paragraph, so "text\n3. three" reads as one paragraph of text.
                if (!first && !(insideListItem && block is ListNode))
                {
                    Blank();
                }

                first = false;
                WriteBlock(block);
            }
        }

        private void WriteBlock(BlockNode block)
        {
            switch (block)
            {
                case ParagraphNode paragraph:
                    WriteParagraph(paragraph);
                    return;

                case ListNode list:
                    WriteList(list);
                    return;

                case TableNode table:
                    WriteTable(table);
                    return;

                case RuleNode:
                    Line("---");
                    return;

                case BlockImageNode image:
                    WriteBlockImage(image);
                    return;

                default:
                    // Reachable only by growing the schema without extending this exporter, which
                    // is exactly when silence would lose a user's content without a trace.
                    throw new NotSupportedException(
                        $"The Markdown exporter does not know how to write a '{block.TypeName}' block.");
            }
        }

        private void WriteParagraph(ParagraphNode paragraph)
        {
            ReportAlign(paragraph.Align);

            if (paragraph.IndentLevel > 0)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown has no paragraph indent, so the indentation was not written.",
                    "indent");
            }

            switch (paragraph.Kind)
            {
                case ParagraphKind.Code:
                    WriteCodeBlock(paragraph);
                    return;

                case ParagraphKind.Quote:
                    WriteWrapped(Inline(paragraph.Content), "> ");
                    return;

                case ParagraphKind.Heading1:
                case ParagraphKind.Heading2:
                case ParagraphKind.Heading3:
                case ParagraphKind.Heading4:
                case ParagraphKind.Heading5:
                case ParagraphKind.Heading6:
                    WriteHeading(paragraph);
                    return;

                default:
                    WriteWrapped(Inline(paragraph.Content), string.Empty);
                    return;
            }
        }

        /// <summary>Writes inline text as lines, turning model newlines into hard breaks.</summary>
        /// <remarks>
        /// The hard break is written as a trailing backslash rather than as two trailing spaces.
        /// Both are CommonMark. The spaces are invisible, and every editor, linter and code review
        /// tool that trims trailing whitespace silently converts them into a soft break, which
        /// merges the two lines into one on the next import.
        /// </remarks>
        private void WriteWrapped(string body, string prefix)
        {
            var lines = body.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var last = i == lines.Length - 1;

                Line(prefix + EscapeLineStart(lines[i]) + (last ? string.Empty : "\\"));
            }
        }

        private void WriteHeading(ParagraphNode paragraph)
        {
            var level = paragraph.Kind - ParagraphKind.Heading1 + 1;
            var body = Inline(paragraph.Content);

            if (body.Contains('\n', StringComparison.Ordinal))
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "A Markdown heading is one line, so the line breaks in this one became spaces.",
                    "heading");

                body = body.Replace('\n', ' ');
            }

            var hashes = new string('#', level);

            if (body.Length == 0)
            {
                Line(hashes);
                return;
            }

            Line($"{hashes} {EscapeHeadingEnd(body)}");
        }

        /// <summary>
        /// Escapes a trailing run of <c>#</c>, which would otherwise close the heading.
        /// </summary>
        /// <remarks>
        /// Only the first of the run needs it: a <c>#</c> preceded by a <c>#</c> rather than by a
        /// space cannot begin a closing sequence.
        /// </remarks>
        private static string EscapeHeadingEnd(string body)
        {
            if (!body.EndsWith('#'))
            {
                return body;
            }

            var start = body.Length;

            while (start > 0 && body[start - 1] == '#')
            {
                start--;
            }

            return body[..start] + "\\" + body[start..];
        }

        private void WriteCodeBlock(ParagraphNode paragraph)
        {
            var text = CodeText(paragraph.Content);

            // One trailing newline is what a fenced block always produces on the way back in — the
            // closing fence has to be on its own line — so writing content that already ends in one
            // would add a second on every round trip.
            if (text.EndsWith('\n'))
            {
                text = text[..^1];
            }

            var fence = new string('`', Math.Max(3, LongestRun(text, '`') + 1));

            Line(fence);

            if (text.Length > 0)
            {
                foreach (var line in text.Split('\n'))
                {
                    Line(line);
                }
            }

            Line(fence);
        }

        /// <summary>Writes a list, one marker per item.</summary>
        /// <remarks>
        /// A pending marker belonging to the item this list is the <em>first</em> block of has to be
        /// taken over rather than overwritten: nothing has consumed it yet, and writing a marker of
        /// this list's own would leave the outer item with none at all, flattening two levels into
        /// one. Taking it over is safe because it and <c>_indent</c> are the same length by
        /// construction — the indent is the marker's width in spaces — so the inner markers below it
        /// still line up.
        /// </remarks>
        private void WriteList(ListNode list)
        {
            var number = list.Start;
            var outer = _indent;
            var pending = _marker;

            _marker = null;

            foreach (var item in list.Items)
            {
                var marker = list.Kind == ListKind.Ordered
                    ? $"{number.ToString(CultureInfo.InvariantCulture)}. "
                    : "- ";

                number++;

                _marker = (pending ?? outer) + marker;
                pending = null;

                // Continuation lines line up under the text, not under the marker, or a second
                // paragraph in the item is read as a new block outside the list.
                _indent = outer + new string(' ', marker.Length);

                WriteBlocks(item.Blocks, insideListItem: true);

                // An item whose only content was an empty paragraph wrote nothing at all, and a
                // marker that never reached the output is an item that vanished.
                FlushMarker();
            }

            _indent = outer;
        }

        private void WriteBlockImage(BlockImageNode image)
        {
            ReportAlign(image.Align);

            Line(Image(image.Image));
        }

        /// <summary>Writes a table as a GFM pipe table.</summary>
        /// <remarks>
        /// <para>
        /// A pipe table always has a header row: the delimiter row that makes it a table sits
        /// directly under it, and there is no notation for leaving it out. So the model's first row
        /// becomes the header whether or not it was one, and the change is reported.
        /// </para>
        /// <para>
        /// Column alignment is written when every cell in the column agrees. It is <em>not</em> read
        /// back — see <c>MarkdownImporter.ReportColumnAlignment</c> — so this is knowingly
        /// one-directional. It is still worth writing, because every other Markdown reader in the
        /// world honours it and the alternative is to discard the author's intent for everyone.
        /// </para>
        /// </remarks>
        private void WriteTable(TableNode table)
        {
            _ = TableGeometry.Place(table, out var width);

            if (table.Rows.Length == 0 || width == 0)
            {
                Report(
                    DiagnosticSeverity.Dropped,
                    "A table with no cells has no Markdown notation, so it was not written.",
                    "table");

                return;
            }

            if (table.Rows[0].Cells.Any(c => !c.IsHeader))
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "A Markdown table must start with a header row, so the first row became one.",
                    "table-header");
            }

            if (table.Rows.Skip(1).Any(r => r.Cells.Any(c => c.IsHeader)))
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "A Markdown table has one header row, so the later header cells became ordinary "
                        + "cells.",
                    "table-header-row");
            }

            var alignments = ColumnAlignments(table, width);

            for (var r = 0; r < table.Rows.Length; r++)
            {
                Line(Row(table.Rows[r].Cells.Select(CellText), width));

                if (r == 0)
                {
                    Line(Row(alignments.Select(Delimiter), width));
                }
            }
        }

        private static string Row(IEnumerable<string> cells, int width)
        {
            var row = new StringBuilder("|");
            var written = 0;

            foreach (var cell in cells.Take(width))
            {
                row.Append(' ').Append(cell).Append(" |");
                written++;
            }

            for (; written < width; written++)
            {
                row.Append("  |");
            }

            return row.ToString();
        }

        private static string Delimiter(TextAlign align) => align switch
        {
            TextAlign.Left => ":---",
            TextAlign.Center => ":---:",
            TextAlign.Right => "---:",
            _ => "---",
        };

        /// <summary>
        /// The alignment of each column, which is only a column's alignment when its cells agree.
        /// </summary>
        /// <remarks>
        /// Markdown aligns by column and the model aligns by paragraph, so a column whose cells
        /// disagree cannot be written at all. Reporting that is better than picking the first cell's
        /// alignment and applying it to the rest, which would move text the user placed.
        /// </remarks>
        private ImmutableArray<TextAlign> ColumnAlignments(TableNode table, int width)
        {
            var alignments = new TextAlign[width];

            for (var column = 0; column < width; column++)
            {
                TextAlign? agreed = null;
                var disagreed = false;

                foreach (var row in table.Rows)
                {
                    if (column >= row.Cells.Length)
                    {
                        continue;
                    }

                    var align = row.Cells[column].Blocks.OfType<ParagraphNode>().FirstOrDefault()
                        ?.Align ?? TextAlign.Default;

                    if (agreed is null)
                    {
                        agreed = align;
                    }
                    else if (agreed != align)
                    {
                        disagreed = true;
                    }
                }

                if (disagreed)
                {
                    Report(
                        DiagnosticSeverity.Downgraded,
                        "Markdown aligns a table column, not a cell, and this column's cells did not "
                            + "agree, so none of them was written.",
                        "table-column-align");
                }

                var value = disagreed ? TextAlign.Default : agreed ?? TextAlign.Default;

                if (value == TextAlign.Justify)
                {
                    Report(
                        DiagnosticSeverity.Downgraded,
                        "Markdown has no justified alignment.",
                        "justify");

                    value = TextAlign.Default;
                }

                alignments[column] = value;
            }

            return [.. alignments];
        }

        /// <summary>One cell's content, flattened to the single line a pipe table allows.</summary>
        private string CellText(TableCellNode cell)
        {
            if (cell.RowSpan > 1 || cell.ColumnSpan > 1)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown has no merged table cells, so the cell was written as a single one.",
                    "table-span");
            }

            if (cell.Background is not null)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown cannot colour a table cell.",
                    "table-cell-background");
            }

            var parts = new List<string>();

            foreach (var block in cell.Blocks)
            {
                if (block is not ParagraphNode paragraph)
                {
                    Report(
                        DiagnosticSeverity.Dropped,
                        "A Markdown table cell holds one line of text, so a "
                            + $"'{block.TypeName}' inside one could not be written.",
                        block.TypeName);

                    continue;
                }

                if (paragraph.Kind != ParagraphKind.Body)
                {
                    Report(
                        DiagnosticSeverity.Downgraded,
                        "A Markdown table cell holds one line of text, so the block inside it became "
                            + "ordinary text.",
                        paragraph.Kind.ToString().ToLowerInvariant());
                }

                var text = Inline(paragraph.Content);

                if (text.Contains('\n', StringComparison.Ordinal))
                {
                    Report(
                        DiagnosticSeverity.Downgraded,
                        "A Markdown table cell is one line, so the line breaks in it became spaces.",
                        "table-cell-break");

                    text = text.Replace('\n', ' ');
                }

                if (text.Length > 0)
                {
                    parts.Add(text);
                }
            }

            if (parts.Count > 1)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "A Markdown table cell holds one paragraph, so the paragraphs in this one were "
                        + "joined.",
                    "table-cell-blocks");
            }

            return string.Join(' ', parts);
        }

        /// <summary>Renders inline content, opening and closing delimiters as the marks change.</summary>
        /// <remarks>
        /// The run boundaries come from <see cref="InlineContent.Runs"/> rather than from
        /// <see cref="InlineContent.Marks"/>, because mark spans are canonical and canonical form
        /// omits stretches that override nothing — unformatted text has no spans at all.
        /// </remarks>
        private string Inline(InlineContent content)
        {
            var markdown = new StringBuilder();
            var embed = 0;

            foreach (var run in content.Runs())
            {
                var mark = run.Value;

                ReportMark(mark);

                var body = mark.Has(TextStyle.Code)
                    ? CodeSpan(RunText(content, run.Start, run.End, ref embed))
                    : RunBody(content, run.Start, run.End, ref embed);

                if (body.Length == 0)
                {
                    continue;
                }

                var open = new StringBuilder();
                var close = new StringBuilder();

                if (mark.Link is { } link)
                {
                    open.Append('[');
                    close.Insert(0, $"]({Destination(link.Href)}{TitleSuffix(link.Title)})");
                }

                // Emphasis delimiters must touch the text they wrap: "** bold **" is four
                // asterisks and two spaces, not strong emphasis. So any space at the edges moves
                // outside the delimiters, where it means the same thing and still parses.
                var lead = string.Empty;
                var trail = string.Empty;

                if (mark.Has(TextStyle.Bold) || mark.Has(TextStyle.Italic)
                    || mark.Has(TextStyle.Strikethrough))
                {
                    var start = 0;
                    var end = body.Length;

                    while (start < end && body[start] is ' ' or '\t')
                    {
                        start++;
                    }

                    while (end > start && body[end - 1] is ' ' or '\t')
                    {
                        end--;
                    }

                    lead = body[..start];
                    trail = body[end..];
                    body = body[start..end];
                }

                if (body.Length == 0)
                {
                    markdown.Append(lead).Append(trail);
                    continue;
                }

                if (mark.Has(TextStyle.Bold))
                {
                    open.Append("**");
                    close.Insert(0, "**");
                }

                if (mark.Has(TextStyle.Italic))
                {
                    open.Append('*');
                    close.Insert(0, "*");
                }

                if (mark.Has(TextStyle.Strikethrough))
                {
                    open.Append("~~");
                    close.Insert(0, "~~");
                }

                markdown.Append(lead).Append(open).Append(body).Append(close).Append(trail);
            }

            return markdown.ToString();
        }

        /// <summary>Escaped text for one run, with images written as image syntax.</summary>
        private string RunBody(InlineContent content, int start, int end, ref int embed)
        {
            var body = new StringBuilder();
            var text = new StringBuilder();

            for (var i = start; i < end; i++)
            {
                var c = content.Text[i];

                if (c != InlineContent.Placeholder)
                {
                    Escape(text, c);
                    continue;
                }

                body.Append(text);
                text.Clear();

                if (embed < content.Embeds.Length && content.Embeds[embed] is ImageEmbed image)
                {
                    body.Append(Image(image));
                }
                else if (embed < content.Embeds.Length)
                {
                    Report(
                        DiagnosticSeverity.Dropped,
                        "Markdown can only embed an image, so this embedded object was not written.",
                        content.Embeds[embed].GetType().Name);
                }

                embed++;
            }

            return body.Append(text).ToString();
        }

        /// <summary>Unescaped text for one run, with images reduced to their fallback text.</summary>
        /// <remarks>
        /// Used for code, where nothing is markup and an image has no notation at all — a code span
        /// cannot contain one, and the placeholder character must never reach the output, because it
        /// would come back as a placeholder with no embed behind it.
        /// </remarks>
        private string RunText(InlineContent content, int start, int end, ref int embed)
        {
            var text = new StringBuilder();

            for (var i = start; i < end; i++)
            {
                var c = content.Text[i];

                if (c != InlineContent.Placeholder)
                {
                    text.Append(c);
                    continue;
                }

                if (embed < content.Embeds.Length)
                {
                    Report(
                        DiagnosticSeverity.Dropped,
                        "Markdown code cannot contain an embedded object, so only its text was "
                            + "written.",
                        content.Embeds[embed].GetType().Name);

                    text.Append(content.Embeds[embed].PlainTextFallback);
                }

                embed++;
            }

            return text.ToString();
        }

        /// <summary>A code block's text, with embeds reduced to their fallback and marks reported.</summary>
        private string CodeText(InlineContent content)
        {
            foreach (var run in content.Runs())
            {
                // Code is the block's own kind here, so a Code mark on top of it says nothing new.
                var extra = run.Value with { Style = run.Value.Style & ~TextStyle.Code };

                if (!extra.IsEmpty)
                {
                    Report(
                        DiagnosticSeverity.Downgraded,
                        "Markdown code blocks carry no formatting, so the formatting inside this one "
                            + "was not written.",
                        "code-block-mark");
                }
            }

            var embed = 0;

            return RunText(content, 0, content.Text.Length, ref embed);
        }

        private string Image(ImageEmbed image)
        {
            if (image.Width is not null || image.Height is not null)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown cannot record an image's size.",
                    "image-size");
            }

            var alt = new StringBuilder();

            foreach (var c in image.AltText ?? string.Empty)
            {
                Escape(alt, c);
            }

            return $"![{alt}]({Destination(image.Source)})";
        }

        private void ReportAlign(TextAlign align)
        {
            if (align == TextAlign.Default)
            {
                return;
            }

            Report(
                DiagnosticSeverity.Downgraded,
                "Markdown can only align a table column, not a paragraph, so the alignment was not "
                    + "written.",
                align.ToString().ToLowerInvariant());
        }

        private void ReportMark(MarkSet mark)
        {
            if (mark.Has(TextStyle.Underline))
            {
                Report(DiagnosticSeverity.Downgraded, "Markdown has no underline.", "underline");
            }

            if (mark.Has(TextStyle.Super))
            {
                Report(DiagnosticSeverity.Downgraded, "Markdown has no superscript.", "superscript");
            }

            if (mark.Has(TextStyle.Sub))
            {
                Report(DiagnosticSeverity.Downgraded, "Markdown has no subscript.", "subscript");
            }

            if (mark.FontFamily is not null)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown cannot choose a font, so the font family was not written.",
                    "font-family");
            }

            if (mark.FontSize is not null)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown cannot size text, so the font size was not written.",
                    "font-size");
            }

            if (mark.Foreground is not null)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown cannot colour text, so the text colour was not written.",
                    "colour");
            }

            if (mark.Highlight is not null)
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown cannot highlight text, so the highlight was not written.",
                    "highlight");
            }
        }

        private void Report(DiagnosticSeverity severity, string message, string context) =>
            _log.ReportOnce(severity, message, context);

        private void Line(string content)
        {
            var prefix = _marker ?? _indent;

            _marker = null;

            // A prefix on its own would be trailing whitespace, which every tool that touches the
            // file strips — and a "- " that becomes "-" is still a list item, whereas the two
            // trailing spaces of a hard break would not survive.
            _text.Append(content.Length == 0 ? prefix.TrimEnd() : prefix + content).Append('\n');
        }

        private void Blank() => _text.Append('\n');

        private void FlushMarker()
        {
            if (_marker is { } marker)
            {
                _marker = null;
                _text.Append(marker.TrimEnd()).Append('\n');
            }
        }

        /// <summary>
        /// Escapes what a line's first characters would otherwise turn the line into.
        /// </summary>
        /// <remarks>
        /// Leading whitespace becomes a character reference rather than a backslash escape, because
        /// there is no backslash escape for a space and four of them make an indented code block out
        /// of an ordinary paragraph. The space itself does not survive the round trip — the HTML
        /// stage collapses it, as it does for every other importer here — but the paragraph does.
        /// </remarks>
        private static string EscapeLineStart(string line)
        {
            var i = 0;

            while (i < line.Length && line[i] is ' ' or '\t')
            {
                i++;
            }

            var escaped = new StringBuilder();

            for (var j = 0; j < i; j++)
            {
                escaped.Append(line[j] == '\t' ? "&#9;" : "&#32;");
            }

            var rest = line[i..];

            if (rest.Length > 0 && rest[0] is '#' or '-' or '+' or '=')
            {
                return escaped.Append('\\').Append(rest).ToString();
            }

            // "1." and "1)" start an ordered list; nothing else about a digit is special.
            var digits = 0;

            while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
            {
                digits++;
            }

            if (digits > 0 && digits < rest.Length && rest[digits] is '.' or ')')
            {
                return escaped.Append(rest[..digits]).Append('\\').Append(rest[digits..]).ToString();
            }

            return escaped.Append(rest).ToString();
        }

        /// <summary>
        /// Escapes one character of ordinary text.
        /// </summary>
        /// <remarks>
        /// The set is every character that can begin an inline construct anywhere on a line, not
        /// just the ones that would begin one here. See the type's remarks for why.
        /// <para>
        /// <c>!</c> is in the set because of what it does to the construct <em>after</em> it rather
        /// than to itself: <c>Sale![click](…)</c> is an image, whose brackets hold alt text rather
        /// than content, so an unescaped <c>!</c> deletes the following link and every word of its
        /// text on the way back in.
        /// </para>
        /// </remarks>
        private static void Escape(StringBuilder markdown, char c)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '&' or '|' or '~'
                or '!')
            {
                markdown.Append('\\');
            }

            markdown.Append(c);
        }

        /// <summary>Wraps text in backticks long enough that nothing inside can close them.</summary>
        /// <remarks>
        /// A reader strips one leading and one trailing space when both are present, so text whose
        /// own edges are spaces has to be padded to survive it — and text that is nothing but spaces
        /// must not be, because the rule does not apply to it and padding would add spaces that stay.
        /// </remarks>
        private string CodeSpan(string text)
        {
            if (text.Contains('\n', StringComparison.Ordinal))
            {
                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown inline code is one line, so the line breaks in it became spaces.",
                    "code-span-break");

                text = text.Replace('\n', ' ');
            }

            var fence = new string('`', LongestRun(text, '`') + 1);
            var allSpaces = text.Length > 0 && text.All(c => c == ' ');
            var pad = !allSpaces
                && (text.StartsWith('`') || text.EndsWith('`')
                    || text.StartsWith(' ') || text.EndsWith(' '))
                ? " "
                : string.Empty;

            return fence + pad + text + pad + fence;
        }

        /// <summary>Writes a link or image destination in a form no reader can misread.</summary>
        /// <remarks>
        /// The angle-bracket form is used for anything containing whitespace, which the bare form
        /// cannot hold at all. Note that no scheme is filtered here: refusing a dangerous scheme is
        /// the importer's job, and an exporter that quietly rewrote a URL would be lying about what
        /// the document contains.
        /// </remarks>
        private static string Destination(string href)
        {
            if (href.Length == 0)
            {
                return "<>";
            }

            if (href.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>'))
            {
                var angled = new StringBuilder("<");

                foreach (var c in href)
                {
                    if (c is '<' or '>' or '\\')
                    {
                        angled.Append('\\');
                    }

                    angled.Append(c is '\n' or '\r' ? ' ' : c);
                }

                return angled.Append('>').ToString();
            }

            var bare = new StringBuilder();

            foreach (var c in href)
            {
                if (c is '(' or ')' or '\\')
                {
                    bare.Append('\\');
                }

                bare.Append(c);
            }

            return bare.ToString();
        }

        private static string TitleSuffix(string? title)
        {
            if (title is null)
            {
                return string.Empty;
            }

            var quoted = new StringBuilder(" \"");

            foreach (var c in title)
            {
                if (c is '"' or '\\')
                {
                    quoted.Append('\\');
                }

                quoted.Append(c is '\n' or '\r' ? ' ' : c);
            }

            return quoted.Append('"').ToString();
        }

        private static int LongestRun(string text, char c)
        {
            var longest = 0;
            var current = 0;

            foreach (var ch in text)
            {
                current = ch == c ? current + 1 : 0;
                longest = Math.Max(longest, current);
            }

            return longest;
        }
    }
}
