namespace VellumText;

/// <summary>
/// Writes a document out in an interchange format.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of the importers, and deliberately a much smaller contract than they have. An
/// importer returns an <see cref="ImportResult"/> because it is handed a format far larger than
/// this document model and has to say what it threw away. An exporter starts from the model, so
/// there is nothing it can be surprised by and nothing it has to report: every construct the
/// model can hold, the exporter is required to write. That is why there is no diagnostics
/// channel here — one would only ever carry an exporter bug.
/// </para>
/// <para>
/// Exporters are stateless and must be safe to share, because the clipboard path holds one of
/// each for the lifetime of the control.
/// </para>
/// </remarks>
public interface IDocumentExporter : IDocumentFormat
{
    /// <summary>Writes <paramref name="doc"/> out to <paramref name="destination"/>.</summary>
    /// <param name="destination">Where to write the payload.</param>
    /// <param name="doc">The document.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>This is the member every format has to write, and the string overload is derived from
    /// it.</b> See <see cref="IDocumentImporter.Import(Stream)"/> for why bytes rather than text
    /// is the primitive.
    /// </para>
    /// <para>
    /// Writes and does not dispose or flush anything it did not create: the caller owns the
    /// stream and may be writing more into it afterwards. Everything written must be flushed
    /// through to <paramref name="destination"/> before returning, which for a
    /// <see cref="StreamWriter"/> means disposing the writer rather than leaving it to a
    /// finalizer.
    /// </para>
    /// <para>
    /// Must not throw for any document the schema allows, including an empty one. A document is
    /// exported at the moment a user presses copy, and a copy that throws loses the selection
    /// they were trying to keep.
    /// </para>
    /// <para>
    /// <b>Do not implement this by calling <see cref="Export(DocumentNode)"/>.</b> The default
    /// implementation of that overload calls this one, so a format that has no string
    /// implementation of its own and writes one in terms of the other recurses forever.
    /// </para>
    /// </remarks>
    void Export(Stream destination, DocumentNode doc);

    /// <summary>Writes <paramref name="doc"/> out.</summary>
    /// <param name="doc">The document.</param>
    /// <returns>The document in this exporter's format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// <see cref="IDocumentFormat.TextEncoding"/> is <see langword="null"/>, so this format has
    /// no text form.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Kept because HTML and RTF genuinely are text and a caller that wants a string should get
    /// one. A text format overrides this with its writer; the default writes through
    /// <see cref="Export(Stream, DocumentNode)"/> and decodes with
    /// <see cref="IDocumentFormat.TextEncoding"/>.
    /// </para>
    /// <para>
    /// This is the one member on either interface that throws, and only for a binary format,
    /// where there is no string to return and no honest empty value either — <c>""</c> would be
    /// a document silently lost. Check <see cref="IDocumentFormat.TextEncoding"/> first, or use
    /// the stream overload, which every format supports.
    /// </para>
    /// <para>
    /// Must not throw for any document the schema allows, including an empty one. A document is
    /// exported at the moment a user presses copy, and a copy that throws loses the selection
    /// they were trying to keep.
    /// </para>
    /// </remarks>
    string Export(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        if (TextEncoding is not { } encoding)
        {
            throw new NotSupportedException(
                $"The '{Format}' format is not text and has no string form. "
                    + "Write it to a stream instead.");
        }

        using var buffer = new MemoryStream();

        Export(buffer, doc);

        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
