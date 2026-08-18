using System.Collections.Immutable;

namespace VellumText.Tests;

/// <summary>
/// Resolving a position used to walk a parent's children one at a time. It now consults a
/// prefix-sum index over their sizes once a node has proven expensive to scan, which is a
/// change of cost and must be no change of answer at all.
/// </summary>
/// <remarks>
/// <para>
/// The old walk is therefore the specification, and it is written out below rather than
/// referred to: an equivalence test that calls the implementation twice proves nothing. Every
/// offset of every shape is checked, because the interesting positions are exactly the ones a
/// hand-written case list forgets — a boundary between two children, the position inside a leaf
/// that does not exist, and the very last position in the document.
/// </para>
/// <para>
/// The shapes matter as much as the offsets. A document of one child never scans far enough to
/// build an index and so exercises the walk; a document of thousands builds one at the root and
/// not in its paragraphs; nested lists and tables build one at some levels and not others, which
/// is the case where a wrong index would show up as a caret one cell out rather than as a crash.
/// </para>
/// </remarks>
public class ResolvedPosEquivalenceTests
{
    public static TheoryData<string> EveryShape()
    {
        var data = new TheoryData<string>();

        foreach (var name in Shapes.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    private static Dictionary<string, DocumentNode> Shapes { get; } = new()
    {
        ["one child"] = new DocumentNode([ParagraphNode.FromText("only")]),
        ["one empty paragraph"] = DocumentNode.Empty,
        ["the nested sample"] = Sample.Nested,
        ["flat, below the index threshold"] = Flat(7),
        ["flat, one either side of the threshold"] = Flat(31),
        ["flat, exactly at the threshold"] = Flat(32),
        ["flat, just past the threshold"] = Flat(33),
        ["flat, well past the threshold"] = Flat(500),
        ["leaves between blocks"] = new DocumentNode(
            Enumerable
                .Range(0, 60)
                .Select(i => i % 2 == 0 ? (BlockNode)ParagraphNode.FromText($"p{i}") : RuleNode.Instance)
                .ToArray()),
        ["a long list"] = new DocumentNode(
        [
            new ListNode(Enumerable.Range(0, 200).Select(i => ListItemNode.FromText($"item {i}"))),
        ]),
        ["nested lists"] = new DocumentNode([NestedList(depth: 4, width: 40)]),
        ["a wide table"] = new DocumentNode([Table(rows: 40, columns: 5)]),
        ["a table inside a list"] = new DocumentNode(
        [
            new ListNode(
            [
                new ListItemNode([ParagraphNode.FromText("before"), Table(rows: 50, columns: 3)]),
                ListItemNode.FromText("after"),
            ]),
        ]),
        ["empty paragraphs, the smallest children there are"] = new DocumentNode(
            Enumerable.Range(0, 300).Select(_ => (BlockNode)ParagraphNode.Empty).ToArray()),
    };

    [Theory]
    [MemberData(nameof(EveryShape))]
    public void EveryShapeIsSchemaValid(string shape)
    {
        // Position arithmetic checked against a document the schema would reject would be
        // agreement about a shape that can never occur.
        Assert.Empty(DocumentSchema.Validate(Shapes[shape]));
    }

    [Theory]
    [MemberData(nameof(EveryShape))]
    public void EveryPositionResolvesToWhatTheOldWalkFound(string shape)
    {
        var doc = Shapes[shape];

        for (var pos = 0; pos <= doc.ContentSize; pos++)
        {
            AssertSame(Reference.Resolve(doc, pos), doc.Resolve(pos), pos);
        }
    }

    [Theory]
    [MemberData(nameof(EveryShape))]
    public void ResolvingFromTheEndBackwardsFindsTheSameThings(string shape)
    {
        // The index is built by whichever lookup first scans far enough, so the order positions
        // are asked for decides which answers come from the walk and which from the index. Going
        // backwards builds it on the very first call, where forwards builds it partway through.
        var doc = Shapes[shape];

        for (var pos = doc.ContentSize; pos >= 0; pos--)
        {
            AssertSame(Reference.Resolve(doc, pos), doc.Resolve(pos), pos);
        }
    }

    [Theory]
    [MemberData(nameof(EveryShape))]
    public void TheSameAnswerComesBackOnceTheIndexIsWarm(string shape)
    {
        var doc = Shapes[shape];

        // The first pass leaves an index behind on every node that was expensive to walk; the
        // second reads it. A stale or mis-built index shows up here and nowhere else.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var pos = 0; pos <= doc.ContentSize; pos++)
            {
                AssertSame(Reference.Resolve(doc, pos), doc.Resolve(pos), pos);
            }
        }
    }

    [Fact]
    public void EditingADocumentDoesNotCarryAnIndexOverToItsNewShape()
    {
        // Nodes are structurally shared, so the paragraphs here survive the edit and the root
        // does not. An index that outlived the children it describes would put the caret in the
        // wrong block from the next keystroke onward.
        var doc = Flat(200);

        for (var pos = 0; pos <= doc.ContentSize; pos++)
        {
            doc.Resolve(pos);
        }

        var edited = (DocumentNode)doc.WithChildren(
            doc.Blocks.Insert(3, ParagraphNode.FromText("a brand new paragraph")).CastArray<Node>());

        for (var pos = 0; pos <= edited.ContentSize; pos++)
        {
            AssertSame(Reference.Resolve(edited, pos), edited.Resolve(pos), pos);
        }
    }

    private static void AssertSame(Reference.Path expected, ResolvedPos actual, int pos)
    {
        Assert.Equal(expected.Levels.Length - 1, actual.Depth);
        Assert.Equal(pos, actual.Pos);

        for (var depth = 0; depth < expected.Levels.Length; depth++)
        {
            var level = expected.Levels[depth];

            Assert.Same(level.Node, actual.NodeAt(depth));
            Assert.Equal(level.Index, actual.IndexAt(depth));
            Assert.Equal(level.Start, actual.Start(depth));
        }
    }

    private static DocumentNode Flat(int paragraphs) =>
        new(Enumerable
            .Range(0, paragraphs)
            .Select(i => (BlockNode)ParagraphNode.FromText($"paragraph number {i}"))
            .ToArray());

    private static ListNode NestedList(int depth, int width)
    {
        var items = Enumerable
            .Range(0, width)
            .Select(i => ListItemNode.FromText($"level {depth} item {i}"))
            .ToList();

        if (depth > 1)
        {
            items.Add(new ListItemNode([NestedList(depth - 1, width)]));
        }

        return new ListNode(items);
    }

    private static TableNode Table(int rows, int columns) =>
        new(Enumerable
            .Range(0, rows)
            .Select(r => new TableRowNode(Enumerable
                .Range(0, columns)
                .Select(c => TableCellNode.FromText($"r{r}c{c}")))));

    /// <summary>
    /// The position walk exactly as it was before there was an index: for each level, step
    /// through the children adding up their sizes until one of them covers the offset.
    /// </summary>
    /// <remarks>
    /// A deliberate copy rather than a call. This is the thing being compared against, so it has
    /// to be able to disagree with the implementation — sharing code with it would make the test
    /// pass for any index, correct or not.
    /// </remarks>
    private static class Reference
    {
        internal readonly record struct Level(Node Node, int Index, int Start);

        internal readonly record struct Path(ImmutableArray<Level> Levels);

        internal static Path Resolve(DocumentNode doc, int pos)
        {
            var levels = ImmutableArray.CreateBuilder<Level>();
            Node node = doc;
            var start = 0;

            while (true)
            {
                var offset = pos - start;

                if (node is ParagraphNode)
                {
                    levels.Add(new Level(node, 0, start));
                    break;
                }

                var index = 0;
                var consumed = 0;

                while (index < node.Children.Length)
                {
                    var size = node.Children[index].NodeSize;

                    if (consumed + size > offset)
                    {
                        break;
                    }

                    consumed += size;
                    index++;
                }

                if (index == node.Children.Length || consumed == offset)
                {
                    levels.Add(new Level(node, index, start));
                    break;
                }

                levels.Add(new Level(node, index, start));
                start += consumed + 1;
                node = node.Children[index];
            }

            return new Path(levels.ToImmutable());
        }
    }
}
