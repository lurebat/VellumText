using Vellum.Avalonia;

namespace Vellum.Avalonia.Tests;

public class DocumentBlocksTests
{
    [Fact]
    public void ADocumentOfParagraphsFlattensToThoseParagraphs()
    {
        var doc = DocumentNode.FromParagraphs("one", "two", "three");
        var slots = DocumentBlocks.Flatten(doc);

        Assert.Equal(3, slots.Length);
        Assert.Equal([0, 0, 0], slots.Select(s => s.Depth));
        Assert.All(slots, s => Assert.Null(s.Marker));
    }

    [Fact]
    public void EveryBlockStartIsThePositionTheCoreAgreesItIs()
    {
        // The one assertion that matters. Rather than restating the arithmetic the walk does,
        // this asks the core's own position machinery where each block's content begins. If
        // the flatten drifts by a single position, every caret in every block after the drift
        // lands in the wrong place, and this is the cheapest possible way to catch it.
        var doc = Mixed();

        foreach (var slot in DocumentBlocks.Flatten(doc))
        {
            if (slot.Node.IsLeaf)
            {
                // A leaf occupies its position rather than containing it, so what the core
                // resolves *at* it is the block after it.
                Assert.Same(slot.Node, doc.Resolve(slot.Start).NodeAfter);
                continue;
            }

            var resolved = doc.Resolve(slot.Start);

            Assert.Same(slot.Node, resolved.Parent);
            Assert.Equal(0, resolved.ParentOffset);
            Assert.Equal(slot.End, doc.Resolve(slot.End).Pos);
            Assert.Same(slot.Node, doc.Resolve(slot.End).Parent);
        }
    }

    [Fact]
    public void BlocksComeOutInTheOrderTheyAreDrawnAndNeverOverlap()
    {
        var slots = DocumentBlocks.Flatten(Mixed());

        for (var i = 1; i < slots.Length; i++)
        {
            Assert.True(
                slots[i].Start > slots[i - 1].End,
                $"block {i} starts at {slots[i].Start}, inside block {i - 1} ending at {slots[i - 1].End}");
        }
    }

    [Fact]
    public void AListContributesNoBlockOfItsOwn()
    {
        var doc = new DocumentNode([
            ParagraphNode.FromText("before"),
            new ListNode([ListItemNode.FromText("a"), ListItemNode.FromText("b")]),
            ParagraphNode.FromText("after"),
        ]);

        var slots = DocumentBlocks.Flatten(doc);

        // Four paragraphs, not four paragraphs plus a list plus two items: structure is not
        // layout, and a list has no box.
        Assert.Equal(4, slots.Length);
        Assert.All(slots, s => Assert.IsType<ParagraphNode>(s.Node));
        Assert.Equal([0, 1, 1, 0], slots.Select(s => s.Depth));
    }

    [Fact]
    public void ListItemsAreNumberedFromTheListsOwnStart()
    {
        var doc = new DocumentNode([
            new ListNode(
                [ListItemNode.FromText("a"), ListItemNode.FromText("b"), ListItemNode.FromText("c")],
                ListKind.Ordered,
                start: 7),
        ]);

        var markers = DocumentBlocks.Flatten(doc).Select(s => s.Marker);

        Assert.Equal(
            [new ListMarker(ListKind.Ordered, 7), new ListMarker(ListKind.Ordered, 8), new ListMarker(ListKind.Ordered, 9)],
            markers);
    }

    [Fact]
    public void AnItemHoldingTwoParagraphsIsMarkedOnce()
    {
        var doc = new DocumentNode([
            new ListNode([
                new ListItemNode([ParagraphNode.FromText("first"), ParagraphNode.FromText("second")]),
                ListItemNode.FromText("next"),
            ]),
        ]);

        var slots = DocumentBlocks.Flatten(doc);

        Assert.Equal(3, slots.Length);
        Assert.NotNull(slots[0].Marker);
        Assert.Null(slots[1].Marker);
        Assert.NotNull(slots[2].Marker);
        Assert.Equal(2, slots[2].Marker!.Value.Ordinal);
    }

    [Fact]
    public void NestingDeepensTheDepthAndRestartsTheNumbering()
    {
        var doc = new DocumentNode([
            new ListNode([
                new ListItemNode([
                    ParagraphNode.FromText("outer"),
                    new ListNode([ListItemNode.FromText("inner")], ListKind.Ordered),
                ]),
            ]),
        ]);

        var slots = DocumentBlocks.Flatten(doc);

        Assert.Equal(2, slots.Length);
        Assert.Equal(1, slots[0].Depth);
        Assert.Equal(2, slots[1].Depth);
        Assert.Equal(new ListMarker(ListKind.Ordered, 1), slots[1].Marker);
    }

    [Fact]
    public void ALeafBlockOccupiesItsPositionRatherThanContainingOne()
    {
        var doc = new DocumentNode([
            ParagraphNode.FromText("ab"),
            RuleNode.Instance,
            ParagraphNode.FromText("cd"),
        ]);

        var slots = DocumentBlocks.Flatten(doc);

        // "ab" is a paragraph of 2, so it spans positions 0..4 inclusive of its boundaries; the
        // rule is the single position 4, and the next paragraph's content starts at 6.
        Assert.Equal(1, slots[0].Start);
        Assert.Equal(4, slots[1].Start);
        Assert.Equal(0, slots[1].ContentSize);
        Assert.Equal(6, slots[2].Start);
    }

    [Fact]
    public void AnEmptyListYieldsNothingRatherThanThrowing()
    {
        var doc = new DocumentNode([new ListNode([]), ParagraphNode.FromText("after")]);
        var slots = DocumentBlocks.Flatten(doc);

        Assert.Single(slots);
        Assert.Equal(0, slots[0].Depth);
    }

    [Fact]
    public void AnItemNestedDirectlyInsideAnotherDoesNotMarkItsSibling()
    {
        // Nothing in the schema forbids a list item holding another list item directly, so the
        // walk has to cope: the marker belongs to the first block reached, wherever that is,
        // and a sibling after it must not inherit it.
        var doc = new DocumentNode([
            new ListNode([
                new ListItemNode([
                    new ListItemNode([ParagraphNode.FromText("inner")]),
                    ParagraphNode.FromText("sibling"),
                ]),
            ]),
        ]);

        var slots = DocumentBlocks.Flatten(doc);

        Assert.Equal(2, slots.Length);
        Assert.Equal(new ListMarker(ListKind.Unordered, 1), slots[0].Marker);
        Assert.Null(slots[1].Marker);
    }

    [Fact]
    public void ADocumentIsRequired() =>
        Assert.Throws<ArgumentNullException>(() => DocumentBlocks.Flatten(null!));

    private static DocumentNode Mixed() =>
        new([
            ParagraphNode.FromText("opening"),
            RuleNode.Instance,
            new ListNode(
                [
                    ListItemNode.FromText("first"),
                    new ListItemNode([
                        ParagraphNode.FromText("second"),
                        new ListNode([ListItemNode.FromText("nested")], ListKind.Ordered, start: 3),
                    ]),
                ],
                ListKind.Unordered),
            ParagraphNode.FromText("closing"),
        ]);
}
