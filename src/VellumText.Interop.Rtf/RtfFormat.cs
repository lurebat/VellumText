using System.Text;

namespace VellumText.Interop.Rtf;

/// <summary>
/// RTF as a clipboard flavour: the importer and the exporter behind one registrable object.
/// </summary>
/// <remarks>
/// <para>
/// Handing this to <c>RichTextView.Formats</c> is what makes copy write RTF and paste read it.
/// The control cannot reach <see cref="RtfImporter"/> or <see cref="RtfExporter"/> itself without
/// dragging an RTF reader into every application that only wanted an editor.
/// </para>
/// <para>
/// <see cref="Instance"/> imports with <see cref="RtfImportOptions.Default"/>. Use
/// <see cref="With"/> to register a configured one — that is the only way
/// <c>AllowRemoteImages</c> and the rest reach the importer when the control is doing the
/// importing.
/// </para>
/// </remarks>
public sealed class RtfFormat : IDocumentImporter, IDocumentExporter
{
    /// <summary>
    /// The shared default: import options are <see cref="RtfImportOptions.Default"/>.
    /// </summary>
    public static readonly RtfFormat Instance = new(RtfImportOptions.Default);

    private RtfFormat(RtfImportOptions importOptions) => ImportOptions = importOptions;

    /// <summary>How much of the incoming RTF this instance trusts.</summary>
    public RtfImportOptions ImportOptions { get; }

    /// <summary>
    /// A format that imports with <paramref name="importOptions"/>, leaving this one unchanged.
    /// </summary>
    /// <param name="importOptions">The options the importer should use.</param>
    /// <returns>A new configured instance; register it in place of <see cref="Instance"/>.</returns>
    /// <remarks>
    /// Registration is the seam a host is told to use, so an option that cannot travel through it
    /// is unreachable in practice — including
    /// <see cref="RtfImportOptions.AllowRemoteImages"/>, which is a security control.
    /// </remarks>
    public RtfFormat With(RtfImportOptions importOptions)
    {
        ArgumentNullException.ThrowIfNull(importOptions);
        return new RtfFormat(importOptions);
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
    public Task<ImportResult> ImportAsync(string text, CancellationToken cancellationToken = default) =>
        RtfImporter.ImportAsync(text, ImportOptions, cancellationToken);

    /// <inheritdoc/>
    public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var text = await DocumentPayload.ReadAllTextAsync(source, Encoding.Latin1, cancellationToken)
            .ConfigureAwait(false);

        return await RtfImporter.ImportAsync(text, ImportOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string Export(DocumentNode doc) => RtfExporter.Instance.Export(doc);

    /// <inheritdoc/>
    public void Export(Stream destination, DocumentNode doc) =>
        DocumentPayload.WriteAllText(destination, Encoding.Latin1, RtfExporter.Instance.Export(doc));
}
