using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// RTF exporter fidelity for headings, lists, pictures and link titles (audit of 0282015, plan
/// unit 4.7), read back through the real import path wherever that path can read the construct.
/// </summary>
public class RtfExportFidelityTests
{
    private static DocumentNode Trip(DocumentNode doc) =>
        RtfImporter.Import(RtfExporter.Instance.Export(doc)).Doc;

    private static ParagraphNode Sized(string text, double size, ParagraphKind kind) =>
        new(InlineContent.Create(
            text,
            [new ValueSpan<MarkSet>(0, text.Length, new MarkSet { FontSize = size })],
            []),
            kind);

    /// <summary>
    /// F-52: an explicit size was multiplied by the heading's scale on the way out and read back as
    /// the explicit size, so it grew on every copy and paste — 12 → 24 → 48 → 96 px in a Heading1.
    /// The heading's scale is for text that asked for no size; text that asked for one is that size.
    /// </summary>
    [Theory]
    [InlineData(ParagraphKind.Heading1)]
    [InlineData(ParagraphKind.Heading4)]
    public void An_explicit_size_inside_a_heading_does_not_grow(ParagraphKind kind)
    {
        var doc = new DocumentNode([Sized("big", 12, kind)]);

        Assert.Contains(@"\fs18", RtfExporter.Instance.Export(doc), StringComparison.Ordinal);

        for (var trip = 0; trip < 3; trip++)
        {
            doc = Trip(doc);

            var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(doc.Blocks));

            Assert.Equal(kind, paragraph.Kind);
            Assert.Equal(12, paragraph.Content.MarkAt(0).FontSize);
        }
    }

    private static ListItemNode Item(params BlockNode[] blocks) => new(blocks);

    private static string TextOf(ListItemNode item) =>
        Assert.IsType<ParagraphNode>(item.Blocks[0]).Content.Text;

    /// <summary>
    /// F-55: depth was carried only by the left indent, which no reader treats as nesting, so a
    /// nested list came back flat. Written as levels of one <c>\listtable</c> list it nests again.
    /// </summary>
    [Fact]
    public void A_nested_list_comes_back_nested()
    {
        var doc = new DocumentNode([
            new ListNode(
                [
                    Item(
                        ParagraphNode.FromText("a"),
                        new ListNode([ListItemNode.FromText("b")], ListKind.Ordered, 2)),
                    ListItemNode.FromText("c"),
                ],
                ListKind.Unordered),
        ]);

        var outer = Assert.IsType<ListNode>(Assert.Single(Trip(doc).Blocks));

        Assert.Equal(ListKind.Unordered, outer.Kind);
        Assert.Equal(2, outer.Items.Length);
        Assert.Equal("a", TextOf(outer.Items[0]));
        Assert.Equal("c", TextOf(outer.Items[1]));

        var inner = Assert.IsType<ListNode>(outer.Items[0].Blocks[1]);

        Assert.Equal(ListKind.Ordered, inner.Kind);
        Assert.Equal(2, inner.Start);
        Assert.Equal("b", TextOf(Assert.Single(inner.Items)));
    }

    /// <summary>
    /// Sibling nested lists keep their own kind and start in the \listtable. A level's numbering
    /// came from the first list at that depth, so a numbered sublist starting at 3 beside a
    /// bulleted one was written with the bullets' definition, and a reader that honours
    /// \listtable (Word does) read it as bullets from 1 (rubber-duck review 5.2).
    /// </summary>
    /// <remarks>
    /// Read from the RTF itself rather than through this branch's importer: RtfPipe treats a
    /// paragraph naming another \ls as a new top-level list, so it cannot read a sublist that
    /// needs a definition of its own. Phase 8's reader (OfficeIMO) does, and is tested there.
    /// </remarks>
    [Fact]
    public void Sibling_nested_lists_keep_their_own_kind_and_start()
    {
        var doc = new DocumentNode([
            new ListNode(
                [
                    Item(ParagraphNode.FromText("a"), new ListNode([ListItemNode.FromText("a1")], ListKind.Unordered)),
                    Item(ParagraphNode.FromText("b"), new ListNode([ListItemNode.FromText("b1"), ListItemNode.FromText("b2")], ListKind.Ordered, 3)),
                    Item(ParagraphNode.FromText("c"), new ListNode([ListItemNode.FromText("c1")], ListKind.Unordered)),
                ],
                ListKind.Unordered),
        ]);

        var rtf = RtfExporter.Instance.Export(doc);

        foreach (var (text, numbered, start) in new[] { ("a1", false, 1), ("b1", true, 3), ("b2", true, 3), ("c1", false, 1) })
        {
            var (ls, ilvl) = ListOf(rtf, text);
            var level = LevelOf(rtf, ls, ilvl);

            Assert.Equal(1, ilvl);
            Assert.Equal(numbered, level.Contains(@"\levelnfc0", StringComparison.Ordinal));
            Assert.Contains(@"\levelstartat" + start.ToString(System.Globalization.CultureInfo.InvariantCulture), level, StringComparison.Ordinal);
        }

        // The outer list is still one list: its items share one \ls.
        Assert.Equal(ListOf(rtf, "a").Ls, ListOf(rtf, "c").Ls);
    }

    /// <summary>The <c>\ls</c> and <c>\ilvl</c> of the paragraph whose text is exactly <paramref name="text"/>.</summary>
    private static (int Ls, int Ilvl) ListOf(string rtf, string text)
    {
        var match = System.Text.RegularExpressions.Regex.Matches(rtf, @"\\pard\\ls(\d+)\\ilvl(\d+)[^{}]*\{\\listtext [^{}]*\}\{" + System.Text.RegularExpressions.Regex.Escape(text) + @"\}\\par")
            .Single();

        return (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The definition of level <paramref name="ilvl"/> of the list <c>\ls</c> <paramref name="ls"/> names.</summary>
    private static string LevelOf(string rtf, int ls, int ilvl)
    {
        var id = System.Text.RegularExpressions.Regex.Match(rtf, @"\{\\listoverride\\listid(\d+)\\listoverridecount0\\ls" + ls.ToString(System.Globalization.CultureInfo.InvariantCulture) + @"\}").Groups[1].Value;
        var list = System.Text.RegularExpressions.Regex.Match(rtf, @"\{\\list\\listtemplateid\d+((?:\{\\listlevel(?:[^{}]|\{[^{}]*\})*\})+)\{\\listname ;\}\\listid" + id + @"\}").Groups[1].Value;

        return System.Text.RegularExpressions.Regex.Matches(list, @"\{\\listlevel(?:[^{}]|\{[^{}]*\})*\}")[ilvl].Value;
    }
    /// <summary>
    /// F-56: two lists written back to back were one list to a reader. Each list now has its own
    /// <c>\ls</c>, which is what tells a reader where one ends.
    /// </summary>
    [Theory]
    [InlineData(ListKind.Unordered)]
    [InlineData(ListKind.Ordered)]
    public void Two_adjacent_lists_stay_two_lists(ListKind kind)
    {
        var doc = new DocumentNode([
            new ListNode([ListItemNode.FromText("a")], kind),
            new ListNode([ListItemNode.FromText("b")], kind),
        ]);

        var lists = Trip(doc).Blocks.OfType<ListNode>().ToArray();

        Assert.Equal(2, lists.Length);
        Assert.Equal("a", TextOf(Assert.Single(lists[0].Items)));
        Assert.Equal("b", TextOf(Assert.Single(lists[1].Items)));
    }

    [Fact]
    public void A_numbered_list_keeps_its_start()
    {
        var doc = new DocumentNode([
            new ListNode([ListItemNode.FromText("a"), ListItemNode.FromText("b")], ListKind.Ordered, 3),
        ]);

        var list = Assert.IsType<ListNode>(Assert.Single(Trip(doc).Blocks));

        Assert.Equal(3, list.Start);
        Assert.Equal(2, list.Items.Length);
    }

    /// <summary>
    /// A list starting at 0 is written with <c>\levelstartat0</c>, which Word honours. RtfPipe reads
    /// a zero start as the default and numbers from 1 — a reader limitation, re-verified with the
    /// Phase 8 reader — so this pins the control word.
    /// </summary>
    [Fact]
    public void A_list_starting_at_zero_says_so()
    {
        var doc = new DocumentNode([
            new ListNode([ListItemNode.FromText("a")], ListKind.Ordered, 0),
        ]);

        Assert.Contains(@"\levelstartat0", RtfExporter.Instance.Export(doc), StringComparison.Ordinal);
    }

    /// <summary>
    /// F-57: a heading inside a list item lost its outline level, which every other paragraph
    /// writes. RtfPipe does not read an outline level inside a list, so this pins the control word.
    /// </summary>
    [Fact]
    public void A_heading_in_a_list_item_keeps_its_outline_level()
    {
        var doc = new DocumentNode([
            new ListNode(
                [Item(new ParagraphNode(InlineContent.FromText("h"), ParagraphKind.Heading2))],
                ListKind.Unordered),
        ]);

        Assert.Contains(@"\outlinelevel1", RtfExporter.Instance.Export(doc), StringComparison.Ordinal);
    }

    private static DocumentNode Linked(LinkMark link) =>
        new([new ParagraphNode(InlineContent.Create(
            "click",
            [new ValueSpan<MarkSet>(0, 5, new MarkSet { Link = link })],
            []))]);

    /// <summary>F-60: a link's title was never written, so every copy dropped it.</summary>
    [Theory]
    [InlineData("Tip")]
    [InlineData("a \"q\" b")]
    [InlineData("a\\b")]
    public void A_link_title_survives(string title)
    {
        var back = Assert.IsType<ParagraphNode>(Assert.Single(Trip(Linked(new LinkMark("https://e.test/", title))).Blocks));
        var link = back.Content.MarkAt(0).Link;

        Assert.Equal("https://e.test/", link?.Href);
        Assert.Equal(title, link?.Title);
    }

    /// <summary>
    /// F-59: an embedded picture's alternative text was never written. It is now the picture's
    /// <c>wzDescription</c> property, which Word reads as alt text. RtfPipe drops picture
    /// properties, so this pins the control words; the Phase 8 reader is re-verified.
    /// </summary>
    [Fact]
    public void An_images_alt_text_is_written_with_the_picture()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromEmbed(new ImageEmbed(
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==",
                altText: "a {pic}\\"))),
        ]);

        var rtf = RtfExporter.Instance.Export(doc);

        Assert.Contains(@"{\pict{\*\picprop{\sp{\sn wzDescription}{\sv a \{pic\}\\}}}\pngblip", rtf, StringComparison.Ordinal);

        // The picture itself still arrives.
        var back = Assert.IsType<ParagraphNode>(Assert.Single(Trip(doc).Blocks));

        Assert.IsType<ImageEmbed>(Assert.Single(back.Content.Embeds));
    }
}
