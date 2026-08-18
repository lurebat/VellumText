using System.Text;

namespace VellumText.Interop.Rtf;

/// <summary>
/// RTF as a clipboard flavour: the importer and the exporter behind one registrable object.
/// </summary>
/// <remarks>
/// Handing this to <c>RichTextView.Formats</c> is what makes copy write RTF and paste read it.
/// The control cannot reach <see cref="RtfImporter"/> or <see cref="RtfExporter"/> itself without
/// dragging an RTF reader into every application that only wanted an editor.
/// </remarks>
public sealed class RtfFormat : IDocumentImporter, IDocumentExporter
{
    /// <summary>The one instance. Importing and exporting are pure, so more would be waste.</summary>
    public static readonly RtfFormat Instance = new();

    private RtfFormat()
    {
    }

    /// <inheritdoc/>
    public string Format => "rtf";

    /// <inheritdoc/>
    public string MediaType => "text/rtf";

    /// <inheritdoc/>
    /// <remarks>
    /// Latin-1 rather than ASCII because it cannot substitute: RTF is seven-bit by construction,
    /// so if anything non-ASCII ever did escape the exporter the bytes stay wrong rather than
    /// silently becoming question marks. A multi-byte guess turns RTF into convincing mojibake
    /// that then parses to an empty document, which reads as an unsupported payload rather than
    /// as the decoding bug it is.
    /// </remarks>
    public Encoding? TextEncoding => Encoding.Latin1;

    /// <inheritdoc/>
    public ImportResult Import(string text) => RtfImporter.Import(text);

    /// <inheritdoc/>
    public ImportResult Import(Stream source) =>
        RtfImporter.Import(DocumentPayload.ReadAllText(source, Encoding.Latin1));

    /// <inheritdoc/>
    public string Export(DocumentNode doc) => RtfExporter.Instance.Export(doc);

    /// <inheritdoc/>
    public void Export(Stream destination, DocumentNode doc) =>
        DocumentPayload.WriteAllText(destination, Encoding.Latin1, RtfExporter.Instance.Export(doc));
}
