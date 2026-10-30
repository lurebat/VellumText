using VellumText.Interop.Json;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;
using VellumText.Interop.Html;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The public clipboard API: what it carries, and the mistake it exists to make unwritable.
/// </summary>
/// <remarks>
/// A clipboard manager holds payloads itself rather than handing them straight to an editor, so
/// these exercise the bytes-in/bytes-out half as the primary API rather than as an implementation
/// detail of copy and paste.
/// </remarks>
public class RichTextClipboardTests
{
    private static readonly DocumentNode Styled = new([
        new ParagraphNode(
            InlineContent.Create(
                "bold and plain",
                [new ValueSpan<MarkSet>(0, 4, new MarkSet { Style = TextStyle.Bold })],
                []),
            ParagraphKind.Heading2),
    ]);

    [AvaloniaFact]
    public void EveryFlavourIsADistinctPlatformSlot()
    {
        // Two flavours sharing a platform format would silently overwrite one another in the same
        // transfer, and the loser would be whichever exporter ran first.
        Assert.NotEmpty(RichTextClipboard.Flavors);
        Assert.Equal(
            RichTextClipboard.Flavors.Count,
            RichTextClipboard.Flavors.Select(flavour => flavour.DataFormat).Distinct().Count());
    }

    [AvaloniaFact]
    public void HtmlIsUtf8AndRtfIsSingleByte()
    {
        // CF_HTML's byte offsets are only correct in UTF-8. RTF is seven-bit by construction, and
        // a multi-byte guess over it is exactly the mojibake case. The encoding belongs to the
        // format rather than to the clipboard flavour, so the exporter is what decides it.
        Assert.Equal(Encoding.UTF8, HtmlFormat.Instance.TextEncoding);
        Assert.Equal(1, RtfFormat.Instance.TextEncoding!.GetByteCount("a"));
    }

    [AvaloniaFact]
    public void AFormatWithNoPlatformConventionGetsAnApplicationFlavour()
    {
        // It used to get null, so registering such a format was accepted and then silently did
        // nothing. An application format is invisible to other programs, which is right: only
        // this model understands it.
        var flavour = RichTextClipboard.FlavorFor("json");

        Assert.NotEqual(RichTextClipboard.FlavorFor("html").DataFormat, flavour.DataFormat);

        // Minted once: a payload is matched by format equality when it is read back.
        Assert.Equal(flavour.DataFormat, RichTextClipboard.FlavorFor("json").DataFormat);
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.FlavorFor(null!));
    }

    [AvaloniaFact]
    public async Task WhatEncodeWritesDecodeReadsBack()
    {
        foreach (var format in new IDocumentFormat[] { HtmlFormat.Instance, RtfFormat.Instance })
        {
            var payload = RichTextClipboard.Encode((IDocumentExporter)format, Styled);

            Assert.NotNull(payload);

            var result = await RichTextClipboard.DecodeAsync((IDocumentImporter)format, payload);

            Assert.NotNull(result);
            Assert.Equal("bold and plain", DocumentText.Of(result.Doc).Trim());
        }
    }

    [AvaloniaFact]
    public async Task DecodingRtfBytesAsUtf16IsWhatDecodeStopsYouDoingByHand()
    {
        // Not a test of VellumText so much as of the reason its API has no string overload: the same
        // bytes read the way a string-shaped API would read them parse to nothing at all.
        var payload = RichTextClipboard.Encode(RtfFormat.Instance, Styled)!;

        var correct = await RichTextClipboard.DecodeAsync(RtfFormat.Instance, payload);
        var mojibake = await RtfFormat.Instance.ImportAsync(Encoding.Unicode.GetString(payload));

        Assert.Equal("bold and plain", DocumentText.Of(correct!.Doc).Trim());
        Assert.NotEqual("bold and plain", DocumentText.Of(mojibake.Doc).Trim());
    }

    [AvaloniaFact]
    public async Task EncodeAndDecodeWorkForAFormatWithNoPlatformConvention()
    {
        var format = new Nowhere();
        var payload = RichTextClipboard.Encode(format, Styled);

        Assert.NotNull(payload);
        Assert.NotNull(await RichTextClipboard.DecodeAsync(format, payload));
    }

    [AvaloniaFact]
    public async Task EncodeAndDecodeRejectNulls()
    {
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.Encode(null!, Styled));
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.Encode(HtmlFormat.Instance, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => RichTextClipboard.DecodeAsync(null!, default));
    }

    [AvaloniaFact]
    public void RicherFormatsAreTriedFirstWhateverOrderTheyArrivedIn()
    {
        // Copying from a word processor puts RTF, HTML and text on the clipboard at once. Taking
        // whichever answers first would silently keep the poorest of the three.
        IDocumentImporter[] backwards = [RtfFormat.Instance, HtmlFormat.Instance];

        Assert.Equal(
            ["html", "rtf"],
            RichTextClipboard.InPreferenceOrder(backwards).Select(i => i.Format));
    }

    /// <summary>A registered format actually travels, rather than being quietly ignored.</summary>
    /// <remarks>
    /// The defect this closes: FlavorFor returned null for anything but HTML and RTF, so
    /// Formats.Add(JsonFormat.Instance) was accepted, validated, and then did nothing at all on
    /// copy or paste. Asserted through Build and Read rather than on FlavorFor, because the
    /// question is whether the payload carries it, not whether a lookup answers.
    ///
    /// Deliberately not asserted here: that JSON is *better* than HTML. It is not, today - §4.9
    /// records that HTML carries everything the model can hold - and the test that claimed
    /// otherwise passed with JSON removed from the list.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARegisteredFormatWithNoPlatformConventionStillTravels()
    {
        IDocumentFormat[] formats = [HtmlFormat.Instance, JsonFormat.Instance];

        var data = RichTextClipboard.Build(Styled, formats.OfType<IDocumentExporter>());
        var flavour = RichTextClipboard.FlavorFor(JsonFormat.Instance.Format);

        // The payload carries it, which it did not before.
        Assert.NotNull(data.Items[0].TryGetValue(flavour.DataFormat));

        // And it can be read back through that flavour alone.
        var jsonOnly = await RichTextClipboard.ReadAsync(data, [JsonFormat.Instance]);

        Assert.NotNull(jsonOnly);
        Assert.Equal(Styled, jsonOnly.Doc);
    }
    [AvaloniaFact]
    public void AFormatWithNoPreferenceRankSortsAfterTheOnesThatHaveOne()
    {
        IDocumentImporter[] formats = [new Nowhere(), RtfFormat.Instance, HtmlFormat.Instance];

        Assert.Equal(
            ["html", "rtf", "nowhere"],
            RichTextClipboard.InPreferenceOrder(formats).Select(i => i.Format));
    }

    [AvaloniaFact]
    public void APayloadCarriesPlainTextAsWellAsEveryRichFlavour()
    {
        var data = RichTextClipboard.Build(Styled, [HtmlFormat.Instance, RtfFormat.Instance]);
        var item = Assert.Single(data.Items);

        Assert.Equal("bold and plain", item.TryGetText()?.Trim());
        Assert.NotNull(item.TryGetValue(RichTextClipboard.FlavorFor("html")!.DataFormat));
        Assert.NotNull(item.TryGetValue(RichTextClipboard.FlavorFor("rtf")!.DataFormat));
    }

    [AvaloniaFact]
    public void AnExporterThatThrowsCostsItsOwnFlavourAndNotTheCopy()
    {
        var data = RichTextClipboard.Build(Styled, [new Broken(), RtfFormat.Instance]);
        var item = Assert.Single(data.Items);

        Assert.Equal("bold and plain", item.TryGetText()?.Trim());
        Assert.Null(item.TryGetValue(RichTextClipboard.FlavorFor("html")!.DataFormat));
        Assert.NotNull(item.TryGetValue(RichTextClipboard.FlavorFor("rtf")!.DataFormat));
    }

    [AvaloniaFact]
    public void SuppliedTextWinsOverTheDocumentsOwn()
    {
        // A manager showing a truncated label on the clipboard wants its own text, not the whole
        // document flattened.
        var data = RichTextClipboard.Build(Styled, [], "a label");

        Assert.Equal("a label", Assert.Single(data.Items).TryGetText());
    }

    [AvaloniaFact]
    public void BuildRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.Build(null!, []));
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.Build(Styled, null!));
    }

    [AvaloniaFact]
    public async Task ADocumentWrittenToAClipboardComesBackAsOne()
    {
        // Build and Decode are unit-tested above; this is the pair, driven end to end. The
        // clipboard is a stand-in because a headless window has none - window.Clipboard is null,
        // which an earlier version of this test quietly returned on, proving nothing.
        var data = RichTextClipboard.Build(Styled, [HtmlFormat.Instance, RtfFormat.Instance]);

        var result = await RichTextClipboard.ReadAsync(
            data, [RtfFormat.Instance, HtmlFormat.Instance]);

        Assert.NotNull(result);
        Assert.Equal("bold and plain", DocumentText.Of(result.Doc).Trim());

        // Richest-first is the behaviour, and it was handed the importers the other way round.
        Assert.Equal(
            ParagraphKind.Heading2,
            Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]).Kind);
    }

    [AvaloniaFact]
    public async Task AClipboardHoldingOnlyPlainTextStillReadsAsADocument()
    {
        var item = new DataTransferItem();

        item.SetText("one\r\ntwo");

        var data = new DataTransfer();

        data.Add(item);

        var result = await RichTextClipboard.ReadAsync(data, [HtmlFormat.Instance]);

        Assert.NotNull(result);
        Assert.Equal(2, result.Doc.Blocks.Length);
        Assert.Equal("one", ((ParagraphNode)result.Doc.Blocks[0]).Content.Text);
        Assert.Equal("two", ((ParagraphNode)result.Doc.Blocks[1]).Content.Text);
    }

    /// <summary>
    /// A rich flavour that imports to nothing gives way to the text beside it. The check meant to
    /// skip it asked whether the document had no blocks, which a document never has, so an empty
    /// HTML flavour was the answer and the text was never read (rubber-duck review 6.3).
    /// </summary>
    [AvaloniaFact]
    public async Task ARichFlavourThatImportsToNothingGivesWayToTheText()
    {
        var item = new DataTransferItem();

        item.Set(RichTextClipboard.FlavorFor("html").DataFormat, System.Text.Encoding.UTF8.GetBytes("<p></p>"));
        item.SetText("the text");

        var data = new DataTransfer();

        data.Add(item);

        var result = await RichTextClipboard.ReadAsync(data, [HtmlFormat.Instance]);

        Assert.NotNull(result);
        Assert.Equal("the text", DocumentText.Of(result.Doc));
    }

    /// <summary>
    /// With nothing else to read, the empty import is still the answer, diagnostics and all: a
    /// payload the sanitizer emptied is not the same as an empty clipboard.
    /// </summary>
    [AvaloniaFact]
    public async Task AnEmptyImportWithNothingElseIsStillTheAnswer()
    {
        var item = new DataTransferItem();

        item.Set(RichTextClipboard.FlavorFor("html").DataFormat, System.Text.Encoding.UTF8.GetBytes("<script>x</script>"));

        var data = new DataTransfer();

        data.Add(item);

        var result = await RichTextClipboard.ReadAsync(data, [HtmlFormat.Instance]);

        Assert.NotNull(result);
        Assert.True(result.IsEmpty);
        Assert.NotEmpty(result.Diagnostics);
    }
    [AvaloniaFact]
    public async Task AnEmptyClipboardReadsAsNothingRatherThanAnEmptyDocument()
    {
        Assert.Null(await RichTextClipboard.ReadAsync(new DataTransfer(), [HtmlFormat.Instance]));
    }

    [AvaloniaFact]
    public async Task ReadAndWriteRejectNulls()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => RichTextClipboard.ReadAsync((IClipboard)null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => RichTextClipboard.ReadAsync((IAsyncDataTransfer)null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => RichTextClipboard.WriteAsync(null!, Styled, []));
    }

    /// <summary>A registered format with no platform clipboard convention of its own.</summary>
    private sealed class Nowhere : IDocumentImporter, IDocumentExporter
    {
        public string Format => "nowhere";

        public string MediaType => "application/x-nowhere";

        public Encoding? TextEncoding => Encoding.UTF8;

        public ImportResult Import(string text) => new(new DocumentNode([]));

        public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default) =>
            Import(await DocumentPayload.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken));

        public string Export(DocumentNode doc) => "{}";

        public void Export(Stream destination, DocumentNode doc) =>
            DocumentPayload.WriteAllText(destination, Encoding.UTF8, Export(doc));
    }

    /// <summary>An exporter that breaks its contract by throwing.</summary>
    private sealed class Broken : IDocumentExporter
    {
        public string Format => "html";

        public string MediaType => "text/html";

        public Encoding? TextEncoding => Encoding.UTF8;

        public string Export(DocumentNode doc) => throw new InvalidOperationException("bug");

        public void Export(Stream destination, DocumentNode doc) =>
            throw new InvalidOperationException("bug");
    }
}
