using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// The HTML exporter, tested almost entirely by round trip.
/// </summary>
/// <remarks>
/// <para>
/// Asserting the exact HTML for each construct would pin the exporter's formatting choices and
/// prove nothing about whether the output can be read back. What matters is that a document
/// survives being written and read again, because that is what happens every time a user copies
/// out of VellumText and pastes into VellumText — and the importer is the same code that reads Word.
/// </para>
/// <para>
/// A handful of tests do look at the markup, but only where the round trip cannot see the
/// difference: escaping, and the CF_HTML envelope.
/// </para>
/// </remarks>
public class HtmlExportTests
{
    /// <summary>Exports a document and reads it straight back.</summary>
    private static DocumentNode RoundTrip(DocumentNode doc)
    {
        var html = HtmlExporter.Instance.Export(doc);
        var result = HtmlImporter.Import(html);

        Assert.Empty(result.Diagnostics);

        return result.Doc;
    }

    private static void Survives(DocumentNode doc) => Assert.Equal(doc, RoundTrip(doc));

    [Fact]
    public void Plain_paragraphs_survive() =>
        Survives(DocumentNode.FromParagraphs("first", "second", "third"));

    [Fact]
    public void An_empty_document_does_not_throw()
    {
        var html = HtmlExporter.Instance.Export(DocumentNode.Empty);

        Assert.NotNull(html);
    }

    /// <summary>
    /// An empty paragraph is the blank line a user pressed Enter twice for. HTML collapses an
    /// empty element to nothing, so without a deliberate break the line is silently lost.
    /// </summary>
    [Fact]
    public void An_empty_paragraph_between_two_others_is_not_swallowed() =>
        Survives(DocumentNode.FromParagraphs("above", "", "below"));

    /// <summary>
    /// HTML throws away a leading space, a trailing space and the second of any pair. A clipboard
    /// has no business reformatting what it was handed, so the exporter has to defend every space
    /// the collapsing rules would otherwise eat.
    /// </summary>
    [Theory]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("two  spaces")]
    [InlineData("a       run")]
    [InlineData("   ")]
    [InlineData(" both ")]
    [InlineData("tab\tseparated")]
    public void Spaces_that_html_would_collapse_survive(string text) =>
        Survives(DocumentNode.FromParagraphs(text));

    /// <summary>
    /// Defending the spaces switches the paragraph onto the preformatted path, where a line break
    /// is written as a real newline rather than a tag. Both have to keep working at once.
    /// </summary>
    [Theory]
    [InlineData("two  spaces\nand a break")]
    [InlineData("\ttabbed\nlines\t")]
    public void A_line_break_survives_alongside_spaces_that_need_defending(string text) =>
        Survives(DocumentNode.FromParagraphs(text));

    /// <summary>
    /// A non-breaking space is content a source chose deliberately, and Word fills its output
    /// with them. It must not be confused with the ordinary spaces being defended around it.
    /// </summary>
    [Fact]
    public void A_non_breaking_space_stays_non_breaking() =>
        Survives(DocumentNode.FromParagraphs("a\u00a0b", "a\u00a0 b"));

    /// <summary>
    /// The round trip cannot see the difference, but everything downstream can: a paragraph that
    /// needs no defending must not carry the declaration, or every paste into Word and Outlook
    /// arrives wearing a rule it never asked for.
    /// </summary>
    [Fact]
    public void Ordinary_text_is_not_given_a_whitespace_rule()
    {
        var html = HtmlExporter.Instance.Export(
            DocumentNode.FromParagraphs("ordinary words", "and a second line"));

        Assert.DoesNotContain("white-space", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_that_needs_defending_is_given_a_whitespace_rule()
    {
        var html = HtmlExporter.Instance.Export(DocumentNode.FromParagraphs("two  spaces"));

        Assert.Contains("white-space:pre-wrap", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ParagraphKind.Heading1)]
    [InlineData(ParagraphKind.Heading2)]
    [InlineData(ParagraphKind.Heading3)]
    [InlineData(ParagraphKind.Heading4)]
    [InlineData(ParagraphKind.Heading5)]
    [InlineData(ParagraphKind.Heading6)]
    [InlineData(ParagraphKind.Quote)]
    [InlineData(ParagraphKind.Code)]
    [InlineData(ParagraphKind.Body)]
    public void Every_paragraph_kind_survives(ParagraphKind kind) =>
        Survives(new DocumentNode([new ParagraphNode(InlineContent.FromText("text"), kind)]));

    /// <summary>
    /// The HTML parser throws away a newline immediately after a &lt;pre&gt; start tag, which is
    /// a rule written for hand-authored markup and a silent truncation for generated markup. A
    /// code block that opens with a blank line has to survive it.
    /// </summary>
    [Theory]
    [InlineData("\nleading blank line")]
    [InlineData("\n\ntwo of them")]
    [InlineData("trailing\n")]
    [InlineData("  indented\n    more\n")]
    public void A_code_block_keeps_the_newlines_at_its_edges(string text) =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText(text), ParagraphKind.Code)]));

    /// <summary>
    /// The lines of a code block — an empty one, indentation, trailing spaces — come back as one
    /// &lt;pre&gt; block with the same text, not a block per line.
    /// </summary>
    [Fact]
    public void A_multi_line_code_block_comes_back_as_one_block_with_the_same_lines() =>
        Survives(new DocumentNode([
            ParagraphNode.FromText("before"),
            new ParagraphNode(InlineContent.FromText("if (x) {\n\n    y();  \n}"), ParagraphKind.Code),
            ParagraphNode.FromText("after")]));

    [Theory]
    [InlineData(TextAlign.Left)]
    [InlineData(TextAlign.Center)]
    [InlineData(TextAlign.Right)]
    [InlineData(TextAlign.Justify)]
    [InlineData(TextAlign.Default)]
    public void Every_alignment_survives(TextAlign align) =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("text"), ParagraphKind.Body, align),
        ]));

    /// <summary>
    /// The indent step is a number shared with the importer. Writing 48px and reading it back as
    /// anything but one level would drift a document one level per copy.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Indent_depth_survives(int level) =>
        Survives(new DocumentNode([
            new ParagraphNode(
                InlineContent.FromText("text"), ParagraphKind.Body, TextAlign.Default, level),
        ]));

    [Theory]
    [InlineData(TextStyle.Bold)]
    [InlineData(TextStyle.Italic)]
    [InlineData(TextStyle.Underline)]
    [InlineData(TextStyle.Strikethrough)]
    [InlineData(TextStyle.Code)]
    [InlineData(TextStyle.Super)]
    [InlineData(TextStyle.Sub)]
    [InlineData(TextStyle.Bold | TextStyle.Italic | TextStyle.Underline)]
    public void Every_character_style_survives(TextStyle style) =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("text", new MarkSet(style))),
        ]));

    [Fact]
    public void Colours_survive() =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText(
                "text",
                new MarkSet(
                    foreground: Rgba.FromRgb(0x12, 0x34, 0x56),
                    highlight: Rgba.FromRgb(0xFF, 0xFF, 0x00)))),
        ]));

    /// <summary>
    /// Explicit black is the colour most easily lost, because a reader that treats it as the
    /// default drops it, and the difference only shows on a dark theme.
    /// </summary>
    [Fact]
    public void Explicit_black_survives() =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("text", new MarkSet(foreground: Rgba.Black))),
        ]));

    [Fact]
    public void Font_family_and_size_survive() =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText(
                "text", new MarkSet(fontFamily: "Georgia", fontSize: 18))),
        ]));

    /// <summary>
    /// A family with a space in it must be quoted, or CSS reads it as several identifiers and
    /// the name comes back mangled.
    /// </summary>
    [Fact]
    public void A_font_family_with_a_space_survives() =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText(
                "text", new MarkSet(fontFamily: "Times New Roman"))),
        ]));

    [Fact]
    public void Links_survive() =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText(
                "text", new MarkSet(link: new LinkMark("https://example.com/a?b=1&c=2")))),
        ]));

    [Fact]
    public void Lists_survive() =>
        Survives(new DocumentNode([
            new ListNode([ListItemNode.FromText("one"), ListItemNode.FromText("two")]),
        ]));

    [Fact]
    public void An_ordered_list_keeps_its_starting_number() =>
        Survives(new DocumentNode([
            new ListNode(
                [ListItemNode.FromText("one"), ListItemNode.FromText("two")],
                ListKind.Ordered,
                start: 7),
        ]));

    [Fact]
    public void A_nested_list_survives() =>
        Survives(new DocumentNode([
            new ListNode([
                new ListItemNode([
                    ParagraphNode.FromText("outer"),
                    new ListNode([ListItemNode.FromText("inner")]),
                ]),
            ]),
        ]));

    [Fact]
    public void Tables_survive() =>
        Survives(new DocumentNode([
            new TableNode([
                new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
                new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
            ]),
        ]));

    [Fact]
    public void A_rule_survives() =>
        Survives(new DocumentNode([
            ParagraphNode.FromText("above"),
            RuleNode.Instance,
            ParagraphNode.FromText("below"),
        ]));

    /// <summary>
    /// A line break inside a paragraph is not a paragraph break, and writing it literally would
    /// let HTML's whitespace collapsing turn it into a space.
    /// </summary>
    [Fact]
    public void A_line_break_inside_a_paragraph_stays_a_line_break() =>
        Survives(new DocumentNode([ParagraphNode.FromText("above\nbelow")]));

    /// <summary>Text that is markup, which must come back as text rather than as elements.</summary>
    [Fact]
    public void Markup_in_the_text_is_escaped()
    {
        var doc = new DocumentNode([
            ParagraphNode.FromText("<script>alert('x' & \"y\")</script>"),
        ]);

        var html = HtmlExporter.Instance.Export(doc);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Survives(doc);
    }

    /// <summary>A quote in a link target must not be able to end the attribute early.</summary>
    [Fact]
    public void A_quote_in_a_link_target_cannot_escape_its_attribute()
    {
        var html = HtmlExporter.Instance.Export(new DocumentNode([
            new ParagraphNode(InlineContent.FromText(
                "text", new MarkSet(link: new LinkMark("https://example.com/\" onload=\"alert(1)")))),
        ]));

        Assert.DoesNotContain("onload=\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Bidi_and_clusters_survive() =>
        Survives(DocumentNode.FromParagraphs(
            "the Hebrew word \u05e9\u05dc\u05d5\u05dd inside English",
            "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 and e\u0301\u0332"));

    /// <summary>
    /// The whole model at once, which is the case that catches an exporter that is correct for
    /// each construct alone and wrong about how they nest.
    /// </summary>
    [Fact]
    public void A_realistic_document_survives() =>
        Survives(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("Title"), ParagraphKind.Heading1),
            new ParagraphNode(InlineContent.Create(
                "bold and a link",
                [
                    new ValueSpan<MarkSet>(0, 4, new MarkSet(TextStyle.Bold)),
                    new ValueSpan<MarkSet>(4, 7, MarkSet.Empty),
                    new ValueSpan<MarkSet>(11, 4, new MarkSet(link: new LinkMark("https://example.com/"))),
                ],
                [])),
            new ListNode([ListItemNode.FromText("one"), ListItemNode.FromText("two")], ListKind.Ordered),
            new ParagraphNode(InlineContent.FromText("quoted"), ParagraphKind.Quote),
            new ParagraphNode(InlineContent.FromText("var x = 1;"), ParagraphKind.Code),
            RuleNode.Instance,
            new TableNode([
                new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            ]),
        ]));

    /// <summary>The clipboard flavour is the same markup inside the CF_HTML envelope.</summary>
    [Fact]
    public void The_clipboard_flavour_carries_the_envelope()
    {
        var doc = DocumentNode.FromParagraphs("hello");
        var payload = HtmlExporter.Instance.ExportForClipboard(doc);

        Assert.StartsWith("Version:0.9", payload, StringComparison.Ordinal);
        Assert.Contains("StartFragment:", payload, StringComparison.Ordinal);

        // And it reads back through the same envelope-aware path a paste uses.
        Assert.Equal(doc, HtmlImporter.Import(ClipboardHtml.ExtractFragment(payload, out _)).Doc);
    }

    /// <summary>The <c>CF_HTML</c> envelope is a Windows convention, so only Windows gets it.</summary>
    /// <remarks>
    /// <para>
    /// Both branches are asserted and CI runs this on Windows, Linux and macOS, so the branch a
    /// given developer's machine never takes is still exercised somewhere. That matters more than
    /// usual here: the clipboard format <em>name</em> is chosen per platform, and writing a
    /// Windows preamble under <c>public.html</c> or <c>text/html</c> puts <c>Version:0.9</c> into
    /// what a receiving application reads as markup.
    /// </para>
    /// <para>
    /// The final assertion is the reason this went unnoticed. Import tolerates the envelope on
    /// every platform, so a round trip succeeds either way and no round-trip test can distinguish
    /// the two. Only an assertion about the payload itself can.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_envelope_is_windows_only()
    {
        var doc = DocumentNode.FromParagraphs("hello");
        var payload = HtmlFormat.Instance.Export(doc);

        if (OperatingSystem.IsWindows())
        {
            Assert.True(ClipboardHtml.IsClipboardHtml(payload));
            Assert.Equal(HtmlExporter.Instance.ExportForClipboard(doc), payload);
        }
        else
        {
            Assert.False(ClipboardHtml.IsClipboardHtml(payload));
            Assert.Equal(HtmlExporter.Instance.Export(doc), payload);
            Assert.StartsWith("<", payload, StringComparison.Ordinal);
        }

        Assert.Equal(doc, HtmlFormat.Instance.Import(payload).Doc);
    }

    [Fact]
    public void The_exporter_advertises_itself()
    {
        Assert.Equal("html", HtmlExporter.Instance.Format);
        Assert.Equal("text/html", HtmlExporter.Instance.MediaType);
        Assert.IsAssignableFrom<IDocumentExporter>(HtmlExporter.Instance);
    }
}
