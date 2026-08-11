namespace Vellum.Tests;

public class SliceTests
{
    [Fact]
    public void AnEmptySliceInsertsNothing()
    {
        Assert.True(Slice.Empty.IsEmpty);
        Assert.Equal(0, Slice.Empty.Size);
    }

    [Fact]
    public void InlineContentIsWrappedInAParagraphOpenAtBothEnds()
    {
        var slice = Slice.OfText("abc");

        Assert.Equal(1, slice.OpenStart);
        Assert.Equal(1, slice.OpenEnd);
        Assert.Equal(3, slice.Size);
    }

    [Fact]
    public void WholeBlocksAreClosedAtBothEnds()
    {
        var slice = Slice.OfBlocks(ParagraphNode.FromText("ab"), ParagraphNode.FromText("cd"));

        Assert.Equal(0, slice.OpenStart);
        Assert.Equal(0, slice.OpenEnd);
        Assert.Equal(8, slice.Size);
    }

    [Fact]
    public void AParagraphSplitIsTwoEmptyParagraphsOpenAtBothEnds()
    {
        // Everything a split does is contained in the shape of the slice: the two open edges
        // merge into what surrounds them, leaving only the boundary between them behind.
        Assert.Equal(2, Slice.ParagraphSplit.Size);
    }

    [Fact]
    public void ASliceCannotBeOpenerThanItIsLarge()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Slice([ParagraphNode.Empty], 2, 2));
    }

    [Fact]
    public void ASliceCannotBeOpenerThanItIsDeep()
    {
        // An open depth is a claim about shape, not just about size. A list item with nothing
        // in it has one level to give, so being open two deep at that edge describes a fragment
        // of a child that is not there. Left unchecked the slice's size stops matching what
        // joining it into a document produces, and a step's map ends up disagreeing with the
        // document that step just made.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Slice([new ListItemNode([])], 0, 2));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Slice([new ListItemNode([])], 2, 0));

        // One level deep is exactly what it has, and stays legal.
        Assert.Equal(1, new Slice([new ListItemNode([])], 0, 1).Size);

        // Three levels of real nesting support three levels of openness.
        var nested = new ListNode([new ListItemNode([ParagraphNode.FromText("x")])]);

        Assert.Equal(4, new Slice([nested], 0, 3).Size);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Slice([nested], 0, 4));
    }

    [Fact]
    public void CuttingAnEmptyRangeGivesNothing()
    {
        Assert.Same(Slice.Empty, Slice.Cut(Sample.Nested, 5, 5));
    }

    [Fact]
    public void CuttingWithinOneParagraphGivesAnOpenParagraph()
    {
        var slice = Slice.Cut(Sample.Nested, 2, 4);

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(slice.Content));
        Assert.Equal("el", paragraph.Content.Text);
        Assert.Equal(1, slice.OpenStart);
        Assert.Equal(1, slice.OpenEnd);
        Assert.Equal(2, slice.Size);
    }

    [Fact]
    public void CuttingAcrossBlocksKeepsWholeNodesInTheMiddle()
    {
        // From inside "hello" to inside the list item: a paragraph fragment, the whole rule,
        // and the list opened just far enough to reach the fragment inside it.
        var slice = Slice.Cut(Sample.Nested, 3, 11);

        Assert.Equal(3, slice.Content.Length);
        Assert.Equal("llo", Assert.IsType<ParagraphNode>(slice.Content[0]).Content.Text);
        Assert.IsType<RuleNode>(slice.Content[1]);
        Assert.IsType<ListNode>(slice.Content[2]);
        Assert.Equal(1, slice.OpenStart);
        Assert.Equal(3, slice.OpenEnd);
    }

    [Fact]
    public void ACutIsAlwaysExactlyAsBigAsTheRangeItCameFrom()
    {
        for (var from = 0; from <= Sample.Nested.ContentSize; from++)
        {
            for (var to = from; to <= Sample.Nested.ContentSize; to++)
            {
                Assert.Equal(to - from, Slice.Cut(Sample.Nested, from, to).Size);
            }
        }
    }

    [Fact]
    public void AnInvertedRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Slice.Cut(Sample.Nested, 5, 4));
    }
}

public class ReplaceStepTests
{
    private static DocumentNode Apply(DocumentNode doc, ReplaceStep step)
    {
        var result = step.Apply(doc);

        Assert.True(result.IsOk, result.Failure);

        return result.Doc!;
    }

    [Fact]
    public void TypingIsAReplacementOfNothingWithOneCharacter()
    {
        var doc = DocumentNode.FromParagraphs("ac");

        var after = Apply(doc, ReplaceStep.Insert(2, Slice.OfText("b")));

        Assert.Equal(DocumentNode.FromParagraphs("abc"), after);
    }

    [Fact]
    public void DeletingWithinAParagraphLeavesTheRest()
    {
        var doc = DocumentNode.FromParagraphs("abcd");

        var after = Apply(doc, ReplaceStep.Delete(2, 4));

        Assert.Equal(DocumentNode.FromParagraphs("ad"), after);
    }

    [Fact]
    public void DeletingAcrossTwoParagraphsMergesThem()
    {
        var doc = DocumentNode.FromParagraphs("ab", "cd");

        // Position 3 is after "ab", position 5 is before "cd".
        var after = Apply(doc, ReplaceStep.Delete(3, 5));

        Assert.Equal(DocumentNode.FromParagraphs("abcd"), after);
    }

    [Fact]
    public void MergingParagraphsKeepsTheAttributesOfTheFirst()
    {
        var doc = new DocumentNode(
        [
            new ParagraphNode(InlineContent.FromText("ab"), ParagraphKind.Heading1),
            ParagraphNode.FromText("cd"),
        ]);

        var after = Apply(doc, ReplaceStep.Delete(3, 5));

        var merged = Assert.IsType<ParagraphNode>(Assert.Single(after.Blocks));
        Assert.Equal(ParagraphKind.Heading1, merged.Kind);
        Assert.Equal("abcd", merged.Content.Text);
    }

    [Fact]
    public void SplittingAParagraphIsAnInsertionOfAnOpenSlice()
    {
        var doc = DocumentNode.FromParagraphs("abcd");

        var after = Apply(doc, ReplaceStep.Insert(3, Slice.ParagraphSplit));

        Assert.Equal(DocumentNode.FromParagraphs("ab", "cd"), after);
    }

    [Fact]
    public void WholeBlocksGoInBetweenExistingOnesUnmerged()
    {
        var doc = DocumentNode.FromParagraphs("ab", "cd");

        // Position 4 is the boundary between the two paragraphs.
        var after = Apply(doc, ReplaceStep.Insert(4, Slice.OfBlocks(RuleNode.Instance)));

        Assert.Equal(3, after.Blocks.Length);
        Assert.IsType<RuleNode>(after.Blocks[1]);
    }

    [Fact]
    public void DeletingAcrossTwoListItemsMergesTheItems()
    {
        var doc = new DocumentNode(
            [new ListNode([ListItemNode.FromText("ab"), ListItemNode.FromText("cd")])]);

        // List content starts at 1; the first item runs 1..7, the second 7..13. Position 5 is
        // after "ab" inside the first item's paragraph, 9 is before "cd" inside the second's.
        var after = Apply(doc, ReplaceStep.Delete(5, 9));

        var list = Assert.IsType<ListNode>(Assert.Single(after.Blocks));
        var item = Assert.Single(list.Items);
        Assert.Equal("abcd", Assert.IsType<ParagraphNode>(Assert.Single(item.Blocks)).Content.Text);
    }

    [Fact]
    public void TypingReachesIntoATableCell()
    {
        var doc = new DocumentNode([new TableNode([new TableRowNode([TableCellNode.FromText("x")])])]);

        // Four boundary tokens stand between the document and the text: table, row, cell,
        // paragraph. So position 4 is just before the "x".
        var after = Apply(doc, ReplaceStep.Insert(4, Slice.OfText("y")));

        var table = Assert.IsType<TableNode>(Assert.Single(after.Blocks));
        var cell = table.Rows[0].Cells[0];
        Assert.Equal("yx", Assert.IsType<ParagraphNode>(cell.Blocks[0]).Content.Text);
    }

    [Fact]
    public void TheMapPredictsTheSizeChangeWithoutApplyingTheStep()
    {
        var doc = DocumentNode.FromParagraphs("abcd");
        var step = new ReplaceStep(2, 4, Slice.OfText("xyz"));

        var map = step.GetMap();
        var after = Apply(doc, step);

        Assert.Equal(map.SizeDelta, after.ContentSize - doc.ContentSize);
    }
}

public class ReplaceStepFailureTests
{
    [Fact]
    public void ARangeOutsideTheDocumentFails()
    {
        var doc = DocumentNode.FromParagraphs("ab");

        Assert.False(ReplaceStep.Delete(0, 99).Apply(doc).IsOk);
        Assert.False(new ReplaceStep(-1, 0, Slice.Empty).Apply(doc).IsOk);
        Assert.False(new ReplaceStep(3, 1, Slice.Empty).Apply(doc).IsOk);
    }

    [Fact]
    public void ASliceOpenDeeperThanItsDestinationFails()
    {
        var doc = DocumentNode.FromParagraphs("ab");

        // Position 0 is between blocks, so there is no paragraph there for an open edge to
        // merge into.
        Assert.False(ReplaceStep.Insert(0, Slice.OfText("x")).Apply(doc).IsOk);
    }

    [Fact]
    public void InconsistentOpenDepthsFail()
    {
        var doc = DocumentNode.FromParagraphs("ab");

        // Open at the start but closed at the end, from inside a paragraph to inside a
        // paragraph: there is no level at which the document closes back up.
        var lopsided = new Slice([ParagraphNode.FromText("x")], 1, 0);

        Assert.False(new ReplaceStep(2, 3, lopsided).Apply(doc).IsOk);
    }

    [Fact]
    public void DeletingAcrossIncompatibleNestingFails()
    {
        // From inside a list item's paragraph to inside a following top-level paragraph. The
        // two sides are at different depths, so there is no seam to close, and inventing one
        // would silently change the document's size after the step already promised otherwise.
        var doc = new DocumentNode(
        [
            new ListNode([ListItemNode.FromText("ab")]),
            ParagraphNode.FromText("cd"),
        ]);

        var result = ReplaceStep.Delete(4, 10).Apply(doc);

        Assert.False(result.IsOk);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public void AStepThatWouldEmptyTheDocumentFails()
    {
        // The schema requires at least one block so the caret always has somewhere to be. A
        // select-all delete is the command layer's problem, not the step's.
        var doc = DocumentNode.FromParagraphs("ab");

        var result = ReplaceStep.Delete(0, doc.ContentSize).Apply(doc);

        Assert.False(result.IsOk);
        Assert.Contains("not a valid document", result.Failure);
    }

    [Fact]
    public void CuttingThroughASurrogatePairFails()
    {
        var doc = DocumentNode.FromParagraphs("a\U0001F600b");

        // Position 3 is between the two halves of the emoji's surrogate pair.
        var result = ReplaceStep.Delete(1, 3).Apply(doc);

        Assert.False(result.IsOk);
    }

    [Fact]
    public void BlockNodesCannotBeInsertedInsideAParagraph()
    {
        var doc = DocumentNode.FromParagraphs("abcd");

        // Closed content at a position inside text: the slice would have to become a child of
        // the paragraph, and paragraphs hold text.
        var result = new ReplaceStep(2, 2, Slice.OfBlocks(RuleNode.Instance)).Apply(doc);

        Assert.False(result.IsOk);
    }
}

public class ReplaceStepRoundTripTests
{
    public static TheoryData<int, int> EveryRange()
    {
        var data = new TheoryData<int, int>();

        for (var from = 0; from <= Sample.Nested.ContentSize; from++)
        {
            for (var to = from; to <= Sample.Nested.ContentSize; to++)
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryRange))]
    public void CuttingARangeAndPuttingItBackChangesNothing(int from, int to)
    {
        // The strongest single statement about the whole machinery: whatever Slice.Cut takes
        // out, ReplaceStep must be able to put back, at every one of the 325 ranges in a
        // document nested four levels deep.
        var slice = Slice.Cut(Sample.Nested, from, to);
        var result = new ReplaceStep(from, to, slice).Apply(Sample.Nested);

        Assert.True(result.IsOk, result.Failure);
        Assert.Equal(Sample.Nested, result.Doc);
    }

    [Theory]
    [MemberData(nameof(EveryRange))]
    public void ADeletionThatSucceedsUndoesExactly(int from, int to)
    {
        var step = ReplaceStep.Delete(from, to);
        var result = step.Apply(Sample.Nested);

        if (!result.IsOk)
        {
            return;
        }

        var back = step.Invert(Sample.Nested).Apply(result.Doc!);

        Assert.True(back.IsOk, back.Failure);
        Assert.Equal(Sample.Nested, back.Doc);
    }

    [Theory]
    [MemberData(nameof(EveryRange))]
    public void ADeletionThatSucceedsChangesSizeByExactlyWhatItsMapSaid(int from, int to)
    {
        var step = ReplaceStep.Delete(from, to);
        var result = step.Apply(Sample.Nested);

        if (!result.IsOk)
        {
            return;
        }

        Assert.Equal(
            step.GetMap().SizeDelta,
            result.Doc!.ContentSize - Sample.Nested.ContentSize);
    }

    [Theory]
    [MemberData(nameof(EveryRange))]
    public void AnInsertionThatSucceedsUndoesExactly(int from, int to)
    {
        // `to` is used only to vary the replaced range; the inserted text is the same.
        var step = new ReplaceStep(from, to, Slice.OfText("zz"));
        var result = step.Apply(Sample.Nested);

        if (!result.IsOk)
        {
            return;
        }

        Assert.Equal(
            step.GetMap().SizeDelta,
            result.Doc!.ContentSize - Sample.Nested.ContentSize);

        var back = step.Invert(Sample.Nested).Apply(result.Doc!);

        Assert.True(back.IsOk, back.Failure);
        Assert.Equal(Sample.Nested, back.Doc);
    }

    [Fact]
    public void TextInsertionSucceedsExactlyWhenBothEndsAreInOneParagraph()
    {
        // An exact statement rather than a success rate. Inserting text is only meaningful
        // where text can go, and a raw step deliberately refuses to guess: expanding a range
        // that straddles a structural boundary into something insertable is a decision for the
        // command layer, which knows what the user was trying to do.
        var doc = Sample.Nested;

        for (var from = 0; from <= doc.ContentSize; from++)
        {
            for (var to = from; to <= doc.ContentSize; to++)
            {
                var start = doc.Resolve(from);
                var end = doc.Resolve(to);

                var sameParagraph = start.IsInText
                    && end.IsInText
                    && start.Depth == end.Depth
                    && start.Start(start.Depth) == end.Start(end.Depth);

                var applied = new ReplaceStep(from, to, Slice.OfText("zz")).Apply(doc).IsOk;

                Assert.True(
                    sameParagraph == applied,
                    $"[{from}, {to}): same paragraph {sameParagraph} but applied {applied}");
            }
        }
    }

    [Fact]
    public void DeletionAlwaysSucceedsWithinASingleParagraph()
    {
        var doc = Sample.Nested;
        var checkedRanges = 0;

        for (var from = 0; from <= doc.ContentSize; from++)
        {
            for (var to = from; to <= doc.ContentSize; to++)
            {
                var start = doc.Resolve(from);
                var end = doc.Resolve(to);

                if (!start.IsInText
                    || !end.IsInText
                    || start.Depth != end.Depth
                    || start.Start(start.Depth) != end.Start(end.Depth))
                {
                    continue;
                }

                var result = ReplaceStep.Delete(from, to).Apply(doc);

                Assert.True(result.IsOk, $"[{from}, {to}): {result.Failure}");
                checkedRanges++;
            }
        }

        Assert.Equal(27, checkedRanges);
    }
}

public class StepMappingTests
{
    [Fact]
    public void ARebasedStepMovesWithTheContentAroundIt()
    {
        var step = new ReplaceStep(10, 14, Slice.OfText("x"));

        var rebased = Assert.IsType<ReplaceStep>(step.Map(Mapping.Of(new StepMap(0, 0, 3))));

        Assert.Equal(13, rebased.From);
        Assert.Equal(17, rebased.To);
    }

    [Fact]
    public void AStepWithNothingLeftToActOnDisappears()
    {
        var step = new ReplaceStep(10, 14, Slice.OfText("x"));

        Assert.Null(step.Map(Mapping.Of(new StepMap(8, 10, 0))));
    }

    [Fact]
    public void AStepWhoseRangeWasPartlyEatenShrinksRatherThanInverting()
    {
        var step = new ReplaceStep(10, 14, Slice.OfText("x"));

        var rebased = Assert.IsType<ReplaceStep>(step.Map(Mapping.Of(new StepMap(12, 6, 0))));

        Assert.Equal(10, rebased.From);
        Assert.Equal(12, rebased.To);
        Assert.True(rebased.To >= rebased.From);
    }
}
