using System.Text;

namespace VellumText.Tests.Interop;

/// <summary>
/// The format contract in both directions: streams for every format, strings for the text ones.
/// </summary>
/// <remarks>
/// The reason the stream members exist. A contract that only spoke in strings could not express a
/// container format — DOCX is a ZIP, ODT is a ZIP — and the model is otherwise perfectly able to
/// hold what either carries. These tests use a format whose payload is bytes no encoding can
/// round-trip, so a string anywhere on the path is provable rather than a matter of opinion.
/// </remarks>
public class DocumentFormatStreamTests
{
    /// <summary>Bytes that survive nothing: a NUL, and several illegal in UTF-8.</summary>
    /// <remarks>Odd-length on purpose — UTF-16 round-trips any even-length run of bytes.</remarks>
    private static readonly byte[] Hostile = [0x00, 0xFF, 0xFE, 0x41, 0x00, 0x80, 0x50];

    [Fact]
    public async Task ABinaryFormatRoundTripsThroughAStreamUnchanged()
    {
        var format = new Opaque();

        using var written = new MemoryStream();

        format.Export(written, DocumentNode.FromParagraphs("anything"));

        Assert.Equal(Hostile, written.ToArray());

        using var read = new MemoryStream(written.ToArray(), writable: false);

        Assert.Equal("intact", DocumentText.Of((await format.ImportAsync(read, cancellationToken: TestContext.Current.CancellationToken)).Doc).Trim());
    }

    [Fact]
    public void TheSameBytesThroughAStringDoNotSurvive()
    {
        // Not a test of VellumText so much as of why the stream members had to exist. Latin-1 is
        // excluded because it does survive this — it is a byte-for-byte map, which is why the RTF
        // path can name it — but a container format naming an encoding it does not use, so that
        // its importer can immediately encode the string back to the bytes it wanted, is a detour
        // that holds only while every layer agrees never to touch what it is carrying.
        foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.ASCII })
        {
            Assert.NotEqual(Hostile, encoding.GetBytes(encoding.GetString(Hostile)));
        }
    }

    [Fact]
    public async Task ABinaryFormatRefusesTheStringImportRatherThanGuessing()
    {
        IDocumentImporter format = new Opaque();

        var result = await format.ImportAsync("not that it could mean anything", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsEmpty);
        Assert.Equal(DiagnosticSeverity.Malformed, Assert.Single(result.Diagnostics).Severity);
    }

    [Fact]
    public void ABinaryFormatRefusesTheStringExportRatherThanLosingTheDocument()
    {
        // The one member on either interface that throws. There is no honest string to return and
        // no honest empty one either: "" would be a document silently lost on the way to a file.
        IDocumentExporter format = new Opaque();

        var error = Assert.Throws<NotSupportedException>(
            () => format.Export(DocumentNode.FromParagraphs("anything")));

        Assert.Contains("opaque", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATextFormatThatOnlyImplementsStreamsStillAnswersTheStringMembers()
    {
        // The default implementations, which is what keeps the string path free for the formats
        // that are genuinely text without making every format write it twice. They are default
        // interface members, so they are reached through the interface rather than the class.
        IDocumentImporter importer = new StreamsOnly();
        IDocumentExporter exporter = new StreamsOnly();

        Assert.Equal("hello", exporter.Export(DocumentNode.FromParagraphs("hello")).Trim());
        Assert.Equal("hello", DocumentText.Of((await importer.ImportAsync("hello", cancellationToken: TestContext.Current.CancellationToken)).Doc).Trim());
    }

    [Fact]
    public async Task TheStringMembersRejectNulls()
    {
        IDocumentImporter importer = new StreamsOnly();
        IDocumentExporter exporter = new StreamsOnly();

        await Assert.ThrowsAsync<ArgumentNullException>(() => importer.ImportAsync((string)null!, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentNullException>(() => exporter.Export((DocumentNode)null!));
    }

    [Fact]
    public void WriteAllTextEmitsNoByteOrderMark()
    {
        // A BOM would break both formats that use this: CF_HTML's byte offsets are computed over a
        // payload without one, and an RTF reader handed EF BB BF before the opening brace refuses
        // the document.
        using var buffer = new MemoryStream();

        DocumentPayload.WriteAllText(buffer, Encoding.UTF8, "hi");

        Assert.Equal("hi"u8.ToArray(), buffer.ToArray());
    }

    [Fact]
    public async Task ReadAllTextLeavesTheStreamOpenForWhateverComesNext()
    {
        // The caller owns the stream. A reader that closed it would make a format the last thing
        // that could ever read from a transfer carrying more than one.
        using var buffer = new MemoryStream(Encoding.UTF8.GetBytes("first"));

        Assert.Equal("first", await DocumentPayload.ReadAllTextAsync(buffer, Encoding.UTF8, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(buffer.CanRead);
    }

    [Fact]
    public async Task ReadAllTextHonoursAByteOrderMarkOverTheEncodingItWasGiven()
    {
        // A file written by another application announces its own encoding, and taking the given
        // one anyway puts three stray characters at the front of the document.
        using var buffer = new MemoryStream([.. Encoding.UTF8.GetPreamble(), .. "hi"u8.ToArray()]);

        Assert.Equal("hi", await DocumentPayload.ReadAllTextAsync(buffer, Encoding.Latin1, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PayloadHelpersRejectNulls()
    {
        using var buffer = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentPayload.ReadAllTextAsync(null!, Encoding.UTF8, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentPayload.ReadAllTextAsync(buffer, null!, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentNullException>(
            () => DocumentPayload.WriteAllText(null!, Encoding.UTF8, "x"));
        Assert.Throws<ArgumentNullException>(() => DocumentPayload.WriteAllText(buffer, null!, "x"));
        Assert.Throws<ArgumentNullException>(
            () => DocumentPayload.WriteAllText(buffer, Encoding.UTF8, null!));
    }

    /// <summary>A format whose payload is bytes and cannot be made into a string at all.</summary>
    private sealed class Opaque : IDocumentImporter, IDocumentExporter
    {
        public string Format => "opaque";

        public string MediaType => "application/x-opaque";

        public Encoding? TextEncoding => null;

        public void Export(Stream destination, DocumentNode doc)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(doc);

            destination.Write(Hostile);
        }

        public Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default) =>
            Task.FromResult(ImportCore(source));

        private ImportResult ImportCore(Stream source)
        {
            ArgumentNullException.ThrowIfNull(source);

            using var buffer = new MemoryStream();

            source.CopyTo(buffer);

            return new ImportResult(
                DocumentNode.FromParagraphs(
                    buffer.ToArray().SequenceEqual(Hostile) ? "intact" : "corrupted"));
        }
    }

    /// <summary>A text format that implements only what the contract requires.</summary>
    private sealed class StreamsOnly : IDocumentImporter, IDocumentExporter
    {
        public string Format => "streams-only";

        public string MediaType => "text/plain";

        public Encoding? TextEncoding => Encoding.UTF8;

        public void Export(Stream destination, DocumentNode doc) =>
            DocumentPayload.WriteAllText(destination, Encoding.UTF8, DocumentText.Of(doc));

        public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default) =>
            new(DocumentNode.FromParagraphs(
                (await DocumentPayload.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken)).Split('\n')));
    }
}
