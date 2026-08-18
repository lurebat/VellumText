using System.Text;

using VellumText.Interop.Html;

namespace VellumText.Interop.Rtf;

/// <summary>
/// Reads Rich Text Format, the shape formatted text arrives in when it is copied out of a
/// Windows application.
/// </summary>
/// <remarks>
/// <para>
/// RTF is converted to HTML by RtfPipe and then imported by <see cref="HtmlImporter"/>, rather
/// than being mapped onto the document model directly. RTF is a large, old, loosely observed
/// format — control words, destinations, code pages, font and colour tables, field instructions,
/// list overrides — and a reader for it written from scratch is a reader that is wrong in ways
/// that only show up on somebody else's document.
/// </para>
/// <para>
/// Going through HTML also means there is exactly one place that decides what a paste is allowed
/// to contain. A second importer with its own idea of which URLs are safe is a second importer to
/// get that wrong in.
/// </para>
/// <para>
/// <b>A tab in the source is dropped, deliberately (decided 2026-08-18).</b> RtfPipe renders one
/// as <c>&lt;span style="display:inline-block;width:48px"&gt;&lt;/span&gt;</c> — an empty spacer
/// element rather than a character — so it arrives here as empty content and goes. Recovering it
/// would mean recognising that library's private rendering choice, which changes without notice;
/// but the stronger reason is that this editor has no tab to recover it <em>into</em>. Tab is
/// bound to indent and outdent, as it is in a browser and in Word's lists, so there is no way to
/// type a tab character and no tab-stop model to lay one out against. Importing one would inject
/// a character the editor can never produce and can never round-trip. If tab stops are ever
/// added, revisit this — and target a space or an indent, not a tab character, until then.
/// </para>
/// <para>
/// Leading and trailing spaces go the same way, by ordinary HTML whitespace collapsing. An
/// interior run survives in <em>length</em> but not in kind: RtfPipe writes it as one space
/// followed by <c>&amp;nbsp;</c> entities, so four spaces arrive as a space and three U+00A0.
/// That matters to anything that searches, wraps or re-exports the text, because a non-breaking
/// space is not a space to any of them. Pinned by a test with the codepoints spelled out, since
/// the two are indistinguishable on screen — I misread this output as four ordinary spaces once
/// already.
/// </para>
/// </remarks>
public static class RtfImporter
{
    private static int _codePagesRegistered;

    /// <summary>Whether a string looks like RTF.</summary>
    /// <param name="text">The candidate.</param>
    /// <returns>Whether it opens with an RTF header.</returns>
    /// <remarks>
    /// Leading whitespace is tolerated because some applications add it. Nothing else is: the
    /// header is the only part of the format that is reliably where it should be.
    /// </remarks>
    public static bool IsRtf(string? text)
    {
        if (text is null)
        {
            return false;
        }

        var span = text.AsSpan().TrimStart();

        return span.StartsWith(@"{\rtf", StringComparison.Ordinal);
    }

    /// <summary>Reads an RTF document.</summary>
    /// <param name="rtf">The RTF payload.</param>
    /// <param name="options">How much to trust it, or null for the paranoid defaults.</param>
    /// <returns>The document, and everything that could not be brought across.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rtf"/> is null.</exception>
    /// <remarks>
    /// Never throws for bad input. What arrives on a clipboard is whatever the last application
    /// put there, including truncated payloads and things that are not RTF at all.
    /// </remarks>
    public static ImportResult Import(string rtf, HtmlImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rtf);

        if (!IsRtf(rtf))
        {
            return new ImportResult(
                DocumentNode.Empty,
                [
                    new ImportDiagnostic(
                        DiagnosticSeverity.Malformed,
                        "The text does not begin with an RTF header, so it was not read as RTF.",
                        "rtf"),
                ]);
        }

        EnsureCodePages();

        string html;

        try
        {
            html = RtfPipe.Rtf.ToHtml(rtf);
        }
        catch (Exception ex)
        {
            // RtfPipe recovers from most damage on its own, but it throws outright on a payload
            // whose header is right and whose body is not. That is a diagnostic, not a crash.
            return new ImportResult(
                DocumentNode.Empty,
                [
                    new ImportDiagnostic(
                        DiagnosticSeverity.Malformed,
                        $"The RTF could not be read: {ex.Message}",
                        ex.GetType().Name),
                ]);
        }

        return HtmlImporter.Import(html, options);
    }

    /// <summary>
    /// Makes the legacy code pages available, which RTF needs and .NET does not load by default.
    /// </summary>
    /// <remarks>
    /// RTF text outside <c>\u</c> escapes is bytes in a code page named by the header, and
    /// <see cref="Encoding.GetEncoding(int)"/> cannot find those without this provider. Without it
    /// every call fails in the encoding table's static constructor rather than anywhere that
    /// suggests what is wrong.
    /// </remarks>
    private static void EnsureCodePages()
    {
        if (Interlocked.Exchange(ref _codePagesRegistered, 1) == 1)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
