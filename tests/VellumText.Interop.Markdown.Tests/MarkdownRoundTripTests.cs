using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// Documents that survive being written to Markdown and read back.
/// </summary>
/// <remarks>
/// <para>
/// Every test here asserts the intermediate Markdown against a literal before importing it. A round
/// trip on its own proves only that the writer and the reader agree with each other, which two
/// implementations wrong in the same direction also do; and three round-trip tests in this
/// repository once asserted nothing but schema validity, which a document of one empty paragraph
/// satisfies. They stayed green for weeks while the importer dropped every empty table row.
/// </para>
/// <para>
/// The literal is the anchor. The import assertion is what makes it a round trip.
/// </para>
/// </remarks>
public class MarkdownRoundTripTests
{
    /// <summary>
    /// Writes <paramref name="doc"/>, checks the Markdown is exactly
    /// <paramref name="expectedMarkdown"/>, then reads it back.
    /// </summary>
    private static DocumentNode Trip(DocumentNode doc, string expectedMarkdown)
    {
        var markdown = MarkdownExporter.Instance.Export(doc);

        Assert.Equal(expectedMarkdown, markdown);

        var result = MarkdownImporter.Import(markdown);

        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Malformed);

        return result.Doc;
    }

    private static ParagraphNode Para(DocumentNode doc, int index) =>
        Assert.IsType<ParagraphNode>(doc.Blocks[index]);

    [Fact]
    public void Paragraphs_come_back_as_paragraphs()
    {
        var back = Trip(
            DocumentNode.FromParagraphs("first", "second", "third"),
            "first\n\nsecond\n\nthird\n");

        Assert.Equal(3, back.Blocks.Length);
        Assert.Equal(
            ["first", "second", "third"],
            back.Blocks.Cast<ParagraphNode>().Select(p => p.Content.Text));
    }

    [Theory]
    [InlineData(ParagraphKind.Heading1, "# Title\n")]
    [InlineData(ParagraphKind.Heading2, "## Title\n")]
    [InlineData(ParagraphKind.Heading3, "### Title\n")]
    [InlineData(ParagraphKind.Heading4, "#### Title\n")]
    [InlineData(ParagraphKind.Heading5, "##### Title\n")]
    [InlineData(ParagraphKind.Heading6, "###### Title\n")]
    public void Every_heading_level_comes_back_at_its_own_level(
        ParagraphKind kind,
        string expected)
    {
        var back = Trip(
            new DocumentNode([new ParagraphNode(InlineContent.FromText("Title"), kind)]),
            expected);

        Assert.Equal(kind, Para(back, 0).Kind);
        Assert.Equal("Title", Para(back, 0).Content.Text);
    }

    [Theory]
    [InlineData(TextStyle.Bold, "a **b** c\n")]
    [InlineData(TextStyle.Italic, "a *b* c\n")]
    [InlineData(TextStyle.Strikethrough, "a ~~b~~ c\n")]
    [InlineData(TextStyle.Code, "a `b` c\n")]
    [InlineData(TextStyle.Bold | TextStyle.Italic, "a ***b*** c\n")]
    public void Inline_styles_come_back_on_the_same_characters(TextStyle style, string expected)
    {
        var back = Trip(
            new DocumentNode([
                new ParagraphNode(InlineContent.Create(
                    "a b c",
                    [new ValueSpan<MarkSet>(2, 1, new MarkSet(style))],
                    []))]),
            expected);

        var content = Para(back, 0).Content;

        Assert.Equal("a b c", content.Text);
        Assert.Equal(TextStyle.None, content.MarkAt(0).Style);
        Assert.Equal(style, content.MarkAt(2).Style);
        Assert.Equal(TextStyle.None, content.MarkAt(4).Style);
    }

    /// <summary>
    /// Escaping is where a writer that only had to satisfy itself gets caught: every one of these
    /// characters begins a construct, and every one has to come back as the character it was.
    /// </summary>
    [Theory]
    [InlineData("a*b*c", "a\\*b\\*c\n")]
    [InlineData("a_b_c", "a\\_b\\_c\n")]
    [InlineData("AT&T", "AT\\&T\n")]
    [InlineData("a<b>c", "a\\<b\\>c\n")]
    [InlineData("[not a link](nowhere)", "\\[not a link\\](nowhere)\n")]
    [InlineData("100% | 200%", "100% \\| 200%\n")]
    [InlineData("a\\b", "a\\\\b\n")]
    [InlineData("# not a heading", "\\# not a heading\n")]
    [InlineData("- not a list", "\\- not a list\n")]
    [InlineData("1. not a list", "1\\. not a list\n")]
    [InlineData("a `code` b", "a \\`code\\` b\n")]
    public void Punctuation_that_means_something_comes_back_as_text(string text, string expected)
    {
        var back = Trip(new DocumentNode([ParagraphNode.FromText(text)]), expected);

        Assert.Equal(text, Para(back, 0).Content.Text);
        Assert.Equal(TextStyle.None, Para(back, 0).Content.MarkAt(0).Style);
    }

    /// <summary>
    /// Leading whitespace is the one piece of paragraph text that does not come back, and it is
    /// reported here rather than quietly left out of the theory above.
    /// </summary>
    /// <remarks>
    /// The exporter writes character references because four leading spaces would make an indented
    /// code block, and every Markdown renderer shows them as spaces. Import loses them anyway: the
    /// reference decodes to a space during the Markdig render, and the HTML stage the importer runs
    /// on collapses leading whitespace in a block, which is HTML's rule and not a decision this
    /// package gets to make. Preserving it would mean teaching the HTML importer to keep leading
    /// whitespace, which would change what every HTML paste produces.
    /// </remarks>
    [Fact]
    public void Leading_whitespace_is_written_but_does_not_survive_the_return_trip()
    {
        var back = Trip(
            new DocumentNode([ParagraphNode.FromText("    indented")]),
            "&#32;&#32;&#32;&#32;indented\n");

        Assert.Equal("indented", Para(back, 0).Content.Text);
    }

    [Fact]
    public void A_hard_break_stays_a_hard_break()
    {
        var back = Trip(
            new DocumentNode([ParagraphNode.FromText("line one\nline two")]),
            "line one\\\nline two\n");

        Assert.Equal("line one\nline two", Para(back, 0).Content.Text);
    }

    [Fact]
    public void A_quote_comes_back_a_quote()
    {
        var back = Trip(
            new DocumentNode([
                new ParagraphNode(InlineContent.FromText("quoted"), ParagraphKind.Quote)]),
            "> quoted\n");

        Assert.Equal(ParagraphKind.Quote, Para(back, 0).Kind);
        Assert.Equal("quoted", Para(back, 0).Content.Text);
    }

    [Fact]
    public void A_code_block_keeps_its_line_breaks_and_its_style()
    {
        var back = Trip(
            new DocumentNode([
                new ParagraphNode(
                    InlineContent.FromText("if (x) {\n    y();\n}"),
                    ParagraphKind.Code)]),
            "```\nif (x) {\n    y();\n}\n```\n");

        Assert.Equal(ParagraphKind.Code, Para(back, 0).Kind);
        Assert.Equal("if (x) {\n    y();\n}\n", Para(back, 0).Content.Text);
    }

    /// <summary>
    /// A fence has to be longer than the longest run of backticks inside it, or the block ends at
    /// the user's own text.
    /// </summary>
    [Fact]
    public void A_code_block_containing_a_fence_survives()
    {
        var back = Trip(
            new DocumentNode([
                new ParagraphNode(InlineContent.FromText("```\nnested\n```"), ParagraphKind.Code)]),
            "````\n```\nnested\n```\n````\n");

        Assert.Equal("```\nnested\n```\n", Para(back, 0).Content.Text);
    }

    [Fact]
    public void A_rule_comes_back_a_rule()
    {
        var back = Trip(
            new DocumentNode([
                ParagraphNode.FromText("above"),
                RuleNode.Instance,
                ParagraphNode.FromText("below")]),
            "above\n\n---\n\nbelow\n");

        Assert.Equal(3, back.Blocks.Length);
        Assert.IsType<RuleNode>(back.Blocks[1]);
    }

    [Fact]
    public void A_bulleted_list_comes_back_bulleted()
    {
        var back = Trip(
            new DocumentNode([
                new ListNode(
                    [ListItemNode.FromText("one"), ListItemNode.FromText("two")],
                    ListKind.Unordered)]),
            "- one\n- two\n");

        var list = Assert.IsType<ListNode>(Assert.Single(back.Blocks));

        Assert.Equal(ListKind.Unordered, list.Kind);
        Assert.Equal(2, list.Items.Length);
        Assert.Equal("one", ((ParagraphNode)list.Items[0].Blocks[0]).Content.Text);
        Assert.Equal("two", ((ParagraphNode)list.Items[1].Blocks[0]).Content.Text);
    }

    [Fact]
    public void A_numbered_list_keeps_the_number_it_started_at()
    {
        var back = Trip(
            new DocumentNode([
                new ListNode(
                    [ListItemNode.FromText("three"), ListItemNode.FromText("four")],
                    ListKind.Ordered,
                    start: 3)]),
            "3. three\n4. four\n");

        var list = Assert.IsType<ListNode>(Assert.Single(back.Blocks));

        Assert.Equal(ListKind.Ordered, list.Kind);
        Assert.Equal(3, list.Start);
    }

    /// <summary>
    /// A list needs a blank line above it, and this is the case that proves it rather than merely
    /// preferring it: an ordered list that does not start at 1 cannot interrupt a paragraph, so
    /// without the blank line <c>3. three</c> is read as more of the paragraph's text.
    /// </summary>
    [Fact]
    public void A_numbered_list_after_a_paragraph_is_still_a_list()
    {
        var back = Trip(
            new DocumentNode([
                ParagraphNode.FromText("Some prose."),
                new ListNode(
                    [ListItemNode.FromText("three"), ListItemNode.FromText("four")],
                    ListKind.Ordered,
                    start: 3)]),
            "Some prose.\n\n3. three\n4. four\n");

        Assert.Equal(2, back.Blocks.Length);
        Assert.Equal("Some prose.", Para(back, 0).Content.Text);

        var list = Assert.IsType<ListNode>(back.Blocks[1]);

        Assert.Equal(ListKind.Ordered, list.Kind);
        Assert.Equal(3, list.Start);
    }

    [Fact]
    public void A_nested_list_comes_back_nested()
    {
        var back = Trip(
            new DocumentNode([
                new ListNode(
                    [
                        new ListItemNode([
                            ParagraphNode.FromText("outer"),
                            new ListNode(
                                [ListItemNode.FromText("inner")],
                                ListKind.Ordered)]),
                    ],
                    ListKind.Unordered)]),
            "- outer\n  1. inner\n");

        var outer = Assert.IsType<ListNode>(Assert.Single(back.Blocks));
        var item = Assert.Single(outer.Items);

        Assert.Equal("outer", ((ParagraphNode)item.Blocks[0]).Content.Text);

        var inner = Assert.IsType<ListNode>(item.Blocks[1]);

        Assert.Equal(ListKind.Ordered, inner.Kind);
        Assert.Equal("inner", ((ParagraphNode)Assert.Single(inner.Items).Blocks[0]).Content.Text);
    }

    /// <summary>
    /// A pipe inside an inline span in a table cell survives the round trip, now that the span is
    /// written the way GFM requires.
    /// </summary>
    /// <remarks>
    /// Markdig's parser is inline-aware, so it neither splits the row on the pipe nor undoes the
    /// escape inside a code span — which means writing the GFM form without reading it back would
    /// have traded a table GitHub cannot read for a backslash in the document. cmark-gfm removes
    /// <c>\|</c> from a row before parsing it, so the two now agree; a literal backslash-pipe inside
    /// a code span in a cell has no GFM notation and cannot be asked for.
    /// </remarks>
    [Theory]
    [InlineData("x|y", "| a |\n| --- |\n| `x\\|y` |\n")]
    [InlineData("x\\|y", "| a |\n| --- |\n| `x\\\\|y` |\n")]
    public void A_pipe_inside_a_code_span_in_a_cell_comes_back_as_a_pipe(
        string text,
        string expected)
    {
        var back = Trip(
            new DocumentNode([
                new TableNode(
                [
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("a")], isHeader: true)]),
                    new TableRowNode([
                        new TableCellNode([
                            new ParagraphNode(InlineContent.Create(
                                text,
                                [
                                    new ValueSpan<MarkSet>(
                                        0,
                                        text.Length,
                                        new MarkSet(style: TextStyle.Code)),
                                ],
                                []))])]),
                ])]),
            expected);

        var table = Assert.IsType<TableNode>(Assert.Single(back.Blocks));
        var cell = Assert.Single(table.Rows[1].Cells);
        var content = ((ParagraphNode)cell.Blocks[0]).Content;

        Assert.Equal(text, content.Text);
        Assert.Equal(TextStyle.Code, content.MarkAt(0).Style);
    }

    /// <summary>
    /// <summary>
    /// Two sibling lists stay two lists, rather than merging into one on the way back.
    /// </summary>
    /// <remarks>
    /// Written with the same bullet and a blank line between them, CommonMark reads the pair as a
    /// single loose list and the boundary is gone for good. The distinction <em>is</em> expressible
    /// — a list can only continue with the marker it started with — so the second list changes
    /// character. Nothing else about it changes: <c>*</c> and <c>-</c> are the same bullet to a
    /// reader, and <c>1)</c> and <c>1.</c> are the same number.
    /// </remarks>
    [Theory]
    [InlineData(ListKind.Unordered, "- a\n\n* b\n")]
    [InlineData(ListKind.Ordered, "1. a\n\n1) b\n")]
    public void Two_sibling_lists_do_not_merge_into_one(ListKind kind, string expected)
    {
        var back = Trip(
            new DocumentNode([
                new ListNode([ListItemNode.FromText("a")], kind),
                new ListNode([ListItemNode.FromText("b")], kind),
            ]),
            expected);

        Assert.Equal(2, back.Blocks.Length);

        var first = Assert.IsType<ListNode>(back.Blocks[0]);
        var second = Assert.IsType<ListNode>(back.Blocks[1]);

        Assert.Equal(kind, first.Kind);
        Assert.Equal(kind, second.Kind);
        Assert.Equal("a", ((ParagraphNode)Assert.Single(first.Items).Blocks[0]).Content.Text);
        Assert.Equal("b", ((ParagraphNode)Assert.Single(second.Items).Blocks[0]).Content.Text);
    }

    /// <summary>
    /// The alternation is between neighbours, not a running toggle: a third list goes back to the
    /// first marker, and a list after a paragraph starts again from it.
    /// </summary>
    [Fact]
    public void The_marker_alternates_only_against_the_list_before_it()
    {
        var back = Trip(
            new DocumentNode([
                new ListNode([ListItemNode.FromText("a")], ListKind.Unordered),
                new ListNode([ListItemNode.FromText("b")], ListKind.Unordered),
                new ListNode([ListItemNode.FromText("c")], ListKind.Unordered),
                ParagraphNode.FromText("between"),
                new ListNode([ListItemNode.FromText("d")], ListKind.Unordered),
            ]),
            "- a\n\n* b\n\n- c\n\nbetween\n\n- d\n");

        Assert.Equal(5, back.Blocks.Length);
        Assert.All(
            new[] { 0, 1, 2, 4 },
            i => Assert.IsType<ListNode>(back.Blocks[i]));
    }

    /// <summary>
    /// A list of the other kind is already a different list to a reader, so it keeps the marker it
    /// would have had on its own.
    /// </summary>
    /// <remarks>
    /// Alternating here would be harmless and still wrong: it would spend the distinction where
    /// there is nothing to distinguish, and leave the rule looking like "every second list" rather
    /// than "a list that would otherwise be swallowed by the one before it".
    /// </remarks>
    [Fact]
    public void A_list_of_the_other_kind_keeps_its_own_marker()
    {
        var back = Trip(
            new DocumentNode([
                new ListNode([ListItemNode.FromText("a")], ListKind.Unordered),
                new ListNode([ListItemNode.FromText("b")], ListKind.Ordered),
            ]),
            "- a\n\n1. b\n");

        Assert.Equal(ListKind.Unordered, Assert.IsType<ListNode>(back.Blocks[0]).Kind);
        Assert.Equal(ListKind.Ordered, Assert.IsType<ListNode>(back.Blocks[1]).Kind);
    }

    /// <summary>
    /// An item whose first block is itself a list still gets its own marker, so the nesting level
    /// it stands for survives.
    /// </summary>
    /// <remarks>
    /// The marker for an item is held pending until a line consumes it. When the item's first block
    /// was a nested list, the inner list wrote its own marker over the pending one before it had
    /// ever been flushed, so the outer item disappeared and the two levels flattened into one —
    /// permanently, with no diagnostic. Also reachable from
    /// <c>&lt;ul&gt;&lt;li&gt;&lt;ul&gt;&lt;li&gt;inner&lt;/li&gt;&lt;/ul&gt;&lt;/li&gt;
    /// &lt;li&gt;after&lt;/li&gt;&lt;/ul&gt;</c>.
    /// </remarks>
    [Fact]
    public void An_item_beginning_with_a_nested_list_keeps_its_own_marker()
    {
        var back = Trip(
            new DocumentNode([
                new ListNode(
                    [
                        new ListItemNode([
                            new ListNode(
                                [ListItemNode.FromText("first"), ListItemNode.FromText("second")],
                                ListKind.Unordered)]),
                        ListItemNode.FromText("after"),
                    ],
                    ListKind.Unordered)]),
            "- - first\n  - second\n- after\n");

        var outer = Assert.IsType<ListNode>(Assert.Single(back.Blocks));

        Assert.Equal(2, outer.Items.Length);

        var inner = Assert.IsType<ListNode>(outer.Items[0].Blocks[0]);

        Assert.Equal(
            ["first", "second"],
            inner.Items.Select(i => ((ParagraphNode)i.Blocks[0]).Content.Text));
        Assert.Equal("after", ((ParagraphNode)outer.Items[1].Blocks[0]).Content.Text);
    }

    /// <summary>
    /// An unescaped <c>!</c> before a link makes it an image, and an image's <c>[…]</c> is alt text
    /// rather than content — so the link, and every word of its text, is deleted on the way back.
    /// </summary>
    /// <remarks>
    /// Reachable from an ordinary HTML paste: <c>&lt;p&gt;Sale!&lt;a href="…"&gt;click&lt;/a&gt;
    /// &lt;/p&gt;</c>. Nothing else in the pipeline notices, because the Markdown is well formed and
    /// so is the document it reads back as.
    /// </remarks>
    [Fact]
    public void A_bang_before_a_link_does_not_turn_it_into_an_image()
    {
        var back = Trip(
            new DocumentNode([
                new ParagraphNode(InlineContent.Create(
                    "Sale!click",
                    [
                        new ValueSpan<MarkSet>(
                            5,
                            5,
                            new MarkSet(link: new LinkMark("https://example.test/x"))),
                    ],
                    []))]),
            "Sale\\![click](https://example.test/x)\n");

        var content = Para(back, 0).Content;

        Assert.Equal("Sale!click", content.Text);
        Assert.Equal("https://example.test/x", content.MarkAt(5).Link?.Href);
    }

    [Fact]
    public void A_link_comes_back_on_the_same_characters()
    {
        var back = Trip(
            new DocumentNode([
                new ParagraphNode(InlineContent.Create(
                    "see here now",
                    [
                        new ValueSpan<MarkSet>(
                            4,
                            4,
                            new MarkSet(link: new LinkMark("https://example.test/a"))),
                    ],
                    []))]),
            "see [here](https://example.test/a) now\n");

        var content = Para(back, 0).Content;

        Assert.Equal("see here now", content.Text);
        Assert.Null(content.MarkAt(0).Link);
        Assert.Equal("https://example.test/a", content.MarkAt(4).Link?.Href);
        Assert.Null(content.MarkAt(9).Link);
    }

    /// <summary>
    /// A remote image is dropped on the way back in unless the host asks for it, so the round trip
    /// is asserted with the option a host that wanted images would have set.
    /// </summary>
    [Fact]
    public void An_image_comes_back_with_its_source_and_alt_text()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                "\uFFFC",
                [],
                [new ImageEmbed("https://example.test/a.png", altText: "a picture")]))]);

        var markdown = MarkdownExporter.Instance.Export(doc);

        Assert.Equal("![a picture](https://example.test/a.png)\n", markdown);

        var back = MarkdownImporter.Import(
            markdown,
            new MarkdownImportOptions { AllowRemoteImages = true });

        var image = Assert.IsType<ImageEmbed>(Assert.Single(Para(back.Doc, 0).Content.Embeds));

        Assert.Equal("https://example.test/a.png", image.Source);
        Assert.Equal("a picture", image.AltText);
    }

    /// <summary>
    /// Tables are the headline feature of this library, so a Markdown export that flattened them
    /// would be a poor showing — and a round trip that only counted rows would not notice if it
    /// did.
    /// </summary>
    [Fact]
    public void A_table_comes_back_with_its_cells_where_they_were()
    {
        var back = Trip(
            new DocumentNode([
                new TableNode([
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("Name")], isHeader: true),
                        new TableCellNode([ParagraphNode.FromText("Qty")], isHeader: true)]),
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("Bolt")]),
                        new TableCellNode([ParagraphNode.FromText("12")])]),
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("Nut")]),
                        new TableCellNode([ParagraphNode.FromText("34")])])])]),
            "| Name | Qty |\n| --- | --- |\n| Bolt | 12 |\n| Nut | 34 |\n");

        var table = Assert.IsType<TableNode>(Assert.Single(back.Blocks));

        Assert.Equal(3, table.Rows.Length);
        Assert.Equal(
            ["Name", "Qty", "Bolt", "12", "Nut", "34"],
            table.Rows.SelectMany(r => r.Cells)
                .Select(c => ((ParagraphNode)c.Blocks[0]).Content.Text));
        Assert.True(table.Rows[0].Cells[0].IsHeader);
        Assert.False(table.Rows[1].Cells[0].IsHeader);
    }

    /// <summary>
    /// An empty cell is the case the RTF importer got wrong for weeks, because a round trip that
    /// counts only non-empty things cannot see it.
    /// </summary>
    [Fact]
    public void A_table_with_empty_cells_keeps_the_shape()
    {
        var back = Trip(
            new DocumentNode([
                new TableNode([
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("A")], isHeader: true),
                        new TableCellNode([ParagraphNode.FromText("B")], isHeader: true)]),
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText(string.Empty)]),
                        new TableCellNode([ParagraphNode.FromText("x")])]),
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("y")]),
                        new TableCellNode([ParagraphNode.FromText(string.Empty)])])])]),
            "| A | B |\n| --- | --- |\n|  | x |\n| y |  |\n");

        var table = Assert.IsType<TableNode>(Assert.Single(back.Blocks));

        Assert.Equal(3, table.Rows.Length);
        Assert.All(table.Rows, row => Assert.Equal(2, row.Cells.Length));
        Assert.Equal(
            ["A", "B", "", "x", "y", ""],
            table.Rows.SelectMany(r => r.Cells)
                .Select(c => ((ParagraphNode)c.Blocks[0]).Content.Text));
    }

    /// <summary>
    /// A pipe inside a cell would end the cell if it were written bare.
    /// </summary>
    [Fact]
    public void A_pipe_inside_a_cell_stays_inside_the_cell()
    {
        var back = Trip(
            new DocumentNode([
                new TableNode([
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("a|b")], isHeader: true)]),
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("c|d")])])])]),
            "| a\\|b |\n| --- |\n| c\\|d |\n");

        var table = Assert.IsType<TableNode>(Assert.Single(back.Blocks));

        Assert.Equal(
            ["a|b", "c|d"],
            table.Rows.SelectMany(r => r.Cells)
                .Select(c => ((ParagraphNode)c.Blocks[0]).Content.Text));
    }

    /// <summary>
    /// A whole document rather than one construct at a time, because ordering and blank-line
    /// bookkeeping between blocks is its own way to be wrong.
    /// </summary>
    [Fact]
    public void A_mixed_document_comes_back_block_for_block()
    {
        var back = Trip(
            new DocumentNode([
                new ParagraphNode(InlineContent.FromText("Title"), ParagraphKind.Heading1),
                ParagraphNode.FromText("Some prose."),
                new ListNode(
                    [ListItemNode.FromText("one"), ListItemNode.FromText("two")],
                    ListKind.Unordered),
                new ParagraphNode(InlineContent.FromText("A quote."), ParagraphKind.Quote),
                RuleNode.Instance,
                new ParagraphNode(InlineContent.FromText("code();"), ParagraphKind.Code),
                new TableNode([
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("H")], isHeader: true)]),
                    new TableRowNode([new TableCellNode([ParagraphNode.FromText("v")])])])]),
            """
            # Title

            Some prose.

            - one
            - two

            > A quote.

            ---

            ```
            code();
            ```

            | H |
            | --- |
            | v |

            """.ReplaceLineEndings("\n"));

        Assert.Equal(
            [
                typeof(ParagraphNode),
                typeof(ParagraphNode),
                typeof(ListNode),
                typeof(ParagraphNode),
                typeof(RuleNode),
                typeof(ParagraphNode),
                typeof(TableNode),
            ],
            back.Blocks.Select(b => b.GetType()));

        Assert.Equal(ParagraphKind.Heading1, Para(back, 0).Kind);
        Assert.Equal(ParagraphKind.Quote, Para(back, 3).Kind);
        Assert.Equal(ParagraphKind.Code, Para(back, 5).Kind);
    }

    /// <summary>
    /// Exporting the imported document again gives the same Markdown, which is the property a user
    /// who opens and saves a file without touching it actually notices.
    /// </summary>
    [Fact]
    public void A_second_trip_changes_nothing()
    {
        const string Source = """
            # Title

            Some **bold** and *italic* and `code`.

            - one
            - two
              1. nested

            > A quote.

            ---

            ```
            code();
            ```

            | H | I |
            | --- | --- |
            | v | w |

            """;

        var markdown = Source.ReplaceLineEndings("\n");
        var once = MarkdownExporter.Instance.Export(MarkdownImporter.Import(markdown).Doc);

        Assert.Equal(markdown, once);

        var twice = MarkdownExporter.Instance.Export(MarkdownImporter.Import(once).Doc);

        Assert.Equal(once, twice);
    }
}
