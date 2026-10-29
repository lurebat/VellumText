using System.Text;
using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// Whether a host can configure the Markdown importer through the object it actually registers,
/// and whether the stream and string members agree.
/// </summary>
/// <remarks>
/// <c>MarkdownImporter.Import(text, options)</c> is not the path a control takes: the control holds
/// an <see cref="IDocumentImporter"/> and calls <c>Import(text)</c>. Without
/// <see cref="MarkdownFormat.With"/> every option — including
/// <see cref="MarkdownImportOptions.AllowRawHtml"/>, which decides whether arbitrary HTML enters
/// the document — is out of reach of an application that pastes Markdown into the editor, which is
/// what the package is for.
/// </remarks>
public class MarkdownFormatOptionsTests
{
    [Fact]
    public void The_format_names_itself_the_way_a_clipboard_does()
    {
        Assert.Equal("markdown", MarkdownFormat.Instance.Format);
        Assert.Equal("text/markdown", MarkdownFormat.Instance.MediaType);
        Assert.Equal(Encoding.UTF8, MarkdownFormat.Instance.TextEncoding);
    }

    [Fact]
    public void The_importer_and_the_exporter_agree_on_the_format_name()
    {
        Assert.Equal(MarkdownFormat.Instance.Format, MarkdownExporter.Instance.Format);
        Assert.Equal(MarkdownFormat.Instance.MediaType, MarkdownExporter.Instance.MediaType);
    }

    [Fact]
    public void The_shared_instance_imports_with_the_paranoid_defaults() =>
        Assert.Same(MarkdownImportOptions.Default, MarkdownFormat.Instance.ImportOptions);

    [Fact]
    public void With_leaves_the_shared_instance_alone()
    {
        var configured = MarkdownFormat.Instance.With(
            new MarkdownImportOptions { AllowRawHtml = true });

        Assert.NotSame(MarkdownFormat.Instance, configured);
        Assert.True(configured.ImportOptions.AllowRawHtml);
        Assert.False(MarkdownFormat.Instance.ImportOptions.AllowRawHtml);
    }

    [Fact]
    public void With_rejects_null_rather_than_silently_meaning_the_defaults() =>
        Assert.Throws<ArgumentNullException>(() => MarkdownFormat.Instance.With(null!));

    /// <summary>
    /// The options reach the importer through an interface-typed call, which is all the control
    /// ever makes.
    /// </summary>
    [Fact]
    public async Task Raw_html_is_read_when_the_registered_format_says_so()
    {
        IDocumentImporter configured = MarkdownFormat.Instance.With(
            new MarkdownImportOptions { AllowRawHtml = true });

        var paragraph = (ParagraphNode)(await configured.ImportAsync("a <b>bold</b> c", cancellationToken: TestContext.Current.CancellationToken)).Doc.Blocks[0];

        Assert.Equal(TextStyle.Bold, paragraph.Content.MarkAt(2).Style);
    }

    /// <summary>
    /// The companion that stops the test above passing for the wrong reason: the shared instance
    /// gives a different answer for the same input.
    /// </summary>
    [Fact]
    public async Task Raw_html_is_still_dropped_by_the_shared_instance()
    {
        IDocumentImporter shared = MarkdownFormat.Instance;

        var paragraph = (ParagraphNode)(await shared.ImportAsync("a <b>bold</b> c", cancellationToken: TestContext.Current.CancellationToken)).Doc.Blocks[0];

        Assert.Equal(TextStyle.None, paragraph.Content.MarkAt(2).Style);
    }

    [Fact]
    public async Task The_stream_overload_carries_the_options_as_well()
    {
        IDocumentImporter configured = MarkdownFormat.Instance.With(
            new MarkdownImportOptions { AllowRawHtml = true });

        using var source = new MemoryStream(Encoding.UTF8.GetBytes("a <b>bold</b> c"));
        var paragraph = (ParagraphNode)(await configured.ImportAsync(source, cancellationToken: TestContext.Current.CancellationToken)).Doc.Blocks[0];

        Assert.Equal(TextStyle.Bold, paragraph.Content.MarkAt(2).Style);
    }

    /// <summary>
    /// Non-ASCII is where a stream overload that guessed an encoding would come apart, and the
    /// literal is here so the test is not merely comparing the reader with the writer.
    /// </summary>
    [Fact]
    public async Task The_stream_overload_reads_utf8()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("héllo — wörld"));

        var paragraph = (ParagraphNode)(await MarkdownFormat.Instance.ImportAsync(source, cancellationToken: TestContext.Current.CancellationToken)).Doc.Blocks[0];

        Assert.Equal("héllo — wörld", paragraph.Content.Text);
    }

    [Fact]
    public void The_export_stream_writes_utf8_without_a_byte_order_mark()
    {
        var doc = new DocumentNode([Paragraph("héllo — wörld")]);
        using var destination = new MemoryStream();

        MarkdownFormat.Instance.Export(destination, doc);

        Assert.Equal(
            Encoding.UTF8.GetBytes(MarkdownFormat.Instance.Export(doc)),
            destination.ToArray());
        Assert.NotEqual(0xEF, destination.ToArray()[0]);
    }

    /// <summary>
    /// Both exporters write the same bytes for the same document. They share a private writer, so
    /// this is guarding against a future where one of them grows a shortcut.
    /// </summary>
    [Fact]
    public void The_format_exports_what_the_exporter_exports()
    {
        var doc = new DocumentNode([Paragraph("hello"), Paragraph("world")]);

        Assert.Equal(MarkdownExporter.Instance.Export(doc), MarkdownFormat.Instance.Export(doc));
        Assert.Equal(
            MarkdownExporter.Instance.Export(doc),
            MarkdownExporter.Instance.ExportWithDiagnostics(doc).Text);
    }

    [Fact]
    public void Exporting_to_a_null_stream_says_so() =>
        Assert.Throws<ArgumentNullException>(
            () => MarkdownExporter.Instance.Export(null!, new DocumentNode([Paragraph("x")])));

    [Fact]
    public void Exporting_a_null_document_says_so()
    {
        Assert.Throws<ArgumentNullException>(() => MarkdownExporter.Instance.Export(null!));
        Assert.Throws<ArgumentNullException>(
            () => MarkdownExporter.Instance.ExportWithDiagnostics(null!));
    }

    [Fact]
    public async Task Importing_null_says_so()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => MarkdownImporter.ImportAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => MarkdownFormat.Instance.ImportAsync((Stream)null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static ParagraphNode Paragraph(string text) =>
        new(InlineContent.FromText(text), ParagraphKind.Body);
}
