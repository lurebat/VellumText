using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// What an import is supposed to keep: the structure and the formatting a person would say
/// they had copied.
/// </summary>
public class HtmlImportTests
{
    private const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static async Task<ImportResult> Import(string html) => await HtmlImporter.ImportAsync(html);

    private static ParagraphNode Para(ImportResult result, int index = 0) =>
        result.Doc.Blocks.OfType<ParagraphNode>().ElementAt(index);

    private static ParagraphNode OnlyPara(ImportResult result) =>
        Assert.IsType<ParagraphNode>(Assert.Single(result.Doc.Blocks));

    private static TextStyle StyleAt(ParagraphNode p, int offset) => p.Content.MarkAt(offset).Style;

    // ---- inline formatting ------------------------------------------------------------------

    [Theory]
    [InlineData("<b>x</b>", TextStyle.Bold)]
    [InlineData("<strong>x</strong>", TextStyle.Bold)]
    [InlineData("<i>x</i>", TextStyle.Italic)]
    [InlineData("<em>x</em>", TextStyle.Italic)]
    [InlineData("<u>x</u>", TextStyle.Underline)]
    [InlineData("<ins>x</ins>", TextStyle.Underline)]
    [InlineData("<s>x</s>", TextStyle.Strikethrough)]
    [InlineData("<del>x</del>", TextStyle.Strikethrough)]
    [InlineData("<strike>x</strike>", TextStyle.Strikethrough)]
    [InlineData("<code>x</code>", TextStyle.Code)]
    [InlineData("<sup>x</sup>", TextStyle.Super)]
    [InlineData("<sub>x</sub>", TextStyle.Sub)]
    public async Task An_element_carries_its_formatting_onto_the_text_inside_it(string html, TextStyle expected)
    {
        var p = OnlyPara(await Import(html));

        Assert.Equal("x", p.Content.Text);
        Assert.Equal(expected, StyleAt(p, 0));
    }

    [Fact]
    public async Task Nested_elements_compose_rather_than_replace()
    {
        var p = OnlyPara(await Import("<b>a<i>b</i>c</b>"));

        Assert.Equal("abc", p.Content.Text);
        Assert.Equal(TextStyle.Bold, StyleAt(p, 0));
        Assert.Equal(TextStyle.Bold | TextStyle.Italic, StyleAt(p, 1));
        Assert.Equal(TextStyle.Bold, StyleAt(p, 2));
    }

    [Fact]
    public async Task Superscript_and_subscript_replace_each_other_because_the_model_forbids_both()
    {
        // MarkSet validates that Sub and Super are mutually exclusive, so the importer has to
        // clear one when it sets the other instead of letting the model throw.
        var p = OnlyPara(await Import("<sup><sub>x</sub></sup>"));

        Assert.Equal(TextStyle.Sub, StyleAt(p, 0));
    }

    [Fact]
    public async Task Css_can_say_the_same_things_the_elements_do()
    {
        var p = OnlyPara(await Import(
            "<span style='font-weight:bold;font-style:italic;text-decoration:underline line-through'>x</span>"));

        Assert.Equal(
            TextStyle.Bold | TextStyle.Italic | TextStyle.Underline | TextStyle.Strikethrough,
            StyleAt(p, 0));
    }

    [Fact]
    public async Task A_numeric_font_weight_of_600_or_more_is_bold()
    {
        Assert.Equal(TextStyle.Bold, StyleAt(OnlyPara(await Import("<span style='font-weight:700'>x</span>")), 0));
        Assert.Equal(TextStyle.None, StyleAt(OnlyPara(await Import("<span style='font-weight:400'>x</span>")), 0));
    }

    [Fact]
    public async Task A_colour_survives_in_whatever_notation_it_arrived_in()
    {
        foreach (var css in new[] { "#ff0000", "red", "rgb(255,0,0)", "rgba(255,0,0,1)" })
        {
            var p = OnlyPara(await Import($"<span style='color:{css}'>x</span>"));

            Assert.Equal(new Rgba(255, 0, 0, 255), p.Content.MarkAt(0).Foreground);
        }
    }

    [Theory]
    [InlineData("#808080", 128, 128, 128)]
    [InlineData("#123456", 18, 52, 86)]
    [InlineData("#ff8800", 255, 136, 0)]
    [InlineData("#c0c0c0", 192, 192, 192)]
    [InlineData("gray", 128, 128, 128)]
    [InlineData("rgb(18,52,86)", 18, 52, 86)]
    [InlineData("rgb(50%,50%,50%)", 128, 128, 128)]
    public async Task A_colour_that_is_not_fully_on_or_fully_off_keeps_its_value(string css, byte r, byte g, byte b)
    {
        // Every colour test here used to be a saturated one, and saturated colours are the only
        // ones the bug this pins could not corrupt: it drove every non-zero channel to 255, so a
        // grey document imported as a white one and pure red came out right by luck.
        var p = OnlyPara(await Import($"<span style='color:{css}'>x</span>"));

        Assert.Equal(new Rgba(r, g, b, 255), p.Content.MarkAt(0).Foreground);
    }

    [Fact]
    public async Task A_translucent_colour_keeps_both_its_channels_and_its_alpha()
    {
        var p = OnlyPara(await Import("<span style='color:rgba(18,52,86,0.5)'>x</span>"));
        var colour = p.Content.MarkAt(0).Foreground!.Value;

        Assert.Equal(18, colour.R);
        Assert.Equal(52, colour.G);
        Assert.Equal(86, colour.B);
        Assert.InRange(colour.A, (byte)126, (byte)129);
    }
    [Theory]
    [InlineData("rgb(50%,50%,50%)", 128, 128, 128)]
    [InlineData("rgb(100%,0%,20%)", 255, 0, 51)]
    [InlineData("rgb(18,52,86)", 18, 52, 86)]
    public void A_percentage_channel_is_scaled_and_rounded(string css, byte r, byte g, byte b)
    {
        // Asserted against the parser rather than through an import, because AngleSharp resolves a
        // percentage to a number before the importer ever sees the declaration -- so a test that
        // goes through HTML cannot reach this branch at all.
        Assert.True(CssStyles.TryColor(css, out var colour));
        Assert.Equal(new Rgba(r, g, b, 255), colour);
    }
    [Fact]
    public async Task A_font_size_in_an_absolute_unit_survives_and_a_relative_one_does_not()
    {
        Assert.Equal(24, OnlyPara(await Import("<span style='font-size:18pt'>x</span>")).Content.MarkAt(0).FontSize);

        // "2em" depends on a parent size the importer does not track, so guessing would be worse
        // than inheriting.
        Assert.Null(OnlyPara(await Import("<span style='font-size:2em'>x</span>")).Content.MarkAt(0).FontSize);
    }

    [Fact]
    public async Task A_link_becomes_a_mark_that_spans_exactly_its_text()
    {
        var p = OnlyPara(await Import("a<a href='https://example.com/'>b</a>c"));

        Assert.Equal("abc", p.Content.Text);
        Assert.Null(p.Content.MarkAt(0).Link);
        Assert.Equal("https://example.com/", p.Content.MarkAt(1).Link?.Href);
        Assert.Null(p.Content.MarkAt(2).Link);
    }

    [Fact]
    public async Task A_links_title_comes_along_with_it()
    {
        var p = OnlyPara(await Import("<a href='https://example.com/' title='t'>b</a>"));

        Assert.Equal("t", p.Content.MarkAt(0).Link?.Title);
    }

    [Fact]
    public async Task A_font_element_still_works_because_old_documents_still_use_it()
    {
        var p = OnlyPara(await Import("<font color='#00ff00' face='Georgia'>x</font>"));

        Assert.Equal(new Rgba(0, 255, 0, 255), p.Content.MarkAt(0).Foreground);
        Assert.Equal("Georgia", p.Content.MarkAt(0).FontFamily);
    }

    // ---- blocks -----------------------------------------------------------------------------

    [Theory]
    [InlineData("h1", ParagraphKind.Heading1)]
    [InlineData("h2", ParagraphKind.Heading2)]
    [InlineData("h3", ParagraphKind.Heading3)]
    [InlineData("h4", ParagraphKind.Heading4)]
    [InlineData("h5", ParagraphKind.Heading5)]
    [InlineData("h6", ParagraphKind.Heading6)]
    public async Task A_heading_keeps_its_level(string tag, ParagraphKind expected)
    {
        Assert.Equal(expected, OnlyPara(await Import($"<{tag}>x</{tag}>")).Kind);
    }

    [Fact]
    public async Task A_quote_is_a_quote_and_a_quote_inside_one_is_indented_further()
    {
        Assert.Equal(ParagraphKind.Quote, OnlyPara(await Import("<blockquote>x</blockquote>")).Kind);

        var nested = OnlyPara(await Import("<blockquote><blockquote>x</blockquote></blockquote>"));

        Assert.Equal(ParagraphKind.Quote, nested.Kind);
        Assert.Equal(1, nested.IndentLevel);
    }

    [Fact]
    public async Task Preformatted_text_keeps_the_spacing_that_ordinary_text_loses()
    {
        var pre = OnlyPara(await Import("<pre>a   b\nc</pre>"));

        Assert.Equal(ParagraphKind.Code, pre.Kind);
        Assert.Equal("a   b\nc", pre.Content.Text);
    }

    [Fact]
    public async Task Consecutive_whitespace_outside_a_pre_collapses_to_one_space()
    {
        Assert.Equal("a b", OnlyPara(await Import("<p>a   \n\t b</p>")).Content.Text);
    }

    [Fact]
    public async Task Whitespace_at_the_edge_of_a_block_is_not_kept()
    {
        Assert.Equal("a", OnlyPara(await Import("<p>   a   </p>")).Content.Text);
    }

    [Fact]
    public async Task Whitespace_between_two_inline_elements_is_still_a_space()
    {
        Assert.Equal("a b", OnlyPara(await Import("<b>a</b> <i>b</i>")).Content.Text);
    }

    [Fact]
    public async Task A_line_break_stays_inside_the_paragraph_it_broke()
    {
        var p = OnlyPara(await Import("<p>a<br>b</p>"));

        Assert.Equal("a\nb", p.Content.Text);
    }

    [Fact]
    public async Task A_line_break_swallows_the_whitespace_on_either_side_of_it()
    {
        Assert.Equal("a\nb", OnlyPara(await Import("<p>a <br> b</p>")).Content.Text);
    }

    [Fact]
    public async Task A_line_break_carries_the_formatting_it_sits_in()
    {
        var p = OnlyPara(await Import("<p><b>a<br>b</b></p>"));

        Assert.Equal("a\nb", p.Content.Text);
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(TextStyle.Bold, StyleAt(p, i)));
    }

    [Fact]
    public async Task A_rule_becomes_a_rule_and_ends_the_paragraph_before_it()
    {
        var blocks = (await Import("<p>a</p><hr><p>b</p>")).Doc.Blocks;

        Assert.Equal(3, blocks.Length);
        Assert.Equal("a", Assert.IsType<ParagraphNode>(blocks[0]).Content.Text);
        Assert.IsType<RuleNode>(blocks[1]);
        Assert.Equal("b", Assert.IsType<ParagraphNode>(blocks[2]).Content.Text);
    }

    [Fact]
    public async Task Two_blocks_do_not_run_into_one_another()
    {
        var result = await Import("<p>a</p><p>b</p>");

        Assert.Equal(2, result.Doc.Blocks.Length);
        Assert.Equal("a", Para(result, 0).Content.Text);
        Assert.Equal("b", Para(result, 1).Content.Text);
    }

    [Theory]
    [InlineData("center", TextAlign.Center)]
    [InlineData("right", TextAlign.Right)]
    [InlineData("justify", TextAlign.Justify)]
    [InlineData("left", TextAlign.Left)]
    public async Task Alignment_comes_through(string css, TextAlign expected)
    {
        Assert.Equal(expected, OnlyPara(await Import($"<p style='text-align:{css}'>x</p>")).Align);
    }

    [Fact]
    public async Task A_left_margin_becomes_an_indent_level()
    {
        // Word writes indentation as a margin in points; the model counts levels.
        Assert.Equal(1, OnlyPara(await Import("<p style='margin-left:48px'>x</p>")).IndentLevel);
        Assert.Equal(2, OnlyPara(await Import("<p style='margin-left:96px'>x</p>")).IndentLevel);
        Assert.Equal(0, OnlyPara(await Import("<p style='margin-left:4px'>x</p>")).IndentLevel);
    }

    // ---- lists ------------------------------------------------------------------------------

    [Fact]
    public async Task A_bulleted_list_becomes_a_list_of_items()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import("<ul><li>a</li><li>b</li></ul>")).Doc.Blocks));

        Assert.Equal(ListKind.Unordered, list.Kind);
        Assert.Equal(2, list.Items.Length);
        Assert.Equal("a", Assert.IsType<ParagraphNode>(list.Items[0].Blocks[0]).Content.Text);
        Assert.Equal("b", Assert.IsType<ParagraphNode>(list.Items[1].Blocks[0]).Content.Text);
    }

    [Fact]
    public async Task A_numbered_list_keeps_the_number_it_starts_at()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import("<ol start='5'><li>a</li></ol>")).Doc.Blocks));

        Assert.Equal(ListKind.Ordered, list.Kind);
        Assert.Equal(5, list.Start);
    }

    [Fact]
    public async Task A_list_start_that_is_not_a_number_does_not_break_the_import()
    {
        var list = Assert.IsType<ListNode>(Assert.Single((await Import("<ol start='banana'><li>a</li></ol>")).Doc.Blocks));

        Assert.Equal(1, list.Start);
    }

    [Fact]
    public async Task A_list_inside_a_list_item_stays_inside_it()
    {
        var outer = Assert.IsType<ListNode>(
            Assert.Single((await Import("<ul><li>a<ul><li>b</li></ul></li></ul>")).Doc.Blocks));

        var item = Assert.Single(outer.Items);

        Assert.Equal("a", Assert.IsType<ParagraphNode>(item.Blocks[0]).Content.Text);

        var inner = Assert.IsType<ListNode>(item.Blocks[1]);

        Assert.Equal("b", Assert.IsType<ParagraphNode>(inner.Items[0].Blocks[0]).Content.Text);
    }

    [Fact]
    public async Task An_empty_list_does_not_become_an_invalid_one()
    {
        // The schema requires at least one item, so a list with nothing in it must not be built.
        var result = await Import("<ul></ul>");

        Assert.DoesNotContain(result.Doc.Blocks, b => b is ListNode);
    }

    // ---- tables -----------------------------------------------------------------------------

    [Fact]
    public async Task A_table_becomes_a_table()
    {
        var table = Assert.IsType<TableNode>(
            Assert.Single((await Import("<table><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>")).Doc.Blocks));

        Assert.Equal(2, table.Rows.Length);
        Assert.Equal(2, table.Rows[0].Cells.Length);
        Assert.Equal("a", Assert.IsType<ParagraphNode>(table.Rows[0].Cells[0].Blocks[0]).Content.Text);
        Assert.Equal("d", Assert.IsType<ParagraphNode>(table.Rows[1].Cells[1].Blocks[0]).Content.Text);
    }

    [Fact]
    public async Task A_header_cell_knows_it_is_one()
    {
        var table = Assert.IsType<TableNode>(
            Assert.Single((await Import("<table><thead><tr><th>h</th></tr></thead><tbody><tr><td>b</td></tr></tbody></table>")).Doc.Blocks));

        Assert.Equal(2, table.Rows.Length);
        Assert.True(table.Rows[0].Cells[0].IsHeader);
        Assert.False(table.Rows[1].Cells[0].IsHeader);
    }

    [Fact]
    public async Task A_spanned_cell_keeps_its_span()
    {
        var table = Assert.IsType<TableNode>(
            Assert.Single((await Import("<table><tr><td colspan='2'>a</td></tr><tr><td>b</td><td>c</td></tr></table>")).Doc.Blocks));

        Assert.Equal(2, table.Rows[0].Cells[0].ColumnSpan);
    }

    [Fact]
    public async Task A_table_whose_rows_do_not_line_up_still_imports_as_something()
    {
        // The schema requires a table's cells to tile a rectangle, so a ragged one cannot be
        // built. Losing the grid is acceptable; losing the words is not.
        var result = await Import("<table><tr><td>a</td><td>b</td></tr><tr><td>c</td></tr></table>");

        Assert.DoesNotContain(result.Doc.Blocks, b => b is TableNode);
        Assert.Equal(["a", "b", "c"], result.Doc.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text));
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Downgraded);
    }

    [Theory]
    [InlineData("<table><tr><td>a</td><td>b</td></tr><tr><td>c</td></tr></table>")]
    [InlineData("<table><tr><td rowspan='2'>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>")]
    [InlineData("<ul></ul>")]
    [InlineData("<ul><li></li></ul>")]
    [InlineData("<table><tr></tr></table>")]
    [InlineData("<table><tr><td></td></tr></table>")]
    [InlineData("<blockquote><ul><li><table><tr><td><p>x</p></td></tr></table></li></ul></blockquote>")]
    public async Task Whatever_comes_out_is_a_document_the_model_would_accept(string html)
    {
        // An import that produces a document the schema refuses is worse than one that produces
        // nothing: the paste would be rejected at the step, with no explanation anywhere.
        var result = await Import(html);

        Assert.True(
            DocumentSchema.IsValid(result.Doc),
            string.Join("; ", DocumentSchema.Validate(result.Doc)));
    }

    [Fact]
    public async Task A_table_inside_a_table_does_not_steal_the_outer_tables_rows()
    {
        var table = Assert.IsType<TableNode>(
            Assert.Single((await Import("<table><tr><td><table><tr><td>i</td></tr></table></td></tr></table>")).Doc.Blocks));

        Assert.Single(table.Rows);
        Assert.IsType<TableNode>(table.Rows[0].Cells[0].Blocks[0]);
    }

    // ---- images -----------------------------------------------------------------------------

    [Fact]
    public async Task An_image_becomes_an_embed_at_one_position()
    {
        var p = OnlyPara(await Import($"a<img src='{Png}' alt='cat' width='10' height='20'>b"));

        Assert.Equal(3, p.Content.Length);
        Assert.Equal('\uFFFC', p.Content.Text[1]);

        var image = Assert.IsType<ImageEmbed>(Assert.Single(p.Content.Embeds));

        Assert.Equal("cat", image.AltText);
        Assert.Equal(10, image.Width);
        Assert.Equal(20, image.Height);
    }

    // ---- robustness -------------------------------------------------------------------------

    [Fact]
    public async Task Nothing_at_all_gives_an_empty_result_rather_than_an_exception()
    {
        foreach (var html in new[] { "", "   ", "\n" })
        {
            var result = await HtmlImporter.ImportAsync(html);

            Assert.True(result.IsEmpty);
        }
    }

    [Fact]
    public async Task Unclosed_and_crossed_tags_do_not_throw()
    {
        var result = await Import("<p><b>a<i>b</p></b>c");

        Assert.Contains("a", PlainText(result));
        Assert.Contains("c", PlainText(result));
    }

    [Fact]
    public async Task Text_with_no_element_around_it_is_still_a_paragraph()
    {
        Assert.Equal("bare", OnlyPara(await Import("bare")).Content.Text);
    }

    [Fact]
    public async Task Entities_are_decoded_once_and_only_once()
    {
        Assert.Equal("a&b<c>", OnlyPara(await Import("a&amp;b&lt;c&gt;")).Content.Text);
        Assert.Equal("&lt;", OnlyPara(await Import("&amp;lt;")).Content.Text);
    }

    [Fact]
    public async Task A_non_breaking_space_is_kept_because_it_is_not_collapsible_whitespace()
    {
        Assert.Equal("a\u00a0b", OnlyPara(await Import("a&nbsp;b")).Content.Text);
    }

    [Fact]
    public async Task Text_that_needs_more_than_one_code_unit_survives_intact()
    {
        const string text = "\u05e9\u05dc\u05d5\u05dd a\u0301 \ud83d\udc69\u200d\ud83d\udc69\u200d\ud83d\udc67";

        Assert.Equal(text, OnlyPara(await Import($"<p>{text}</p>")).Content.Text);
    }

    // ---- where a collapsed space belongs ------------------------------------------------------

    [Fact]
    public async Task A_space_before_an_element_does_not_join_the_element()
    {
        // The space is outside <b>, so charging it to the bold run would make the run one
        // character too wide — and the formatting would then creep a character to the left on
        // every import, growing a little more each time a document was copied and pasted.
        var p = OnlyPara(await Import("<p>x <b>bold</b></p>"));

        Assert.Equal("x bold", p.Content.Text);
        Assert.Equal(TextStyle.None, StyleAt(p, 1));
        Assert.Equal(TextStyle.Bold, StyleAt(p, 2));
    }

    [Fact]
    public async Task A_space_inside_an_element_keeps_the_element_formatting()
    {
        var p = OnlyPara(await Import("<p><b>bold </b>x</p>"));

        Assert.Equal("bold x", p.Content.Text);
        Assert.Equal(TextStyle.Bold, StyleAt(p, 4));
        Assert.Equal(TextStyle.None, StyleAt(p, 5));
    }

    [Fact]
    public async Task A_space_before_a_link_is_not_part_of_the_link()
    {
        var p = OnlyPara(await Import("<p>see <a href=\"https://example.com/\">here</a></p>"));

        Assert.Equal("see here", p.Content.Text);
        Assert.Null(p.Content.MarkAt(3).Link);
        Assert.NotNull(p.Content.MarkAt(4).Link);
    }

    // ---- blank lines --------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_paragraph_is_a_blank_line_and_is_kept()
    {
        Assert.Equal(["a", string.Empty, "b"], PlainText(await Import("<p>a</p><p></p><p>b</p>")).Split('\n'));
    }

    [Fact]
    public async Task A_paragraph_of_nothing_but_whitespace_is_also_a_blank_line()
    {
        Assert.Equal(["a", string.Empty, "b"], PlainText(await Import("<p>a</p><p>   </p><p>b</p>")).Split('\n'));
    }

    [Fact]
    public async Task An_empty_div_is_layout_and_is_not_a_blank_line()
    {
        // Real pages are full of empty wrappers, spacers and clearfixes. Treating those the way
        // an empty <p> is treated would pad every paste with blank lines.
        Assert.Equal(["a", "b"], PlainText(await Import("<p>a</p><div></div><p>b</p>")).Split('\n'));
    }

    [Fact]
    public async Task A_wrapper_around_a_paragraph_does_not_add_a_blank_line_after_it()
    {
        Assert.Equal(["a"], PlainText(await Import("<div><p>a</p></div>")).Split('\n'));
    }

    [Fact]
    public async Task A_heading_holding_a_block_does_not_add_a_blank_line_after_it()
    {
        // The parser closes a <p> before block-level content but leaves a heading open, so a
        // heading really can end up wrapping the paragraph it produced. It is not empty just
        // because nothing was left pending at its end tag.
        Assert.Equal(["x"], PlainText(await Import("<h1><div>x</div></h1>")).Split('\n'));
    }

    private static string PlainText(ImportResult result) =>
        string.Join("\n", result.Doc.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text));
}
