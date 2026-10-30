using VellumText.Interop.Html;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// RTF as it actually arrives: written by Word and Outlook, sometimes truncated, sometimes not
/// RTF at all.
/// </summary>
public class RtfImportTests
{
    /// <summary>
    /// A fragment shaped the way the Windows clipboard shapes one: font and colour tables, a
    /// centred heading, character formatting, an indent, a bullet list and a hyperlink field.
    /// </summary>
    private const string Wordish =
        @"{\rtf1\ansi\ansicpg1252\deff0" +
        @"{\fonttbl{\f0\fswiss Calibri;}{\f1\froman Georgia;}}" +
        @"{\colortbl;\red255\green0\blue0;\red0\green0\blue255;}" +
        @"\pard\qc\b\fs32 Heading\b0\fs22\par " +
        @"\pard\ql Plain, \b bold\b0 , \i italic\i0 , \ul underlined\ulnone , \strike struck\strike0 .\par " +
        @"\pard\li720 Indented paragraph.\par " +
        @"\pard\cf1 Red\cf0  and \f1 Georgia\f0 .\par " +
        @"{\pntext\f0\'B7\tab}{\*\pn\pnlvlblt\pnf0\pnindent0{\pntxtb\'B7}}\fi-360\li720 First bullet\par " +
        @"{\pntext\f0\'B7\tab}{\*\pn\pnlvlblt\pnf0\pnindent0{\pntxtb\'B7}}\fi-360\li720 Second bullet\par " +
        @"\pard{\field{\*\fldinst{HYPERLINK ""https://example.com/""}}{\fldrslt{\ul\cf2 a link}}}\par " +
        @"\pard Caf\'e9, \u1513?\u1500?\u1493?\u1501? and \u55357?\u56832?.\par " +
        @"}";

    private static async Task<ImportResult> Import(string rtf) => await RtfImporter.ImportAsync(rtf);

    private static string PlainText(ImportResult result) =>
        string.Join("\n", Paragraphs(result).Select(p => p.Content.Text));

    private static IEnumerable<ParagraphNode> Paragraphs(ImportResult result) =>
        Flatten(result.Doc.Blocks).OfType<ParagraphNode>();

    private static IEnumerable<BlockNode> Flatten(IEnumerable<BlockNode> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;

            if (block is ListNode list)
            {
                foreach (var inner in list.Items.SelectMany(i => Flatten(i.Blocks)))
                {
                    yield return inner;
                }
            }
        }
    }

    private static bool AnyRunHas(ImportResult result, Func<MarkSet, bool> predicate) =>
        Paragraphs(result).Any(p => Enumerable.Range(0, p.Content.Length).Any(i => predicate(p.Content.MarkAt(i))));

    // ---- recognising it ---------------------------------------------------------------------

    [Theory]
    [InlineData(@"{\rtf1\ansi hello}", true)]
    [InlineData("   {\\rtf1 hello}", true)]
    [InlineData("<p>hello</p>", false)]
    [InlineData("plain text", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Rtf_is_told_apart_from_everything_else(string? text, bool expected)
    {
        Assert.Equal(expected, RtfImporter.IsRtf(text));
    }

    // ---- what a real document carries -------------------------------------------------------

    /// <summary>What RTF whitespace does and does not survive, pinned deliberately.</summary>
    /// <remarks>
    /// The character count survives and the characters themselves do not: a run of spaces comes
    /// back as one space followed by non-breaking ones, so four spaces are a space and three
    /// U+00A0. (RtfPipe wrote it that way; the reader that replaced it writes plain spaces, and the
    /// HTML stage keeps the run for RTF.) That matters for anything that searches, wraps or
    /// re-exports the text - a non-breaking space is not a space to any of them.
    ///
    /// Pinned with the codepoints spelled out because the two are indistinguishable on screen. I
    /// first read this output as four ordinary spaces and reported the review finding as wrong;
    /// it was closer to right than I was.
    ///
    /// The tab is dropped, and that is a decision rather than an oversight: see the remarks on
    /// RtfImporter. Asserted here so that "fixing" it is a deliberate act with a failing test in
    /// front of it.
    /// </remarks>
    [Fact]
    public async Task Interior_spaces_survive_and_a_tab_does_not()
    {
        var spaced = (await RtfImporter.ImportAsync(@"{\rtf1\ansi a    b}")).Doc;

        Assert.Equal("a \u00A0\u00A0\u00A0b", ((ParagraphNode)spaced.Blocks[0]).Content.Text);

        var tabbed = (await RtfImporter.ImportAsync(@"{\rtf1\ansi tab\tab here}")).Doc;

        Assert.Equal("tabhere", ((ParagraphNode)tabbed.Blocks[0]).Content.Text);
    }
    [Fact]
    public async Task The_words_survive()
    {
        var text = PlainText(await Import(Wordish));

        Assert.Contains("Heading", text, StringComparison.Ordinal);
        Assert.Contains("Plain,", text, StringComparison.Ordinal);
        Assert.Contains("First bullet", text, StringComparison.Ordinal);
        Assert.Contains("a link", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TextStyle.Bold)]
    [InlineData(TextStyle.Italic)]
    [InlineData(TextStyle.Underline)]
    [InlineData(TextStyle.Strikethrough)]
    public async Task Character_formatting_survives(TextStyle style)
    {
        Assert.True(AnyRunHas(await Import(Wordish), m => m.Style.HasFlag(style)));
    }

    [Fact]
    public async Task Colour_survives()
    {
        Assert.True(AnyRunHas(await Import(Wordish), m => m.Foreground == new Rgba(255, 0, 0, 255)));
    }

    /// <summary>
    /// The red in <see cref="Wordish"/> is saturated, and a saturated colour is the one
    /// family that survives a channel being clamped to 255. Pasted greys once imported as
    /// white with every other colour test passing, so the interesting cases are here.
    /// </summary>
    [Theory]
    [InlineData(128, 128, 128)]
    [InlineData(255, 136, 0)]
    [InlineData(18, 52, 86)]
    [InlineData(220, 20, 60)]
    public async Task A_colour_that_is_not_saturated_arrives_intact(byte r, byte g, byte b)
    {
        var rtf =
            @"{\rtf1\ansi{\fonttbl{\f0 Calibri;}}{\colortbl ;\red" + r + @"\green" + g + @"\blue" + b + @";}" +
            @"\pard\cf1 shade\par}";

        Assert.True(AnyRunHas(await Import(rtf), m => m.Foreground == new Rgba(r, g, b, 255)));
    }

    [Fact]
    public async Task The_font_a_run_asked_for_survives()
    {
        Assert.True(AnyRunHas(await Import(Wordish), m => m.FontFamily is not null && m.FontFamily.Contains("Georgia", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_centred_paragraph_is_still_centred()
    {
        var result = await Import(Wordish);

        Assert.Contains(Paragraphs(result), p => p.Align == TextAlign.Center);
    }

    [Fact]
    public async Task An_indent_becomes_an_indent()
    {
        Assert.Contains(Paragraphs(await Import(Wordish)), p => p.IndentLevel > 0);
    }

    [Fact]
    public async Task A_bullet_list_becomes_a_list_and_not_a_paragraph_starting_with_a_dot()
    {
        var result = await Import(Wordish);

        var list = Assert.Single(result.Doc.Blocks.OfType<ListNode>());

        Assert.Equal(ListKind.Unordered, list.Kind);
        Assert.Equal(2, list.Items.Length);

        // The bullet character itself must not survive as text; it is the list that carries it.
        Assert.DoesNotContain('\u00b7', PlainText(result));
    }

    [Fact]
    public async Task A_hyperlink_field_becomes_a_link()
    {
        var result = await Import(Wordish);

        Assert.True(AnyRunHas(result, m => m.Link is not null && m.Link.Href == "https://example.com/"));
    }

    [Fact]
    public async Task Text_that_is_not_ascii_survives_however_it_was_encoded()
    {
        var text = PlainText(await Import(Wordish));

        // \'e9 is a code-page byte, \uN is a Unicode escape, and an astral character arrives as a
        // surrogate pair written as two of them.
        Assert.Contains("Caf\u00e9", text, StringComparison.Ordinal);
        Assert.Contains("\u05e9\u05dc\u05d5\u05dd", text, StringComparison.Ordinal);
        Assert.Contains("\ud83d\ude00", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_comes_out_is_a_document_the_model_would_accept()
    {
        Assert.True(
            DocumentSchema.IsValid((await Import(Wordish)).Doc),
            string.Join("; ", DocumentSchema.Validate((await Import(Wordish)).Doc)));
    }

    // ---- what arrives broken ----------------------------------------------------------------

    [Theory]
    [InlineData("<p>not rtf</p>")]
    [InlineData("plain text")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Something_that_is_not_rtf_is_reported_and_not_thrown_over(string input)
    {
        // A clipboard manager offers whatever it has to whichever reader might take it, so being
        // handed the wrong format is routine rather than exceptional.
        var result = await RtfImporter.ImportAsync(input);

        Assert.True(result.IsEmpty);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Malformed);
    }

    [Theory]
    [InlineData(@"{\rtf1\ansi truncated")]
    [InlineData(@"{\rtf1\ansi {\b unbalanced }")]
    [InlineData(@"{\rtf1\ansi \'e")]
    [InlineData(@"{\rtf1\ansi \u")]
    [InlineData(@"{\rtf1")]
    [InlineData(@"{\rtf1\ansi \")]
    [InlineData(@"{\rtf1\ansi {\fonttbl}{\colortbl}\par}")]
    public async Task Damaged_rtf_produces_a_result_rather_than_an_exception(string input)
    {
        var result = await RtfImporter.ImportAsync(input);

        Assert.True(DocumentSchema.IsValid(result.Doc));
    }

    [Fact]
    public async Task Nothing_at_all_is_the_callers_mistake_and_is_reported_as_one()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => RtfImporter.ImportAsync(null!));
    }

    // ---- what must not come through ---------------------------------------------------------

    [Fact]
    public async Task A_picture_does_not_arrive_as_a_format_nothing_can_decode()
    {
        // A Windows metafile is not an image type anything here can decode, whatever data: URL the
        // reader renders it as (RtfPipe said "windows/metafile"), and the importer must not take it
        // on faith.
        var result = await RtfImporter.ImportAsync(
            @"{\rtf1\ansi before{\pict\wmetafile8\picw100\pich100 0102030405}after}");

        // The surrounding words prove the document was read at all, so that the absence of an
        // embed below is a decision rather than an empty result.
        Assert.Contains("before", PlainText(result), StringComparison.Ordinal);
        Assert.Contains("after", PlainText(result), StringComparison.Ordinal);
        Assert.Empty(Paragraphs(result).SelectMany(p => p.Content.Embeds));
    }

    [Fact]
    public async Task A_picture_the_reader_cannot_convert_is_reported_once()
    {
        // Rubber-duck review finding 9.6: the converter's own reasons for dropping a picture were
        // thrown away, and a metafile vanished without a word.
        var result = await RtfImporter.ImportAsync(
            @"{\rtf1\ansi before{\pict\wmetafile8\picw100\pich100 0102030405}mid{\pict\wmetafile8\picw100\pich100 0607}after}");

        Assert.Equal("beforemidafter", PlainText(result));
        Assert.True(result.AnythingDropped);
        Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Dropped);
    }

    [Fact]
    public async Task Outlooks_encapsulated_html_keeps_its_bold()
    {
        // Rubber-duck review finding 9.4: hoisting the fallback's font invalidated the HTML, and
        // the import was the fallback, unbolded.
        var result = await Import(RtfFormattingHoistTests.OutlookBold);

        Assert.Equal("hello", PlainText(result));
        Assert.True(AnyRunHas(result, m => m.Style.HasFlag(TextStyle.Bold)));
    }

    [Fact]
    public async Task A_large_outlook_message_is_imported_whole()
    {
        // Rubber-duck review finding 9.3: the converter refuses encapsulated HTML of more than
        // 10,000 nodes, and the import of a 40 KB message was empty. No paste is capped by its size.
        const int Count = 5_001;
        var html = string.Concat(Enumerable.Range(0, Count).Select(i => $"<p>p{i}</p>"));
        var result = await Import(
            @"{\rtf1\ansi\fromhtml1{\*\htmltag1 <html><body>" + html + @"</body></html>}\htmlrtf1 fallback\htmlrtf0\par}");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Malformed);
        Assert.Equal(Count, Paragraphs(result).Count());
        Assert.Equal($"p{Count - 1}", Paragraphs(result).Last().Content.Text);
    }

    [Fact]
    public async Task Outlooks_encapsulated_html_is_read_as_html()
    {
        // It reaches the HTML stage as it is, and white space in HTML collapses; the reader's own
        // HTML is the one that keeps every space.
        var result = await Import(
            @"{\rtf1\ansi\fromhtml1{\*\htmltag1 <html><body><p>a    b</p></body></html>}\htmlrtf1 a    b\htmlrtf0\par}");

        Assert.Equal("a b", PlainText(result));
    }

    [Fact]
    public async Task A_link_to_somewhere_dangerous_does_not_survive_the_trip_through_html()
    {
        // Going through the HTML importer is what makes this true; a second RTF-specific idea of
        // which URLs are safe would be a second one to get wrong.
        var result = await RtfImporter.ImportAsync(
            @"{\rtf1\ansi{\field{\*\fldinst{HYPERLINK ""javascript:alert(1)""}}{\fldrslt{click}}}}");

        Assert.Contains("click", PlainText(result), StringComparison.Ordinal);
        Assert.False(AnyRunHas(result, m => m.Link is not null));
    }

    [Fact]
    public async Task Coloured_text_keeps_its_colour()
    {
        var result = await Import(
            @"{\rtf1\ansi{\colortbl;\red255\green0\blue0;}\pard\cf1 red\cf0  plain\par}");

        Assert.True(
            AnyRunHas(result, m => m.Foreground == new Rgba(255, 0, 0, 255)),
            "The red run came back with no colour on it.");
    }

    [Fact]
    public async Task Highlighted_text_keeps_its_highlight()
    {
        // A reader may write the highlight as the `background` shorthand rather than
        // `background-color` (RtfPipe did), and the CSS reader expands it. Worth pinning: reading
        // one form only would silently drop every highlight, and a clipboard manager would never
        // show one.
        var result = await Import(
            @"{\rtf1\ansi{\colortbl;\red255\green255\blue0;}\pard\highlight1 lit\highlight0  plain\par}");

        Assert.True(
            AnyRunHas(result, m => m.Highlight == new Rgba(255, 255, 0, 255)),
            "The highlighted run came back with no highlight on it.");
    }

    [Fact]
    public async Task The_hosts_options_reach_the_html_importer()
    {
        var rtf =
            @"{\rtf1\ansi{\field{\*\fldinst{HYPERLINK ""/relative""}}{\fldrslt{click}}}}";

        var withBase = await RtfImporter.ImportAsync(
            rtf,
            new RtfImportOptions { BaseUri = new Uri("https://example.com/dir/page") });

        Assert.True(AnyRunHas(withBase, m => m.Link?.Href == "https://example.com/relative"));
        Assert.False(AnyRunHas(await RtfImporter.ImportAsync(rtf), m => m.Link is not null));
    }

    [Theory]
    [InlineData(-10240, "\uFFFD")]
    [InlineData(-9216, "\uFFFD")]
    public async Task An_unpaired_surrogate_becomes_a_replacement_character_and_the_rest_survives(
        int unit, string expected)
    {
        // \u takes a signed 16-bit number, so a lone surrogate is two keystrokes of RTF. The model
        // refuses one, and that refusal used to discard the whole paste.
        var result = await Import($@"{{\rtf1\ansi a\u{unit}?b\par keep\par}}");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Malformed);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == "unpaired-surrogate");
        Assert.Equal($"a{expected}b\nkeep", PlainText(result));
    }

    /// <remarks>
    /// The reader (OfficeIMO, since 2026-10-27) joins the halves into the character, with the
    /// formatting of the second half, and then adds a stray U+FFFD where the first half stood. That
    /// is pinned as a reader quirk, so that a version which stops doing it shows up here; it is not
    /// reported, because nothing was lost. RtfPipe passed the halves through and the HTML stage
    /// reported a split character.
    /// </remarks>
    [Fact]
    public async Task A_surrogate_pair_split_by_formatting_keeps_the_character_and_the_rest()
    {
        // U+1F600 written as two \u escapes with a bold group between its halves.
        var result = await Import(@"{\rtf1\ansi x\u-10179?{\b\u-8704?}y\par keep\par}");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Malformed);
        Assert.DoesNotContain(result.Diagnostics, d => d.Context == "unpaired-surrogate");
        Assert.Equal("x\uD83D\uDE00\uFFFDy\nkeep", PlainText(result));

        var first = Paragraphs(result).First();
        Assert.Equal(TextStyle.Bold, first.Content.MarkAt(1).Style & TextStyle.Bold);
        Assert.Equal(TextStyle.None, first.Content.MarkAt(4).Style & TextStyle.Bold);
    }
}
