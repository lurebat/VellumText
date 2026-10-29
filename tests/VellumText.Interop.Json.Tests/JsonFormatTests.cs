using VellumText;
using VellumText.Interop.Json;

namespace VellumText.Tests.Interop.Json;

/// <summary>
/// The JSON format, whose whole claim is that it loses nothing.
/// </summary>
/// <remarks>
/// The round trips assert node equality rather than comparing exported text, because comparing
/// text only proves the writer is deterministic. Equality is what "lossless" actually means.
/// </remarks>
public class JsonFormatTests
{
    private static async Task<DocumentNode> RoundTrip(DocumentNode doc) =>
        (await JsonFormat.Instance.ImportAsync(JsonFormat.Instance.Export(doc))).Doc;

    [Fact]
    public async Task PlainParagraphsSurvive()
    {
        var doc = DocumentNode.FromParagraphs("one", "two", "three");

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task EveryParagraphKindSurvives()
    {
        foreach (var kind in Enum.GetValues<ParagraphKind>())
        {
            var doc = new DocumentNode([new ParagraphNode(InlineContent.FromText("text"), kind)]);

            Assert.Equal(doc, await RoundTrip(doc));
        }
    }

    [Fact]
    public async Task AlignmentAndIndentSurvive()
    {
        foreach (var align in Enum.GetValues<TextAlign>())
        {
            var doc = new DocumentNode([
                new ParagraphNode(InlineContent.FromText("text"), ParagraphKind.Body, align, 3),
            ]);

            Assert.Equal(doc, await RoundTrip(doc));
        }
    }

    [Fact]
    public async Task EveryCharacterStyleSurvives()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                "bold italic under strike sub super code",
                [
                    new ValueSpan<MarkSet>(0, 4, new MarkSet { Style = TextStyle.Bold }),
                    new ValueSpan<MarkSet>(5, 6, new MarkSet { Style = TextStyle.Italic }),
                    new ValueSpan<MarkSet>(12, 5, new MarkSet { Style = TextStyle.Underline }),
                    new ValueSpan<MarkSet>(18, 6, new MarkSet { Style = TextStyle.Strikethrough }),
                    new ValueSpan<MarkSet>(25, 3, new MarkSet { Style = TextStyle.Sub }),
                    new ValueSpan<MarkSet>(29, 5, new MarkSet { Style = TextStyle.Super }),
                    new ValueSpan<MarkSet>(35, 4, new MarkSet { Style = TextStyle.Code }),
                ],
                [])),
        ]);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task CombinedStylesOnOneSpanSurvive()
    {
        // The style is a flags enum, so this is the case a naive single-name encoding loses.
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                "loud",
                [new ValueSpan<MarkSet>(0, 4, new MarkSet
                {
                    Style = TextStyle.Bold | TextStyle.Italic | TextStyle.Underline,
                })],
                [])),
        ]);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task ColoursFontsAndLinksSurvive()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                "styled",
                [new ValueSpan<MarkSet>(0, 6, new MarkSet(TextStyle.None, "Georgia", 13.5)
                {
                    Foreground = new Rgba(18, 52, 86, 255),
                    Highlight = new Rgba(255, 255, 0, 128),
                    Link = new LinkMark("https://example.com", "a title"),
                })],
                [])),
        ]);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task ATranslucentColourKeepsItsAlpha()
    {
        // Alpha rides in the top byte, so an encoding that dropped it would still look right for
        // every opaque colour and be wrong for every highlight.
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                "faint",
                [new ValueSpan<MarkSet>(0, 5, new MarkSet { Highlight = new Rgba(1, 2, 3, 4) })],
                [])),
        ]);

        var mark = ((ParagraphNode)(await RoundTrip(doc)).Blocks[0]).Content.MarkAt(0);

        Assert.Equal(new Rgba(1, 2, 3, 4), mark.Highlight);
    }

    [Fact]
    public async Task ListsAndTheirNestingSurvive()
    {
        var doc = new DocumentNode([
            new ListNode(
                [
                    new ListItemNode([ParagraphNode.FromText("first")]),
                    new ListItemNode([
                        ParagraphNode.FromText("second"),
                        new ListNode(
                            [new ListItemNode([ParagraphNode.FromText("nested")])],
                            ListKind.Unordered),
                    ]),
                ],
                ListKind.Ordered,
                5),
        ]);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task TablesKeepTheirSpansHeadersColoursAndWidths()
    {
        var doc = new DocumentNode([
            new TableNode(
                [
                    new TableRowNode([
                        new TableCellNode([ParagraphNode.FromText("head")], isHeader: true),
                        new TableCellNode(
                            [ParagraphNode.FromText("spanned")],
                            rowSpan: 2,
                            columnSpan: 3,
                            background: new Rgba(200, 210, 220, 255)),
                    ]),
                    new TableRowNode([new TableCellNode([ParagraphNode.FromText("body")])]),
                ],
                [80.5, 120, 40]),
        ]);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    /// <summary>
    /// Column resize writes proportions summing to one, so the numbers are long decimals rather
    /// than the round ones a hand-written table carries. A format that shortened them would drift
    /// the proportions a little on every save, and the sum would stop being one.
    /// </summary>
    [Fact]
    public async Task ProportionalColumnWidthsSurviveToTheirLastDigit()
    {
        var widths = new[] { 0.28374659812734, 0.41283746501927, 1 - 0.28374659812734 - 0.41283746501927 };
        var doc = new DocumentNode([
            new TableNode(
                [
                    new TableRowNode([
                        TableCellNode.FromText("a"),
                        TableCellNode.FromText("b"),
                        TableCellNode.FromText("c"),
                    ]),
                ],
                widths),
        ]);

        var read = (TableNode)(await RoundTrip(doc)).Blocks[0];

        Assert.Equal(widths, read.ColumnWidths.AsEnumerable());
        Assert.Equal(1.0, read.ColumnWidths.Sum(), 12);
    }

    [Fact]
    public async Task RulesAndBlockImagesSurvive()
    {
        var doc = new DocumentNode([
            RuleNode.Instance,
            new BlockImageNode(new ImageEmbed("a.png", 640, 480, "alt text"), TextAlign.Center),
        ]);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task InlineImagesSurviveWithTheirPlaceholder()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                $"before{InlineContent.Placeholder}after",
                [],
                [new ImageEmbed("inline.png", 16, 16)])),
        ]);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task AnEmptyDocumentSurvives()
    {
        var doc = new DocumentNode([]);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    // ---- the compatibility contract -----------------------------------------------------------

    /// <summary>
    /// Schema version 1 is pinned to a fixed payload, in both directions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other test here is a round trip: export, import, compare. That proves the writer and
    /// the reader agree with each other, which they will continue to do if both are changed
    /// together — and the on-disk format will have become incompatible with every file already
    /// written, silently, with the whole suite green. The payload declares
    /// <c>"VellumText": 1</c>, and until now nothing said what 1 <em>is</em>.
    /// </para>
    /// <para>
    /// So the oracle is a literal neither side produces. Reading it proves a stored document
    /// still loads; writing it proves a stored document still round-trips through a future
    /// reader. A deliberate format change should break this test and be answered by bumping
    /// <see cref="JsonFormat.SchemaVersion"/> and adding a second fixture, not by editing this
    /// one in place.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SchemaVersionOneIsExactlyThisPayload()
    {
        const string Golden =
            """
            {"VellumText":1,"blocks":[{"type":"paragraph","content":{"text":"Hi \uD83D\uDE00","marks":[{"start":0,"length":2,"mark":{"style":"Bold"}}]}},{"type":"list","kind":"Ordered","start":3,"items":[{"blocks":[{"type":"paragraph","content":{"text":"one"}}]}]},{"type":"table","rows":[{"cells":[{"blocks":[{"type":"paragraph","content":{"text":"c"}}]}]}]},{"type":"rule"}]}
            """;

        var expected = new DocumentNode(
        [
            new ParagraphNode(InlineContent.Create(
                "Hi \U0001F600",
                [new ValueSpan<MarkSet>(0, 2, new MarkSet { Style = TextStyle.Bold })],
                [])),
            new ListNode([new ListItemNode([ParagraphNode.FromText("one")])], ListKind.Ordered, 3),
            new TableNode([new TableRowNode([TableCellNode.FromText("c")])]),
            RuleNode.Instance,
        ]);

        // The reader cannot drift: a file written by this version must still load as this.
        var read = await JsonFormat.Instance.ImportAsync(Golden, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(read.Diagnostics);
        Assert.Equal(expected, read.Doc);

        // And the writer cannot drift: what this version writes must still be readable by anything
        // that understands version 1.
        Assert.Equal(Golden, JsonFormat.Instance.Export(expected));
    }

    [Fact]
    public void ThePayloadDeclaresItsSchemaVersion()
    {
        Assert.Contains($"\"VellumText\":{JsonFormat.SchemaVersion}", JsonFormat.Instance.Export(DocumentNode.Empty));
    }

    [Fact]
    public async Task ADocumentFromANewerVersionIsRefusedRatherThanPartlyRead()
    {
        // The failure mode this prevents: a reader that ignored the version would parse the blocks
        // it recognised, drop the ones it did not, and hand back a document that looks complete.
        var payload = $"{{\"VellumText\":{JsonFormat.SchemaVersion + 1},\"blocks\":[" +
            "{\"type\":\"paragraph\",\"content\":{\"text\":\"hello\"}}]}";

        var result = await JsonFormat.Instance.ImportAsync(payload, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Doc.Blocks.IsEmpty);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("newer version"));
    }

    [Fact]
    public async Task APayloadWithNoVersionIsNotTakenForOne()
    {
        var result = await JsonFormat.Instance.ImportAsync("{\"blocks\":[]}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"VellumText\":0,\"blocks\":[]}")]
    [InlineData("{\"VellumText\":\"one\"}")]
    [InlineData("{\"VellumText\":1,\"blocks\":42}")]
    [InlineData("{\"VellumText\":1,\"blocks\":[{\"type\":\"paragraph\",\"content\":{\"text\":\"hi\",\"marks\":[{\"start\":0,\"length\":99,\"mark\":{}}]}}]}")]
    [InlineData("{\"VellumText\":1,\"blocks\":[{\"type\":\"table\",\"rows\":[{\"cells\":[{\"rowSpan\":-5,\"blocks\":[]}]}]}]}")]
    [InlineData("{\"VellumText\":1,\"blocks\":[{\"type\":\"table\",\"columns\":[\"wide\"],\"rows\":[]}]}")]
    public async Task ABadPayloadIsReportedRatherThanThrown(string payload)
    {
        // Importers never throw. A store full of files written by something else has to degrade
        // into "cannot open these", not into an unhandled exception on a background thread.
        var result = await JsonFormat.Instance.ImportAsync(payload, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task AnUnknownBlockTypeIsSkippedRatherThanFatal()
    {
        var payload = $"{{\"VellumText\":{JsonFormat.SchemaVersion},\"blocks\":[" +
            "{\"type\":\"from-the-future\"}," +
            "{\"type\":\"paragraph\",\"content\":{\"text\":\"kept\"}}]}";

        var result = await JsonFormat.Instance.ImportAsync(payload, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("kept", ((ParagraphNode)Assert.Single(result.Doc.Blocks)).Content.Text);
    }

    [Fact]
    public void TheDefaultsAreOmittedRatherThanWritten()
    {
        // Not cosmetic: every omitted property is one an older reader can ignore, which is what
        // lets the schema version stay put when an optional property is added.
        var json = JsonFormat.Instance.Export(DocumentNode.FromParagraphs("plain"));

        Assert.DoesNotContain("\"kind\"", json);
        Assert.DoesNotContain("\"align\"", json);
        Assert.DoesNotContain("\"indent\"", json);
        Assert.DoesNotContain("\"marks\"", json);
        Assert.DoesNotContain("\"embeds\"", json);
    }

    [Fact]
    public void TheFormatIdentifiesItself()
    {
        Assert.Equal("json", JsonFormat.Instance.Format);
        Assert.Equal("application/vnd.VellumText+json", JsonFormat.Instance.MediaType);
    }

    [Fact]
    public void ExportRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => JsonFormat.Instance.Export(null!));
    }

    [Fact]
    public async Task TextThatBreaksNaiveEncodersSurvives()
    {
        var doc = DocumentNode.FromParagraphs(
            "quotes \" and \\ backslash",
            "\u05e9\u05dc\u05d5\u05dd inside English",
            "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 and e\u0301",
            "line\tseparated\u2028and\u2029paragraph");

        Assert.Equal(doc, await RoundTrip(doc));
    }

    /// <summary>
    /// A link destination survives this format verbatim, including one the interchange formats
    /// refuse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pinning a deliberate asymmetry so it cannot be mistaken for an oversight, and so that
    /// adding sanitization here fails loudly rather than silently breaking the round-trip
    /// guarantee this format exists for. HTML, RTF and Markdown all reach the model through
    /// <c>HtmlImporter</c> and all three drop these destinations; this reader reconstructs what
    /// the payload says, because a host that stores a document using its own scheme would
    /// otherwise find storage destroying it.
    /// </para>
    /// <para>
    /// The values are the ones that mattered: <c>//evil.test/steal</c> and the UNC form were a
    /// real defect in the HTML importer, where <c>UriKind.Absolute</c> was used as a proxy for
    /// "has a scheme" and let both through as <c>file:</c> links. That fix belongs where
    /// untrusted input arrives, which is not here.
    /// </para>
    /// </remarks>
    /// <param name="href">The destination to round-trip.</param>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("//evil.test/steal")]
    [InlineData(@"\\evil.test\share")]
    [InlineData("myapp://note/42")]
    public async Task A_link_destination_is_stored_verbatim_because_this_format_is_not_interchange(
        string href)
    {
        var content = InlineContent.FromText("click")
            .ApplyMarks(0, 5, new MarkSet { Link = new LinkMark(href, null) }, MarkFields.Link);
        var doc = new DocumentNode([new ParagraphNode(content)]);

        var back = Assert.IsType<ParagraphNode>((await RoundTrip(doc)).Blocks[0]);

        Assert.Equal(href, back.Content.MarkAt(0).Link?.Href);
    }
}
