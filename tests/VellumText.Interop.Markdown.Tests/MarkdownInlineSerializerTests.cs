using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// Fixed cases for the inline serializer, each one a shape the generated round trip found or the
/// audit of 2026-10-25 reported (F-07, F-22, F-25, F-27, F-34).
/// </summary>
/// <remarks>
/// <see cref="MarkdownRoundTripPropertyTests"/> finds shapes; these pin what is written for them,
/// so that a regression names the rule it broke rather than a seed.
/// </remarks>
public class MarkdownInlineSerializerTests
{
    private static InlineContent Styled(string text, params (int Start, int Length, MarkSet Mark)[] spans) =>
        InlineContent.Create(
            text,
            [.. spans.Select(s => new ValueSpan<MarkSet>(s.Start, s.Length, s.Mark))],
            []);

    private static MarkdownExportResult Export(InlineContent content) =>
        MarkdownExporter.Instance.ExportWithDiagnostics(new DocumentNode([new ParagraphNode(content)]));

    private static async Task<InlineContent> Back(string markdown) =>
        Assert.IsType<ParagraphNode>((await MarkdownImporter.ImportAsync(markdown)).Doc.Blocks[0]).Content;

    /// <summary>
    /// F-07: a styled run one escaped character or one surrogate pair long is one unit of two
    /// chars. Moving its edge punctuation out left nothing behind, and the next flanking check
    /// indexed an empty string.
    /// </summary>
    [Theory]
    [InlineData("~&1", 1, 1, TextStyle.Italic)]
    [InlineData("a[[", 1, 1, TextStyle.Strikethrough)]
    [InlineData("x&y", 1, 1, TextStyle.Italic)]
    [InlineData("!a", 0, 1, TextStyle.Bold)]
    [InlineData("R&D", 1, 1, TextStyle.Italic)]
    [InlineData("AT&T", 2, 1, TextStyle.Bold)]
    [InlineData("Wow!", 3, 1, TextStyle.Bold)]
    [InlineData("\U0001F44Dx", 0, 2, TextStyle.Bold)]
    [InlineData("\U0001F600a", 0, 2, TextStyle.Italic)]
    public async Task A_style_on_one_escaped_or_astral_character_exports(
        string text, int start, int length, TextStyle style)
    {
        var exported = Export(Styled(text, (start, length, new MarkSet(style))));
        var back = (await Back(exported.Text));

        Assert.Equal(text, back.Text);

        // Either the style came back on exactly that character, or the loss was reported.
        var kept = back.MarkAt(start).Has(style);

        Assert.True(
            kept || exported.Diagnostics.Any(d => d.Context == "emphasis-flanking"),
            $"md=[{exported.Text.TrimEnd()}]");
    }

    /// <summary>
    /// F-22: the closer of one run and the opener of the next touch and are one run to a reader.
    /// Here the merged <c>***</c> cannot open before "$", so the italic opener is written with
    /// the other character, which does not merge.
    /// </summary>
    [Fact]
    public async Task A_closer_and_the_next_opener_that_would_merge_use_different_characters()
    {
        var exported = Export(Styled(
            "Price$5",
            (0, 5, new MarkSet(TextStyle.Bold)),
            (5, 2, new MarkSet(TextStyle.Italic))));

        Assert.Equal("**Price**_$5_\n", exported.Text);
        Assert.Empty(exported.Diagnostics);

        var back = (await Back(exported.Text));

        Assert.Equal("Price$5", back.Text);
        Assert.Equal(TextStyle.Bold, back.MarkAt(4).Style);
        Assert.Equal(TextStyle.Italic, back.MarkAt(5).Style);
        Assert.Equal(TextStyle.Italic, back.MarkAt(6).Style);
    }

    /// <summary>
    /// The merge that does work is kept: between two letters the run of three closes the bold and
    /// opens the italic, and an underscore could not close before the "s".
    /// </summary>
    [Fact]
    public async Task A_merged_run_that_both_closes_and_opens_is_kept()
    {
        var exported = Export(Styled(
            "PriceTags",
            (0, 5, new MarkSet(TextStyle.Bold)),
            (5, 3, new MarkSet(TextStyle.Italic))));

        Assert.Equal("**Price***Tag*s\n", exported.Text);
        Assert.Empty(exported.Diagnostics);

        var back = (await Back(exported.Text));

        Assert.Equal(TextStyle.Bold, back.MarkAt(4).Style);
        Assert.Equal(TextStyle.Italic, back.MarkAt(5).Style);
        Assert.Equal(TextStyle.None, back.MarkAt(8).Style);
    }

    /// <summary>
    /// The rule of three: between two punctuation marks the merged run of four can both open and
    /// close, and two plus four is a multiple of three, so <c>**\[****~~#~~**</c> matches nothing
    /// and four literal asterisks arrive in the text. The second run is written with underscores.
    /// </summary>
    [Fact]
    public async Task A_merge_the_rule_of_three_forbids_is_not_written()
    {
        var exported = Export(Styled(
            "[#",
            (0, 1, new MarkSet(TextStyle.Bold)),
            (1, 1, new MarkSet(TextStyle.Bold | TextStyle.Strikethrough))));

        Assert.Equal("**\\[**__~~#~~__\n", exported.Text);
        Assert.Empty(exported.Diagnostics);

        var back = (await Back(exported.Text));

        Assert.Equal("[#", back.Text);
        Assert.Equal(TextStyle.Bold, back.MarkAt(0).Style);
        Assert.Equal(TextStyle.Bold | TextStyle.Strikethrough, back.MarkAt(1).Style);
    }

    /// <summary>
    /// Bold beside bold italic: the run of five between two letters closes the bold with two and
    /// opens the bold italic with the three left, so it is kept.
    /// </summary>
    [Fact]
    public async Task A_merged_run_of_five_closes_bold_and_opens_bold_italic()
    {
        var exported = Export(Styled(
            "abc",
            (0, 1, new MarkSet(TextStyle.Bold)),
            (1, 1, new MarkSet(TextStyle.Bold | TextStyle.Italic))));

        Assert.Equal("**a*****b***c\n", exported.Text);
        Assert.Empty(exported.Diagnostics);

        var back = (await Back(exported.Text));

        Assert.Equal("abc", back.Text);
        Assert.Equal(TextStyle.Bold, back.MarkAt(0).Style);
        Assert.Equal(TextStyle.Bold | TextStyle.Italic, back.MarkAt(1).Style);
        Assert.Equal(TextStyle.None, back.MarkAt(2).Style);
    }

    /// <summary>
    /// A change Markdown cannot write — the colour here — no longer closes and reopens the bold
    /// around it, which wrote <c>**Hello****World**</c>: one run of four that no reader takes for
    /// a boundary.
    /// </summary>
    [Fact]
    public async Task A_mark_markdown_cannot_write_does_not_split_the_emphasis()
    {
        var bold = new MarkSet(TextStyle.Bold);
        var exported = Export(Styled(
            "HelloWorld",
            (0, 5, bold),
            (5, 5, bold with { Foreground = Rgba.FromRgb(255, 0, 0) })));

        Assert.Equal("**HelloWorld**\n", exported.Text);
        Assert.Contains(exported.Diagnostics, d => d.Context == "colour");
        Assert.Equal(TextStyle.Bold, (await Back(exported.Text)).MarkAt(7).Style);
    }

    /// <summary>
    /// F-25: a hard break has no notation at the end of a paragraph — the backslash comes back as
    /// a literal backslash — so it is dropped and reported.
    /// </summary>
    [Fact]
    public async Task A_paragraph_final_line_break_is_dropped_with_a_diagnostic()
    {
        var exported = Export(InlineContent.FromText("line one\n"));

        Assert.Equal("line one\n", exported.Text);

        var diagnostic = Assert.Single(exported.Diagnostics);

        Assert.Equal(DiagnosticSeverity.Downgraded, diagnostic.Severity);
        Assert.Equal("trailing-break", diagnostic.Context);
        Assert.Equal("line one", (await Back(exported.Text)).Text);
    }

    /// <summary>
    /// The same rule at the end of a link's text, which is the end of the link's inline container:
    /// the break is written after the link, where it separates the same two lines.
    /// </summary>
    [Fact]
    public async Task A_line_break_at_the_end_of_a_link_is_written_after_it()
    {
        var link = new MarkSet(TextStyle.None) with { Link = new LinkMark("https://e.test/") };
        var exported = Export(Styled("x\ny", (0, 2, link)));

        Assert.Equal("[x](https://e.test/)\\\ny\n", exported.Text);
        Assert.Empty(exported.Diagnostics);

        var back = (await Back(exported.Text));

        Assert.Equal("x\ny", back.Text);
        Assert.Equal("https://e.test/", back.MarkAt(0).Link?.Href);
    }

    /// <summary>
    /// F-27: a reader will not open a code span straight after a backtick, even an escaped one, so
    /// <c>\``a*`</c> came back as the text <c>``a*`</c>. The literal is written as a character
    /// reference instead.
    /// </summary>
    [Fact]
    public async Task A_code_span_after_a_literal_backtick_stays_a_code_span()
    {
        var exported = Export(Styled("`a*", (1, 2, new MarkSet(TextStyle.Code))));

        Assert.Equal("&#96;`a*`\n", exported.Text);
        Assert.Empty(exported.Diagnostics);

        var back = (await Back(exported.Text));

        Assert.Equal("`a*", back.Text);
        Assert.False(back.MarkAt(0).Has(TextStyle.Code));
        Assert.True(back.MarkAt(1).Has(TextStyle.Code));
        Assert.True(back.MarkAt(2).Has(TextStyle.Code));
    }

    /// <summary>
    /// Two code spans with nothing written between them are one run of backticks. That happens
    /// only when a style that would have separated them could not be written; the second is then
    /// written as text, and both losses are reported.
    /// </summary>
    [Fact]
    public async Task Two_code_spans_that_would_touch_are_not_written_touching()
    {
        var exported = Export(Styled(
            "abx",
            (0, 1, new MarkSet(TextStyle.Code)),
            (1, 1, new MarkSet(TextStyle.Code | TextStyle.Bold))));

        Assert.Equal("`a`bx\n", exported.Text);
        Assert.Contains(exported.Diagnostics, d => d.Context == "code-span-adjacent");
        Assert.Equal("abx", (await Back(exported.Text)).Text);
    }

    /// <summary>
    /// F-34: whitespace at a styled run's edges was moved out a unit at a time, each move a copy
    /// of the rest — 40,000 trailing spaces took 2.3 s and 3.2 GB. Measured in bytes allocated
    /// rather than time, which a shared machine cannot disturb.
    /// </summary>
    [Fact]
    public void Edge_whitespace_is_moved_out_in_one_pass()
    {
        var text = "a" + new string(' ', 40_000) + "b";
        var content = Styled(text, (0, 40_001, new MarkSet(TextStyle.Bold)));

        Export(content);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var exported = Export(content);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.StartsWith("**a**", exported.Text, StringComparison.Ordinal);
        Assert.True(allocated < 20_000_000, $"allocated {allocated:N0} bytes");
    }
}
