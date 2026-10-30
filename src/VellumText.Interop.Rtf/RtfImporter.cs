using System.Text;

using OfficeIMO.Html;
using OfficeIMO.Rtf;
using OfficeIMO.Rtf.Diagnostics;
using OfficeIMO.Rtf.Syntax;

using VellumText.Interop.Html;

namespace VellumText.Interop.Rtf;

/// <summary>
/// Reads Rich Text Format, the shape formatted text arrives in when it is copied out of a
/// Windows application.
/// </summary>
/// <remarks>
/// <para>
/// RTF is read by OfficeIMO (<c>OfficeIMO.Rtf</c>, then <c>OfficeIMO.Html.Rtf</c> to HTML) and the
/// HTML is imported by <see cref="HtmlImporter"/>, rather than being mapped onto the document model
/// directly. RTF is a large, old, loosely observed format — control words, destinations, code
/// pages, font and colour tables, field instructions, list overrides — and a reader for it written
/// from scratch is a reader that is wrong in ways that only show up on somebody else's document.
/// The reader replaced RtfPipe on 2026-10-27; why, and the risks taken with it, are in
/// architecture §5.4.
/// </para>
/// <para>
/// Going through HTML also means there is exactly one place that decides what a paste is allowed
/// to contain. A second importer with its own idea of which URLs are safe is a second importer to
/// get that wrong in — which is why the reader keeps every link target and leaves the judging to
/// the HTML stage.
/// </para>
/// <para>
/// <b>A tab in the source is dropped, deliberately (decided 2026-08-18).</b> This editor has no
/// tab to recover it <em>into</em>: Tab is bound to indent and outdent, as it is in a browser and
/// in Word's lists, so there is no way to type a tab character and no tab-stop model to lay one
/// out against. Importing one would inject a character the editor can never produce and can never
/// round-trip. If tab stops are ever added, revisit this — and target a space or an indent, not a
/// tab character, until then.
/// </para>
/// <para>
/// <b>An interior run of spaces keeps its length but not its kind:</b> four spaces arrive as a
/// space and three U+00A0, as they did through RtfPipe. The reader writes them as plain spaces,
/// which HTML would collapse to one, so the HTML stage is told to keep the run (an internal option
/// of <c>HtmlImportOptions</c>). That matters to anything that searches, wraps or re-exports the
/// text, because a non-breaking space is not a space to any of them. Leading and trailing spaces
/// go, by ordinary whitespace collapsing.
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
    /// <param name="cancellationToken">
    /// Abandons the wait. The parse itself cannot be stopped part-way; its result is discarded.
    /// </param>
    /// <returns>The document, and everything that could not be brought across.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rtf"/> is null.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled — the only way the task faults.
    /// </exception>
    /// <remarks>
    /// Never faults for bad input. What arrives on a clipboard is whatever the last application
    /// put there, including truncated payloads and things that are not RTF at all.
    /// </remarks>
    public static Task<ImportResult> ImportAsync(
        string rtf, RtfImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rtf);

        if (!IsRtf(rtf))
        {
            return Task.FromResult(new ImportResult(
                DocumentNode.Empty,
                [
                    new ImportDiagnostic(
                        DiagnosticSeverity.Malformed,
                        "The text does not begin with an RTF header, so it was not read as RTF.",
                        "rtf"),
                ]));
        }

        EnsureCodePages();

        // The reader and the HTML stage after it both walk what the input nested; see LargeStack.
        return LargeStack.RunAsync(
            () => ImportOnLargeStack(rtf, options ?? RtfImportOptions.Default),
            cancellationToken);
    }

    private static ImportResult ImportOnLargeStack(string rtf, RtfImportOptions options)
    {
        RtfReadResult read;
        string? encapsulated;
        string html;
        IReadOnlyList<HtmlRtfConversionDiagnostic> converted = [];
        var pictures = new InlinePictures();

        try
        {
            read = RtfDocument.ParseResult(rtf, ReadOptions(options), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The reader is bounded and meant to recover from damage on its own; anything it still
            // throws is a diagnostic, not a crash.
            return Unreadable(ex);
        }

        // The importer contract: no input makes an import throw. Finding the encapsulated HTML and
        // converting the model both run the reader's code over whatever the document holds, so a
        // failure in either is the document's, and becomes a diagnostic rather than a crash.
        // ast-grep-ignore: broad-catch
        try
        {
            encapsulated = Encapsulated(read.Document);

            if (encapsulated is null)
            {
                (html, converted) = ToHtml(read.Document, hoist: true, options, pictures);
            }
            else
            {
                html = encapsulated;
            }
        }
        catch (Exception ex)
        {
            return Unreadable(ex);
        }

        var result = HtmlImporter.ImportOnCurrentStack(
            html, encapsulated is null ? options.ToHtmlOptions() with { Pictures = pictures } : options.ToEncapsulatedHtmlOptions());

        return new ImportResult(
            result.Doc, [.. ReaderDiagnostics(read), .. ConverterDiagnostics(converted, options), .. result.Diagnostics]);
    }

    /// <summary>The HTML the reader makes of <paramref name="rtf"/>, before the HTML stage.</summary>
    /// <remarks>For tests and the benchmark, which measure what the HTML stage is given.</remarks>
    /// <param name="rtf">The RTF.</param>
    /// <param name="hoist">
    /// Whether the shared font is hoisted (<see cref="RtfFormattingHoist"/>); false only to test that
    /// hoisting changes nothing.
    /// </param>
    internal static string ReadToHtml(string rtf, bool hoist = true)
    {
        var document = RtfDocument.ParseResult(rtf, ReadOptions(RtfImportOptions.Default), CancellationToken.None).Document;

        return Encapsulated(document) ?? ToHtml(document, hoist, RtfImportOptions.Default, pictures: null).Html;
    }

    /// <summary>
    /// The HTML Outlook encapsulated in the RTF (<c>\fromhtml1</c>), or null when there is none.
    /// </summary>
    /// <remarks>
    /// It goes to the HTML stage as it is, not through the converter, which reads it back into
    /// RTF first: the converter refuses HTML of more than 10,000 nodes, so a 40 KB message came
    /// out empty (rubber-duck review finding 9.3), and no paste is capped by its total size. The
    /// HTML stage has its own limits on depth and work. Asked before anything edits the model —
    /// the list-shadow correction and the hoist both do — since an edit makes the encapsulation
    /// stale, and the converter would then write the plain-text fallback beside it instead
    /// (finding 9.4).
    /// </remarks>
    private static string? Encapsulated(RtfDocument document) =>
        document.IsHtmlEncapsulationCurrent && document.HtmlEncapsulation is { Html: { } html } && !string.IsNullOrWhiteSpace(html)
            ? html
            : null;

    /// <summary>
    /// The reader's HTML for a document, with its shared font written once, and what the
    /// converter reported writing it.
    /// </summary>
    private static (string Html, IReadOnlyList<HtmlRtfConversionDiagnostic> Diagnostics) ToHtml(
        RtfDocument document, bool hoist, RtfImportOptions options, InlinePictures? pictures)
    {
        RtfLegacyListShadow.Drop(document);

        var css = hoist ? RtfFormattingHoist.Hoist(document) : null;
        var result = document.ToHtmlResult(HtmlOptions(options, pictures));

        return (RtfFormattingHoist.Wrap(result.RequireValue(), css), result.RtfDiagnostics);
    }
    /// <summary>What the reader may do, given what the host trusts.</summary>
    /// <remarks>
    /// <para>
    /// The reader's own budgets on whole-document size (characters, tokens, groups, blocks, total
    /// image bytes) are lifted: the library caps no paste by its total size, because a large paste
    /// is not a dangerous one (architecture §7), and an over-budget document would otherwise be
    /// refused whole. So is the reader's per-image limit, which also refuses the whole document:
    /// the one image over <see cref="RtfImportOptions.MaxDataImageBytes"/> is dropped by the
    /// converter instead (<see cref="HtmlOptions"/>), and the paste kept.
    /// </para>
    /// <para>
    /// What stays is the reader's group-depth limit, which bounds its recursion; content nested
    /// deeper is dropped with a diagnostic. Embedded objects and file references stay unread.
    /// </para>
    /// </remarks>
    private static RtfReadOptions ReadOptions(RtfImportOptions options) => new()
    {
        MaxInputBytes = null,
        MaxInputCharacters = null,
        MaxTokenCount = null,
        MaxGroupCount = null,
        MaxTextCharacters = null,
        MaxBinaryBytesPerPayload = null,
        MaxTotalBinaryBytes = null,
        MaxImageCount = null,
        MaxImageBytesPerImage = null,
        MaxTotalImageBytes = null,
        MaxSemanticBlockCount = null,
        ReadEmbeddedObjects = false,
        ReadFileReferences = false,

        // Every link target is kept for the HTML stage to judge, which is the one place that
        // decides; the reader's web-and-mail policy dropped a relative link before a host's base
        // URI could resolve it. Nothing is fetched either way.
        HyperlinkPolicy = RtfHyperlinkReadPolicy.AllowAll,
        WarnOnUnsupportedDestinations = true,
    };

    /// <summary>A self-contained HTML fragment, images inline, for the HTML stage to judge.</summary>
    /// <remarks>
    /// <para>
    /// A picture the host's options refuse is not embedded at all. Written out as a data URL only
    /// for the HTML stage to drop, a picture cost about 12.6 times the RTF that carried it — the
    /// base64, the markup around it, and the sanitizer's pass over both (rubber-duck review
    /// finding 9.1). The converter compares the decoded bytes with the limit, as the HTML stage
    /// does, and its findings are reported as the HTML stage would report them
    /// (<see cref="ConverterDiagnostics"/>). The HTML stage still checks what is embedded.
    /// </para>
    /// <para>
    /// Nothing the converter adds for display (default styles, metadata) is asked for.
    /// </para>
    /// <para>
    /// A picture that will be kept — one the converter embeds (PNG, JPEG), within the host's limit —
    /// is not written out at all: the converter is given a token for it (<see cref="InlinePictures"/>),
    /// and the HTML stage takes the picture from its bytes. As a data URL it went through the input
    /// guard, the parser and the DOM, about 17 times the RTF as a string for a 4 MiB picture. Any
    /// other picture gets no token, and the converter handles it as it always has.
    /// </para>
    /// </remarks>
    private static RtfToHtmlOptions HtmlOptions(RtfImportOptions options, InlinePictures? pictures) => new()
    {
        FragmentOnly = true,
        EmbedImagesAsDataUri = options.AllowDataImages,
        MaxEmbeddedImageBytes = options.MaxDataImageBytes,
        IncludeDefaultStyles = false,
        IncludeMetadata = false,
        IncludeRoundTripMetadata = false,
        ImageSourceResolver = pictures is null ? null! : image => Token(image, options, pictures)!,
    };

    /// <summary>The token for a picture that will be kept, or null to leave it to the converter.</summary>
    private static string? Token(RtfImage image, RtfImportOptions options, InlinePictures pictures)
    {
        var mediaType = image.Format switch
        {
            RtfImageFormat.Png => "image/png",
            RtfImageFormat.Jpeg => "image/jpeg",
            _ => null,
        };

        return mediaType is null
            || !options.AllowDataImages
            || image.Data is not { Length: > 0 } bytes
            || bytes.Length > options.MaxDataImageBytes
            ? null
            : pictures.Add(mediaType, bytes);
    }

    /// <summary>The converter's findings that cost the document a picture, each reported once.</summary>
    /// <remarks>
    /// The converter embeds PNG and JPEG only, and leaves out any other picture, a Windows
    /// metafile for one, which the reader binds without complaint (rubber-duck review finding 9.6).
    /// A picture the host's options refuse (<see cref="HtmlOptions"/>) is reported in the HTML
    /// stage's words, since that is where it was reported before the converter left it out.
    /// </remarks>
    private static IEnumerable<ImportDiagnostic> ConverterDiagnostics(
        IReadOnlyList<HtmlRtfConversionDiagnostic> diagnostics, RtfImportOptions options)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Code == "RtfHtmlImageEmbeddingDisabled")
            {
                if (reported.Add(diagnostic.Code))
                {
                    yield return new ImportDiagnostic(DiagnosticSeverity.Dropped, "Removed an inline image.", "data:");
                }

                continue;
            }

            if (diagnostic.Code == "RtfHtmlImageEmbeddingLimitExceeded")
            {
                if (reported.Add(diagnostic.Code))
                {
                    yield return new ImportDiagnostic(
                        DiagnosticSeverity.Dropped,
                        $"Removed an inline image larger than {options.MaxDataImageBytes} bytes.",
                        "data:");
                }

                continue;
            }

            if (diagnostic.Code != "RtfHtmlImageFormatUnsupported" || !reported.Add(diagnostic.Source ?? string.Empty))
            {
                continue;
            }

            var format = diagnostic.Source switch
            {
                nameof(RtfImageFormat.Wmf) => "a Windows metafile",
                nameof(RtfImageFormat.Emf) => "an enhanced metafile",
                nameof(RtfImageFormat.Dib) => "a device-independent bitmap",
                _ => "a format it does not recognise",
            };

            yield return new ImportDiagnostic(
                DiagnosticSeverity.Dropped,
                $"Removed a picture stored as {format}, which this reader cannot convert.",
                "pict");
        }
    }

    /// <summary>The fallback skip count above which a <c>\uc</c> is reported.</summary>
    /// <remarks>
    /// Real writers use 0, 1 or 2. A larger count is legal, and the reader honours it as the
    /// specification says — skipping that many characters after every <c>\u</c> to the end of the
    /// group — so 32 bytes of <c>\uc2147483647</c> silently swallow the rest of a group (F-30).
    /// </remarks>
    private const int UnusualSkipCount = 16;

    /// <summary>The reader's findings that cost the document something, as import diagnostics.</summary>
    private static IEnumerable<ImportDiagnostic> ReaderDiagnostics(RtfReadResult read)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var diagnostic in read.Diagnostics)
        {
            if (diagnostic.Code == "RTF101" && diagnostic.Message.Contains("'shppict'", StringComparison.Ordinal))
            {
                // Word stores a picture twice: as a shape (\shppict) and as a fallback metafile
                // (\nonshppict). The reader binds neither, so the picture is gone.
                if (reported.Add("shppict"))
                {
                    yield return new ImportDiagnostic(
                        DiagnosticSeverity.Dropped,
                        "Removed a picture stored as a Word shape, which this reader cannot read.",
                        "shppict");
                }
            }
            else if (diagnostic.Severity == RtfDiagnosticSeverity.Error && reported.Add(diagnostic.Code))
            {
                yield return new ImportDiagnostic(
                    DiagnosticSeverity.Downgraded,
                    $"Part of the RTF could not be read and was left out: {diagnostic.Message}",
                    diagnostic.Code);
            }
        }

        var (unusualSkip, unpairedSurrogate) = Scan(read.SyntaxTree);

        if (unusualSkip)
        {
            yield return new ImportDiagnostic(
                DiagnosticSeverity.Downgraded,
                $"Text was skipped because the RTF declared more than {UnusualSkipCount} fallback "
                + "characters after each Unicode escape (\\uc).",
                "uc");
        }

        if (unpairedSurrogate)
        {
            // The reader replaces the half with U+FFFD itself, so the HTML stage, which used to
            // report this, never sees it.
            yield return new ImportDiagnostic(
                DiagnosticSeverity.Downgraded,
                "A Unicode escape named half of a character, which was replaced with U+FFFD.",
                "unpaired-surrogate");
        }
    }

    /// <summary>
    /// Whether any <c>\uc</c> declares an unusual skip count, and whether any <c>\u</c> names half
    /// of a surrogate pair that the escape next to it does not complete.
    /// </summary>
    /// <remarks>
    /// Walked in document order with an explicit stack: the tree is as deep as the input made it,
    /// and a pair split across a group boundary (<c>\u-10179?{\b\u-8704?}</c>) is still a pair.
    /// </remarks>
    private static (bool UnusualSkip, bool UnpairedSurrogate) Scan(RtfSyntaxTree tree)
    {
        var unusualSkip = false;
        var unpaired = false;
        var highPending = false;
        var pending = new Stack<(RtfGroup Group, int Next)>();

        pending.Push((tree.Root, 0));

        while (pending.TryPop(out var frame))
        {
            var (group, next) = frame;

            while (next < group.Children.Count)
            {
                var node = group.Children[next++];

                if (node is RtfGroup child)
                {
                    pending.Push((group, next));
                    pending.Push((child, 0));
                    break;
                }

                if (node is not RtfControlWord { Parameter: { } parameter } control)
                {
                    continue;
                }

                if (control.Name == "uc" && parameter > UnusualSkipCount)
                {
                    unusualSkip = true;
                }
                else if (control.Name == "u")
                {
                    var unit = (char)(parameter < 0 ? parameter + 65536 : parameter);

                    unpaired |= highPending != char.IsLowSurrogate(unit);
                    highPending = char.IsHighSurrogate(unit);
                }
            }
        }

        return (unusualSkip, unpaired || highPending);
    }
    private static ImportResult Unreadable(Exception ex) => new(
        DocumentNode.Empty,
        [
            new ImportDiagnostic(
                DiagnosticSeverity.Malformed,
                $"The RTF could not be read: {ex.Message}",
                ex.GetType().Name),
        ]);
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
