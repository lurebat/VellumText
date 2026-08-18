using System.Text;

namespace VellumText;

/// <summary>
/// An interchange format the editor can move a document through — what
/// <see cref="IDocumentImporter"/> and <see cref="IDocumentExporter"/> have in common.
/// </summary>
/// <remarks>
/// This exists so that a format can be handed to the editor as one thing. Reading and writing are
/// separate contracts because a format can genuinely support one and not the other, but a caller
/// registering HTML support is not registering two unrelated objects, and should not have to say
/// so twice.
/// </remarks>
public interface IDocumentFormat
{
    /// <summary>A short stable name for the format, such as <c>html</c> or <c>rtf</c>.</summary>
    string Format { get; }

    /// <summary>
    /// The IANA media type, which is also what most clipboard backends want as a format name.
    /// </summary>
    string MediaType { get; }

    /// <summary>
    /// The encoding this format's payload is text in, or <see langword="null"/> when the payload
    /// is opaque bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place a format says whether it is text at all, and it is what makes the string
    /// members of <see cref="IDocumentImporter"/> and <see cref="IDocumentExporter"/> derivable
    /// from the stream ones rather than a second thing to implement. HTML and RTF are genuinely
    /// text and answer with an encoding; a container format — DOCX, ODT — answers
    /// <see langword="null"/>, and for those the string members are not meaningful and must not
    /// be called.
    /// </para>
    /// <para>
    /// It is not derivable from <see cref="MediaType"/>: RTF is <c>text/rtf</c> and is
    /// seven-bit by construction, so a multi-byte guess turns it into mojibake, and
    /// <c>CF_HTML</c>'s byte offsets are only correct in UTF-8. Both facts belong to the format
    /// and nowhere else.
    /// </para>
    /// </remarks>
    Encoding? TextEncoding { get; }
}
