using System.Text;

namespace VellumText;

/// <summary>
/// Moving a text format's payload between a stream and a string, for the formats that are text.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IDocumentImporter.ImportAsync(Stream, CancellationToken)"/> and
/// <see cref="IDocumentExporter.Export(Stream, DocumentNode)"/> are what every format has to
/// implement, and for a text format the body is always the same two steps: decode the bytes, hand
/// the string to the parser. These are those two steps, in one place, because getting them subtly
/// wrong is easy and the symptom is a truncated or mojibake document rather than an exception.
/// </para>
/// <para>
/// Public rather than internal because the formats that need it — HTML, RTF, JSON — are three
/// separate assemblies that cannot see each other, and so is any format a host writes.
/// </para>
/// </remarks>
public static class DocumentPayload
{
    /// <summary>Reads a stream to its end as text.</summary>
    /// <param name="source">The payload, positioned at its first byte.</param>
    /// <param name="encoding">The encoding to decode with.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>Everything the stream held.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <remarks>
    /// The stream is left open, because the caller owns it and may be reading more from it. A byte
    /// order mark wins over <paramref name="encoding"/> when one is present, which is what lets a
    /// file written by another application be read back rather than arriving with three stray
    /// characters at the front.
    /// </remarks>
    public static async Task<string> ReadAllTextAsync(
        Stream source, Encoding encoding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(encoding);

        using var reader = new StreamReader(
            source, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes text to a stream.</summary>
    /// <param name="destination">Where to write.</param>
    /// <param name="encoding">The encoding to write in.</param>
    /// <param name="text">What to write.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// No byte order mark, and the stream is left open. A BOM would be wrong on every path this
    /// library writes: <c>CF_HTML</c>'s byte offsets are computed over a payload without one, and
    /// an RTF reader handed <c>EF BB BF</c> before the opening brace rejects the document. That is
    /// why the bytes are encoded directly rather than through a <see cref="StreamWriter"/>, which
    /// asks the encoding for a preamble and writes it.
    /// </remarks>
    public static void WriteAllText(Stream destination, Encoding encoding, string text)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(text);

        destination.Write(encoding.GetBytes(text));
    }
}
