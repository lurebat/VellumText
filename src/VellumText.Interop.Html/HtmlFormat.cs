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
/// <para>
/// <see cref="Instance"/> imports with <see cref="HtmlImportOptions.Default"/>. Use
/// <see cref="With"/> to register a configured one — that is the only way <c>BaseUri</c> and
/// <c>AllowRemoteImages</c> reach the importer when the control is doing the importing.
/// </para>
/// </remarks>
public sealed class HtmlFormat : IDocumentImporter, IDocumentExporter
{
    /// <summary>
    /// The shared default: import options are <see cref="HtmlImportOptions.Default"/>.
    /// </summary>
    public static readonly HtmlFormat Instance = new(HtmlImportOptions.Default);

    private HtmlFormat(HtmlImportOptions importOptions) => ImportOptions = importOptions;

    /// <summary>How much of the incoming HTML this instance trusts.</summary>
    public HtmlImportOptions ImportOptions { get; }

    /// <summary>
    /// A format that imports with <paramref name="importOptions"/>, leaving this one unchanged.
    /// </summary>
    /// <param name="importOptions">The options the importer should use.</param>
    /// <returns>A new configured instance; register it in place of <see cref="Instance"/>.</returns>
    /// <remarks>
    /// Registration is the seam a host is told to use, so options that cannot travel through it are
    /// unreachable in practice: <see cref="HtmlImportOptions.AllowRemoteImages"/> is a security
    /// control and <see cref="HtmlImportOptions.BaseUri"/> decides whether a relative image URL
    /// resolves at all, and neither means anything if the only way to pass it is a static method
    /// the control never calls.
    /// </remarks>
    public HtmlFormat With(HtmlImportOptions importOptions)
    {
        ArgumentNullException.ThrowIfNull(importOptions);
        return new HtmlFormat(importOptions);
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
    public Task<ImportResult> ImportAsync(string text, CancellationToken cancellationToken) =>
        HtmlImporter.ImportClipboardAsync(text, ImportOptions, cancellationToken);

    /// <inheritdoc/>
#pragma warning disable RS0027 // Ties with the string overload on purpose; see IDocumentImporter.
    public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
#pragma warning restore RS0027
    {
        var text = await DocumentPayload.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        return await HtmlImporter.ImportClipboardAsync(text, ImportOptions, cancellationToken)
            .ConfigureAwait(false);
    }

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
