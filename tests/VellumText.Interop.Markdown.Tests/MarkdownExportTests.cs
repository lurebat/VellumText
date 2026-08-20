using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// The Markdown exporter, tested against literal Markdown.
/// </summary>
/// <remarks>
/// Round-trip tests live in <see cref="MarkdownRoundTripTests"/> and are not enough on their own: a
/// round trip proves the writer and the reader agree with each other, which two implementations
/// that are wrong in the same direction do too. Every construct here is pinned to a literal string
/// that neither half of the library computes, so a change of notation has to be a deliberate one.
/// </remarks>
public class MarkdownExportTests
{
    private static string Export(DocumentNode doc) => MarkdownExporter.Instance.Export(doc);

    private static string Export(params BlockNode[] blocks) => Export(new DocumentNode(blocks));

    private static ParagraphNode Marked(string text, int start, int length, MarkSet mark) =>
        new(InlineContent.Create(text, [new ValueSpan<MarkSet>(start, length, mark)], []));

    [Fact]
    public void Paragraphs_are_separated_by_a_blank_line() =>
        Assert.Equal(
            "first\n\nsecond\n\nthird\n",
            Export(DocumentNode.FromParagraphs("first", "second", "third")));

    [Theory]
    [InlineData(ParagraphKind.Heading1, "# Title\n")]
    [InlineData(ParagraphKind.Heading2, "## Title\n")]
    [InlineData(ParagraphKind.Heading3, "### Title\n")]
    [InlineData(ParagraphKind.Heading4, "#### Title\n")]
    [InlineData(ParagraphKind.Heading5, "##### Title\n")]
    [InlineData(ParagraphKind.Heading6, "###### Title\n")]
    public void Headings_are_written_with_hashes(ParagraphKind kind, string expected) =>
        Assert.Equal(expected, Export(new ParagraphNode(InlineContent.FromText("Title"), kind)));

    /// <summary>
    /// A heading whose text ends in a hash would otherwise come back one hash shorter: a run of
    /// them after a space is a closing sequence, not text.
    /// </summary>
    [Fact]
    public void A_heading_ending_in_a_hash_escapes_it() =>
        Assert.Equal(
            "# C \\#\n",
            Export(new ParagraphNode(InlineContent.FromText("C #"), ParagraphKind.Heading1)));

    [Fact]
    public void An_empty_heading_is_a_bare_hash() =>
        Assert.Equal(
            "###\n",
            Export(new ParagraphNode(InlineContent.Empty, ParagraphKind.Heading3)));

    [Theory]
    [InlineData(TextStyle.Bold, "a **bold** c\n")]
    [InlineData(TextStyle.Italic, "a *bold* c\n")]
    [InlineData(TextStyle.Strikethrough, "a ~~bold~~ c\n")]
    [InlineData(TextStyle.Code, "a `bold` c\n")]
    [InlineData(TextStyle.Bold | TextStyle.Italic, "a ***bold*** c\n")]
    public void Inline_styles_are_written_with_their_delimiters(TextStyle style, string expected) =>
        Assert.Equal(expected, Export(Marked("a bold c", 2, 4, new MarkSet(style))));

    /// <summary>
    /// "** bold **" is four asterisks and two spaces, not strong emphasis: a delimiter has to touch
    /// the text it wraps. Real documents produce this constantly, because selecting a word by
    /// double-clicking takes the space after it.
    /// </summary>
    [Fact]
    public void A_style_run_with_spaces_at_its_edges_puts_them_outside_the_delimiters() =>
        Assert.Equal(
            "a **bold** c\n",
            Export(Marked("a bold c", 1, 6, new MarkSet(TextStyle.Bold))));

    [Fact]
    public void A_run_that_is_only_spaces_is_written_without_delimiters() =>
        Assert.Equal("a  c\n", Export(Marked("a  c", 1, 2, new MarkSet(TextStyle.Bold))));

    /// <summary>
    /// Backticks inside a code span have to be shorter than the fence around it, and text whose own
    /// edges are backticks needs the padding spaces a reader will strip back off.
    /// </summary>
    [Theory]
    [InlineData("a", "`a`\n")]
    [InlineData("a`b", "``a`b``\n")]
    [InlineData("`a`", "`` `a` ``\n")]
    [InlineData("a``b`c", "```a``b`c```\n")]
    [InlineData(" ", "` `\n")]
    [InlineData(" a ", "`  a  `\n")]
    public void A_code_span_is_fenced_longer_than_anything_inside_it(string text, string expected) =>
        Assert.Equal(
            expected,
            Export(new ParagraphNode(InlineContent.FromText(text, new MarkSet(TextStyle.Code)))));

    [Fact]
    public void A_link_is_written_with_its_destination() =>
        Assert.Equal(
            "[text](https://example.test/a)\n",
            Export(new ParagraphNode(InlineContent.FromText(
                "text",
                new MarkSet(link: new LinkMark("https://example.test/a"))))));

    [Fact]
    public void A_link_title_is_written_in_quotes() =>
        Assert.Equal(
            "[text](https://example.test/a \"tip\")\n",
            Export(new ParagraphNode(InlineContent.FromText(
                "text",
                new MarkSet(link: new LinkMark("https://example.test/a", "tip"))))));

    /// <summary>
    /// A destination with a space in it cannot be written bare at all — the reader stops at the
    /// space and treats the rest as a title.
    /// </summary>
    [Fact]
    public void A_destination_with_a_space_is_written_in_angle_brackets() =>
        Assert.Equal(
            "[text](<https://example.test/a b>)\n",
            Export(new ParagraphNode(InlineContent.FromText(
                "text",
                new MarkSet(link: new LinkMark("https://example.test/a b"))))));

    [Fact]
    public void A_destination_with_parentheses_escapes_them() =>
        Assert.Equal(
            "[text](https://example.test/a\\(b\\))\n",
            Export(new ParagraphNode(InlineContent.FromText(
                "text",
                new MarkSet(link: new LinkMark("https://example.test/a(b)"))))));

    [Fact]
    public void A_link_around_bold_text_wraps_it() =>
        Assert.Equal(
            "[**text**](https://example.test/a)\n",
            Export(new ParagraphNode(InlineContent.FromText(
                "text",
                new MarkSet(TextStyle.Bold, link: new LinkMark("https://example.test/a"))))));

    [Fact]
    public void An_image_is_written_with_its_alt_text() =>
        Assert.Equal(
            "![a picture](https://example.test/i.png)\n",
            Export(new ParagraphNode(InlineContent.FromEmbed(
                new ImageEmbed("https://example.test/i.png", altText: "a picture")))));

    [Fact]
    public void A_block_image_is_written_as_a_paragraph_of_its_own() =>
        Assert.Equal(
            "text\n\n![](https://example.test/i.png)\n",
            Export(
                ParagraphNode.FromText("text"),
                new BlockImageNode(new ImageEmbed("https://example.test/i.png"))));

    [Fact]
    public void A_quote_is_written_with_an_angle_bracket() =>
        Assert.Equal(
            "> quoted\n",
            Export(new ParagraphNode(InlineContent.FromText("quoted"), ParagraphKind.Quote)));

    [Fact]
    public void A_code_block_is_fenced() =>
        Assert.Equal(
            "```\nvar x = 1;\n```\n",
            Export(new ParagraphNode(InlineContent.FromText("var x = 1;"), ParagraphKind.Code)));

    /// <summary>
    /// A fenced block always ends with a newline before its closing fence, so content that already
    /// ends in one must not be written with both or the block grows a line on every round trip.
    /// </summary>
    [Fact]
    public void A_code_block_ending_in_a_newline_does_not_gain_a_blank_line() =>
        Assert.Equal(
            "```\nvar x = 1;\n```\n",
            Export(new ParagraphNode(InlineContent.FromText("var x = 1;\n"), ParagraphKind.Code)));

    [Fact]
    public void A_code_block_containing_a_fence_is_fenced_longer() =>
        Assert.Equal(
            "````\n```\n````\n",
            Export(new ParagraphNode(InlineContent.FromText("```"), ParagraphKind.Code)));

    /// <summary>
    /// Code is the one place nothing is escaped, because a backslash in it is a backslash.
    /// </summary>
    [Fact]
    public void A_code_block_is_not_escaped() =>
        Assert.Equal(
            "```\na *b* \\c\n```\n",
            Export(new ParagraphNode(InlineContent.FromText("a *b* \\c"), ParagraphKind.Code)));

    [Fact]
    public void A_rule_is_three_dashes() =>
        Assert.Equal(
            "above\n\n---\n\nbelow\n",
            Export(
                ParagraphNode.FromText("above"),
                RuleNode.Instance,
                ParagraphNode.FromText("below")));

    [Fact]
    public void A_bulleted_list_uses_dashes() =>
        Assert.Equal(
            "- a\n- b\n",
            Export(new ListNode([ListItemNode.FromText("a"), ListItemNode.FromText("b")])));

    [Fact]
    public void A_numbered_list_starts_where_the_model_says() =>
        Assert.Equal(
            "3. three\n4. four\n",
            Export(new ListNode(
                [ListItemNode.FromText("three"), ListItemNode.FromText("four")],
                ListKind.Ordered,
                3)));

    /// <summary>
    /// Continuation lines line up under the text rather than under the marker, which is what makes
    /// the nested list part of the item instead of a new list after it.
    /// </summary>
    [Fact]
    public void A_nested_list_is_indented_under_its_item() =>
        Assert.Equal(
            "- a\n- b\n  - b1\n",
            Export(new ListNode(
            [
                ListItemNode.FromText("a"),
                new ListItemNode(
                [
                    ParagraphNode.FromText("b"),
                    new ListNode([ListItemNode.FromText("b1")]),
                ]),
            ])));

    [Fact]
    public void A_list_nested_under_a_numbered_item_is_indented_by_the_marker_width() =>
        Assert.Equal(
            "10. ten\n    - under\n",
            Export(new ListNode(
                [
                    new ListItemNode(
                    [
                        ParagraphNode.FromText("ten"),
                        new ListNode([ListItemNode.FromText("under")]),
                    ]),
                ],
                ListKind.Ordered,
                10)));

    [Fact]
    public void A_second_paragraph_in_an_item_is_indented_and_separated() =>
        Assert.Equal(
            "- one\n\n  two\n",
            Export(new ListNode(
            [
                new ListItemNode([ParagraphNode.FromText("one"), ParagraphNode.FromText("two")]),
            ])));

    /// <summary>
    /// An item whose only content is an empty paragraph writes nothing, and a marker that never
    /// reaches the output is an item that vanished from the list.
    /// </summary>
    [Fact]
    public void An_empty_list_item_still_writes_its_marker() =>
        Assert.Equal(
            "- a\n-\n- c\n",
            Export(new ListNode(
            [
                ListItemNode.FromText("a"),
                ListItemNode.FromText(string.Empty),
                ListItemNode.FromText("c"),
            ])));

    [Fact]
    public void A_table_is_written_as_a_pipe_table() =>
        Assert.Equal(
            "| a | b |\n| --- | --- |\n| 1 | 2 |\n",
            Export(Table(
                Row(true, "a", "b"),
                Row(false, "1", "2"))));

    [Fact]
    public void An_empty_table_cell_is_still_a_column() =>
        Assert.Equal(
            "| a | b |\n| --- | --- |\n|  |  |\n",
            Export(Table(
                Row(true, "a", "b"),
                Row(false, string.Empty, string.Empty))));

    /// <summary>
    /// Markdown aligns a column and the model aligns a paragraph, so the marker is only written
    /// when every cell in the column asks for the same thing.
    /// </summary>
    [Fact]
    public void A_column_whose_cells_agree_on_alignment_gets_an_alignment_marker() =>
        Assert.Equal(
            "| a | b |\n| --- | :---: |\n| 1 | 2 |\n",
            Export(new TableNode(
            [
                new TableRowNode(
                [
                    new TableCellNode([ParagraphNode.FromText("a")], isHeader: true),
                    new TableCellNode(
                        [new ParagraphNode(
                            InlineContent.FromText("b"),
                            align: TextAlign.Center)],
                        isHeader: true),
                ]),
                new TableRowNode(
                [
                    new TableCellNode([ParagraphNode.FromText("1")]),
                    new TableCellNode(
                        [new ParagraphNode(
                            InlineContent.FromText("2"),
                            align: TextAlign.Center)]),
                ]),
            ])));

    [Fact]
    public void A_column_whose_cells_disagree_on_alignment_gets_no_marker() =>
        Assert.Equal(
            "| a |\n| --- |\n| 1 |\n",
            Export(new TableNode(
            [
                new TableRowNode(
                [
                    new TableCellNode(
                        [new ParagraphNode(
                            InlineContent.FromText("a"),
                            align: TextAlign.Center)],
                        isHeader: true),
                ]),
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("1")])]),
            ])));

    /// <summary>
    /// Two body cells disagreeing is enough on its own, with no help from the header.
    /// </summary>
    /// <remarks>
    /// The case above has one header cell and one body cell, so it cannot tell whether the header
    /// was counted: drop the header from the vote and a single body cell agrees with itself.
    /// </remarks>
    [Fact]
    public void Two_body_cells_that_disagree_on_alignment_get_no_marker() =>
        Assert.Equal(
            "| a |\n| --- |\n| 1 |\n| 2 |\n",
            Export(new TableNode(
            [
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("a")], isHeader: true)]),
                new TableRowNode(
                [
                    new TableCellNode(
                        [new ParagraphNode(
                            InlineContent.FromText("1"),
                            align: TextAlign.Center)]),
                ]),
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("2")])]),
            ])));

    /// <summary>
    /// The header cell is one of the cells that has to agree, and here it is the only one that
    /// disagrees.
    /// </summary>
    /// <remarks>
    /// This is the case that pins the header <em>into</em> the vote rather than merely allowing it:
    /// two body cells that agree with each other and a header that does not. Skipping the header
    /// row turns unanimity on and writes a marker for an alignment the header never had. Neither of
    /// the two tests above can see that, because in both the header is on the winning side of a vote
    /// that has already been lost.
    /// </remarks>
    [Fact]
    public void A_header_that_disagrees_with_its_body_cells_removes_the_marker() =>
        Assert.Equal(
            "| a |\n| --- |\n| 1 |\n| 2 |\n",
            Export(new TableNode(
            [
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("a")], isHeader: true)]),
                new TableRowNode(
                [
                    new TableCellNode(
                        [new ParagraphNode(
                            InlineContent.FromText("1"),
                            align: TextAlign.Center)]),
                ]),
                new TableRowNode(
                [
                    new TableCellNode(
                        [new ParagraphNode(
                            InlineContent.FromText("2"),
                            align: TextAlign.Center)]),
                ]),
            ])));

    /// <summary>
    /// A row shortened by a cell spanning into it from above keeps its cells under the columns they
    /// belong to, rather than shifting them all left.
    /// </summary>
    /// <remarks>
    /// A pipe table has no notation for a span, so the slots a spanning cell covers have to be
    /// written as empty — but they still have to be written. Writing each row's cell array in order
    /// and padding at the end instead puts <c>D</c> under <c>A</c>'s heading and <c>E</c> under
    /// <c>B</c>'s, which is not a downgrade of the merge but content under the wrong headers.
    /// </remarks>
    [Fact]
    public void A_row_shortened_by_a_row_span_keeps_its_cells_under_their_own_columns() =>
        Assert.Equal(
            "| A | B | C |\n| --- | --- | --- |\n|  | D | E |\n",
            Export(new TableNode(
            [
                new TableRowNode(
                [
                    new TableCellNode([ParagraphNode.FromText("A")], isHeader: true, rowSpan: 2),
                    new TableCellNode([ParagraphNode.FromText("B")], isHeader: true),
                    new TableCellNode([ParagraphNode.FromText("C")], isHeader: true),
                ]),
                new TableRowNode(
                [
                    new TableCellNode([ParagraphNode.FromText("D")]),
                    new TableCellNode([ParagraphNode.FromText("E")]),
                ]),
            ])));

    /// <summary>
    /// Column alignment is read off the same grid, so a spanning cell does not make the column below
    /// it look like the column beside it.
    /// </summary>
    /// <remarks>
    /// Indexing <c>row.Cells[column]</c> reads <c>D</c> — column 1's cell — as column 0's, so
    /// column 0 sees <c>A</c> and <c>D</c> disagreeing and column 1 sees only <c>B</c>. Both answers
    /// belong to columns the cells are not in, and both are wrong in the direction that looks
    /// plausible: a marker where there should be none, and none where there should be one.
    /// </remarks>
    [Fact]
    public void Column_alignment_is_read_from_the_grid_rather_than_the_row_array() =>
        Assert.Equal(
            "| A | B |\n| :---: | --- |\n|  | D |\n",
            Export(new TableNode(
            [
                new TableRowNode(
                [
                    new TableCellNode(
                        [new ParagraphNode(
                            InlineContent.FromText("A"),
                            align: TextAlign.Center)],
                        isHeader: true,
                        rowSpan: 2),
                    new TableCellNode([ParagraphNode.FromText("B")], isHeader: true),
                ]),
                new TableRowNode(
                [
                    new TableCellNode(
                        [new ParagraphNode(
                            InlineContent.FromText("D"),
                            align: TextAlign.Right)]),
                ]),
            ])));

    /// <summary>
    /// A short row would otherwise write fewer pipes than the delimiter row promised, and a reader
    /// that counts columns off the delimiter row would misalign every cell after it.
    /// </summary>
    [Fact]
    public void A_row_with_too_few_cells_is_padded_to_the_table_width() =>
        Assert.Equal(
            "| a | b |\n| --- | --- |\n| 1 |  |\n",
            Export(Table(
                Row(true, "a", "b"),
                Row(false, "1"))));

    [Fact]
    public void A_pipe_inside_a_cell_is_escaped() =>
        Assert.Equal(
            "| a |\n| --- |\n| x \\| y |\n",
            Export(Table(Row(true, "a"), Row(false, "x | y"))));

    /// <summary>
    /// A pipe inside an inline span in a cell is escaped too, because GFM splits a row on its
    /// unescaped pipes <em>before</em> anything inside it is parsed as an inline.
    /// </summary>
    /// <remarks>
    /// Markdig's parser is inline-aware and does not split there, so this library's own round trip
    /// was green either way. It is GitHub and cmark-gfm that read the unescaped form as three cells
    /// with a stray backtick in each — a table this library wrote and no other reader can see.
    /// </remarks>
    [Theory]
    [InlineData("code", "| a |\n| --- |\n| `x\\|y` |\n")]
    [InlineData("link", "| a |\n| --- |\n| [t](https://e.test/x\\|y) |\n")]
    [InlineData("title", "| a |\n| --- |\n| [t](https://e.test/ \"x\\|y\") |\n")]
    public void A_pipe_inside_an_inline_span_in_a_cell_is_escaped(string kind, string expected)
    {
        var content = kind switch
        {
            "code" => InlineContent.Create(
                "x|y",
                [new ValueSpan<MarkSet>(0, 3, new MarkSet(style: TextStyle.Code))],
                []),
            "link" => InlineContent.Create(
                "t",
                [new ValueSpan<MarkSet>(0, 1, new MarkSet(link: new LinkMark("https://e.test/x|y")))],
                []),
            _ => InlineContent.Create(
                "t",
                [
                    new ValueSpan<MarkSet>(
                        0,
                        1,
                        new MarkSet(link: new LinkMark("https://e.test/", "x|y"))),
                ],
                []),
        };

        Assert.Equal(
            expected,
            Export(new TableNode(
            [
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("a")], isHeader: true)]),
                new TableRowNode([new TableCellNode([new ParagraphNode(content)])]),
            ])));
    }

    /// <summary>
    /// Outside a table there is nothing to split on, and a backslash inside a code span is a
    /// backslash — so escaping there would put one in the document.
    /// </summary>
    [Fact]
    public void A_pipe_inside_a_code_span_outside_a_table_is_not_escaped() =>
        Assert.Equal(
            "`x|y`\n",
            Export(Marked("x|y", 0, 3, new MarkSet(style: TextStyle.Code))));

    /// <summary>
    /// Every character that can begin an inline construct is escaped wherever it appears, rather
    /// than only where it would be read as one. A writer that guesses when a character is inert is
    /// wrong on somebody else's text.
    /// </summary>
    [Theory]
    [InlineData("a*b", "a\\*b\n")]
    [InlineData("a_b", "a\\_b\n")]
    [InlineData("a[b]c", "a\\[b\\]c\n")]
    [InlineData("a`b", "a\\`b\n")]
    [InlineData("a\\b", "a\\\\b\n")]
    [InlineData("a<b>c", "a\\<b\\>c\n")]
    [InlineData("AT&T", "AT\\&T\n")]
    [InlineData("a|b", "a\\|b\n")]
    [InlineData("a~b", "a\\~b\n")]
    [InlineData("a!b", "a\\!b\n")]
    public void Inline_markup_characters_are_escaped(string text, string expected) =>
        Assert.Equal(expected, Export(ParagraphNode.FromText(text)));

    /// <summary>
    /// These only mean something at the start of a line, and escaping them everywhere would make
    /// ordinary prose unreadable.
    /// </summary>
    [Theory]
    [InlineData("# not a heading", "\\# not a heading\n")]
    [InlineData("- not a list", "\\- not a list\n")]
    [InlineData("+ not a list", "\\+ not a list\n")]
    [InlineData("1. not a list", "1\\. not a list\n")]
    [InlineData("1) not a list", "1\\) not a list\n")]
    [InlineData("=== not a heading", "\\=== not a heading\n")]
    [InlineData("a - b", "a - b\n")]
    [InlineData("a 1. b", "a 1. b\n")]
    public void Line_leading_markup_is_escaped_only_at_the_start(string text, string expected) =>
        Assert.Equal(expected, Export(ParagraphNode.FromText(text)));

    /// <summary>
    /// Four leading spaces make an indented code block out of an ordinary paragraph, and there is
    /// no backslash escape for a space.
    /// </summary>
    [Fact]
    public void Leading_whitespace_is_written_as_a_character_reference() =>
        Assert.Equal(
            "&#32;&#32;&#32;&#32;indented\n",
            Export(ParagraphNode.FromText("    indented")));

    /// <summary>
    /// A trailing backslash rather than two trailing spaces, because trailing spaces are invisible
    /// and every tool that trims them turns the break into a soft one.
    /// </summary>
    [Fact]
    public void A_newline_inside_a_paragraph_becomes_a_hard_break() =>
        Assert.Equal(
            "line one\\\nline two\n",
            Export(ParagraphNode.FromText("line one\nline two")));

    [Fact]
    public void A_multi_line_quote_prefixes_every_line() =>
        Assert.Equal(
            "> line one\\\n> line two\n",
            Export(new ParagraphNode(
                InlineContent.FromText("line one\nline two"),
                ParagraphKind.Quote)));

    [Fact]
    public void An_empty_document_exports_to_nothing() =>
        Assert.Equal(string.Empty, Export(DocumentNode.Empty));

    /// <summary>
    /// A blank line is Markdown's block separator, so an empty paragraph has no notation of its
    /// own and cannot be written. Pinned because it is the one loss the exporter does not report.
    /// </summary>
    [Fact]
    public void An_empty_paragraph_between_two_others_is_not_written() =>
        Assert.Equal(
            "above\n\nbelow\n",
            Export(DocumentNode.FromParagraphs("above", string.Empty, "below")));

    [Fact]
    public void The_stream_overload_writes_the_same_bytes_as_the_string_one()
    {
        var doc = DocumentNode.FromParagraphs("first", "second");
        using var stream = new MemoryStream();

        MarkdownExporter.Instance.Export(stream, doc);

        Assert.Equal(
            "first\n\nsecond\n",
            System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static TableNode Table(params TableRowNode[] rows) => new(rows);

    private static TableRowNode Row(bool header, params string[] cells) =>
        new(cells.Select(c => new TableCellNode(
            [ParagraphNode.FromText(c)],
            isHeader: header)));
}
