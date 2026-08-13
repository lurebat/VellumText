using System.Text;
using Avalonia.Input;

namespace Vellum.Avalonia;

/// <summary>
/// The platform clipboard formats a rich document is written to and read from, and the encoding
/// each of them insists on.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a <em>bytes</em> format rather than a string format, and that is not an
/// implementation detail. Asking a clipboard backend for a native format as a string lets it
/// reinterpret the bytes with whatever encoding it guesses, which for RTF produces convincing
/// mojibake that then parses to nothing. Bytes in, deliberate decode after.
/// </para>
/// <para>
/// The format names are the platform's own, not media types: Windows wants <c>HTML Format</c> and
/// <c>Rich Text Format</c>, macOS wants uniform type identifiers, and everything else is
/// freedesktop, where the media type happens to be right.
/// </para>
/// <para>
/// What is <em>not</em> here is the shape of the payload itself. The <c>CF_HTML</c> preamble and
/// its byte offsets belong to the HTML format, which already knows how to write and read them and
/// which this assembly cannot see. This type decides which format and which encoding, and stops.
/// </para>
/// </remarks>
internal static class RichTextClipboardFormats
{
    /// <summary>The formats, in the order a paste should prefer them.</summary>
    /// <remarks>
    /// HTML first because it carries more than RTF does and because it is what a browser and an
    /// online editor put on the clipboard. RTF second because it is what word processors put
    /// there. Plain text is not here: it is the floor the control falls back to on its own.
    /// </remarks>
    public static readonly string[] Preference = ["html", "rtf"];

    private static readonly DataFormat<byte[]> HtmlFormat =
        Platform("HTML Format", "public.html", "text/html");

    private static readonly DataFormat<byte[]> RtfFormat =
        Platform("Rich Text Format", "public.rtf", "text/rtf");

    /// <summary>The clipboard format a short format name is carried in, if it has one.</summary>
    /// <param name="format">A short name, as on <see cref="IDocumentFormat.Format"/>.</param>
    /// <returns>The platform format, or <see langword="null"/> for one with no clipboard flavour.</returns>
    public static DataFormat<byte[]>? For(string format) => format switch
    {
        "html" => HtmlFormat,
        "rtf" => RtfFormat,
        _ => null,
    };

    /// <summary>The encoding a format is carried in on the clipboard.</summary>
    /// <param name="format">A short name, as on <see cref="IDocumentFormat.Format"/>.</param>
    /// <returns>The encoding.</returns>
    /// <remarks>
    /// <c>CF_HTML</c> is defined as UTF-8, and its byte offsets are only correct in it. RTF is a
    /// seven-bit format by construction — the exporter escapes everything else — so any
    /// single-byte encoding round-trips it. Latin1 is chosen over ASCII because it cannot
    /// substitute: if something non-ASCII ever did escape the exporter, the bytes would still be
    /// wrong rather than silently becoming question marks.
    /// </remarks>
    public static Encoding EncodingFor(string format) =>
        format == "html" ? Encoding.UTF8 : Encoding.Latin1;

    private static DataFormat<byte[]> Platform(string windows, string mac, string other) =>
        DataFormat.CreateBytesPlatformFormat(
            OperatingSystem.IsWindows() ? windows :
            OperatingSystem.IsMacOS() ? mac : other);
}
