using Vellum.Interop.Html;
using Vellum.Interop.Rtf;

namespace Vellum.Tests.Interop.Rtf;

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

    private static ImportResult Import(string rtf) => RtfImporter.Import(rtf);

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

    [Fact]
    public void The_words_survive()
    {
        var text = PlainText(Import(Wordish));

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
    public void Character_formatting_survives(TextStyle style)
    {
        Assert.True(AnyRunHas(Import(Wordish), m => m.Style.HasFlag(style)));
    }

    [Fact]
    public void Colour_survives()
    {
        Assert.True(AnyRunHas(Import(Wordish), m => m.Foreground == new Rgba(255, 0, 0, 255)));
    }

    [Fact]
    public void The_font_a_run_asked_for_survives()
    {
        Assert.True(AnyRunHas(Import(Wordish), m => m.FontFamily is not null && m.FontFamily.Contains("Georgia", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_centred_paragraph_is_still_centred()
    {
        var result = Import(Wordish);

        Assert.Contains(Paragraphs(result), p => p.Align == TextAlign.Center);
    }

    [Fact]
    public void An_indent_becomes_an_indent()
    {
        Assert.Contains(Paragraphs(Import(Wordish)), p => p.IndentLevel > 0);
    }

    [Fact]
    public void A_bullet_list_becomes_a_list_and_not_a_paragraph_starting_with_a_dot()
    {
        var result = Import(Wordish);

        var list = Assert.Single(result.Doc.Blocks.OfType<ListNode>());

        Assert.Equal(ListKind.Unordered, list.Kind);
        Assert.Equal(2, list.Items.Length);

        // The bullet character itself must not survive as text; it is the list that carries it.
        Assert.DoesNotContain('\u00b7', PlainText(result));
    }

    [Fact]
    public void A_hyperlink_field_becomes_a_link()
    {
        var result = Import(Wordish);

        Assert.True(AnyRunHas(result, m => m.Link is not null && m.Link.Href == "https://example.com/"));
    }

    [Fact]
    public void Text_that_is_not_ascii_survives_however_it_was_encoded()
    {
        var text = PlainText(Import(Wordish));

        // \'e9 is a code-page byte, \uN is a Unicode escape, and an astral character arrives as a
        // surrogate pair written as two of them.
        Assert.Contains("Caf\u00e9", text, StringComparison.Ordinal);
        Assert.Contains("\u05e9\u05dc\u05d5\u05dd", text, StringComparison.Ordinal);
        Assert.Contains("\ud83d\ude00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void What_comes_out_is_a_document_the_model_would_accept()
    {
        Assert.True(
            DocumentSchema.IsValid(Import(Wordish).Doc),
            string.Join("; ", DocumentSchema.Validate(Import(Wordish).Doc)));
    }

    // ---- what arrives broken ----------------------------------------------------------------

    [Theory]
    [InlineData("<p>not rtf</p>")]
    [InlineData("plain text")]
    [InlineData("")]
    [InlineData("   ")]
    public void Something_that_is_not_rtf_is_reported_and_not_thrown_over(string input)
    {
        // A clipboard manager offers whatever it has to whichever reader might take it, so being
        // handed the wrong format is routine rather than exceptional.
        var result = RtfImporter.Import(input);

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
    public void Damaged_rtf_produces_a_result_rather_than_an_exception(string input)
    {
        var result = RtfImporter.Import(input);

        Assert.True(DocumentSchema.IsValid(result.Doc));
    }

    [Fact]
    public void Nothing_at_all_is_the_callers_mistake_and_is_reported_as_one()
    {
        Assert.Throws<ArgumentNullException>(() => RtfImporter.Import(null!));
    }

    // ---- what must not come through ---------------------------------------------------------

    [Fact]
    public void A_picture_does_not_arrive_as_a_format_nothing_can_decode()
    {
        // RtfPipe renders \pict as a data: URL whose media type is "windows/metafile". It is not
        // an image type anything here can decode, and the importer must not take it on faith.
        var result = RtfImporter.Import(
            @"{\rtf1\ansi before{\pict\wmetafile8\picw100\pich100 0102030405}after}");

        // The surrounding words prove the document was read at all, so that the absence of an
        // embed below is a decision rather than an empty result.
        Assert.Contains("before", PlainText(result), StringComparison.Ordinal);
        Assert.Contains("after", PlainText(result), StringComparison.Ordinal);
        Assert.Empty(Paragraphs(result).SelectMany(p => p.Content.Embeds));
    }

    [Fact]
    public void A_link_to_somewhere_dangerous_does_not_survive_the_trip_through_html()
    {
        // Going through the HTML importer is what makes this true; a second RTF-specific idea of
        // which URLs are safe would be a second one to get wrong.
        var result = RtfImporter.Import(
            @"{\rtf1\ansi{\field{\*\fldinst{HYPERLINK ""javascript:alert(1)""}}{\fldrslt{click}}}}");

        Assert.Contains("click", PlainText(result), StringComparison.Ordinal);
        Assert.False(AnyRunHas(result, m => m.Link is not null));
    }

    [Fact]
    public void The_hosts_options_reach_the_html_importer()
    {
        var rtf =
            @"{\rtf1\ansi{\field{\*\fldinst{HYPERLINK ""/relative""}}{\fldrslt{click}}}}";

        var withBase = RtfImporter.Import(
            rtf,
            new HtmlImportOptions { BaseUri = new Uri("https://example.com/dir/page") });

        Assert.True(AnyRunHas(withBase, m => m.Link?.Href == "https://example.com/relative"));
        Assert.False(AnyRunHas(RtfImporter.Import(rtf), m => m.Link is not null));
    }
}
