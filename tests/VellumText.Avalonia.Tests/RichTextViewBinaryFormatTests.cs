using System.Text;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A format that is not text, carried by the clipboard and by a file, unchanged.
/// </summary>
/// <remarks>
/// DOCX and ODT are ZIP containers, and the format contract used to be able to say so only by
/// pretending a ZIP was a string. These tests use a stand-in whose payload is bytes no encoding
/// round-trips, so a string anywhere between the exporter and the importer shows up as a corrupted
/// document rather than as an opinion about the design.
/// </remarks>
public class RichTextViewBinaryFormatTests
{
    /// <summary>A PK header, a NUL, and bytes that are not valid UTF-8. Odd length on purpose.</summary>
    private static readonly byte[] Container =
        [0x50, 0x4B, 0x03, 0x04, 0x00, 0xFF, 0xFE, 0x80, 0x00, 0x41, 0x9C];

    [AvaloniaFact]
    public void A_copy_puts_a_binary_flavour_on_the_clipboard_byte_for_byte()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));

        view.Formats.Add(new ContainerFormat());

        var item = Assert.Single(view.BuildCopyData("hello").Items);

        Assert.Equal(Container, item.TryGetValue(RichTextClipboard.FlavorFor("zip-doc").DataFormat));
    }

    /// <summary>
    /// The whole path, through the real clipboard rather than through its two halves. A string on
    /// any hop between the two would arrive as <c>corrupted</c>.
    /// </summary>
    [AvaloniaFact]
    public async Task A_binary_format_survives_a_copy_and_a_paste()
    {
        var window = new global::Avalonia.Controls.Window { Width = 400, Height = 300 };
        var view = Editor(DocumentNode.FromParagraphs("hello"));

        view.Formats.Add(new ContainerFormat());

        window.Content = view;
        window.Show();

        Assert.True(await view.CopyAsync());

        var target = Editor(DocumentNode.FromParagraphs(string.Empty));

        target.Formats.Add(new ContainerFormat());
        window.Content = target;

        Assert.True(await target.PasteAsync());

        Assert.Equal("intact", DocumentText.Of(target.State.Doc).Trim());
    }

    [AvaloniaFact]
    public void Encode_and_decode_carry_a_binary_payload_between_them()
    {
        var format = new ContainerFormat();
        var payload = RichTextClipboard.Encode(format, DocumentNode.FromParagraphs("hello"));

        Assert.Equal(Container, payload);
        Assert.Equal("intact", DocumentText.Of(RichTextClipboard.Decode(format, payload).Doc).Trim());
    }

    /// <summary>
    /// The other half of the capability: a document written to a file and read back. Nothing in
    /// the library opens files, so this is what says a host can.
    /// </summary>
    [AvaloniaFact]
    public void A_binary_format_survives_a_file()
    {
        IDocumentExporter exporter = new ContainerFormat();
        IDocumentImporter importer = new ContainerFormat();
        var path = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():n}.bin");

        try
        {
            using (var writing = File.Create(path))
            {
                exporter.Export(writing, DocumentNode.FromParagraphs("hello"));
            }

            Assert.Equal(Container, File.ReadAllBytes(path));

            using var reading = File.OpenRead(path);

            Assert.Equal("intact", DocumentText.Of(importer.Import(reading).Doc).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Registration does not require a text form, and a binary format is refused nowhere along the
    /// way. This is the test that fails if anything on the path starts asking for a string first.
    /// </summary>
    [AvaloniaFact]
    public void A_binary_format_registers_without_a_text_form()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));
        var format = new ContainerFormat();

        view.Formats.Add(format);

        Assert.Null(format.TextEncoding);
        Assert.Contains(format, view.Formats);
    }

    private static RichTextView Editor(DocumentNode doc) => new()
    {
        State = EditorState.Create(doc, TextSelection.Create(doc, 1, doc.ContentSize - 1)),
    };

    /// <summary>A stand-in for DOCX: a container whose bytes are not text in any encoding.</summary>
    private sealed class ContainerFormat : IDocumentImporter, IDocumentExporter
    {
        public string Format => "zip-doc";

        public string MediaType => "application/vnd.vellum.test-container";

        public Encoding? TextEncoding => null;

        public void Export(Stream destination, DocumentNode doc) => destination.Write(Container);

        public ImportResult Import(Stream source)
        {
            using var buffer = new MemoryStream();

            source.CopyTo(buffer);

            return new ImportResult(
                DocumentNode.FromParagraphs(
                    buffer.ToArray().AsSpan().SequenceEqual(Container) ? "intact" : "corrupted"));
        }
    }
}
