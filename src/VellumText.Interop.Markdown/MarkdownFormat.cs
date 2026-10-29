using System.Text;

namespace VellumText.Interop.Markdown;

/// <summary>
/// Markdown as a clipboard flavour: the importer and the exporter behind one registrable object.
/// </summary>
/// <remarks>
/// <para>
/// Handing this to <c>RichTextView.Formats</c> is what makes copy write Markdown and paste read it.
/// The control cannot reach <see cref="MarkdownImporter"/> or <see cref="MarkdownExporter"/> itself
/// without dragging a Markdown parser into every application that only wanted an editor.
/// </para>
/// <para>
/// <see cref="Instance"/> imports with <see cref="MarkdownImportOptions.Default"/>. Use
/// <see cref="With"/> to register a configured one — that is the only way
/// <see cref="MarkdownImportOptions.AllowRawHtml"/> and the rest reach the importer when the
/// control is doing the importing.
/// </para>
/// <para>
/// Exporting through this type discards the diagnostics, because <see cref="IDocumentExporter"/> has
/// nowhere to put them. A host that wants to tell the user what Markdown could not hold calls
/// <see cref="MarkdownExporter.ExportWithDiagnostics"/> directly.
/// </para>
/// </remarks>
public sealed class MarkdownFormat : IDocumentImporter, IDocumentExporter
{
    /// <summary>
    /// The shared default: import options are <see cref="MarkdownImportOptions.Default"/>.
    /// </summary>
    public static readonly MarkdownFormat Instance = new(MarkdownImportOptions.Default);

    private MarkdownFormat(MarkdownImportOptions importOptions) => ImportOptions = importOptions;

    /// <summary>How much of the incoming Markdown this instance trusts.</summary>
    public MarkdownImportOptions ImportOptions { get; }

    /// <summary>
    /// A format that imports with <paramref name="importOptions"/>, leaving this one unchanged.
    /// </summary>
    /// <param name="importOptions">The options the importer should use.</param>
    /// <returns>A new configured instance; register it in place of <see cref="Instance"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="importOptions"/> is null.</exception>
    /// <remarks>
    /// Registration is the seam a host is told to use, so an option that cannot travel through it is
    /// unreachable in practice — including
    /// <see cref="MarkdownImportOptions.AllowRawHtml"/>, which is a security control.
    /// </remarks>
    public MarkdownFormat With(MarkdownImportOptions importOptions)
    {
        ArgumentNullException.ThrowIfNull(importOptions);

        return new MarkdownFormat(importOptions);
    }

    /// <inheritdoc/>
    public string Format => "markdown";

    /// <inheritdoc/>
    public string MediaType => "text/markdown";

    /// <inheritdoc/>
    /// <remarks>
    /// UTF-8 without qualification. Markdown has no encoding declaration of its own, and every
    /// convention that has grown up around it — GitHub, the CommonMark reference implementations,
    /// every static site generator — assumes UTF-8.
    /// </remarks>
    public Encoding? TextEncoding => Encoding.UTF8;

    /// <inheritdoc/>
    public Task<ImportResult> ImportAsync(string text, CancellationToken cancellationToken) =>
        MarkdownImporter.ImportAsync(text, ImportOptions, cancellationToken);

    /// <inheritdoc/>
#pragma warning disable RS0027 // Ties with the string overload on purpose; see IDocumentImporter.
    public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
#pragma warning restore RS0027
    {
        var text = await DocumentPayload.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        return await MarkdownImporter.ImportAsync(text, ImportOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string Export(DocumentNode doc) => MarkdownExporter.Instance.Export(doc);

    /// <inheritdoc/>
    public void Export(Stream destination, DocumentNode doc) =>
        MarkdownExporter.Instance.Export(destination, doc);
}
