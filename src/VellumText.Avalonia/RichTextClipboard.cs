using System.Collections.Concurrent;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace VellumText.Avalonia;

/// <summary>
/// One clipboard flavour a rich document travels in: the platform format it is published under.
/// </summary>
/// <param name="Format">The short name, as on <see cref="IDocumentFormat.Format"/>.</param>
/// <param name="DataFormat">The platform format to read and write it under.</param>
/// <remarks>
/// A flavour is the platform slot and nothing else. How the payload's bytes are made is the
/// format's business, not the clipboard's: <see cref="IDocumentExporter.Export(Stream, DocumentNode)"/>
/// writes them and <see cref="IDocumentImporter.ImportAsync(Stream, CancellationToken)"/> reads them, so a format that is
/// not text at all — a container like DOCX — travels here exactly as HTML does. This used to carry
/// an <see cref="Encoding"/> as well, which forced every payload through a string and made a
/// binary flavour impossible to express.
/// </remarks>
public sealed record ClipboardFlavor(string Format, DataFormat<byte[]> DataFormat);

/// <summary>
/// Reading and writing rich documents on the system clipboard, in the formats other applications
/// publish them in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every flavour here is a bytes format, and that is the whole point of this type.</b> Asking a
/// clipboard backend for a native format <em>as a string</em> lets it reinterpret the bytes with
/// whatever encoding it guesses. For RTF that guess produces convincing mojibake which then parses
/// to an empty document — a failure that looks like an unsupported payload rather than a decoding
/// bug, and costs an afternoon to find. So there is deliberately no overload anywhere on this type
/// that takes a clipboard payload as a <see cref="string"/>: bytes in, deliberate decode after.
/// </para>
/// <para>
/// <see cref="ReadAsync(IClipboard, IEnumerable{IDocumentImporter})"/> and <see cref="WriteAsync"/>
/// are the whole API for an ordinary
/// application, and neither exposes a byte. <see cref="DecodeAsync"/> and <see cref="Encode"/> exist
/// for applications that hold the payload themselves — a clipboard manager storing what it
/// captured, to parse later or never — and <see cref="Flavors"/> gives them the platform formats
/// to capture under, including for formats they do not intend to parse at all.
/// </para>
/// <para>
/// This assembly cannot see the interop packages, by design: an application that wants a text
/// editor should not acquire an HTML parser to get one. So the importers and exporters are passed
/// in. <c>HtmlFormat.Instance</c> and <c>RtfFormat.Instance</c> are what you pass.
/// </para>
/// </remarks>
public static class RichTextClipboard
{
    /// <summary>The formats, in the order a paste should prefer them.</summary>
    /// <remarks>
    /// <para>
    /// HTML first because it carries more than RTF does and because it is what a browser and an
    /// online editor put on the clipboard. RTF second because it is what word processors put
    /// there. Plain text is not here: it is the floor that is fallen back to on its own.
    /// </para>
    /// <para>
    /// A registered format that is not named here still travels — it simply sorts last. Ranking
    /// JSON above HTML was tried and reverted: it would only be an improvement if it carried
    /// more, and the "As built" note in §4.9 records that HTML already carries everything the
    /// model can hold. The test written to justify it passed with JSON taken out of the list,
    /// which is what settled the question.
    /// </para>
    /// </remarks>
    private static readonly string[] PreferenceOrder = ["html", "rtf"];

    /// <summary>The formats, in the order a paste should prefer them.</summary>
    public static IReadOnlyList<string> Preference { get; } = PreferenceOrder;

    private static readonly ClipboardFlavor HtmlFlavor = new(
        "html", Platform("HTML Format", "public.html", "text/html"));

    private static readonly ClipboardFlavor RtfFlavor = new(
        "rtf", Platform("Rich Text Format", "public.rtf", "text/rtf"));

    /// <summary>Every flavour a document can travel in, richest first.</summary>
    public static IReadOnlyList<ClipboardFlavor> Flavors { get; } = [HtmlFlavor, RtfFlavor];

    /// <summary>The flavour a short format name travels in.</summary>
    /// <param name="format">A short name, as on <see cref="IDocumentFormat.Format"/>.</param>
    /// <returns>The flavour the format travels in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="format"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// HTML and RTF are named explicitly because their clipboard identifiers are platform
    /// conventions rather than anything derivable — <c>HTML Format</c> on Windows,
    /// <c>public.html</c> on macOS, <c>text/html</c> elsewhere — and naming one wrong means no
    /// other application sees the data at all.
    /// </para>
    /// <para>
    /// Anything else registered gets an application-specific format named after it. It used to
    /// get <see langword="null"/>, which meant <c>Formats.Add(JsonFormat.Instance)</c> was
    /// accepted, validated, and then quietly did nothing on copy or paste. A seam that ignores a
    /// registration is worse than one that refuses it, because nothing tells the caller. An
    /// application format is invisible to other programs, which is exactly right: these are
    /// formats only this model understands.
    /// </para>
    /// </remarks>
    public static ClipboardFlavor FlavorFor(string format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return format switch
        {
            "html" => HtmlFlavor,
            "rtf" => RtfFlavor,
            _ => ApplicationFlavors.GetOrAdd(
                format,
                name => new ClipboardFlavor(
                    name,
                    DataFormat.CreateBytesApplicationFormat($"VellumText-{name}"))),
        };
    }

    /// <summary>Flavours minted for registered formats with no platform convention of their own.</summary>
    /// <remarks>
    /// Cached because a <see cref="DataFormat"/> is matched by equality when a payload is read
    /// back, and a fresh instance per call would fail to match the one it was written under.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, ClipboardFlavor> ApplicationFlavors =
        new(StringComparer.Ordinal);

    /// <summary>Turns a document into the bytes its format travels in on the clipboard.</summary>
    /// <param name="exporter">The exporter to write with.</param>
    /// <param name="doc">The document to write.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The exporter writes its own bytes, so nothing here has to know or guess how the format is
    /// encoded — which is what makes the wrong encoding unreachable, and what lets a format that
    /// is not text at all travel on the clipboard unchanged.
    /// </remarks>
    public static byte[] Encode(IDocumentExporter exporter, DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(doc);

        using var buffer = new MemoryStream();

        exporter.Export(buffer, doc);

        return buffer.ToArray();
    }

    /// <summary>Reads a clipboard payload back into a document, off the calling thread.</summary>
    /// <param name="importer">The importer to read with.</param>
    /// <param name="payload">The bytes exactly as they came off the clipboard.</param>
    /// <param name="cancellationToken">Abandons the import.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="importer"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is the method a clipboard manager wants: it stored the bytes when the capture
    /// happened, and parses them later or never.
    /// </para>
    /// <para>
    /// The importer always runs on a background thread, whatever its own implementation does —
    /// see <see cref="IDocumentImporter"/>. There is no overload taking the payload as a
    /// <see cref="string"/>, deliberately. Handing the bytes to the importer is this method's job
    /// precisely because decoding them at the call site is the mistake this type exists to
    /// prevent.
    /// </para>
    /// </remarks>
    public static Task<ImportResult> DecodeAsync(
        IDocumentImporter importer,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importer);

        return ImportOnBackgroundAsync(importer, payload.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Runs an importer on a thread-pool thread, never the caller's — the one way the editor and
    /// this type call an importer.
    /// </summary>
    /// <remarks>
    /// The hop is unconditional. An importer that is genuinely asynchronous would release the
    /// caller at its first await anyway, but one that does its work before that await — every
    /// synchronous parser wrapped in a task does — would run it on the UI thread and freeze it.
    /// The payload has already been read on the caller's thread; only decoding moves.
    /// </remarks>
    internal static Task<ImportResult> ImportOnBackgroundAsync(
        IDocumentImporter importer, byte[] payload, CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                using var source = new MemoryStream(payload, writable: false);

                return await importer.ImportAsync(source, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>
    /// The clipboard payload for a document: plain text, plus a flavour for each exporter that has
    /// one.
    /// </summary>
    /// <param name="doc">The document to write.</param>
    /// <param name="exporters">The formats to write beyond plain text.</param>
    /// <param name="text">
    /// The plain text flavour, or <see langword="null"/> to take it from <paramref name="doc"/>.
    /// </param>
    /// <returns>The payload, ready for <see cref="IClipboard.SetDataAsync"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="doc"/> or <paramref name="exporters"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// An exporter that throws costs its own flavour and nothing else. Exporters are contractually
    /// forbidden from throwing, so reaching that is a bug in one — but losing the whole copy over
    /// it would be a worse answer than losing one of its flavours.
    /// </remarks>
    public static DataTransfer Build(
        DocumentNode doc, IEnumerable<IDocumentExporter> exporters, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(exporters);

        var item = new DataTransferItem();

        item.SetText(text ?? DocumentText.Of(doc));

        foreach (var exporter in exporters)
        {
            try
            {
                item.Set(FlavorFor(exporter.Format).DataFormat, Encode(exporter, doc));
            }
            catch (Exception)
            {
            }
        }

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    /// <summary>Puts a document on the clipboard in every format that has a flavour.</summary>
    /// <param name="clipboard">The clipboard to write to.</param>
    /// <param name="doc">The document to write.</param>
    /// <param name="exporters">The formats to write beyond plain text.</param>
    /// <param name="text">
    /// The plain text flavour, or <see langword="null"/> to take it from <paramref name="doc"/>.
    /// </param>
    /// <returns><see langword="true"/> if the write completed.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="clipboard"/>, <paramref name="doc"/> or <paramref name="exporters"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <paramref name="text"/> lets a caller keep its own authority over the plain text flavour:
    /// a control already knows what it shows selected, and a rectangle of table cells is copied as
    /// a rebuilt document rather than a cut of the original. The two agree today; the parameter is
    /// what keeps the clipboard's text the control's answer rather than a re-derivation of it.
    /// </remarks>
    public static async Task<bool> WriteAsync(
        IClipboard clipboard,
        DocumentNode doc,
        IEnumerable<IDocumentExporter> exporters,
        string? text = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(exporters);

        try
        {
            await clipboard.SetDataAsync(Build(doc, exporters, text)).ConfigureAwait(true);

            return true;
        }
        catch (Exception)
        {
            // The clipboard belongs to the whole desktop and can fail for reasons that are nobody's
            // business here - another process holding it open is the usual one.
            return false;
        }
    }

    /// <summary>Reads the clipboard's richest readable flavour as a document.</summary>
    /// <param name="clipboard">The clipboard to read.</param>
    /// <param name="importers">The formats to try, in any order.</param>
    /// <returns>
    /// The result, or <see langword="null"/> when the clipboard holds nothing any of them could
    /// read. Plain text counts: it arrives as a document of one paragraph per line.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The importers are tried in <see cref="Preference"/> order rather than the order given,
    /// because the order is the whole behaviour: copying from a word processor puts RTF, HTML and
    /// plain text on the clipboard at once, and taking whichever answers first would silently keep
    /// the poorest of the three.
    /// </remarks>
    public static async Task<ImportResult?> ReadAsync(
        IClipboard clipboard, IEnumerable<IDocumentImporter> importers)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(importers);

        try
        {
            using var data = await clipboard.TryGetDataAsync().ConfigureAwait(true);

            return data is null ? null : await ReadAsync(data, importers).ConfigureAwait(true);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Reads the richest readable flavour of a transfer as a document.</summary>
    /// <param name="data">The transfer, from a clipboard or a drop.</param>
    /// <param name="importers">The formats to try, in any order.</param>
    /// <returns>
    /// The result, or <see langword="null"/> when the transfer holds nothing any of them could
    /// read. Plain text counts: it arrives as a document of one paragraph per line.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// This overload is the one a drop handler wants, since a drop hands over a transfer and never
    /// touches the clipboard. It is also the only one that can be tested: <see cref="IClipboard"/>
    /// cannot be implemented outside Avalonia, and a headless window does not have one.
    /// </remarks>
    public static async Task<ImportResult?> ReadAsync(
        IAsyncDataTransfer data, IEnumerable<IDocumentImporter> importers)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(importers);

        try
        {
            ImportResult? empty = null;

            foreach (var importer in InPreferenceOrder(importers))
            {
                // Bytes, deliberately, and never as a string. See the type's remarks.
                if (await data.TryGetValueAsync(FlavorFor(importer.Format).DataFormat).ConfigureAwait(true)
                    is not { Length: > 0 } bytes)
                {
                    continue;
                }

                var result = await ImportOnBackgroundAsync(importer, bytes, CancellationToken.None)
                    .ConfigureAwait(true);

                // Not Doc.Blocks.IsEmpty: a document always has a block, so that kept a flavour
                // that imported to one empty paragraph and never read the rest (rubber-duck
                // review 6.3). The empty one is kept in case nothing else is there, since a
                // payload the sanitizer emptied is not the same as an empty clipboard.
                if (!result.IsEmpty)
                {
                    return result;
                }

                empty ??= result;
            }

            return FromPlainText(await data.TryGetTextAsync().ConfigureAwait(true)) ?? empty;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Decides what a paste or a drop inserts: the one rule both of them follow.
    /// </summary>
    /// <param name="source">The transfer's flavours.</param>
    /// <param name="importers">The formats to try, in any order.</param>
    /// <param name="cancellationToken">Abandons the imports.</param>
    /// <returns>What to insert.</returns>
    /// <remarks>
    /// <para>
    /// Rich formats first, richest first, and the first that imports to anything wins — except
    /// that an image on the transfer wins over a rich import that lost an image to the sanitizer,
    /// or over none at all (owner decision OD-4b). A browser's "Copy image" puts the bitmap and an
    /// <c>&lt;img&gt;</c> pointing at the network on the clipboard; the sanitizer removes the
    /// remote image, and taking the rich flavour anyway pasted nothing while reporting success.
    /// An image is not preferred over a rich import that lost nothing: a spreadsheet copies a
    /// picture of its cells as well as the table, and the table is what was meant.
    /// </para>
    /// <para>
    /// Plain text comes last and is returned as text rather than as a document, so that a paste
    /// and a drop both put it through the same seam and it lands with the formatting of where it
    /// lands. Paste and drop used to resolve the same transfer differently: a drop took the image
    /// first and built text into body paragraphs, so the same clip landed differently by gesture.
    /// </para>
    /// <para>
    /// "Lost an image" is read from the sanitizer's diagnostics: a <see cref="DiagnosticSeverity.Dropped"/>
    /// in context <c>img</c>, which the HTML sanitizer reports for every image source it refuses,
    /// whatever the reason. Inline data it refuses anywhere else, such as a <c>data:</c> link, is
    /// reported in context <c>data:</c> and is not a lost image.
    /// </para>
    /// </remarks>
    internal static async Task<TransferResolution> ResolveAsync(
        TransferSource source,
        IEnumerable<IDocumentImporter> importers,
        CancellationToken cancellationToken)
    {
        ImportResult? rich = null;
        ImportResult? empty = null;

        foreach (var importer in InPreferenceOrder(importers))
        {
            // Bytes, deliberately, and never as a string. See the type's remarks.
            if (await source.BytesAsync(FlavorFor(importer.Format).DataFormat).ConfigureAwait(true)
                is not { Length: > 0 } bytes)
            {
                continue;
            }

            var result = await ImportOnBackgroundAsync(importer, bytes, cancellationToken)
                .ConfigureAwait(true);

            // Not Doc.Blocks.IsEmpty: a document always has a block, so that let a flavour that
            // imported to one empty paragraph win and shut out every flavour after it.
            if (result.IsEmpty)
            {
                empty ??= result;
                continue;
            }

            rich = result;
            break;
        }

        if (rich is not null && !LostAnImage(rich))
        {
            return new TransferResolution(Document: rich);
        }

        if (await source.BitmapPngAsync().ConfigureAwait(true) is { } png)
        {
            return new TransferResolution(ImagePng: png);
        }

        if (await source.FilesAsync().ConfigureAwait(true) is { } files
            && files.Any(file => RichTextView.MediaTypeOf(file.Name) is not null))
        {
            return new TransferResolution(ImageFiles: files);
        }

        if (rich is not null)
        {
            return new TransferResolution(Document: rich);
        }

        if (await source.TextAsync().ConfigureAwait(true) is { Length: > 0 } text)
        {
            return new TransferResolution(Text: text);
        }

        // Only when nothing else was there. A flavour the sanitizer emptied still goes through
        // the import seam, so that the host hears what was removed rather than nothing at all.
        return empty is not null ? new TransferResolution(Document: empty) : TransferResolution.Nothing;
    }

    private static bool LostAnImage(ImportResult result) =>
        result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Dropped && d.Context is "img");

    /// <summary>Plain text as a document of one paragraph per line, or null if there is none.</summary>
    /// <remarks>
    /// <para>
    /// Shared by both read overloads, and the same rules the controls' own plain-text paste
    /// follows. It used to be <c>FromParagraphs(text.ReplaceLineEndings("\n").Split('\n'))</c> in
    /// each of them, which throws for text carrying U+FFFC — the placeholder the model reserves
    /// for embeds — straight into the broad handler around it. So a clip containing one was read
    /// as nothing at all, and a drop of text a paste accepted quietly inserted nothing.
    /// </para>
    /// <para>
    /// <see cref="string.ReplaceLineEndings(string)"/> takes the form feed, NEL, and the Unicode
    /// line and paragraph separators as well as CR, LF and CRLF. It does not take the vertical tab
    /// Word writes for Shift+Enter, which stays in the text as a character — the model has soft
    /// breaks, and turning one into a paragraph split silently would be a guess.
    /// </para>
    /// </remarks>
    private static ImportResult? FromPlainText(string? text) =>
        text is { Length: > 0 }
            ? new ImportResult(DocumentNode.FromParagraphs(Lines(text)))
            : null;

    /// <summary>The paragraphs a run of plain text becomes.</summary>
    /// <remarks>
    /// Internal so that the controls' paste path builds its paragraphs the same way rather than
    /// beside it: the two disagreeing is exactly what made a drop and a paste of one clip do
    /// different things.
    /// </remarks>
    internal static string[] Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n').Select(WithoutPlaceholder).ToArray();

    /// <summary>A line with the reserved embed placeholder taken out.</summary>
    /// <remarks>
    /// U+FFFC marks where an embed sits, and <c>InlineContent.FromText</c> throws for text carrying
    /// one without matching embeds. Plain text from outside can perfectly well contain it — Word
    /// writes it for objects it cannot otherwise express, and anything that has already been
    /// through a rich text model carries it. The HTML importer removes it on the way in for exactly
    /// this reason.
    /// </remarks>
    internal static string WithoutPlaceholder(string line) =>
        line.Contains(InlineContent.Placeholder, StringComparison.Ordinal)
            ? line.Replace(InlineContent.Placeholder.ToString(), string.Empty, StringComparison.Ordinal)
            : line;

    /// <summary>The importers, richest first.</summary>
    internal static IEnumerable<IDocumentImporter> InPreferenceOrder(
        IEnumerable<IDocumentImporter> importers) =>
        importers.OrderBy(importer => Array.IndexOf(PreferenceOrder, importer.Format) switch
        {
            < 0 => int.MaxValue,
            var rank => rank,
        });

    private static DataFormat<byte[]> Platform(string windows, string mac, string other) =>
        DataFormat.CreateBytesPlatformFormat(
            OperatingSystem.IsWindows() ? windows :
            OperatingSystem.IsMacOS() ? mac : other);
}
