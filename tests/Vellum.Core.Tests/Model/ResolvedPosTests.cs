namespace Vellum.Tests;

/// <summary>
/// Positions are the pivotal choice in the model, so the expected numbers here are worked
/// out by hand from the document's shape rather than derived from the implementation. If the
/// size scheme ever changes, these should be the first thing to break.
/// </summary>
public static class Sample
{
    /// <summary>
    /// A document exercising every level of nesting the schema allows.
    /// </summary>
    /// <remarks>
    /// <code>
    ///  0 &lt;para&gt;             para: content 5, size 7
    ///  1  h e l l o
    ///  6 &lt;/para&gt;
    ///  7 &lt;rule/&gt;            leaf: size 1
    ///  8 &lt;list&gt;             list: content 5, size 7
    ///  9  &lt;item&gt;            item: content 3, size 5
    /// 10   &lt;para&gt;           para: content 1, size 3
    /// 11    a
    /// 12   &lt;/para&gt;
    /// 13  &lt;/item&gt;
    /// 14 &lt;/list&gt;
    /// 15 &lt;table&gt;            table: content 7, size 9
    /// 16  &lt;row&gt;             row:   content 5, size 7
    /// 17   &lt;cell&gt;           cell:  content 3, size 5
    /// 18    &lt;para&gt;          para:  content 1, size 3
    /// 19     x
    /// 20    &lt;/para&gt;
    /// 21   &lt;/cell&gt;
    /// 22  &lt;/row&gt;
    /// 23 &lt;/table&gt;
    /// 24 (end of document)
    /// </code>
    /// </remarks>
    public static DocumentNode Nested { get; } = new(
    [
        ParagraphNode.FromText("hello"),
        RuleNode.Instance,
        new ListNode([ListItemNode.FromText("a")]),
        new TableNode([new TableRowNode([TableCellNode.FromText("x")])]),
    ]);
}

public class ResolvedPosStructureTests
{
    [Fact]
    public void TheSampleDocumentHasTheSizeTheDiagramClaims()
    {
        Assert.Equal(24, Sample.Nested.ContentSize);
    }

    [Fact]
    public void TheSampleDocumentIsSchemaValid()
    {
        // Position arithmetic tested against a document the schema would reject would be
        // testing a shape that can never occur.
        Assert.Empty(DocumentSchema.Validate(Sample.Nested));
    }

    [Fact]
    public void PositionZeroIsInTheDocumentBeforeTheFirstBlock()
    {
        var at = Sample.Nested.Resolve(0);

        Assert.Equal(0, at.Depth);
        Assert.IsType<DocumentNode>(at.Parent);
        Assert.Equal(0, at.ParentOffset);
        Assert.Equal(0, at.ParentIndex);
        Assert.Null(at.NodeBefore);
        Assert.IsType<ParagraphNode>(at.NodeAfter);
    }

    [Fact]
    public void PositionOneIsTheStartOfTheFirstParagraphsText()
    {
        var at = Sample.Nested.Resolve(1);

        Assert.Equal(1, at.Depth);
        Assert.True(at.IsInText);
        Assert.Equal(0, at.ParentOffset);
        Assert.Equal("hello", at.Paragraph!.Content.Text);
    }

    [Fact]
    public void PositionSixIsTheEndOfTheFirstParagraphsText()
    {
        var at = Sample.Nested.Resolve(6);

        Assert.Equal(1, at.Depth);
        Assert.Equal(5, at.ParentOffset);
        Assert.Equal(1, at.Start(1));
        Assert.Equal(6, at.End(1));
        Assert.Equal(0, at.Before(1));
        Assert.Equal(7, at.After(1));
    }

    [Fact]
    public void PositionSevenIsBetweenTheParagraphAndTheRule()
    {
        var at = Sample.Nested.Resolve(7);

        Assert.Equal(0, at.Depth);
        Assert.Equal(1, at.ParentIndex);
        Assert.IsType<ParagraphNode>(at.NodeBefore);
        Assert.IsType<RuleNode>(at.NodeAfter);
    }

    [Fact]
    public void ALeafHasNoInteriorSoTheOnlyPositionsAreEitherSideOfIt()
    {
        // The rule occupies exactly one position, 7 to 8. Neither of those resolves into it.
        Assert.Equal(0, Sample.Nested.Resolve(7).Depth);
        Assert.Equal(0, Sample.Nested.Resolve(8).Depth);
        Assert.IsType<RuleNode>(Sample.Nested.Resolve(8).NodeBefore);
    }

    [Fact]
    public void PositionNineIsInsideTheListBeforeItsItem()
    {
        var at = Sample.Nested.Resolve(9);

        Assert.Equal(1, at.Depth);
        Assert.IsType<ListNode>(at.Parent);
        Assert.Equal(0, at.ParentOffset);
        Assert.IsType<ListItemNode>(at.NodeAfter);
    }

    [Fact]
    public void PositionElevenIsTheTextInsideTheListItemsParagraph()
    {
        var at = Sample.Nested.Resolve(11);

        Assert.Equal(3, at.Depth);
        Assert.IsType<DocumentNode>(at.NodeAt(0));
        Assert.IsType<ListNode>(at.NodeAt(1));
        Assert.IsType<ListItemNode>(at.NodeAt(2));
        Assert.IsType<ParagraphNode>(at.NodeAt(3));
        Assert.Equal(0, at.ParentOffset);
    }

    [Fact]
    public void PositionNineteenIsFourLevelsDownInsideTheTableCell()
    {
        var at = Sample.Nested.Resolve(19);

        Assert.Equal(4, at.Depth);
        Assert.IsType<TableNode>(at.NodeAt(1));
        Assert.IsType<TableRowNode>(at.NodeAt(2));
        Assert.IsType<TableCellNode>(at.NodeAt(3));
        Assert.Equal("x", at.Paragraph!.Content.Text);
        Assert.Equal(0, at.ParentOffset);

        Assert.Equal(16, at.Start(1));
        Assert.Equal(17, at.Start(2));
        Assert.Equal(18, at.Start(3));
        Assert.Equal(19, at.Start(4));

        Assert.Equal(23, at.End(1));
        Assert.Equal(22, at.End(2));
        Assert.Equal(21, at.End(3));
        Assert.Equal(20, at.End(4));
    }

    [Fact]
    public void TheLastPositionIsInTheDocumentAfterEverything()
    {
        var at = Sample.Nested.Resolve(24);

        Assert.Equal(0, at.Depth);
        Assert.Equal(4, at.ParentIndex);
        Assert.IsType<TableNode>(at.NodeBefore);
        Assert.Null(at.NodeAfter);
    }

    [Fact]
    public void AnEmptyParagraphStillHasAPositionInsideIt()
    {
        var doc = new DocumentNode([ParagraphNode.Empty]);

        Assert.Equal(2, doc.ContentSize);

        var at = doc.Resolve(1);

        Assert.Equal(1, at.Depth);
        Assert.True(at.IsInText);
        Assert.Equal(0, at.ParentOffset);
        Assert.Equal(at.Start(1), at.End(1));
    }
}

public class ResolvedPosInvariantTests
{
    public static TheoryData<int> EveryPosition()
    {
        var data = new TheoryData<int>();

        for (var pos = 0; pos <= Sample.Nested.ContentSize; pos++)
        {
            data.Add(pos);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPosition))]
    public void TheParentOffsetAlwaysReconstructsThePosition(int pos)
    {
        var at = Sample.Nested.Resolve(pos);

        Assert.Equal(pos, at.Start(at.Depth) + at.ParentOffset);
        Assert.Same(at.Parent, at.NodeAt(at.Depth));
    }

    [Theory]
    [MemberData(nameof(EveryPosition))]
    public void EveryAncestorEnclosesThePosition(int pos)
    {
        var at = Sample.Nested.Resolve(pos);

        for (var depth = 0; depth <= at.Depth; depth++)
        {
            Assert.InRange(pos, at.Start(depth), at.End(depth));
        }
    }

    [Theory]
    [MemberData(nameof(EveryPosition))]
    public void EveryAncestorSpansExactlyItsOwnNodeSize(int pos)
    {
        var at = Sample.Nested.Resolve(pos);

        for (var depth = 1; depth <= at.Depth; depth++)
        {
            Assert.Equal(at.NodeAt(depth).NodeSize, at.After(depth) - at.Before(depth));
        }
    }

    [Theory]
    [MemberData(nameof(EveryPosition))]
    public void TheAncestorChainIsActuallyAChain(int pos)
    {
        var at = Sample.Nested.Resolve(pos);

        for (var depth = 1; depth <= at.Depth; depth++)
        {
            var parent = at.NodeAt(depth - 1);

            Assert.Same(parent.Children[at.IndexAt(depth - 1)], at.NodeAt(depth));
        }
    }

    [Theory]
    [MemberData(nameof(EveryPosition))]
    public void OnlyParagraphsEverEndUpAsATextParent(int pos)
    {
        var at = Sample.Nested.Resolve(pos);

        Assert.Equal(at.Parent is ParagraphNode, at.IsInText);

        if (at.IsInText)
        {
            Assert.InRange(at.ParentOffset, 0, at.Paragraph!.Content.Text.Length);
        }
        else
        {
            Assert.InRange(at.ParentIndex, 0, at.Parent.Children.Count);
        }
    }
}

public class ResolvedPosBoundaryTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    public void APositionOutsideTheDocumentIsRefused(int pos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample.Nested.Resolve(pos));
    }

    [Fact]
    public void ThereIsNothingOutsideTheDocumentToAddress()
    {
        var at = Sample.Nested.Resolve(11);

        Assert.Throws<ArgumentOutOfRangeException>(() => at.Before(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => at.After(0));
    }

    [Fact]
    public void ADepthDeeperThanThePositionIsRefused()
    {
        var at = Sample.Nested.Resolve(7);

        Assert.Equal(0, at.Depth);
        Assert.Throws<ArgumentOutOfRangeException>(() => at.NodeAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => at.Start(-1));
    }
}

public class SharedDepthTests
{
    [Fact]
    public void TwoPositionsInTheSameParagraphShareItsFullDepth()
    {
        var from = Sample.Nested.Resolve(11);
        var to = Sample.Nested.Resolve(12);

        Assert.Equal(3, from.SharedDepth(to));
    }

    [Fact]
    public void PositionsInDifferentTopLevelBlocksShareOnlyTheDocument()
    {
        var from = Sample.Nested.Resolve(2);
        var to = Sample.Nested.Resolve(11);

        Assert.Equal(0, from.SharedDepth(to));
    }

    [Fact]
    public void TheSharedAncestorIsTheDeepestOneThatContainsBoth()
    {
        // 11 is inside the item's paragraph; 13 is in the item, after that paragraph.
        var from = Sample.Nested.Resolve(11);
        var to = Sample.Nested.Resolve(13);

        var shared = from.SharedDepth(to);

        Assert.Equal(2, shared);
        Assert.IsType<ListItemNode>(from.NodeAt(shared));
    }

    [Fact]
    public void SharedDepthDoesNotCareWhichWayRoundItIsAsked()
    {
        var from = Sample.Nested.Resolve(11);
        var to = Sample.Nested.Resolve(19);

        Assert.Equal(from.SharedDepth(to), to.SharedDepth(from));
        Assert.Equal(0, from.SharedDepth(to));
    }
}
