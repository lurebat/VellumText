using System.Text;

namespace VellumText.Interop.Html;

/// <summary>
/// HTML as a clipboard flavour: the importer and the exporter behind one registrable object.
/// </summary>
/// <remarks>
/// <para>
/// Handing this to <c>RichTextView.Formats</c> is what makes copy write HTML and paste read it.
/// The control cannot reach <see cref="HtmlImporter"/> or <see cref="HtmlExporter"/> itself
/// without dragging an HTML parser into every application that only wanted an editor.
/// </para>
/// <para>
/// On Windows this is <c>CF_HTML</c>: the payload carries the preamble and byte offsets Windows
/// requires and every other application on Windows expects. Elsewhere it is bare HTML, because
/// the envelope is a Windows convention rather than part of the format — macOS
/// <c>public.html</c> and the X11/Wayland <c>text/html</c> target both carry markup directly,
/// and an application pasting a preamble from either would show it as content.
/// </para>
/// <para>
/// Reading tolerates the envelope's absence <em>and</em> its presence on every platform, because
/// plenty of applications write bare HTML and a paste that refused them would be worse than
/// useless.
/// </para>
/// </remarks>
public sealed class HtmlFormat : IDocumentImporter, IDocumentExporter
{
    /// <summary>The one instance. Importing and exporting are pure, so more would be waste.</summary>
    public static readonly HtmlFormat Instance = new();

    private HtmlFormat()
    {
    }

    /// <inheritdoc/>
    public string Format => "html";

    /// <inheritdoc/>
    public string MediaType => "text/html";

    /// <inheritdoc/>
    /// <remarks>
    /// UTF-8 because <c>CF_HTML</c> is defined in it and its byte offsets are only correct in it.
    /// </remarks>
    public Encoding? TextEncoding => Encoding.UTF8;

    /// <inheritdoc/>
    public ImportResult Import(string text) => HtmlImporter.ImportClipboard(text);

    /// <inheritdoc/>
    public ImportResult Import(Stream source) =>
        HtmlImporter.ImportClipboard(DocumentPayload.ReadAllText(source, Encoding.UTF8));

    /// <inheritdoc/>
    /// <remarks>
    /// Windows gets the <c>CF_HTML</c> envelope and no other platform does. The clipboard format
    /// <em>name</em> is already chosen per platform — <c>HTML Format</c>, <c>public.html</c>,
    /// <c>text/html</c> — and only the first of those three is defined to carry a preamble. Use
    /// <see cref="HtmlExporter.ExportForClipboard"/> to demand the envelope regardless of platform,
    /// and <see cref="HtmlExporter.Export(DocumentNode)"/> to demand bare markup.
    /// </remarks>
    public string Export(DocumentNode doc) =>
        OperatingSystem.IsWindows()
            ? HtmlExporter.Instance.ExportForClipboard(doc)
            : HtmlExporter.Instance.Export(doc);

    /// <inheritdoc/>
    public void Export(Stream destination, DocumentNode doc) =>
        DocumentPayload.WriteAllText(destination, Encoding.UTF8, Export(doc));
}
