using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// RTF exporter fidelity for headings, lists, pictures and link titles (audit of 0282015, plan
/// unit 4.7), read back through the real import path wherever that path can read the construct.
/// </summary>
public class RtfExportFidelityTests
{
    private static DocumentNode Trip(DocumentNode doc) =>
        RtfImporter.Import(RtfExporter.Instance.Export(doc)).Doc;

    private static ParagraphNode Sized(string text, double size, ParagraphKind kind) =>
        new(InlineContent.Create(
            text,
            [new ValueSpan<MarkSet>(0, text.Length, new MarkSet { FontSize = size })],
            []),
            kind);

    /// <summary>
    /// F-52: an explicit size was multiplied by the heading's scale on the way out and read back as
    /// the explicit size, so it grew on every copy and paste — 12 → 24 → 48 → 96 px in a Heading1.
    /// The heading's scale is for text that asked for no size; text that asked for one is that size.
    /// </summary>
    [Theory]
    [InlineData(ParagraphKind.Heading1)]
    [InlineData(ParagraphKind.Heading4)]
    public void An_explicit_size_inside_a_heading_does_not_grow(ParagraphKind kind)
    {
        var doc = new DocumentNode([Sized("big", 12, kind)]);

        Assert.Contains(@"\fs18", RtfExporter.Instance.Export(doc), StringComparison.Ordinal);

        for (var trip = 0; trip < 3; trip++)
        {
            doc = Trip(doc);

            var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(doc.Blocks));

            Assert.Equal(kind, paragraph.Kind);
            Assert.Equal(12, paragraph.Content.MarkAt(0).FontSize);
        }
    }
}
