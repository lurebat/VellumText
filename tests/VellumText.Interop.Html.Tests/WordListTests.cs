using VellumText;
using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// Word's clipboard HTML has no <c>ul</c> or <c>ol</c>. Each list item is a paragraph whose style
/// says <c>mso-list:l0 level1 lfo1</c>, with its bullet written out as text between
/// <c>&lt;![if !supportLists]&gt;</c> and <c>&lt;![endif]&gt;</c>. Read as ordinary paragraphs, a
/// pasted list arrived as "·" paragraphs in the Symbol font.
/// </summary>
public class WordListTests
{
    private static async Task<ImportResult> Import(string html) => await HtmlImporter.ImportAsync(html);

    private static string Text(BlockNode block) => Assert.IsType<ParagraphNode>(block).Content.Text;

    private static string[] ItemTexts(ListNode list) => [.. list.Items.Select(item => Text(item.Blocks[0]))];

    /// <summary>A Word list paragraph, as Word writes one.</summary>
    private static string Item(string list, int level, string marker, string text) =>
        $"<p class=MsoListParagraphCxSpMiddle style='margin-left:{36 * level}.0pt;text-indent:-18.0pt;"
        + $"mso-list:{list} level{level} lfo1'><![if !supportLists]><span style='font-family:Symbol'>"
        + $"<span style='mso-list:Ignore'>{marker}<span style='font:7.0pt \"Times New Roman\"'>"
        + "&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; </span></span></span><![endif]>"
        + $"<span dir=LTR></span>{text}<o:p></o:p></p>\r\n";

    [Fact]
    public async Task A_bulleted_list_copied_from_word_pastes_as_a_list()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "word-docx-all", "03-HTML_Format.bin");

        using var stream = File.OpenRead(path);

        var blocks = (await HtmlFormat.Instance.ImportAsync(stream)).Doc.Blocks;
        var list = Assert.IsType<ListNode>(Assert.Single(blocks, block => block is ListNode));

        Assert.Equal(ListKind.Unordered, list.Kind);
        Assert.Equal(["Bullet one", "Bullet two"], ItemTexts(list));

        // In place: after the blank line that precedes it in the document and before the table.
        var at = blocks.IndexOf(list);
        Assert.IsType<ParagraphNode>(blocks[at - 1]);
        Assert.IsType<TableNode>(blocks[at + 1]);

        // And the bullet is the list's to draw, not text.
        Assert.DoesNotContain(
            blocks.OfType<ParagraphNode>(),
            p => p.Content.Text.Contains('·', StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_numbered_marker_makes_an_ordered_list()
    {
        var list = Assert.IsType<ListNode>(Assert.Single(
            (await Import(Item("l1", 1, "1.", "One") + Item("l1", 1, "2.", "Two"))).Doc.Blocks));

        Assert.Equal(ListKind.Ordered, list.Kind);
        Assert.Equal(["One", "Two"], ItemTexts(list));
    }

    [Theory]
    [InlineData("a)")]
    [InlineData("iv.")]
    [InlineData("(3)")]
    public async Task Letters_and_numerals_are_numbering_too(string marker)
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(Item("l1", 1, marker, "x"))).Doc.Blocks));

        Assert.Equal(ListKind.Ordered, list.Kind);
    }

    [Theory]
    [InlineData("o")]
    [InlineData("§")]
    [InlineData("-")]
    public async Task Any_other_marker_is_a_bullet(string marker)
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(Item("l1", 1, marker, "x"))).Doc.Blocks));

        Assert.Equal(ListKind.Unordered, list.Kind);
        Assert.Equal(["x"], ItemTexts(list));
    }

    [Fact]
    public async Task A_deeper_level_nests_inside_the_item_before_it()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(
            Item("l0", 1, "·", "A") + Item("l0", 2, "o", "B") + Item("l0", 2, "o", "C") + Item("l0", 1, "·", "D")))
            .Doc.Blocks));

        Assert.Equal(["A", "D"], ItemTexts(list));
        Assert.Equal(2, list.Items[0].Blocks.Length);
        var nested = Assert.IsType<ListNode>(list.Items[0].Blocks[1]);
        Assert.Equal(["B", "C"], ItemTexts(nested));
        Assert.Single(list.Items[1].Blocks);
    }

    [Fact]
    public async Task A_level_that_skips_ahead_nests_only_one_deeper()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(
            Item("l0", 1, "·", "A") + Item("l0", 3, "§", "B"))).Doc.Blocks));

        var nested = Assert.IsType<ListNode>(Assert.Single(list.Items).Blocks[1]);
        Assert.Equal(["B"], ItemTexts(nested));
        Assert.IsType<ParagraphNode>(Assert.Single(nested.Items).Blocks[0]);
    }

    /// <summary>
    /// A copy that starts part-way down a nested list repeats its first level as siblings. The
    /// count of lists already open was compared with Word's declared level, so two level-2 items
    /// with nothing above them nested one inside the other (rubber-duck review 4.2).
    /// </summary>
    [Fact]
    public async Task Items_at_a_repeated_deeper_level_are_siblings()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(
            Item("l0", 2, "o", "A") + Item("l0", 2, "o", "B") + Item("l0", 2, "o", "C"))).Doc.Blocks));

        Assert.Equal(["A", "B", "C"], ItemTexts(list));
        Assert.All(list.Items, item => Assert.Single(item.Blocks));
    }

    [Fact]
    public async Task Items_after_a_skipped_level_are_siblings_under_the_item_above()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(
            Item("l0", 1, "·", "One") + Item("l0", 3, "§", "X") + Item("l0", 3, "§", "Y") + Item("l0", 1, "·", "Two")))
            .Doc.Blocks));

        Assert.Equal(["One", "Two"], ItemTexts(list));
        var nested = Assert.IsType<ListNode>(list.Items[0].Blocks[1]);
        Assert.Equal(["X", "Y"], ItemTexts(nested));
        Assert.All(nested.Items, item => Assert.Single(item.Blocks));
    }

    [Fact]
    public async Task A_change_of_marker_kind_at_one_level_starts_a_new_list()
    {
        var blocks = (await Import(Item("l0", 1, "·", "a") + Item("l0", 1, "1.", "b"))).Doc.Blocks;

        Assert.Equal(2, blocks.Length);
        Assert.Equal(ListKind.Unordered, Assert.IsType<ListNode>(blocks[0]).Kind);
        Assert.Equal(ListKind.Ordered, Assert.IsType<ListNode>(blocks[1]).Kind);
    }

    [Fact]
    public async Task A_different_word_list_is_a_different_list()
    {
        var blocks = (await Import(Item("l0", 1, "·", "a") + Item("l1", 1, "·", "b"))).Doc.Blocks;

        Assert.Equal(2, blocks.Length);
        Assert.Equal(["a"], ItemTexts(Assert.IsType<ListNode>(blocks[0])));
        Assert.Equal(["b"], ItemTexts(Assert.IsType<ListNode>(blocks[1])));
    }

    [Fact]
    public async Task A_paragraph_between_items_ends_the_list()
    {
        var blocks = (await Import(Item("l0", 1, "·", "a") + "<p class=MsoNormal>between</p>" + Item("l0", 1, "·", "b")))
            .Doc.Blocks;

        Assert.Equal(3, blocks.Length);
        Assert.Equal(["a"], ItemTexts(Assert.IsType<ListNode>(blocks[0])));
        Assert.Equal("between", Text(blocks[1]));
        Assert.Equal(["b"], ItemTexts(Assert.IsType<ListNode>(blocks[2])));
    }

    [Fact]
    public async Task Without_the_conditional_fence_the_ignored_marker_is_still_dropped()
    {
        // Outlook and older Word write the marker span without <![if !supportLists]> around it.
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(
            "<p style='mso-list:l0 level1 lfo1'><span style='mso-list:Ignore'>1.&nbsp;&nbsp;</span>One</p>"))
            .Doc.Blocks));

        Assert.Equal(ListKind.Ordered, list.Kind);
        Assert.Equal(["One"], ItemTexts(list));
    }

    [Fact]
    public async Task The_fence_alone_is_enough_to_find_the_marker()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(
            "<p style='mso-list:l0 level1 lfo1'><![if !supportLists]>1.&nbsp;&nbsp;<![endif]>One</p>"))
            .Doc.Blocks));

        Assert.Equal(ListKind.Ordered, list.Kind);
        Assert.Equal(["One"], ItemTexts(list));
    }

    [Fact]
    public async Task An_unclosed_fence_does_not_swallow_the_item()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(
            "<p style='mso-list:l0 level1 lfo1'><![if !supportLists]><span style='mso-list:Ignore'>·</span>Text</p>"))
            .Doc.Blocks));

        Assert.Equal(["Text"], ItemTexts(list));
    }

    [Fact]
    public async Task A_comment_between_items_does_not_split_the_list()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import(
            Item("l0", 1, "·", "a") + "<!-- between -->" + Item("l0", 1, "·", "b"))).Doc.Blocks));

        Assert.Equal(["a", "b"], ItemTexts(list));
    }

    [Fact]
    public async Task A_numbered_heading_after_a_list_stays_a_heading()
    {
        // Word numbers headings with mso-list too. Only paragraphs become list items.
        var blocks = (await Import(Item("l0", 1, "·", "a") + "<h1 style='mso-list:l0 level1 lfo1'>Heading</h1>")).Doc.Blocks;

        Assert.Equal(2, blocks.Length);
        Assert.Equal(["a"], ItemTexts(Assert.IsType<ListNode>(blocks[0])));
        Assert.Equal(ParagraphKind.Heading1, Assert.IsType<ParagraphNode>(blocks[1]).Kind);
    }

    [Fact]
    public async Task Paragraphs_that_are_not_word_list_items_are_left_alone()
    {
        var blocks = (await Import(
            "<p style='mso-list:Ignore'>ignored</p><p style='mso-list:none'>none</p>"
            + "<h1 style='mso-list:l0 level1 lfo1'>heading</h1><p style='color:red'>plain mso-list text</p>"))
            .Doc.Blocks;

        Assert.Equal(4, blocks.Length);
        Assert.All(blocks, block => Assert.IsType<ParagraphNode>(block));
        Assert.Equal(ParagraphKind.Heading1, Assert.IsType<ParagraphNode>(blocks[2]).Kind);
    }
}
