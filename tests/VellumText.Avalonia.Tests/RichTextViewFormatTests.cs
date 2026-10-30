using System.Text;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;
using VellumText.Interop.Html;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Copying and pasting formatting, not just characters.
/// </summary>
/// <remarks>
/// The control cannot see the interop packages, so everything here turns on registration. These
/// tests are what say the registration point actually carries a document rather than merely
/// existing.
/// </remarks>
public class RichTextViewFormatTests
{
    private static readonly DocumentNode Styled = new([
        new ParagraphNode(
            InlineContent.Create(
                "bold and plain",
                [new ValueSpan<MarkSet>(0, 4, new MarkSet { Style = TextStyle.Bold })],
                []),
            ParagraphKind.Heading2),
    ]);

    // ---- what a copy puts on the clipboard --------------------------------------------------

    [AvaloniaFact]
    public void A_copy_with_no_formats_registered_is_still_text()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));

        var item = Assert.Single(view.BuildCopyData("hello").Items);

        Assert.Equal([DataFormat.Text], item.Formats);
    }

    [AvaloniaFact]
    public void A_copy_carries_a_flavour_for_every_registered_exporter()
    {
        var view = Editor(Styled);

        view.Formats.Add(HtmlFormat.Instance);
        view.Formats.Add(RtfFormat.Instance);

        var item = Assert.Single(view.BuildCopyData("bold and plain").Items);

        Assert.Contains(DataFormat.Text, item.Formats);
        Assert.NotNull(Payload(item, "html"));
        Assert.NotNull(Payload(item, "rtf"));
    }

    [AvaloniaFact]
    public async Task A_copied_html_flavour_reads_back_as_the_document_that_was_copied()
    {
        var view = Editor(Styled);

        view.Formats.Add(HtmlFormat.Instance);

        var item = Assert.Single(view.BuildCopyData("bold and plain").Items);
        var doc = (await HtmlFormat.Instance.ImportAsync(Decode(item, "html"))).Doc;
        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(doc.Blocks));

        Assert.Equal("bold and plain", paragraph.Content.Text);
        Assert.Equal(ParagraphKind.Heading2, paragraph.Kind);
        Assert.True(paragraph.Content.MarkAt(0).Has(TextStyle.Bold));
        Assert.False(paragraph.Content.MarkAt(5).Has(TextStyle.Bold));
    }

    [AvaloniaFact]
    public async Task A_copied_rtf_flavour_reads_back_as_the_document_that_was_copied()
    {
        var view = Editor(Styled);

        view.Formats.Add(RtfFormat.Instance);

        var item = Assert.Single(view.BuildCopyData("bold and plain").Items);
        var doc = (await RtfFormat.Instance.ImportAsync(Decode(item, "rtf"))).Doc;
        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(doc.Blocks));

        Assert.Equal("bold and plain", paragraph.Content.Text);
        Assert.Equal(ParagraphKind.Heading2, paragraph.Kind);
    }

    /// <summary>
    /// The flavours describe the selection, not the document. Copying one paragraph out of three
    /// and pasting all three back is the bug this exists to keep out.
    /// </summary>
    [AvaloniaFact]
    public async Task A_copy_carries_only_the_selection()
    {
        var doc = DocumentNode.FromParagraphs("first", "second", "third");
        var view = Editor(doc);

        view.Formats.Add(HtmlFormat.Instance);

        // The whole of the second paragraph: one past its opening token, to just before its close.
        view.State = EditorState.Create(doc, TextSelection.Create(doc, 8, 14));

        var item = Assert.Single(view.BuildCopyData(view.SelectedText()).Items);
        var copied = (await HtmlFormat.Instance.ImportAsync(Decode(item, "html"))).Doc;

        Assert.Equal("second", Assert.IsType<ParagraphNode>(Assert.Single(copied.Blocks)).Content.Text);
    }

    /// <summary>
    /// An exporter is contractually forbidden from throwing. If one does anyway, the flavour is
    /// what is lost — not the user's copy, and not the application.
    /// </summary>
    [AvaloniaFact]
    public void A_broken_exporter_costs_its_own_flavour_and_nothing_else()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));

        view.Formats.Add(new BrokenFormat());
        view.Formats.Add(RtfFormat.Instance);

        var item = Assert.Single(view.BuildCopyData("hello").Items);

        Assert.Contains(DataFormat.Text, item.Formats);
        Assert.NotNull(Payload(item, "rtf"));
    }

    // ---- what a paste prefers ----------------------------------------------------------------

    [AvaloniaFact]
    public void A_paste_prefers_html_to_rtf_however_they_were_registered()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));

        view.Formats.Add(RtfFormat.Instance);
        view.Formats.Add(HtmlFormat.Instance);

        Assert.Equal(["html", "rtf"], view.ReadableFormats().Select(format => format.Format));
    }

    [AvaloniaFact]
    public void A_paste_tries_a_format_it_has_no_ranking_for_last()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));

        view.Formats.Add(new MarkdownFormat());
        view.Formats.Add(HtmlFormat.Instance);

        Assert.Equal(["html", "markdown"], view.ReadableFormats().Select(format => format.Format));
    }

    // ---- pasting a document ------------------------------------------------------------------

    [AvaloniaFact]
    public void Pasting_a_paragraph_merges_it_into_the_one_the_caret_is_in()
    {
        var doc = DocumentNode.FromParagraphs("hello world");
        var view = Editor(doc);

        view.State = EditorState.Create(doc, TextSelection.Cursor(7));

        Assert.True(view.PasteDocument(DocumentNode.FromParagraphs("brave ")));

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(view.State.Doc.Blocks));

        Assert.Equal("hello brave world", paragraph.Content.Text);
    }

    /// <summary>
    /// A slice may only be opened into a paragraph. Opening one into a table claims a depth the
    /// block does not have, and the step is refused — which would lose the paste silently.
    /// </summary>
    [AvaloniaFact]
    public void Pasting_a_document_that_starts_with_a_table_still_pastes()
    {
        var doc = DocumentNode.FromParagraphs("hello");
        var view = Editor(doc);

        view.State = EditorState.Create(doc, TextSelection.Cursor(6));

        var table = new TableNode([
            new TableRowNode([new TableCellNode([new ParagraphNode(InlineContent.FromText("cell"))])]),
        ]);

        Assert.True(view.PasteDocument(new DocumentNode([table])));
        Assert.Contains(view.State.Doc.Blocks, block => block is TableNode);
    }

    /// <summary>
    /// Pasting into an empty paragraph keeps the pasted block's own kind. Merging is right in the
    /// middle of a sentence and wrong here — an empty paragraph has no style worth preserving, and
    /// preserving it anyway is how a copied heading arrives as body text.
    /// </summary>
    [AvaloniaFact]
    public void Pasting_into_an_empty_paragraph_keeps_the_pasted_kind()
    {
        var doc = DocumentNode.FromParagraphs(string.Empty);
        var view = Editor(doc);

        view.State = EditorState.Create(doc, TextSelection.Cursor(1));

        Assert.True(view.PasteDocument(Styled));

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(view.State.Doc.Blocks));

        Assert.Equal(ParagraphKind.Heading2, paragraph.Kind);
        Assert.Equal("bold and plain", paragraph.Content.Text);
    }

    /// <summary>
    /// Replacing the host outright must use the blocks as they came, not the padded ones the open
    /// path needs — otherwise the padding survives as a stray empty paragraph.
    /// </summary>
    [AvaloniaFact]
    public void Pasting_a_table_into_an_empty_paragraph_leaves_no_stray_paragraph()
    {
        var doc = DocumentNode.FromParagraphs(string.Empty);
        var view = Editor(doc);

        view.State = EditorState.Create(doc, TextSelection.Cursor(1));

        var table = new TableNode([
            new TableRowNode([new TableCellNode([new ParagraphNode(InlineContent.FromText("cell"))])]),
        ]);

        Assert.True(view.PasteDocument(new DocumentNode([table])));
        Assert.IsType<TableNode>(Assert.Single(view.State.Doc.Blocks));
    }

    [AvaloniaFact]
    public void Pasting_an_empty_document_changes_nothing()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));

        Assert.False(view.PasteDocument(new DocumentNode([])));
    }

    // ---- registration -------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_format_that_can_neither_be_read_nor_written_is_refused()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));

        Assert.Throws<ArgumentException>(() => view.Formats.Add(new InertFormat()));
    }

    // ---- the whole path ------------------------------------------------------------------------

    /// <summary>
    /// Copy and paste for real, through the clipboard, rather than through the two halves
    /// separately. This is the test that would have caught the flavours being written under a
    /// format nothing reads them back from.
    /// </summary>
    [AvaloniaFact]
    public async Task Formatting_survives_a_copy_and_a_paste()
    {
        var window = new global::Avalonia.Controls.Window { Width = 400, Height = 300 };
        var view = Editor(Styled);

        view.Formats.Add(HtmlFormat.Instance);
        view.Formats.Add(RtfFormat.Instance);

        window.Content = view;
        window.Show();

        Assert.True(await view.CopyAsync());

        // Somewhere else entirely, so that what arrives can only have come from the clipboard.
        var target = Editor(DocumentNode.FromParagraphs(string.Empty));

        target.Formats.Add(HtmlFormat.Instance);
        target.Formats.Add(RtfFormat.Instance);

        window.Content = target;

        Assert.True(await target.PasteAsync());

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(target.State.Doc.Blocks));

        Assert.Equal("bold and plain", paragraph.Content.Text);
        Assert.Equal(ParagraphKind.Heading2, paragraph.Kind);
        Assert.True(paragraph.Content.MarkAt(0).Has(TextStyle.Bold));
        Assert.False(paragraph.Content.MarkAt(5).Has(TextStyle.Bold));
    }

    /// <summary>
    /// With nothing registered the same copy and paste still moves the text. A consumer who wants
    /// a plain editor gets one that works, not one that has quietly lost its clipboard.
    /// </summary>
    [AvaloniaFact]
    public async Task Text_survives_a_copy_and_a_paste_with_no_formats_registered()
    {
        var window = new global::Avalonia.Controls.Window { Width = 400, Height = 300 };
        var view = Editor(Styled);

        window.Content = view;
        window.Show();

        Assert.True(await view.CopyAsync());

        var target = Editor(DocumentNode.FromParagraphs(string.Empty));

        window.Content = target;

        Assert.True(await target.PasteAsync());

        Assert.Equal("bold and plain", Assert.IsType<ParagraphNode>(Assert.Single(target.State.Doc.Blocks)).Content.Text);
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>An editor holding <paramref name="doc"/>, with all of it selected.</summary>
    /// <remarks>
    /// Selected, because a copy describes the selection and a copy of nothing is empty — which is
    /// correct, and says nothing at all about whether the formats work. The range is computed
    /// rather than taken from <see cref="RichTextView.SelectAll"/>, which needs a laid-out control
    /// to find the first and last text position and selects nothing without one.
    /// </remarks>
    private static RichTextView Editor(DocumentNode doc) => new()
    {
        State = EditorState.Create(doc, TextSelection.Create(doc, 1, doc.ContentSize - 1)),
    };

    private static byte[]? Payload(IDataTransferItem item, string format) =>
        item.TryGetValue(RichTextClipboard.FlavorFor(format)!.DataFormat);

    private static string Decode(IDataTransferItem item, string format) =>
        Encoding.UTF8.GetString(Payload(item, format)!);

    private sealed class BrokenFormat : IDocumentExporter
    {
        public string Format => "html";

        public string MediaType => "text/html";

        public Encoding? TextEncoding => Encoding.UTF8;

        public string Export(DocumentNode doc) => throw new InvalidOperationException("bug");

        public void Export(Stream destination, DocumentNode doc) =>
            throw new InvalidOperationException("bug");
    }

    private sealed class MarkdownFormat : IDocumentImporter
    {
        public string Format => "markdown";

        public string MediaType => "text/markdown";

        public Encoding? TextEncoding => Encoding.UTF8;

        public ImportResult Import(string text) => new(DocumentNode.FromParagraphs(text));

        public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default) =>
            Import(await DocumentPayload.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken));
    }

    private sealed class InertFormat : IDocumentFormat
    {
        public string Format => "inert";

        public string MediaType => "application/octet-stream";

        public Encoding? TextEncoding => null;
    }
}
