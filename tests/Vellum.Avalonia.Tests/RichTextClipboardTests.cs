using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Headless.XUnit;
using Vellum.Avalonia;
using Vellum.Interop.Html;
using Vellum.Interop.Rtf;

namespace Vellum.Tests.Avalonia;

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
    public void EveryFlavourCarriesBytesRatherThanText()
    {
        // The whole design. A string flavour would let the backend pick an encoding, and its pick
        // for RTF is the bug this type exists to prevent.
        Assert.NotEmpty(RichTextClipboard.Flavors);
        Assert.All(
            RichTextClipboard.Flavors,
            flavour =>
            {
                Assert.NotNull(flavour.DataFormat);
                Assert.NotNull(flavour.Encoding);
            });
    }

    [AvaloniaFact]
    public void HtmlIsUtf8AndRtfIsSingleByte()
    {
        // CF_HTML's byte offsets are only correct in UTF-8. RTF is seven-bit by construction, and
        // a multi-byte guess over it is exactly the mojibake case.
        Assert.Equal(Encoding.UTF8, RichTextClipboard.FlavorFor("html")!.Encoding);
        Assert.Equal(1, RichTextClipboard.FlavorFor("rtf")!.Encoding.GetByteCount("a"));
    }

    [AvaloniaFact]
    public void AFormatWithNoClipboardPresenceReportsNoneRatherThanGuessing()
    {
        Assert.Null(RichTextClipboard.FlavorFor("json"));
        Assert.Null(RichTextClipboard.FlavorFor(""));
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.FlavorFor(null!));
    }

    [AvaloniaFact]
    public void WhatEncodeWritesDecodeReadsBack()
    {
        foreach (var format in new IDocumentFormat[] { HtmlFormat.Instance, RtfFormat.Instance })
        {
            var payload = RichTextClipboard.Encode((IDocumentExporter)format, Styled);

            Assert.NotNull(payload);

            var result = RichTextClipboard.Decode((IDocumentImporter)format, payload);

            Assert.NotNull(result);
            Assert.Equal("bold and plain", DocumentText.Of(result.Doc).Trim());
        }
    }

    [AvaloniaFact]
    public void DecodingRtfBytesAsUtf16IsWhatDecodeStopsYouDoingByHand()
    {
        // Not a test of Vellum so much as of the reason its API has no string overload: the same
        // bytes read the way a string-shaped API would read them parse to nothing at all.
        var payload = RichTextClipboard.Encode(RtfFormat.Instance, Styled)!;

        var correct = RichTextClipboard.Decode(RtfFormat.Instance, payload);
        var mojibake = RtfFormat.Instance.Import(Encoding.Unicode.GetString(payload));

        Assert.Equal("bold and plain", DocumentText.Of(correct!.Doc).Trim());
        Assert.NotEqual("bold and plain", DocumentText.Of(mojibake.Doc).Trim());
    }

    [AvaloniaFact]
    public void EncodeAndDecodeRefuseAFormatWithNoFlavourRatherThanInventingOne()
    {
        Assert.Null(RichTextClipboard.Encode(new Nowhere(), Styled));
        Assert.Null(RichTextClipboard.Decode(new Nowhere(), new byte[] { 1, 2, 3 }));
    }

    [AvaloniaFact]
    public void EncodeAndDecodeRejectNulls()
    {
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.Encode(null!, Styled));
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.Encode(HtmlFormat.Instance, null!));
        Assert.Throws<ArgumentNullException>(() => RichTextClipboard.Decode(null!, default));
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

    [AvaloniaFact]
    public void AFormatWithNoPreferenceRankSortsAfterTheOnesThatHaveOne()
    {
        IDocumentImporter[] formats = [new Nowhere(), RtfFormat.Instance, HtmlFormat.Instance];

        Assert.Equal(
            ["html", "rtf", "json"],
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

    /// <summary>A format that can be read and written but has no clipboard flavour.</summary>
    private sealed class Nowhere : IDocumentImporter, IDocumentExporter
    {
        public string Format => "json";

        public string MediaType => "application/json";

        public ImportResult Import(string text) => new(new DocumentNode([]));

        public string Export(DocumentNode doc) => "{}";
    }

    /// <summary>An exporter that breaks its contract by throwing.</summary>
    private sealed class Broken : IDocumentExporter
    {
        public string Format => "html";

        public string MediaType => "text/html";

        public string Export(DocumentNode doc) => throw new InvalidOperationException("bug");
    }
}
