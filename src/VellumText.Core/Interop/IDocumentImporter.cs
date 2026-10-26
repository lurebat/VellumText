namespace VellumText;

/// <summary>
/// Reads a document in from an interchange format.
/// </summary>
/// <remarks>
/// <para>
/// The importers themselves are static classes, because importing is a pure function and nothing
/// about it wants an object. This interface exists for the one caller that cannot name them: the
/// editor control, which must not reference the interop packages — a consumer who wants a plain
/// editor should not acquire an HTML parser and an RTF reader to get one. Registering a format
/// with the control is how those packages get in, and this is the shape they get in as.
/// </para>
/// <para>
/// Importers are stateless and must be safe to share, because the clipboard path holds one of
/// each for the lifetime of the control.
/// </para>
/// </remarks>
public interface IDocumentImporter : IDocumentFormat
{
    /// <summary>Reads a document out of <paramref name="source"/>.</summary>
    /// <param name="source">The payload, positioned at its first byte.</param>
    /// <param name="cancellationToken">
    /// Abandons the import. Cancellation is the one way the returned task may fault.
    /// </param>
    /// <returns>What was understood, and what had to be given up to understand it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This is the member every format has to write, and the string overload is derived from
    /// it.</b> A format is not required to be text — DOCX is a ZIP container and ODT another, and
    /// a contract that only spoke in strings could not express either. Reversing the two so that
    /// bytes are the primitive costs the text formats one line each and stops the model from
    /// excluding a whole class of format it is otherwise perfectly able to hold.
    /// </para>
    /// <para>
    /// Reads to the end of the stream and does not dispose it: the caller owns it, and the same
    /// stream may carry more than one thing. An implementation must not seek — a clipboard or
    /// network stream cannot.
    /// </para>
    /// <para>
    /// Must not throw, or fault the task, for any input at all, including input that is not this
    /// format; cancellation is the only exception. A paste is handed whatever another application
    /// put on the clipboard, and an application that dies on a malformed paste is worse than one
    /// that pastes nothing.
    /// </para>
    /// <para>
    /// <b>Do not implement this by calling
    /// <see cref="ImportAsync(string, CancellationToken)"/>.</b> The default implementation of that
    /// overload calls this one, so a format that has no string implementation of its own and
    /// writes one in terms of the other recurses forever. Text formats implement both, with each
    /// calling the underlying parser directly.
    /// </para>
    /// </remarks>
    Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default);

    /// <summary>Reads <paramref name="text"/> in.</summary>
    /// <param name="text">The document in this importer's format.</param>
    /// <param name="cancellationToken">
    /// Abandons the import. Cancellation is the one way the returned task may fault.
    /// </param>
    /// <returns>What was understood, and what had to be given up to understand it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Kept because HTML and RTF genuinely are text, and forcing a caller who has a string to
    /// wrap it in a stream to hand it over would be pure ceremony. A text format overrides this
    /// with its parser; the default encodes through <see cref="IDocumentFormat.TextEncoding"/>
    /// and defers to <see cref="ImportAsync(Stream, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// Meaningful only when <see cref="IDocumentFormat.TextEncoding"/> is not
    /// <see langword="null"/>. A binary format returns an empty result carrying a
    /// <see cref="DiagnosticSeverity.Malformed"/> diagnostic saying so, because an importer that
    /// throws is exactly what <see cref="ImportResult"/> exists to avoid.
    /// </para>
    /// <para>
    /// Must not throw, or fault the task, for any input at all, including input that is not this
    /// format; cancellation is the only exception.
    /// </para>
    /// </remarks>
    async Task<ImportResult> ImportAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (TextEncoding is not { } encoding)
        {
            return new ImportResult(
                DocumentNode.Empty,
                [
                    new ImportDiagnostic(
                        DiagnosticSeverity.Malformed,
                        $"The '{Format}' format is not text and cannot be read from a string. "
                            + "Read it from a stream instead.",
                        Format),
                ]);
        }

        using var source = new MemoryStream(encoding.GetBytes(text), writable: false);

        return await ImportAsync(source, cancellationToken).ConfigureAwait(false);
    }
}