using System.Text.RegularExpressions;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// The font a document states on every run is written once, not once per run (phase 8, owner
/// decision 2026-10-28, option b).
/// </summary>
/// <remarks>
/// <para>
/// The reader that replaced RtfPipe writes a <c>&lt;span&gt;</c> for every run, repeating the
/// font family and size the whole document shares, so its HTML was about 3.5 times RtfPipe's and
/// the import of 10,000 paragraphs took 2.7 s against RtfPipe's 1.36 s. The importer now hoists
/// a family or size that every run states into one wrapping element before conversion — in the
/// reader's own model, not by parsing anything — and the import must come out exactly as it did.
/// </para>
/// <para>
/// Counted in elements rather than timed, which a shared machine cannot disturb.
/// </para>
/// </remarks>
public class RtfFormattingHoistTests
{
    private static readonly MarkSet Body = new() { FontFamily = "Calibri", FontSize = 14.67 };

    /// <summary>A Word-like body: every run in the body font, some bold, italic or coloured.</summary>
    internal static DocumentNode Styled(int paragraphs) =>
        new([.. Enumerable.Range(0, paragraphs).Select(i => (BlockNode)Paragraph($"Paragraph {i} with ", "enough", " words in ", "it", " to need ", "shaping", " and to wrap once."))]);

    private static ParagraphNode Paragraph(params string[] parts)
    {
        var text = string.Concat(parts);
        var marks = new List<ValueSpan<MarkSet>>();
        var at = 0;

        for (var i = 0; i < parts.Length; i++)
        {
            var mark = i switch
            {
                1 => Body with { Style = TextStyle.Bold },
                3 => Body with { Style = TextStyle.Italic },
                5 => Body with { Foreground = Rgba.FromRgb(192, 0, 0) },
                _ => Body,
            };

            marks.Add(new ValueSpan<MarkSet>(at, parts[i].Length, mark));
            at += parts[i].Length;
        }

        return new ParagraphNode(InlineContent.Create(text, marks, []));
    }

    [Fact]
    public void A_font_every_run_states_is_written_once()
    {
        const int Paragraphs = 1_000;

        var html = RtfImporter.ReadToHtml(RtfExporter.Instance.Export(Styled(Paragraphs)));
        var spans = Regex.Count(html, "<span", RegexOptions.CultureInvariant);
        var families = Regex.Count(html, "font-family", RegexOptions.CultureInvariant);

        // One span per paragraph is left: the coloured run, which says something the paragraph
        // around it does not.
        Assert.True(spans <= Paragraphs, $"{spans} spans for {Paragraphs} paragraphs");
        Assert.True(families <= 1, $"font-family written {families} times");
    }

    private static string Fixture(string capture, string file) =>
        System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", capture, file)))
            .TrimEnd('\0');

    private static string Exported(params BlockNode[] blocks) => RtfExporter.Instance.Export(new DocumentNode(blocks));

    private static ParagraphNode BodyText(string text, ParagraphKind kind = ParagraphKind.Body) =>
        new(InlineContent.Create(text, [new ValueSpan<MarkSet>(0, text.Length, Body)], []), kind);

    /// <summary>
    /// Inputs that exercise every reason hoisting may or may not happen, each with whether it should.
    /// </summary>
    public static TheoryData<string, string, bool> Inputs() => new()
    {
        // Real clipboard captures. Word's has fields, breaks and a stylesheet; Excel's are tables.
        { "word", Fixture("word-docx-all", "02-Rich_Text_Format.bin"), false },
        { "excel A1:D6", Fixture("excel-range-A1D6", "17-Rich_Text_Format.bin"), true },
        { "excel B2:C3", Fixture("excel-range-B2C3", "17-Rich_Text_Format.bin"), true },

        // What the editor itself copies.
        { "styled body", RtfExporter.Instance.Export(Styled(50)), true },
        { "headings", Exported(BodyText("Title", ParagraphKind.Heading1), BodyText("Sub", ParagraphKind.Heading3), BodyText("text")), true },
        { "list", Exported(new ListNode([new ListItemNode([BodyText("one")]), new ListItemNode([BodyText("two")])], ListKind.Ordered), BodyText("after")), true },
        { "table", Exported(new TableNode([new TableRowNode([new TableCellNode([BodyText("a")]), new TableCellNode([BodyText("b")])])])), true },
        { "break", Exported(BodyText("a\nb")), false },

        // A run with no font of its own would inherit the hoisted one: nothing is hoisted.
        { "unfonted run", @"{\rtf1\ansi{\fonttbl{\f1 Georgia;}}{\f1\fs22 a}{ b}\par}", false },
        { "unfonted cell", @"{\rtf1\ansi{\fonttbl{\f1 Georgia;}}{\f1\fs22 a}\par\trowd\cellx1000\pard\intbl{ b}\cell\row\pard{\f1\fs22 c}\par}", false },

        // Sizes differ: the commoner is hoisted, the other keeps its own.
        { "mixed sizes", @"{\rtf1\ansi{\fonttbl{\f1 Georgia;}}{\f1\fs22 a}{\f1\fs40 b}{\f1\fs22 c}\par}", true },

        // A paragraph style states another font: clearing the run's own would let it through, so
        // that run keeps it.
        { "style beneath", @"{\rtf1\ansi{\fonttbl{\f1 Georgia;}{\f2 Courier New;}}{\stylesheet{\s0 Normal;}{\s1\f2\fs30 Code;}}\pard\s0{\f1\fs22 body}\par\pard\s1{\f1\fs22 code}\par}", true },

        // A colour table, bold and a hyperlink field: the field is not a run, so nothing is hoisted.
        { "link", @"{\rtf1\ansi{\fonttbl{\f1 Georgia;}}{\f1\fs22 see }{\field{\*\fldinst{HYPERLINK ""https://e.test/""}}{\fldrslt{\f1\fs22 here}}}\par}", false },

        // Outlook's encapsulated HTML: touching a run would invalidate it, and the reader would
        // write the plain-text fallback instead of the HTML (rubber-duck review finding 9.4).
        { "outlook", OutlookBold, false },
    };

    /// <summary>
    /// RTF as Outlook writes it: the message's HTML encapsulated (<c>\fromhtml1</c>), with a bold
    /// the plain-text fallback beside it does not have.
    /// </summary>
    internal const string OutlookBold =
        @"{\rtf1\ansi\fromhtml1{\fonttbl{\f1 Georgia;}}{\*\htmltag1 <html><body><p><b>hello</b></p></body></html>}\htmlrtf1{\f1\fs22 hello}\htmlrtf0\par}";

    /// <summary>The import is exactly what it was without hoisting: document and diagnostics.</summary>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void Hoisting_changes_nothing_the_import_produces(string name, string rtf, bool hoists)
    {
        var options = RtfImportOptions.Default.ToHtmlOptions();
        var hoisted = RtfImporter.ReadToHtml(rtf, hoist: true);
        var plain = RtfImporter.ReadToHtml(rtf, hoist: false);

        Assert.True(hoists == hoisted.StartsWith("<div style=", StringComparison.Ordinal), $"{name}: hoisted = {!hoists}");

        var expected = VellumText.Interop.Html.HtmlImporter.ImportOnCurrentStack(plain, options);
        var actual = VellumText.Interop.Html.HtmlImporter.ImportOnCurrentStack(hoisted, options);

        Assert.Equal(VellumText.Interop.Json.JsonFormat.Instance.Export(expected.Doc), VellumText.Interop.Json.JsonFormat.Instance.Export(actual.Doc));
        Assert.Equal(expected.Diagnostics.Select(d => (d.Severity, d.Context)), actual.Diagnostics.Select(d => (d.Severity, d.Context)));
    }}
