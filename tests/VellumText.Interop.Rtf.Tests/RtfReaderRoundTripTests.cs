using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// The reader-side RTF losses that waited for the reader to be replaced (plan 4.8, re-verified in
/// phase 8): each round-trips a document through the exporter and the new reader.
/// </summary>
/// <remarks>
/// With RtfPipe these were reader limitations, so phase 4 pinned the control words it wrote instead
/// of the round trip. Each test here asserts the round trip; where the new reader still loses
/// something, the test says what and pins it, so that a reader upgrade that fixes it shows up.
/// </remarks>
public class RtfReaderRoundTripTests
{
    private const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static async Task<DocumentNode> Trip(DocumentNode doc) =>
        (await RtfImporter.ImportAsync(RtfExporter.Instance.Export(doc))).Doc;

    private static ParagraphNode Marked(string text, MarkSet mark) =>
        new(InlineContent.Create(text, [new ValueSpan<MarkSet>(0, text.Length, mark)], []));

    private static ParagraphNode Only(DocumentNode doc) => Assert.IsType<ParagraphNode>(Assert.Single(doc.Blocks));

    /// <summary>F-53: RtfPipe painted every link explicitly black.</summary>
    [Fact]
    public async Task A_link_comes_back_without_a_colour_it_never_had()
    {
        var back = Only(await Trip(new DocumentNode([Marked("click", new MarkSet { Link = new LinkMark("https://e.test/") })])));

        Assert.Equal("https://e.test/", back.Content.MarkAt(0).Link?.Href);
        Assert.Null(back.Content.MarkAt(0).Foreground);
    }

    /// <summary>F-53, second part: a link's own underline was dropped.</summary>
    [Fact]
    public async Task A_links_own_underline_survives()
    {
        var back = Only(await Trip(new DocumentNode([
            Marked("click", new MarkSet { Link = new LinkMark("https://e.test/"), Style = TextStyle.Underline }),
        ])));

        Assert.Equal(TextStyle.Underline, back.Content.MarkAt(0).Style & TextStyle.Underline);
    }

    /// <summary>F-54: a rule came back as a paragraph holding a line break.</summary>
    [Fact]
    public async Task A_rule_comes_back_as_a_rule()
    {
        var back = await Trip(new DocumentNode([ParagraphNode.FromText("a"), RuleNode.Instance, ParagraphNode.FromText("b")]));

        Assert.Contains(back.Blocks, b => b is RuleNode);
        Assert.DoesNotContain(back.Blocks.OfType<ParagraphNode>(), p => p.Content.Text.Contains('\n', StringComparison.Ordinal));
    }

    /// <summary>F-58: an empty cell came back holding a non-breaking space.</summary>
    [Fact]
    public async Task An_empty_cell_comes_back_empty()
    {
        var back = await Trip(new DocumentNode([
            new TableNode([
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("a")]), new TableCellNode([ParagraphNode.FromText(string.Empty)])]),
            ]),
        ]));

        var cell = Assert.IsType<TableNode>(Assert.Single(back.Blocks)).Rows[0].Cells[1];

        Assert.All(cell.Blocks.OfType<ParagraphNode>(), p => Assert.Equal(string.Empty, p.Content.Text));
    }

    /// <summary>F-61: a white highlight was dropped.</summary>
    [Fact]
    public async Task A_white_highlight_survives()
    {
        var white = new Rgba(255, 255, 255, 255);
        var back = Only(await Trip(new DocumentNode([Marked("lit", new MarkSet { Highlight = white })])));

        Assert.Equal(white, back.Content.MarkAt(0).Highlight);
    }

    /// <summary>Phase 4 pinned <c>\levelstartat0</c> because RtfPipe numbered from 1.</summary>
    [Fact]
    public async Task A_list_starting_at_zero_comes_back_starting_at_zero()
    {
        var back = await Trip(new DocumentNode([new ListNode([ListItemNode.FromText("a")], ListKind.Ordered, 0)]));

        Assert.Equal(0, Assert.IsType<ListNode>(Assert.Single(back.Blocks)).Start);
    }

    /// <summary>
    /// F-57: a heading in a list item. Still a reader limitation: OfficeIMO does not read
    /// <c>\outlinelevel</c> inside a list, so the paragraph comes back as body text that keeps the
    /// heading's size and weight. Pinned so a reader that fixes it shows up; the control word stays
    /// pinned in <c>RtfExportFidelityTests</c>.
    /// </summary>
    [Fact]
    public async Task A_heading_in_a_list_item_keeps_its_look_but_not_its_kind()
    {
        var back = await Trip(new DocumentNode([
            new ListNode([new ListItemNode([new ParagraphNode(InlineContent.FromText("h"), ParagraphKind.Heading2)])], ListKind.Unordered),
        ]));

        var item = Assert.Single(Assert.IsType<ListNode>(Assert.Single(back.Blocks)).Items);
        var paragraph = Assert.IsType<ParagraphNode>(item.Blocks[0]);

        Assert.Equal(ParagraphKind.Body, paragraph.Kind);
        Assert.Equal(TextStyle.Bold, paragraph.Content.MarkAt(0).Style & TextStyle.Bold);
        Assert.NotNull(paragraph.Content.MarkAt(0).FontSize);
    }

    /// <summary>
    /// F-59: an image's alt text. Still a reader limitation: OfficeIMO does not read the
    /// <c>wzDescription</c> picture property (Word does), so the picture arrives without it. Pinned
    /// so a reader that fixes it shows up; the control word stays pinned in
    /// <c>RtfExportFidelityTests</c>.
    /// </summary>
    [Fact]
    public async Task An_images_alt_text_is_not_read_back_yet()
    {
        var back = Only(await Trip(new DocumentNode([
            new ParagraphNode(InlineContent.FromEmbed(new ImageEmbed(Png, altText: "a {pic}\\"))),
        ])));

        Assert.Null(Assert.IsType<ImageEmbed>(Assert.Single(back.Content.Embeds)).AltText);
    }

    /// <summary>F-57: phase 4 pinned cell alignment because RtfPipe put it on the row.</summary>
    [Fact]
    public async Task A_cells_alignment_comes_back()
    {
        var back = await Trip(new DocumentNode([
            new TableNode([
                new TableRowNode([new TableCellNode([new ParagraphNode(InlineContent.FromText("c"), ParagraphKind.Body, TextAlign.Center)])]),
            ]),
        ]));

        var cell = Assert.IsType<TableNode>(Assert.Single(back.Blocks)).Rows[0].Cells[0];

        Assert.Equal(TextAlign.Center, Assert.IsType<ParagraphNode>(cell.Blocks[0]).Align);
    }
}
