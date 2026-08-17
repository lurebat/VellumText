using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// The RTF exporter, tested by round trip through the importer that reads Word.
/// </summary>
/// <remarks>
/// <para>
/// The round trip is deliberately the whole path a paste takes: the document is written, handed
/// to RtfPipe, converted to HTML and read by the HTML importer. Testing the writer alone would
/// prove that it emits the control words this file expects and nothing about whether any reader
/// agrees, which is the only question worth asking of an interchange format.
/// </para>
/// <para>
/// Comparison is against a normalised document rather than the original, because RTF has no way
/// to say "unset". Every reader resolves the effective font and size onto every run, so text that
/// asked for nothing in particular comes back explicitly wearing the document default. Removing
/// exactly that — and nothing else — is what makes an equality assertion mean something.
/// </para>
/// <para>
/// What RTF genuinely cannot carry is pinned by its own tests further down, so that the losses
/// are recorded rather than quietly tolerated.
/// </para>
/// </remarks>
public class RtfExportTests
{
    /// <summary>The font named by the exported header, which every run comes back wearing.</summary>
    private const string DefaultFont = "Segoe UI";

    /// <summary>The size the exported header implies, in the pixels the model measures in.</summary>
    private const double DefaultSize = 16;

    private static DocumentNode RoundTrip(DocumentNode doc)
    {
        var rtf = RtfExporter.Instance.Export(doc);
        var result = RtfImporter.Import(rtf);

        return result.Doc;
    }

    /// <summary>Removes the document default that RTF cannot leave unsaid.</summary>
    private static DocumentNode Normalize(DocumentNode doc) =>
        (DocumentNode)Rewrite(doc);

    private static Node Rewrite(Node node)
    {
        if (node is ParagraphNode paragraph)
        {
            return new ParagraphNode(
                Rewrite(paragraph.Content, paragraph.Kind),
                paragraph.Kind,
                paragraph.Align,
                paragraph.IndentLevel);
        }

        return node.Children.Count == 0
            ? node
            : node.WithChildren([.. node.Children.Select(Rewrite)]);
    }

    private static InlineContent Rewrite(InlineContent content, ParagraphKind kind)
    {
        var marks = content.Marks
            .Select(span => new ValueSpan<MarkSet>(span.Start, span.Length, Rewrite(span.Value, kind)))
            .Where(span => !span.Value.IsEmpty)
            .ToArray();

        return InlineContent.Create(content.Text, marks, content.Embeds);
    }

    private static MarkSet Rewrite(MarkSet mark, ParagraphKind kind)
    {
        if (mark.FontFamily == DefaultFont)
        {
            mark = mark with { FontFamily = null };
        }

        // A heading has no separate existence in RTF: it is written as the size and weight it is
        // drawn at, and comes back wearing both explicitly. Removing exactly the appearance the
        // heading itself implies leaves whatever formatting the text actually asked for.
        if (mark.FontSize == SizeOf(kind))
        {
            mark = mark with { FontSize = null };
        }

        if (kind is not ParagraphKind.Body && mark.Has(TextStyle.Bold) && IsHeading(kind))
        {
            mark = mark with { Style = mark.Style & ~TextStyle.Bold };
        }

        return mark;
    }

    private static bool IsHeading(ParagraphKind kind) => kind
        is ParagraphKind.Heading1 or ParagraphKind.Heading2 or ParagraphKind.Heading3
        or ParagraphKind.Heading4 or ParagraphKind.Heading5 or ParagraphKind.Heading6;

    private static double SizeOf(ParagraphKind kind) => kind switch
    {
        ParagraphKind.Heading1 => 32,
        ParagraphKind.Heading2 => 24,
        ParagraphKind.Heading3 => 20,
        ParagraphKind.Heading5 => 14,
        _ => DefaultSize,
    };

    private static void Survives(DocumentNode doc) =>
        Assert.Equal(Normalize(doc), Normalize(RoundTrip(doc)));

    private static string TextOf(DocumentNode doc) =>
        string.Join("\n", doc.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text));

    private static ParagraphNode Para(DocumentNode doc, int index = 0) =>
        doc.Blocks.OfType<ParagraphNode>().ElementAt(index);

    // ---- the words themselves -----------------------------------------------------------------

    [Fact]
    public void The_words_survive() =>
        Survives(DocumentNode.FromParagraphs("first", "second", "third"));

    [Fact]
    public void An_empty_document_does_not_throw() =>
        Assert.StartsWith(@"{\rtf", RtfExporter.Instance.Export(DocumentNode.Empty), StringComparison.Ordinal);

    /// <summary>
    /// A brace and a backslash are RTF's own punctuation. Written literally they would end the
    /// group the text is in, and everything after them would be read as markup.
    /// </summary>
    [Theory]
    [InlineData(@"a\b")]
    [InlineData("{braced}")]
    [InlineData(@"}\{")]
    [InlineData(@"C:\Users\name")]
    public void Rtf_punctuation_in_the_text_is_escaped(string text) =>
        Survives(DocumentNode.FromParagraphs(text));

    /// <summary>
    /// RTF's body is bytes in a code page, so anything outside it travels as a \u escape — and
    /// \u takes a signed 16-bit integer, which means an astral character is two negative ones.
    /// </summary>
    [Theory]
    [InlineData("Caf\u00e9")]
    [InlineData("\u05e9\u05dc\u05d5\u05dd and English")]
    [InlineData("\ud83d\udc69\u200d\ud83d\udc67")]
    [InlineData("a\u0301 combining")]
    [InlineData("\u20ac\u00a3\u00a5")]
    public void Text_outside_the_code_page_survives(string text) =>
        Survives(DocumentNode.FromParagraphs(text));

    [Fact]
    public void A_line_break_inside_a_paragraph_stays_a_line_break() =>
        Survives(DocumentNode.FromParagraphs("one\ntwo\nthree"));

    /// <summary>
    /// A tab is written as RTF's own tab control word, which is what a word processor reads.
    /// It does not come back: see <see cref="Whitespace_between_words_is_written_faithfully"/>.
    /// </summary>
    [Fact]
    public void A_tab_is_written_as_a_tab() =>
        Assert.Contains(@"\tab ", RtfExporter.Instance.Export(DocumentNode.FromParagraphs("left\tright")));

    // ---- formatting ---------------------------------------------------------------------------

    [Theory]
    [InlineData(TextStyle.Bold)]
    [InlineData(TextStyle.Italic)]
    [InlineData(TextStyle.Underline)]
    [InlineData(TextStyle.Strikethrough)]
    [InlineData(TextStyle.Super)]
    [InlineData(TextStyle.Sub)]
    public void Character_formatting_survives(TextStyle style) =>
        Survives(Styled("plain marked plain", 6, 6, new MarkSet { Style = style }));

    [Fact]
    public void Several_styles_at_once_survive() =>
        Survives(Styled(
            "plain marked plain",
            6,
            6,
            new MarkSet { Style = TextStyle.Bold | TextStyle.Italic | TextStyle.Underline }));

    [Fact]
    public void Adjacent_runs_do_not_leak_into_each_other() =>
        Survives(new DocumentNode([new ParagraphNode(InlineContent.Create(
            "boldplainitalic",
            [
                new ValueSpan<MarkSet>(0, 4, new MarkSet { Style = TextStyle.Bold }),
                new ValueSpan<MarkSet>(9, 6, new MarkSet { Style = TextStyle.Italic }),
            ],
            []))]));

    [Fact]
    public void Colour_survives() =>
        Survives(Styled("plain marked plain", 6, 6, new MarkSet { Foreground = Rgba.FromRgb(200, 30, 40) }));

    [Fact]
    public void A_highlight_survives() =>
        Survives(Styled("plain marked plain", 6, 6, new MarkSet { Highlight = Rgba.FromRgb(255, 255, 0) }));

    [Fact]
    public void One_colour_used_twice_is_written_to_the_table_once()
    {
        var doc = new DocumentNode([new ParagraphNode(InlineContent.Create(
            "red and red",
            [
                new ValueSpan<MarkSet>(0, 3, new MarkSet { Foreground = Rgba.FromRgb(255, 0, 0) }),
                new ValueSpan<MarkSet>(8, 3, new MarkSet { Foreground = Rgba.FromRgb(255, 0, 0) }),
            ],
            []))]);

        var rtf = RtfExporter.Instance.Export(doc);

        Assert.Contains(@"{\colortbl;\red255\green0\blue0;}", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void A_font_family_survives() =>
        Survives(Styled("plain marked plain", 6, 6, new MarkSet { FontFamily = "Georgia" }));

    [Fact]
    public void A_font_size_survives() =>
        Survives(Styled("plain marked plain", 6, 6, new MarkSet { FontSize = 32 }));

    [Fact]
    public void A_link_survives()
    {
        var doc = new DocumentNode([new ParagraphNode(InlineContent.Create(
            "see here now",
            [new ValueSpan<MarkSet>(4, 4, new MarkSet { Link = new LinkMark("https://example.com/x?a=1&b=2") })],
            []))]);

        var back = RoundTrip(doc);

        Assert.Equal("see here now", TextOf(back));
        Assert.Equal("https://example.com/x?a=1&b=2", Para(back).Content.MarkAt(4).Link?.Href);
        Assert.Null(Para(back).Content.MarkAt(0).Link);
        Assert.Null(Para(back).Content.MarkAt(9).Link);
    }

    /// <summary>
    /// A quote in a link target would close the HYPERLINK field's own quoting, and everything
    /// after it would be read as further field instructions.
    /// </summary>
    [Fact]
    public void A_link_target_cannot_break_out_of_its_field()
    {
        var doc = new DocumentNode([new ParagraphNode(InlineContent.Create(
            "click",
            [new ValueSpan<MarkSet>(0, 5, new MarkSet { Link = new LinkMark("https://x/\"}{\\evil") })],
            []))]);

        Assert.Equal("click", TextOf(RoundTrip(doc)));
    }

    // ---- paragraphs ---------------------------------------------------------------------------

    [Theory]
    [InlineData(TextAlign.Center)]
    [InlineData(TextAlign.Right)]
    [InlineData(TextAlign.Justify)]
    [InlineData(TextAlign.Default)]
    public void Alignment_survives(TextAlign align) =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("text"), ParagraphKind.Body, align)]));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Indent_depth_survives(int level) =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("text"), ParagraphKind.Body, TextAlign.Default, level)]));

    [Theory]
    [InlineData(ParagraphKind.Heading1)]
    [InlineData(ParagraphKind.Heading2)]
    [InlineData(ParagraphKind.Heading3)]
    public void A_heading_survives_as_a_heading(ParagraphKind kind)
    {
        // \outlinelevel is the one part of a heading RTF can state outright, and it is enough for
        // the level to come back. The size and weight travel separately, as appearance.
        var back = RoundTrip(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("title"), kind)]));

        Assert.Equal(kind, Para(back).Kind);
        Assert.Equal("title", TextOf(back));
    }

    // ---- lists --------------------------------------------------------------------------------

    [Fact]
    public void A_bullet_list_survives() =>
        Survives(new DocumentNode([
            new ListNode([ListItemNode.FromText("one"), ListItemNode.FromText("two")], ListKind.Unordered)]));

    [Fact]
    public void A_numbered_list_survives() =>
        Survives(new DocumentNode([
            new ListNode([ListItemNode.FromText("one"), ListItemNode.FromText("two")], ListKind.Ordered)]));

    [Fact]
    public void A_numbered_list_keeps_its_starting_number() =>
        Survives(new DocumentNode([
            new ListNode([ListItemNode.FromText("seven"), ListItemNode.FromText("eight")], ListKind.Ordered, 7)]));

    [Fact]
    public void Formatting_inside_a_list_item_survives() =>
        Survives(new DocumentNode([
            new ListNode(
                [new ListItemNode([new ParagraphNode(InlineContent.Create(
                    "plain bold",
                    [new ValueSpan<MarkSet>(6, 4, new MarkSet { Style = TextStyle.Bold })],
                    []))])],
                ListKind.Unordered)]));

    // ---- tables -------------------------------------------------------------------------------

    [Fact]
    public void A_table_survives_with_its_shape_and_its_words()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode([ParagraphNode.FromText("one")]),
                    new TableCellNode([ParagraphNode.FromText("two")])]),
                new TableRowNode([
                    new TableCellNode([ParagraphNode.FromText("three")]),
                    new TableCellNode([ParagraphNode.FromText("four")])])])]);

        var table = Assert.IsType<TableNode>(Assert.Single(RoundTrip(doc).Blocks));

        Assert.Equal(2, table.Rows.Length);
        Assert.All(table.Rows, row => Assert.Equal(2, row.Cells.Length));
        Assert.Equal(
            ["one", "two", "three", "four"],
            table.Rows.SelectMany(r => r.Cells).Select(c => ((ParagraphNode)c.Blocks[0]).Content.Text));
    }

    // ---- what RTF cannot carry ----------------------------------------------------------------

    /// <summary>
    /// RTF has no quotation. The indent it is drawn with is the only part that can travel, so a
    /// quote comes back as an indented paragraph — which is what it looked like.
    /// </summary>
    [Fact]
    public void A_quote_arrives_as_an_indent()
    {
        var back = RoundTrip(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("quoted"), ParagraphKind.Quote)]));

        Assert.Equal(ParagraphKind.Body, Para(back).Kind);
        Assert.Equal(1, Para(back).IndentLevel);
        Assert.Equal("quoted", TextOf(back));
    }

    /// <summary>
    /// A code block loses its kind for the same reason, and keeps only its words. The monospaced
    /// font a reader would draw it in is not something the model records at block level.
    /// </summary>
    [Fact]
    public void A_code_block_keeps_its_words_but_not_its_kind()
    {
        var back = RoundTrip(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("var x = 1;"), ParagraphKind.Code)]));

        Assert.Equal(ParagraphKind.Body, Para(back).Kind);
        Assert.Equal("var x = 1;", TextOf(back));
    }

    /// <summary>
    /// A header cell is a table cell drawn in bold. Nothing in RTF says which cells are headers,
    /// so the emphasis survives and the role does not.
    /// </summary>
    [Fact]
    public void A_header_cell_keeps_its_emphasis_but_not_its_role()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("head")], isHeader: true)])])]);

        var table = Assert.IsType<TableNode>(Assert.Single(RoundTrip(doc).Blocks));
        var cell = table.Rows[0].Cells[0];
        var paragraph = (ParagraphNode)cell.Blocks[0];

        Assert.False(cell.IsHeader);
        Assert.True(paragraph.Content.MarkAt(0).Has(TextStyle.Bold));
    }

    /// <summary>
    /// A space at the very edge of a paragraph does not survive the trip through HTML that every
    /// RTF reader in reach performs. The words do; the padding around them does not.
    /// </summary>
    [Theory]
    [InlineData(" leading", "leading")]
    [InlineData("trailing ", "trailing")]
    public void A_space_at_the_edge_of_a_paragraph_is_lost(string written, string read) =>
        Assert.Equal(read, TextOf(RoundTrip(DocumentNode.FromParagraphs(written))));

    /// <summary>
    /// Whitespace between words is written faithfully — RTF keeps every space, and a word
    /// processor reading the file sees them. What comes back is not the same characters, because
    /// every RTF reader in reach goes through HTML: a doubled space returns with the second space
    /// turned into a non-breaking one, and a tab vanishes entirely, HTML having nothing to map it
    /// onto. The loss is in the reader, so the assertion that matters is on the bytes we produce.
    /// </summary>
    [Theory]
    [InlineData("two  spaces", "two  spaces", "two \u00a0spaces")]
    [InlineData("left\tright", @"\tab ", "leftright")]
    public void Whitespace_between_words_is_written_faithfully(string text, string written, string read)
    {
        var doc = DocumentNode.FromParagraphs(text);

        Assert.Contains(written, RtfExporter.Instance.Export(doc));
        Assert.Equal(read, TextOf(RoundTrip(doc)));
    }

    // ---- the exporter itself ------------------------------------------------------------------

    [Fact]
    public void The_exporter_advertises_itself()
    {
        Assert.Equal("rtf", RtfExporter.Instance.Format);
        Assert.Equal("text/rtf", RtfExporter.Instance.MediaType);
    }

    [Fact]
    public void A_null_document_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => RtfExporter.Instance.Export(null!));

    /// <summary>Everything at once, which is what a real copy looks like.</summary>
    [Fact]
    public void A_realistic_document_survives()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromText("Report"), ParagraphKind.Heading1),
            new ParagraphNode(InlineContent.Create(
                "Sales rose by 12% in Q3, which beat the forecast.",
                [
                    new ValueSpan<MarkSet>(14, 3, new MarkSet { Style = TextStyle.Bold }),
                    new ValueSpan<MarkSet>(21, 2, new MarkSet { Foreground = Rgba.FromRgb(0, 128, 0) }),
                ],
                [])),
            new ListNode(
                [ListItemNode.FromText("North"), ListItemNode.FromText("South")],
                ListKind.Unordered),
            new ParagraphNode(
                InlineContent.FromText("Prepared in Jerusalem."),
                ParagraphKind.Body,
                TextAlign.Right),
        ]);

        Survives(doc);
    }

    private static DocumentNode Styled(string text, int start, int length, MarkSet mark) =>
        new([new ParagraphNode(InlineContent.Create(
            text,
            [new ValueSpan<MarkSet>(start, length, mark)],
            []))]);
}
