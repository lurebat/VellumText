using Vellum;

namespace Vellum.Tests.Core;

/// <summary>
/// Mapping between document positions and text offsets, in both directions.
/// </summary>
public class DocumentTextMapTests
{
    /// <summary>Documents covering every shape the walk has to handle.</summary>
    public static TheoryData<string, DocumentNode> Documents()
    {
        var data = new TheoryData<string, DocumentNode>
        {
            { "empty document", new DocumentNode([]) },
            { "one paragraph", DocumentNode.FromParagraphs("hello") },
            { "two paragraphs", DocumentNode.FromParagraphs("ab", "cd") },
            { "empty paragraph", DocumentNode.FromParagraphs("") },
            { "blank runs", DocumentNode.FromParagraphs("a", "", "", "b") },
            { "all blank", DocumentNode.FromParagraphs("", "", "") },
            {
                "an embed",
                new DocumentNode([
                    new ParagraphNode(InlineContent.Create(
                        $"a{InlineContent.Placeholder}b", [], [new ImageEmbed("x.png", altText: "PHOTO")])),
                ])
            },
            {
                "two embeds and no text",
                new DocumentNode([
                    new ParagraphNode(InlineContent.Create(
                        $"{InlineContent.Placeholder}{InlineContent.Placeholder}",
                        [],
                        [new ImageEmbed("a.png"), new ImageEmbed("b.png", altText: "B")])),
                ])
            },
            {
                "a list",
                new DocumentNode([
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
                        ListKind.Ordered),
                    ParagraphNode.FromText("after"),
                ])
            },
            {
                "a table",
                new DocumentNode([
                    ParagraphNode.FromText("before"),
                    new TableNode([
                        new TableRowNode([
                            new TableCellNode([ParagraphNode.FromText("a")]),
                            new TableCellNode([ParagraphNode.FromText("b")]),
                        ]),
                        new TableRowNode([
                            new TableCellNode([ParagraphNode.FromText("c")]),
                            new TableCellNode([ParagraphNode.FromText("d")]),
                        ]),
                    ]),
                    ParagraphNode.FromText("after"),
                ])
            },
            {
                "leaves between paragraphs",
                new DocumentNode([
                    ParagraphNode.FromText("above"),
                    RuleNode.Instance,
                    new BlockImageNode(new ImageEmbed("pic.png", altText: "PIC")),
                    ParagraphNode.FromText("below"),
                ])
            },
            {
                "surrogates and clusters",
                DocumentNode.FromParagraphs(
                    "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 family",
                    "e\u0301 combining",
                    "\u05e9\u05dc\u05d5\u05dd bidi")
            },
        };

        return data;
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryTextPositionSurvivesTheRoundTrip(string name, DocumentNode doc)
    {
        // The property that makes the map worth having: a position that is inside text comes back
        // as itself. Positions between blocks do not, and cannot - they have no character.
        var map = DocumentTextMap.Of(doc);

        for (var position = 0; position <= doc.ContentSize; position++)
        {
            if (!doc.Resolve(position).IsInText)
            {
                continue;
            }

            Assert.Equal(position, map.ToPosition(map.ToOffset(position)));
        }

        Assert.True(true, name);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryOffsetSurvivesTheRoundTrip(string name, DocumentNode doc)
    {
        var map = DocumentTextMap.Of(doc);

        for (var offset = 0; offset <= map.Text.Length; offset++)
        {
            Assert.Equal(offset, map.ToOffset(map.ToPosition(offset)));
        }

        Assert.True(true, name);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryPositionItReturnsIsOneTheDocumentHas(string name, DocumentNode doc)
    {
        // A map that returned a position outside the document, or one the schema does not accept,
        // would fail later and somewhere else.
        var map = DocumentTextMap.Of(doc);

        for (var offset = 0; offset <= map.Text.Length; offset++)
        {
            var position = map.ToPosition(offset);

            Assert.InRange(position, 0, doc.ContentSize);
            Assert.True(
                doc.Resolve(position).IsInText || doc.Blocks.IsEmpty,
                $"{name}: offset {offset} mapped to {position}, which is not a text position");
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void OrderIsPreservedInBothDirections(string name, DocumentNode doc)
    {
        var map = DocumentTextMap.Of(doc);
        var previous = -1;

        for (var offset = 0; offset <= map.Text.Length; offset++)
        {
            var position = map.ToPosition(offset);

            Assert.True(position >= previous, $"{name}: offset {offset} went backwards");
            previous = position;
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheTextMatchesDocumentTextWhereverThereAreNoEmbeds(string name, DocumentNode doc)
    {
        var map = DocumentTextMap.Of(doc);

        var embedded = doc.Blocks
            .SelectMany(Flatten)
            .OfType<ParagraphNode>()
            .Any(p => !p.Content.Embeds.IsEmpty);

        if (!embedded)
        {
            Assert.Equal(DocumentText.Of(doc), map.Text);
        }

        Assert.True(true, name);
    }

    [Fact]
    public void AnEmbedIsOneCharacterWhateverItsAltTextSays()
    {
        // The reason this type exists. DocumentText expands an embed to its fallback, so an image
        // with alt text occupies one position and five characters, and one without occupies one
        // position and none - neither of which can be inverted.
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                $"a{InlineContent.Placeholder}b", [], [new ImageEmbed("x.png", altText: "PHOTO")])),
        ]);

        var map = DocumentTextMap.Of(doc);

        Assert.Equal("aPHOTOb", DocumentText.Of(doc));
        Assert.Equal($"a{InlineContent.Placeholder}b", map.Text);
        Assert.Equal(doc.ContentSize - 2, map.Text.Length);
    }

    [Fact]
    public void ABlockBoundaryIsOneCharacterAndTwoPositions()
    {
        // "ab" and "cd": positions 1..3 are the first paragraph, 5..7 the second, and position 4
        // is the gap between them that no character corresponds to.
        var map = DocumentTextMap.Of(DocumentNode.FromParagraphs("ab", "cd"));

        Assert.Equal("ab\ncd", map.Text);
        Assert.Equal(1, map.ToPosition(0));
        Assert.Equal(3, map.ToPosition(2));
        Assert.Equal(5, map.ToPosition(3));
        Assert.Equal(7, map.ToPosition(5));
    }

    [Fact]
    public void AGapBetweenBlocksResolvesToTheEndOfTheTextBeforeIt()
    {
        var map = DocumentTextMap.Of(DocumentNode.FromParagraphs("ab", "cd"));

        // Position 4 sits between the paragraphs. A selection reaching it has reached the end of
        // "ab", which is offset 2.
        Assert.Equal(2, map.ToOffset(4));
        Assert.Equal(0, map.ToOffset(0));
    }

    [Fact]
    public void ARangeMapsToARangeAndBack()
    {
        var map = DocumentTextMap.Of(DocumentNode.FromParagraphs("hello world"));

        var (from, to) = map.ToPositions(6, 5);

        Assert.Equal("world", map.Slice(from, to));
        Assert.Equal((6, 5), map.ToOffsets(from, to));
    }

    [Fact]
    public void ARangeCanSpanBlocks()
    {
        var map = DocumentTextMap.Of(DocumentNode.FromParagraphs("one", "two", "three"));

        var (from, to) = map.ToPositions(2, 6);

        Assert.Equal("e\ntwo\n", map.Slice(from, to));
    }

    [Fact]
    public void ReplacingAMappedRangeKeepsTheFormattingAroundIt()
    {
        // The whole point of going through positions rather than splicing the text: a transform
        // applied to part of a paragraph must not flatten the rest of it.
        var doc = new DocumentNode([
            new ParagraphNode(
                InlineContent.Create(
                    "keep CHANGE keep",
                    [
                        new ValueSpan<MarkSet>(0, 4, new MarkSet { Style = TextStyle.Bold }),
                        new ValueSpan<MarkSet>(12, 4, new MarkSet { Style = TextStyle.Italic }),
                    ],
                    []),
                ParagraphKind.Heading2),
        ]);

        var map = DocumentTextMap.Of(doc);
        var (from, to) = map.ToPositions(5, 6);

        var state = EditorState.Create(doc);
        var transaction = state.Transaction();

        transaction.Step(new ReplaceStep(from, to, Slice.OfText("changed")));

        var edited = state.Apply(transaction).Doc;
        var paragraph = Assert.IsType<ParagraphNode>(edited.Blocks[0]);

        Assert.Equal("keep changed keep", paragraph.Content.Text);
        Assert.Equal(ParagraphKind.Heading2, paragraph.Kind);
        Assert.Equal(TextStyle.Bold, paragraph.Content.MarkAt(0).Style);
        Assert.Equal(TextStyle.Italic, paragraph.Content.MarkAt(13).Style);
        Assert.Equal(TextStyle.None, paragraph.Content.MarkAt(6).Style);
    }

    [Fact]
    public void ARangeWithAnEmbedInItIsFlaggedBeforeItThrows()
    {
        // Slice.OfText refuses text carrying a placeholder with no embed behind it - correctly,
        // but from a long way from here. The failing pattern is the obvious one: slice the text,
        // transform it, put it back. Upper-casing does not remove a placeholder, so the text comes
        // back still carrying one and the replace throws.
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                $"a{InlineContent.Placeholder}b", [], [new ImageEmbed("x.png", altText: "P")])),
        ]);

        var map = DocumentTextMap.Of(doc);
        var (from, to) = map.ToPositions(0, 3);

        Assert.True(map.ContainsEmbeds(from, to));
        Assert.Throws<ArgumentException>(() => Slice.OfText(map.Slice(from, to).ToUpperInvariant()));

        // A range beside the embed is clean, so a caller can still transform that.
        var (safeFrom, safeTo) = map.ToPositions(2, 1);

        Assert.False(map.ContainsEmbeds(safeFrom, safeTo));
        Assert.Equal("b", map.Slice(safeFrom, safeTo));
    }

    [Fact]
    public void ReplacingARangeWithAnEmbedDiscardsTheEmbed()
    {
        // The other honest answer, and it works: replacing with text that carries no placeholder
        // removes the image along with the characters. Pinned so it cannot change silently.
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                $"a{InlineContent.Placeholder}b", [], [new ImageEmbed("x.png", altText: "P")])),
        ]);

        var map = DocumentTextMap.Of(doc);
        var (from, to) = map.ToPositions(0, 3);
        var state = EditorState.Create(doc);
        var transaction = state.Transaction();

        transaction.Step(new ReplaceStep(from, to, Slice.OfText("Z")));

        var edited = state.Apply(transaction).Doc;

        Assert.Equal("Z", DocumentText.Of(edited));
        Assert.Empty(((ParagraphNode)edited.Blocks[0]).Content.Embeds);
    }

    [Fact]
    public void ReplacingAcrossBlocksLeavesOneBlock()
    {
        // Replacing the boundary is what joins the blocks, so this is inherent rather than a
        // defect - but a caller transforming a multi-paragraph selection needs to expect it.
        var doc = DocumentNode.FromParagraphs("one", "two", "three");
        var map = DocumentTextMap.Of(doc);
        var (from, to) = map.ToPositions(2, 6);
        var state = EditorState.Create(doc);
        var transaction = state.Transaction();

        transaction.Step(new ReplaceStep(from, to, Slice.OfText("X")));

        var edited = state.Apply(transaction).Doc;

        Assert.Single(edited.Blocks);
        Assert.Equal("onXthree", DocumentText.Of(edited));
    }

    [Fact]
    public void TheArgumentsAreChecked()
    {
        var map = DocumentTextMap.Of(DocumentNode.FromParagraphs("abc"));

        Assert.Throws<ArgumentNullException>(() => DocumentTextMap.Of(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToPosition(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToPosition(map.Text.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToOffset(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToPositions(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToOffsets(3, 1));
    }

    private static IEnumerable<Node> Flatten(Node node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
