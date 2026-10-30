using System.Buffers;
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

        /// <summary>
        /// Whether what is being written is inside a pipe table cell, where a pipe has to be
        /// escaped even in the spans that escape nothing else. See <see cref="EscapeCellPipes"/>.
        /// </summary>
        private bool _inTableCell;

        /// <summary>Characters a delimiter run can find itself against without being written.</summary>
        private static readonly Rune Asterisk = new('*');

        private static readonly Rune Tilde = new('~');

        private static readonly Rune Bracket = new('[');

        private static readonly Rune CloseBracket = new(']');

        public MarkdownExportResult Result => new(_text.ToString(), _log.Items);

        public void WriteBlocks(IEnumerable<BlockNode> blocks, bool insideListItem = false)
        {
            var first = true;
            var alternate = false;
            BlockNode? previous = null;

            foreach (var block in blocks)
            {
                // An empty body paragraph has no notation: a blank line in Markdown is the
                // separator between blocks, so writing one produces nothing to read back. This is
                // the one loss not reported, because a document being edited routinely ends with
                // an empty paragraph and a warning on every save would train the user to ignore
                // the whole list. Skipped without becoming the previous block, because what it
                // writes is nothing and the blocks either side of it end up adjacent.
                if (block is ParagraphNode { Kind: ParagraphKind.Body, Content.Length: 0 })
                {
                    continue;
                }

                // Two lists of the same kind separated by a blank line are one loose list to a
                // CommonMark reader, so the boundary between them would be gone for good. A list
                // can only continue with the marker it started with, so changing the second one's
                // is the whole distinction — and it costs nothing, since "*" and "-" are the same
                // bullet to a reader and "1)" and "1." the same number. Against the block before
                // rather than a running toggle, so a third list goes back to the first marker.
                alternate = block is ListNode list
                    && previous is ListNode prior
                    && prior.Kind == list.Kind
                    && !alternate;

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
                previous = block;
                WriteBlock(block, alternate);
            }
        }

        private void WriteBlock(BlockNode block, bool alternateListMarker)
        {
            switch (block)
            {
                case ParagraphNode paragraph:
                    WriteParagraph(paragraph);
                    return;

                case ListNode list:
                    WriteList(list, alternateListMarker);
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

            // Every line of the code is written as a line, including an empty last one: the
            // newline before the closing fence ends that line and is not part of the code, and the
            // importer reads it that way.
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
        private void WriteList(ListNode list, bool alternateMarker)
        {
            var number = list.Start;
            var outer = _indent;
            var pending = _marker;

            _marker = null;

            foreach (var item in list.Items)
            {
                var marker = list.Kind == ListKind.Ordered
                    ? $"{number.ToString(CultureInfo.InvariantCulture)}{(alternateMarker ? ')' : '.')} "
                    : alternateMarker ? "* " : "- ";

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
        /// <para>
        /// Cells are written from the grid <see cref="TableGeometry.Place"/> works out, not from
        /// each row's cell array. A cell spanning rows leaves the rows below it holding fewer cells
        /// than the table is wide, and writing those in order puts every one of them under the wrong
        /// heading — a shift, not a downgrade of the merge. The slots a spanning cell covers are
        /// written empty, because a pipe table has no notation for a span, but they are written.
        /// </para>
        /// </remarks>
        private void WriteTable(TableNode table)
        {
            var placements = TableGeometry.Place(table, out var width);

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

            var grid = Grid(table, placements, width);
            var alignments = ColumnAlignments(grid, width);

            for (var r = 0; r < table.Rows.Length; r++)
            {
                var row = new string[width];

                for (var c = 0; c < width; c++)
                {
                    row[c] = grid[r, c] is { } cell ? CellText(cell) : string.Empty;
                }

                Line(Row(row, width));

                if (r == 0)
                {
                    Line(Row(alignments.Select(Delimiter), width));
                }
            }
        }

        /// <summary>Lays a table's cells out in the grid slots they occupy.</summary>
        /// <remarks>
        /// Only the slot a cell starts in holds it. A slot a cell merely covers stays null, because
        /// what a covered slot needs is to be written as empty rather than to be written twice.
        /// </remarks>
        private static TableCellNode?[,] Grid(
            TableNode table,
            ImmutableArray<CellPlacement> placements,
            int width)
        {
            var grid = new TableCellNode?[table.Rows.Length, width];
            var index = 0;

            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    var placement = placements[index++];

                    grid[placement.Top, placement.Left] = cell;
                }
            }

            return grid;
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
        /// <para>
        /// Read from the grid for the same reason the rows are: <c>row.Cells[column]</c> asks a
        /// row shortened by a span for a cell it does not have and gets the next column's.
        /// </para>
        /// </remarks>
        private ImmutableArray<TextAlign> ColumnAlignments(TableCellNode?[,] grid, int width)
        {
            var alignments = new TextAlign[width];

            for (var column = 0; column < width; column++)
            {
                TextAlign? agreed = null;
                var disagreed = false;

                for (var row = 0; row < grid.GetLength(0); row++)
                {
                    if (grid[row, column] is not { } cell)
                    {
                        continue;
                    }

                    var align = cell.Blocks.OfType<ParagraphNode>().FirstOrDefault()
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
            _inTableCell = true;

            try
            {
                return CellTextCore(cell);
            }
            finally
            {
                _inTableCell = false;
            }
        }

        private string CellTextCore(TableCellNode cell)
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
        /// <para>
        /// Two passes, because a delimiter's legality depends on what is written either side of it
        /// (see <see cref="Flank"/>) and the character after a run's closing delimiter belongs to
        /// the next run. The first pass turns each run into a <see cref="Segment"/>; the second
        /// walks them left to right, where everything before is already written and everything
        /// after is still inspectable.
        /// </para>
        /// </remarks>
        private string Inline(InlineContent content)
        {
            var segments = new List<Segment>();
            var embed = 0;

            foreach (var run in content.Runs())
            {
                var mark = run.Value;

                ReportMark(mark);

                var code = mark.Has(TextStyle.Code);

                var body = code
                    ? CodeSpan(RunText(content, run.Start, run.End, ref embed))
                    : RunBody(content, run.Start, run.End, ref embed);

                if (body.Length == 0)
                {
                    continue;
                }

                var segment = new Segment
                {
                    Body = body,
                    Bold = mark.Has(TextStyle.Bold),
                    Italic = mark.Has(TextStyle.Italic),
                    Strike = mark.Has(TextStyle.Strikethrough),

                    // A code span's edges are its own backticks and an image's are its own syntax:
                    // moving either outside the delimiters would take the construct apart.
                    Movable = !code
                        && content.Text.AsSpan(run.Start, run.End - run.Start)
                            .IndexOf(InlineContent.Placeholder) < 0,
                };

                if (mark.Link is { } link)
                {
                    segment.LinkOpen = "[";
                    segment.LinkClose = $"]({Destination(link.Href)}{TitleSuffix(link.Title)})";
                }

                // Emphasis delimiters must touch the text they wrap: "** bold **" is four asterisks
                // and two spaces, not strong emphasis. So any space at the edges moves outside the
                // delimiters, where it means the same thing and still parses.
                if (segment.Emphasis)
                {
                    while (segment.Body.Length > 0 && IsSpace(UnitRune(segment.Body, 0)))
                    {
                        segment.MoveLeadingUnitOut();
                    }

                    while (segment.Body.Length > 0
                        && IsSpace(UnitRune(segment.Body, LastUnitStart(segment.Body))))
                    {
                        segment.MoveTrailingUnitOut();
                    }

                    if (segment.Body.Length == 0)
                    {
                        segment.Bold = segment.Italic = segment.Strike = false;
                    }
                }

                segments.Add(segment);
            }

            var markdown = new StringBuilder();

            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];

                Flank(segment, markdown, NextFirst(segments, i));

                markdown.Append(segment.Lead)
                    .Append(segment.LinkOpen)
                    .Append(segment.Asterisks)
                    .Append(segment.Tildes)
                    .Append(segment.Body)
                    .Append(segment.Tildes)
                    .Append(segment.Asterisks)
                    .Append(segment.LinkClose)
                    .Append(segment.Trail);
            }

            return markdown.ToString();
        }

        /// <summary>
        /// Settles <paramref name="segment"/>'s delimiters against the text either side of them, so
        /// that every one of them can actually open or close where it is written.
        /// </summary>
        /// <remarks>
        /// CommonMark will not let a delimiter run open unless it is left-flanking or close unless
        /// it is right-flanking, and both are decided by the two characters touching it. A run of
        /// asterisks between punctuation and an alphanumeric is neither, which is why
        /// <c>**Ver.**2</c> is four literal asterisks and no emphasis at all rather than a bold
        /// "Ver.".
        /// <para>
        /// The first remedy costs nothing but the mark on one character: edge punctuation moves
        /// outside the delimiters, where it is the same text and lets them flank —
        /// <c>**Ver**.2</c>. Where that cannot work the delimiters themselves are the obstacle,
        /// because <c>~~</c> against <c>*</c> is punctuation that no edit to the text can separate;
        /// then strikethrough goes first, as the one style whose loss leaves the other two, and all
        /// of them only if that still does not parse.
        /// </para>
        /// </remarks>
        private void Flank(Segment segment, StringBuilder written, Rune? next)
        {
            while (segment.Emphasis)
            {
                var opens = Opens(segment, written);
                var closes = Closes(segment, next);

                if (opens && closes)
                {
                    return;
                }

                if (segment.Movable && segment.Body.Length > 1)
                {
                    if (!opens && IsPunctuation(UnitRune(segment.Body, 0)))
                    {
                        segment.MoveLeadingUnitOut();
                        ReportEdge();
                        continue;
                    }

                    if (!closes
                        && IsPunctuation(UnitRune(segment.Body, LastUnitStart(segment.Body))))
                    {
                        segment.MoveTrailingUnitOut();
                        ReportEdge();
                        continue;
                    }
                }

                if (segment.Strike && (segment.Bold || segment.Italic))
                {
                    segment.Strike = false;
                }
                else
                {
                    segment.Bold = segment.Italic = segment.Strike = false;
                }

                Report(
                    DiagnosticSeverity.Downgraded,
                    "Markdown emphasis has to touch text it can be told apart from, and this could "
                        + "not be written where it was, so the style was dropped.",
                    "emphasis-flanking");
            }
        }

        private void ReportEdge() =>
            Report(
                DiagnosticSeverity.Downgraded,
                "Markdown emphasis cannot end against punctuation, so the punctuation at the edge "
                    + "of this styled text was written outside the style.",
                "emphasis-edge");

        /// <summary>Whether every one of a segment's opening delimiter runs is left-flanking.</summary>
        private static bool Opens(Segment segment, StringBuilder written)
        {
            var before = segment.Lead.Length > 0 ? UnitRune(segment.Lead, LastUnitStart(segment.Lead))
                : segment.LinkOpen.Length > 0 ? Bracket
                : LastRune(written);

            var first = UnitRune(segment.Body, 0);

            if (segment.Asterisks.Length > 0
                && !IsLeftFlanking(before, segment.Strike ? Tilde : first))
            {
                return false;
            }

            return segment.Tildes.Length == 0
                || IsLeftFlanking(segment.Asterisks.Length > 0 ? Asterisk : before, first);
        }

        /// <summary>Whether every one of a segment's closing delimiter runs is right-flanking.</summary>
        private static bool Closes(Segment segment, Rune? next)
        {
            var after = segment.LinkClose.Length > 0 ? CloseBracket
                : segment.Trail.Length > 0 ? UnitRune(segment.Trail, 0)
                : next;

            var last = UnitRune(segment.Body, LastUnitStart(segment.Body));

            if (segment.Asterisks.Length > 0
                && !IsRightFlanking(segment.Strike ? Tilde : last, after))
            {
                return false;
            }

            return segment.Tildes.Length == 0
                || IsRightFlanking(last, segment.Asterisks.Length > 0 ? Asterisk : after);
        }

        /// <summary>
        /// The first character the segments after <paramref name="index"/> will write, or
        /// <see langword="null"/> at the end of the content, which is the end of a line.
        /// </summary>
        /// <remarks>
        /// A segment that carries emphasis really writes a delimiter first, which is punctuation and
        /// so the friendlier answer. This reports the body's first character instead, because the
        /// delimiter is only written if the segment's own flanking works out and the two answers
        /// disagree exactly where guessing wrong is silent: <c>**Sale!***x*</c> emphasises nothing,
        /// while the pessimistic <c>**Sale**!*x*</c> emphasises both.
        /// </remarks>
        private static Rune? NextFirst(List<Segment> segments, int index)
        {
            for (var i = index + 1; i < segments.Count; i++)
            {
                var segment = segments[i];

                if (segment.Lead.Length > 0)
                {
                    return UnitRune(segment.Lead, 0);
                }

                if (segment.LinkOpen.Length > 0)
                {
                    return Bracket;
                }

                if (segment.Body.Length > 0)
                {
                    return UnitRune(segment.Body, 0);
                }

                if (segment.Trail.Length > 0)
                {
                    return UnitRune(segment.Trail, 0);
                }
            }

            return null;
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

            return fence + pad + EscapeCellPipes(text) + pad + fence;
        }

        /// <summary>
        /// Escapes the pipes in text that is inside a table cell, and leaves any other text alone.
        /// </summary>
        /// <remarks>
        /// GFM splits a row on its unescaped pipes <em>before</em> parsing anything inside it as an
        /// inline, so a pipe in a code span, a destination or a title breaks the row there — three
        /// cells with a stray backtick in each. Markdig's parser is inline-aware and does not split,
        /// which is why this library's own round trip was green while what it wrote was unreadable
        /// to GitHub and cmark-gfm.
        /// <para>
        /// Only inside a cell, because outside one there is nothing to split on and a backslash
        /// inside a code span is a backslash rather than an escape — escaping there would put one
        /// into the document.
        /// </para>
        /// </remarks>
        private string EscapeCellPipes(string text) =>
            _inTableCell ? text.Replace("|", "\\|", StringComparison.Ordinal) : text;

        /// <summary>Writes a link or image destination in a form no reader can misread.</summary>
        /// <remarks>
        /// The angle-bracket form is used for anything containing whitespace, which the bare form
        /// cannot hold at all. Note that no scheme is filtered here: refusing a dangerous scheme is
        /// the importer's job, and an exporter that quietly rewrote a URL would be lying about what
        /// the document contains.
        /// </remarks>
        private string Destination(string href)
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

                return EscapeCellPipes(angled.Append('>').ToString());
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

            return EscapeCellPipes(bare.ToString());
        }

        private string TitleSuffix(string? title)
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

            return EscapeCellPipes(quoted.Append('"').ToString());
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

        /// <summary>
        /// Whether a delimiter run written between <paramref name="before"/> and
        /// <paramref name="after"/> could open emphasis, per CommonMark 0.31 §6.2.
        /// </summary>
        /// <summary>
        /// Whether a delimiter run written between <paramref name="before"/> and
        /// <paramref name="after"/> could open emphasis, per CommonMark 0.31 §6.2.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> is the start or the end of a line, which the rules count as
        /// whitespace. Only <c>*</c> and <c>~</c> are ever written here, so the extra conditions
        /// CommonMark puts on <c>_</c> are not needed.
        /// </remarks>
        private static bool IsLeftFlanking(Rune? before, Rune? after) =>
            after is { } a
            && !IsSpace(a)
            && (!IsPunctuation(a) || before is not { } b || IsSpace(b) || IsPunctuation(b));

        /// <summary>The mirror of <see cref="IsLeftFlanking"/>: whether it could close.</summary>
        private static bool IsRightFlanking(Rune? before, Rune? after) =>
            before is { } b
            && !IsSpace(b)
            && (!IsPunctuation(b) || after is not { } a || IsSpace(a) || IsPunctuation(a));

        /// <summary>Unicode whitespace as CommonMark defines it, which is not what .NET does.</summary>
        /// <remarks>
        /// <see cref="char.IsWhiteSpace(char)"/> also counts a line tabulation and a next line,
        /// neither of which CommonMark treats as whitespace — a delimiter beside one of those
        /// flanks, and a writer that thought otherwise would move text that did not need moving.
        /// </remarks>
        private static bool IsSpace(Rune rune) =>
            rune.Value is '\t' or '\n' or '\f' or '\r'
            || Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator;

        /// <summary>Unicode punctuation as CommonMark 0.31 defines it: every P and S category.</summary>
        /// <remarks>
        /// The S categories are the ones a reader reaching for
        /// <see cref="char.IsPunctuation(char)"/> loses, and they are not exotic: <c>=</c> is Sm,
        /// <c>€</c> is Sc and every emoji is So, so the difference decides whether <c>**a**=b</c>
        /// or <c>**a=**b</c> is written.
        /// </remarks>
        private static bool IsPunctuation(Rune rune) => Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;

        /// <summary>The character a unit of written text stands for.</summary>
        /// <remarks>
        /// A backslash escape is one unit of two characters, and both of them are punctuation — the
        /// backslash because it is Po and whatever it escapes because nothing else is ever escaped —
        /// so reading through to the escaped character cannot change the answer while splitting the
        /// pair would put a lone backslash into the document.
        /// </remarks>
        private static Rune UnitRune(string text, int index)
        {
            var at = text[index] == '\\' && index + 1 < text.Length ? index + 1 : index;

            return Rune.DecodeFromUtf16(text.AsSpan(at), out var rune, out _) == OperationStatus.Done
                ? rune
                : Rune.ReplacementChar;
        }

        private static int UnitLength(string text, int index) =>
            text[index] == '\\' && index + 1 < text.Length
                ? 1 + CharLength(text, index + 1)
                : CharLength(text, index);

        private static int CharLength(string text, int index) =>
            char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;

        private static int LastUnitStart(string text)
        {
            var start = 0;

            for (var i = 0; i < text.Length; i += UnitLength(text, i))
            {
                start = i;
            }

            return start;
        }

        private static Rune? LastRune(StringBuilder written)
        {
            if (written.Length == 0)
            {
                return null;
            }

            var last = written[^1];

            if (!char.IsLowSurrogate(last) || written.Length < 2)
            {
                return Rune.TryCreate(last, out var single) ? single : Rune.ReplacementChar;
            }

            return Rune.TryCreate(written[^2], last, out var pair) ? pair : Rune.ReplacementChar;
        }

        /// <summary>One run of inline content, taken apart so its delimiters can be settled.</summary>
        /// <remarks>
        /// Written as <c>Lead LinkOpen Asterisks Tildes Body Tildes Asterisks LinkClose Trail</c>.
        /// <see cref="Lead"/> and <see cref="Trail"/> hold what has been moved out from the edges of
        /// <see cref="Body"/> so that the delimiters can flank — see <see cref="Flank"/>.
        /// </remarks>
        private sealed class Segment
        {
            public string Lead { get; private set; } = string.Empty;

            public string Body { get; set; } = string.Empty;

            public string Trail { get; private set; } = string.Empty;

            public string LinkOpen { get; set; } = string.Empty;

            public string LinkClose { get; set; } = string.Empty;

            public bool Bold { get; set; }

            public bool Italic { get; set; }

            public bool Strike { get; set; }

            /// <summary>Whether text may be moved out of <see cref="Body"/>'s edges.</summary>
            public bool Movable { get; init; }

            public bool Emphasis => Bold || Italic || Strike;

            public string Asterisks =>
                Bold && Italic ? "***" : Bold ? "**" : Italic ? "*" : string.Empty;

            public string Tildes => Strike ? "~~" : string.Empty;

            public void MoveLeadingUnitOut()
            {
                var length = UnitLength(Body, 0);

                Lead += Body[..length];
                Body = Body[length..];
            }

            public void MoveTrailingUnitOut()
            {
                var start = LastUnitStart(Body);

                Trail = Body[start..] + Trail;
                Body = Body[..start];
            }
        }
    }
}
