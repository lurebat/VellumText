using System.Text;
using VellumText.ImportProbe;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// What the RTF reader that replaced RtfPipe (phase 8, OD-8) reads, loses and reports.
/// </summary>
/// <remarks>
/// <para>
/// The reader is OfficeIMO, chosen through a selection gate and pinned to one exact version. These
/// tests pin the conditions the owner set on adopting it: losses it cannot avoid are reported
/// rather than silent, the hostile inputs RtfPipe stalled on stay bounded, and none of the reader's
/// own whole-document budgets refuses a paste for its size (the library caps no paste by size;
/// architecture §7).
/// </para>
/// <para>
/// The Word and Excel inputs are real clipboard captures from the audit's end-to-end pass, byte
/// for byte (see <c>Fixtures\*\manifest.json</c>).
/// </para>
/// </remarks>
public class RtfReaderTests
{
    /// <summary>A 1×1 PNG, as the hex a <c>\pict</c> carries.</summary>
    private static readonly string Png = Convert.ToHexString(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));

    private static string Fixture(string capture, string file) =>
        Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", capture, file)))
            .TrimEnd('\0');

    private static string PlainText(ImportResult result) => DocumentText.Of(result.Doc);

    private static IEnumerable<Node> All(Node node)
    {
        var pending = new Stack<Node>();

        pending.Push(node);

        while (pending.TryPop(out var current))
        {
            yield return current;

            foreach (var child in current.Children)
            {
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<MarkSet> Marks(ImportResult result) =>
        All(result.Doc).OfType<ParagraphNode>()
            .SelectMany(p => Enumerable.Range(0, p.Content.Length).Select(p.Content.MarkAt));

    [Fact]
    public async Task Explicit_black_arrives_as_black()
    {
        // RtfPipe wrote no colour for black, so text coloured explicitly black followed the theme
        // (the known limitation this reader removes).
        var result = await RtfImporter.ImportAsync(
            @"{\rtf1\ansi{\colortbl;\red0\green0\blue0;}\pard\cf1 black text\par}");

        Assert.Contains("black text", PlainText(result), StringComparison.Ordinal);
        Assert.Contains(Marks(result), m => m.Foreground == new Rgba(0, 0, 0, 255));
    }

    /// <summary>
    /// F-30: 32 bytes of <c>\uc2147483647</c> stalled RtfPipe for seconds and silently dropped
    /// the rest. The reader honours the count as the specification says — the fallback swallows the
    /// rest of the group — and the import says so.
    /// </summary>
    [Fact]
    public async Task A_huge_unicode_skip_count_skips_to_the_end_of_its_group_and_says_so()
    {
        var result = await RtfImporter.ImportAsync(@"{\rtf1\ansi {\uc2147483647\u65 skipped}after\par}");

        Assert.StartsWith("A", PlainText(result), StringComparison.Ordinal);
        Assert.DoesNotContain("skipped", PlainText(result), StringComparison.Ordinal);
        Assert.Contains("after", PlainText(result), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == "uc");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task An_ordinary_unicode_skip_count_is_not_reported(int count)
    {
        // The empty group ends the control word, so that a count of 0 does not eat the space as
        // the escape's delimiter.
        var result = await RtfImporter.ImportAsync($@"{{\rtf1\ansi\uc{count} caf\u233{new string('?', count)}{{}} au lait\par}}");

        Assert.Equal("café au lait", PlainText(result).Trim());
        Assert.DoesNotContain(result.Diagnostics, d => d.Context == "uc");
    }

    /// <summary>
    /// Word stores a picture as a shape (<c>\shppict</c>) with a metafile fallback, and the reader
    /// binds neither. The loss is reported once, as a dropped picture, and the rest of the document
    /// arrives.
    /// </summary>
    [Fact]
    public async Task A_picture_word_stored_as_a_shape_is_reported_as_dropped()
    {
        var result = await RtfImporter.ImportAsync(Fixture("word-docx-all", "02-Rich_Text_Format.bin"));
        var text = PlainText(result);

        Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Dropped && d.Context == "shppict");
        Assert.Contains("Word audit heading", text, StringComparison.Ordinal);
        Assert.Contains("Bullet one", text, StringComparison.Ordinal);
        Assert.Contains(All(result.Doc), n => n is TableNode);
        Assert.Contains(Marks(result), m => m.Link?.Href == "https://example.com/word");
    }

    [Fact]
    public async Task A_standalone_picture_is_kept()
    {
        var result = await RtfImporter.ImportAsync(
            @"{\rtf1\ansi before{\pict\pngblip\picw1\pich1 " + Png + @"}after\par}");

        Assert.Single(All(result.Doc).OfType<ParagraphNode>().SelectMany(p => p.Content.Embeds).OfType<ImageEmbed>());
        Assert.DoesNotContain(result.Diagnostics, d => d.Context == "shppict");
    }

    /// <summary>F-58's merged cells: Word's vertical merge and Excel's horizontal one.</summary>
    /// <summary>
    /// A list just before a table stays outside it. Word's clipboard RTF has bullets and then a
    /// table, and the table's header row came out with each cell wrapped in a bulleted list:
    /// the list state leaked into the row that followed it (found live, phase 8).
    /// </summary>
    [Fact]
    public async Task A_list_before_a_table_does_not_leak_into_its_cells()
    {
        var result = await RtfImporter.ImportAsync(Fixture("word-docx-all", "02-Rich_Text_Format.bin"));

        var table = Assert.Single(All(result.Doc).OfType<TableNode>());

        Assert.DoesNotContain(All(table), n => n is ListNode);
        Assert.Contains(All(result.Doc), n => n is ListNode list
            && DocumentText.Of(new DocumentNode([list])).Contains("Bullet one", StringComparison.Ordinal));
    }

    /// <summary>
    /// Only the stray shadow goes: in the same document, a cell paragraph that states its own
    /// <c>\ls</c> is still a list item, and so is the list paragraph whose shadow leaked.
    /// </summary>
    [Fact]
    public async Task A_list_a_cell_states_itself_survives_the_leak_correction()
    {
        const string lists =
            @"{\*\listtable{\list\listtemplateid1{\listlevel\levelnfc23{\leveltext\'01\'B7;}{\levelnumbers;}}\listid7}}" +
            @"{\*\listoverridetable{\listoverride\listid7\listoverridecount0\ls1}}";
        const string bullet = @"{\*\pn\pnlvlblt\ls1{\pntxtb\'b7}}\ls1 ";

        var result = await RtfImporter.ImportAsync(
            @"{\rtf1\ansi" + lists + @"\pard" + bullet + @"{item\par }" +
            @"\trowd\cellx2000\cellx4000\pard\plain\intbl leaked\cell \pard\intbl" + bullet + @"listed\cell\row " +
            @"\pard\plain after\par}");

        var table = Assert.Single(All(result.Doc).OfType<TableNode>());
        var cells = table.Rows[0].Cells;

        Assert.IsNotType<ListNode>(cells[0].Blocks[0]);
        Assert.IsType<ListNode>(cells[1].Blocks[0]);
        Assert.Contains(result.Doc.Blocks, b => b is ListNode);
    }

    /// <summary>
    /// A paragraph numbered by its own <c>\pn</c> is a list, even in a document with a list table:
    /// only a shadow carried over from a <c>\ls</c> paragraph is dropped (rubber-duck review
    /// finding 9.5).
    /// </summary>
    [Theory]
    [InlineData(@"\pard{\*\pn\pnlvlblt\pnf1\pnindent360{\pntxtb\'B7}}\fi-360\li720 legacy\par\pard\ls1\fi-360\li720 modern\par")]
    [InlineData(@"\pard{\*\pn\pnlvlblt\ls1{\pntxtb\'B7}}\ls1\fi-360\li720 modern\par\pard{\*\pn\pnlvlbody\pndec\pnstart1{\pntxta.}}\fi-360\li720 legacy\par")]
    public async Task A_legacy_list_a_paragraph_states_itself_survives_the_leak_correction(string body)
    {
        const string lists =
            @"{\*\listtable{\list\listtemplateid1{\listlevel\levelnfc23{\leveltext\'01\'B7;}{\levelnumbers;}}\listid7}}" +
            @"{\*\listoverridetable{\listoverride\listid7\listoverridecount0\ls1}}";

        var result = await RtfImporter.ImportAsync(@"{\rtf1\ansi" + lists + body + "}");

        Assert.All(result.Doc.Blocks, b => Assert.IsType<ListNode>(b));
        Assert.Contains("legacy", PlainText(result), StringComparison.Ordinal);
        Assert.Contains("modern", PlainText(result), StringComparison.Ordinal);
    }
    /// <summary>
    /// The reader's own bookkeeping is not a loss. OfficeIMO writes private <c>--officeimo-rtf-*</c>
    /// properties for its round trip, and each surfaced as "Removed a style the editor cannot
    /// represent" — noise a user can do nothing about (found live, phase 8). Styles that really
    /// are lost are still reported.
    /// </summary>
    [Fact]
    public async Task The_readers_private_styles_are_removed_without_a_diagnostic()
    {
        var result = await RtfImporter.ImportAsync(Fixture("word-docx-all", "02-Rich_Text_Format.bin"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Context?.StartsWith("--officeimo", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == "line-height");
    }
    [Fact]
    public async Task Merged_cells_arrive_merged()
    {
        var word = await RtfImporter.ImportAsync(Fixture("word-docx-all", "02-Rich_Text_Format.bin"));
        var excel = await RtfImporter.ImportAsync(Fixture("excel-range-A1D6", "17-Rich_Text_Format.bin"));

        Assert.Contains(All(word.Doc), n => n is TableCellNode { RowSpan: > 1 });
        Assert.Contains(All(excel.Doc), n => n is TableCellNode { ColumnSpan: 2 });
        Assert.Contains("Merged A6:B6", PlainText(excel), StringComparison.Ordinal);
    }

    /// <summary>100,000 <c>\cellx</c> in one row kept RtfPipe busy past 30 s.</summary>
    [Fact]
    public void A_row_with_a_hundred_thousand_cell_edges_does_not_hang()
    {
        var rtf = @"{\rtf1\trowd" + string.Concat(Enumerable.Range(1, 100_000).Select(i => $@"\cellx{i}"))
            + @"\pard\intbl a\cell\row after}";

        var result = ProbeRunner.Run("rtf", rtf, TimeSpan.FromSeconds(60));

        Assert.True(result.ExitCode == 0, result.ToString());
    }

    /// <summary>
    /// The reader's group-depth limit is the one budget kept, and what it cuts off is reported.
    /// </summary>
    [Fact]
    public async Task Content_nested_below_the_depth_limit_is_dropped_and_reported()
    {
        var result = await RtfImporter.ImportAsync(
            @"{\rtf1\ansi " + new string('{', 1_000) + "deep" + new string('}', 1_000) + @" after\par}");

        Assert.Contains("after", PlainText(result), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Downgraded);
    }

    /// <summary>
    /// The reader's default budget refuses a document over 100,000 blocks outright. The library
    /// caps no paste by its size, so the budget is lifted and every paragraph arrives.
    /// </summary>
    [Fact]
    public async Task A_document_past_the_readers_own_block_budget_still_arrives_whole()
    {
        var rtf = @"{\rtf1\ansi " + string.Concat(Enumerable.Range(0, 120_000).Select(i => $@"p{i}\par ")) + "}";

        var result = await RtfImporter.ImportAsync(rtf);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Malformed);
        Assert.Contains("p119999", PlainText(result), StringComparison.Ordinal);
    }

    /// <summary>The reader's default budget stops at 256 images; the paste does not.</summary>
    [Fact]
    public async Task A_document_past_the_readers_own_image_budget_keeps_every_image()
    {
        var picture = @"{\pict\pngblip\picw1\pich1 " + Png + "}";
        var rtf = @"{\rtf1\ansi " + string.Concat(Enumerable.Repeat(picture + @" x\par ", 300)) + "}";

        var result = await RtfImporter.ImportAsync(rtf);

        Assert.Equal(300, All(result.Doc).OfType<ParagraphNode>().SelectMany(p => p.Content.Embeds).OfType<ImageEmbed>().Count());
    }
}
