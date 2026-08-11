using System.Collections.Immutable;

namespace Vellum.Tests.Transform;

/// <summary>
/// A document whose formatting is deliberately patchy, so that inverting a formatting command
/// has something it could plausibly get wrong.
/// </summary>
public static class MixedSample
{
    /// <summary>Bold.</summary>
    public static MarkSet Bold { get; } = new() { Style = TextStyle.Bold };

    /// <summary>Italic and red.</summary>
    public static MarkSet ItalicRed { get; } = new()
    {
        Style = TextStyle.Italic,
        Foreground = Rgba.FromRgb(0xFF, 0x00, 0x00),
    };

    /// <summary>
    /// A three-paragraph document, the middle one inside a list, with formatting that changes
    /// several times inside single paragraphs.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 0                          doc, ContentSize 25
    /// 0  &lt;p&gt;   1 a l p h a 6      "alpha", offsets 0..2 bold
    /// 7  &lt;list&gt; 8 &lt;li&gt; 9 &lt;p&gt;
    ///                  10 b r a v o 15   "bravo", offsets 1..4 italic red
    ///        16 &lt;/p&gt; 17 &lt;/li&gt; 18 &lt;/list&gt;
    /// 18 &lt;p&gt;  19 c a r l o 24     "carlo", unformatted
    /// 25 end
    /// </code>
    /// </remarks>
    public static DocumentNode Doc { get; } = new(
    [
        new ParagraphNode(
            InlineContent.FromText("alpha").ApplyMarks(0, 2, Bold, MarkFields.All)),
        new ListNode(
        [
            new ListItemNode(
            [
                new ParagraphNode(
                    InlineContent.FromText("bravo").ApplyMarks(1, 3, ItalicRed, MarkFields.All)),
            ]),
        ]),
        ParagraphNode.FromText("carlo"),
    ]);
}

/// <summary>Applying formatting.</summary>
public class MarkStepTests
{
    private static MarkSet MarkAt(DocumentNode doc, int pos)
    {
        var at = doc.Resolve(pos);

        return at.Paragraph!.Content.MarkAt(at.ParentOffset);
    }

    [Fact]
    public void BoldingARangeMarksExactlyThatRange()
    {
        // "alpha" occupies 1..6; bold 3..5, which is "ph".
        var result = new AddMarkStep(3, 5, MixedSample.Bold, MarkFields.Bold)
            .Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);

        var doc = result.Doc!;
        Assert.True(MarkAt(doc, 2).Has(TextStyle.Bold), "already bold before the step");
        Assert.False(MarkAt(doc, 2).Has(TextStyle.Italic));
        Assert.True(MarkAt(doc, 3).Has(TextStyle.Bold));
        Assert.True(MarkAt(doc, 4).Has(TextStyle.Bold));
        Assert.False(MarkAt(doc, 5).Has(TextStyle.Bold));
    }

    [Fact]
    public void AnEmptyRangeLeavesTheDocumentUntouched()
    {
        // Formatting a range of nothing is nothing, and the paragraph must come back as the very
        // node it went in as. Rewriting it anyway is invisible in the result of the step itself
        // but corrupts undo: the marks a restore step carries are addressed to a range, and
        // handing it an empty one writes them somewhere they do not belong.
        var doc = MixedSample.Doc;
        var captured = new AddMarkStep(3, 5, MixedSample.Bold, MarkFields.Bold).Invert(doc);

        var results = new[]
        {
            new AddMarkStep(3, 3, MixedSample.Bold, MarkFields.Bold).Apply(doc),
            new RemoveMarkStep(3, 3, MarkFields.Bold).Apply(doc),
            ((RestoreMarksStep)captured with { From = 3, To = 3 }).Apply(doc),
        };

        foreach (var result in results)
        {
            Assert.True(result.IsOk, result.Failure);
            Assert.Same(doc, result.Doc);
        }
    }

    [Fact]
    public void AFieldMaskLeavesTheFieldsItDoesNotName()
    {
        // The first two characters are already bold. Setting only the colour must not clear that.
        var red = new MarkSet { Foreground = Rgba.FromRgb(0xFF, 0x00, 0x00) };
        var result = new AddMarkStep(1, 3, red, MarkFields.Foreground).Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.True(MarkAt(result.Doc!, 1).Has(TextStyle.Bold));
        Assert.Equal(Rgba.FromRgb(0xFF, 0x00, 0x00), MarkAt(result.Doc!, 1).Foreground);
    }

    [Fact]
    public void FormattingReachesIntoNestedBlocks()
    {
        // 1..24 spans all three paragraphs, one of which is two levels down inside a list.
        var result = new AddMarkStep(1, 24, MixedSample.Bold, MarkFields.Bold)
            .Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);

        foreach (var pos in (int[])[1, 5, 10, 14, 19, 23])
        {
            Assert.True(MarkAt(result.Doc!, pos).Has(TextStyle.Bold), $"position {pos}");
        }
    }

    [Fact]
    public void FormattingDoesNotLeakPastTheRangeIntoTheNextParagraph()
    {
        // 1..7 ends on the paragraph's closing boundary. The list that follows must be untouched.
        var result = new AddMarkStep(1, 7, MixedSample.Bold, MarkFields.Bold)
            .Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.False(MarkAt(result.Doc!, 10).Has(TextStyle.Bold));
    }

    [Fact]
    public void RemovingAFieldClearsIt()
    {
        var result = new RemoveMarkStep(1, 3, MarkFields.Bold).Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.False(MarkAt(result.Doc!, 1).Has(TextStyle.Bold));
    }

    [Fact]
    public void RemovingAFieldLeavesTheOthers()
    {
        // "bravo" 11..14 is italic AND red. Clearing italic must keep the colour.
        var result = new RemoveMarkStep(11, 14, MarkFields.Italic).Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.False(MarkAt(result.Doc!, 11).Has(TextStyle.Italic));
        Assert.Equal(Rgba.FromRgb(0xFF, 0x00, 0x00), MarkAt(result.Doc!, 11).Foreground);
    }

    [Fact]
    public void AnEmptyRangeChangesNothing()
    {
        var result = new AddMarkStep(3, 3, MixedSample.Bold, MarkFields.Bold)
            .Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.Same(MixedSample.Doc, result.Doc);
    }

    [Fact]
    public void AnEmptyFieldMaskChangesNothing()
    {
        var result = new AddMarkStep(1, 6, MixedSample.Bold, MarkFields.None)
            .Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.Same(MixedSample.Doc, result.Doc);
    }

    [Fact]
    public void UntouchedSubtreesAreNotRebuilt()
    {
        // Formatting inside the list must leave the two outer paragraphs shared by reference.
        var result = new AddMarkStep(11, 14, MixedSample.Bold, MarkFields.Bold)
            .Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.Same(MixedSample.Doc.Children[0], result.Doc!.Children[0]);
        Assert.Same(MixedSample.Doc.Children[2], result.Doc!.Children[2]);
    }

    [Fact]
    public void FormattingReportsTheIdentityMap()
    {
        var map = new AddMarkStep(1, 6, MixedSample.Bold, MarkFields.Bold).GetMap();

        Assert.True(map.IsIdentity);
        Assert.Equal(3, map.Map(3));
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(3, 1)]
    [InlineData(1, 99)]
    public void ARangeOutsideTheDocumentFails(int from, int to)
    {
        Assert.False(new AddMarkStep(from, to, MixedSample.Bold, MarkFields.Bold)
            .Apply(MixedSample.Doc).IsOk);
    }

    [Fact]
    public void AnEndpointInsideASurrogatePairFails()
    {
        var doc = new DocumentNode([ParagraphNode.FromText("a\U0001F600b")]);

        // The emoji occupies 2..4, so 3 is between its two code units.
        var result = new AddMarkStep(1, 3, MixedSample.Bold, MarkFields.Bold).Apply(doc);

        Assert.False(result.IsOk);
    }
}

/// <summary>Undoing formatting.</summary>
public class MarkStepInversionTests
{
    /// <summary>Every range of the mixed document, so nothing can hide behind a lucky choice.</summary>
    public static TheoryData<int, int> Ranges
    {
        get
        {
            var data = new TheoryData<int, int>();

            for (var from = 0; from <= MixedSample.Doc.ContentSize; from++)
            {
                for (var to = from; to <= MixedSample.Doc.ContentSize; to++)
                {
                    data.Add(from, to);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void AddingThenUndoingRestoresTheDocumentExactly(int from, int to)
    {
        var step = new AddMarkStep(from, to, MixedSample.Bold, MarkFields.All);
        var applied = step.Apply(MixedSample.Doc);

        Assert.True(applied.IsOk, applied.Failure);

        var inverse = Assert.IsType<RestoreMarksStep>(step.Invert(MixedSample.Doc));

        // Guards against a vacuous round trip. Setting every field to plain bold changes the
        // document exactly when some character in range was not already exactly plain bold, so
        // the ranges that prove nothing are identified rather than quietly counted as passes.
        var shouldChange = inverse.Spans.Any(span => !span.Value.Equals(MixedSample.Bold));
        Assert.Equal(shouldChange, !MixedSample.Doc.Equals(applied.Doc));

        var undone = inverse.Apply(applied.Doc!);

        Assert.True(undone.IsOk, undone.Failure);
        Assert.Equal(MixedSample.Doc, undone.Doc);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void RemovingThenUndoingRestoresTheDocumentExactly(int from, int to)
    {
        var step = new RemoveMarkStep(from, to, MarkFields.All);
        var applied = step.Apply(MixedSample.Doc);

        Assert.True(applied.IsOk, applied.Failure);

        var undone = step.Invert(MixedSample.Doc).Apply(applied.Doc!);

        Assert.True(undone.IsOk, undone.Failure);
        Assert.Equal(MixedSample.Doc, undone.Doc);
    }

    [Fact]
    public void UndoingAcrossAMixtureIsNotAFlatRemoval()
    {
        // The point of capturing spans: 1..6 is half bold, so its inverse cannot be one
        // RemoveMarkStep. Redoing then undoing must reproduce the mixture, not flatten it.
        var step = new AddMarkStep(1, 6, MixedSample.Bold, MarkFields.Bold);
        var bolded = step.Apply(MixedSample.Doc).Doc!;
        var restored = step.Invert(MixedSample.Doc).Apply(bolded).Doc!;

        var content = ((ParagraphNode)restored.Children[0]).Content;

        Assert.True(content.MarkAt(0).Has(TextStyle.Bold));
        Assert.True(content.MarkAt(1).Has(TextStyle.Bold));
        Assert.False(content.MarkAt(2).Has(TextStyle.Bold));
    }

    [Fact]
    public void ACapturedRunIsRecordedInDocumentCoordinates()
    {
        // Spanning two paragraphs whose text starts at different document positions is what
        // would expose paragraph-local offsets colliding.
        var step = new AddMarkStep(1, 24, MixedSample.Bold, MarkFields.All);
        var inverse = Assert.IsType<RestoreMarksStep>(step.Invert(MixedSample.Doc));

        Assert.All(inverse.Spans, span => Assert.InRange(span.Start, 1, 24));
        Assert.Equal(
            inverse.Spans.Select(span => span.Start).OrderBy(start => start).ToList(),
            inverse.Spans.Select(span => span.Start).ToList());
    }

    [Fact]
    public void RestoringIsItsOwnInverse()
    {
        var step = new AddMarkStep(1, 6, MixedSample.Bold, MarkFields.All);
        var bolded = step.Apply(MixedSample.Doc).Doc!;
        var inverse = step.Invert(MixedSample.Doc);

        var redone = inverse.Invert(bolded).Apply(inverse.Apply(bolded).Doc!);

        Assert.True(redone.IsOk, redone.Failure);
        Assert.Equal(bolded, redone.Doc);
    }

    [Fact]
    public void TwoRestoreStepsWithEqualSpansAreEqual()
    {
        // ImmutableArray compares by reference, so the record's generated equality would say no.
        var spans = new[] { new ValueSpan<MarkSet>(1, 2, MixedSample.Bold) };
        var a = new RestoreMarksStep(1, 3, [.. spans]);
        var b = new RestoreMarksStep(1, 3, [.. spans]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}

/// <summary>Rebasing formatting over other edits.</summary>
public class MarkStepMappingTests
{
    [Fact]
    public void ARangeShiftsWhenTextIsInsertedBeforeIt()
    {
        var mapping = Mapping.Of(new StepMap(1, 0, 3));
        var mapped = Assert.IsType<AddMarkStep>(
            new AddMarkStep(4, 6, MixedSample.Bold, MarkFields.Bold).Map(mapping));

        Assert.Equal(7, mapped.From);
        Assert.Equal(9, mapped.To);
    }

    [Fact]
    public void ARangeShrinksWhenTextInsideItIsDeleted()
    {
        var mapping = Mapping.Of(new StepMap(2, 2, 0));
        var mapped = Assert.IsType<AddMarkStep>(
            new AddMarkStep(1, 6, MixedSample.Bold, MarkFields.Bold).Map(mapping));

        Assert.Equal(1, mapped.From);
        Assert.Equal(4, mapped.To);
    }

    [Fact]
    public void ARangeThatIsEntirelyDeletedDisappears()
    {
        var mapping = Mapping.Of(new StepMap(1, 5, 0));

        Assert.Null(new AddMarkStep(2, 5, MixedSample.Bold, MarkFields.Bold).Map(mapping));
    }

    [Fact]
    public void ARangeWhoseEndsCrossOverDisappears()
    {
        // The two ends bind to opposite edges so that text typed inside a bold range inherits
        // the bold. An empty range has both ends at the same place, so a pure insertion there
        // pushes them past one another: from moves with the insertion, to stays. The result was
        // an inverted range that no consumer could make sense of.
        var mapping = Mapping.Of(new StepMap(3, 0, 2));

        Assert.Null(new AddMarkStep(3, 3, MixedSample.Bold, MarkFields.Bold).Map(mapping));
        Assert.Null(new RemoveMarkStep(3, 3, MarkFields.Bold).Map(mapping));
    }

    [Fact]
    public void ARestoreStepDeclinesToRebase()
    {
        var step = new RestoreMarksStep(1, 3, [new ValueSpan<MarkSet>(1, 2, MixedSample.Bold)]);

        Assert.Same(step, step.Map(Mapping.Empty));
        Assert.Null(step.Map(Mapping.Of(new StepMap(0, 1, 4))));
    }
}

/// <summary>Changing a block's attributes.</summary>
public class SetBlockAttrStepTests
{
    [Fact]
    public void AParagraphBecomesAHeading()
    {
        var step = new SetBlockAttrStep(0, BlockAttr.ParagraphKind, (int)ParagraphKind.Heading1);
        var result = step.Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.Equal(ParagraphKind.Heading1, ((ParagraphNode)result.Doc!.Children[0]).Kind);
    }

    [Fact]
    public void TheTextDoesNotMove()
    {
        var step = new SetBlockAttrStep(0, BlockAttr.ParagraphKind, (int)ParagraphKind.Heading1);
        var result = step.Apply(MixedSample.Doc);

        Assert.True(step.GetMap().IsIdentity);
        Assert.Equal(MixedSample.Doc.ContentSize, result.Doc!.ContentSize);
        Assert.Equal(
            ((ParagraphNode)MixedSample.Doc.Children[0]).Content,
            ((ParagraphNode)result.Doc!.Children[0]).Content);
    }

    [Fact]
    public void ANestedBlockIsReachable()
    {
        // Position 9 is directly before the paragraph inside the list item.
        var step = new SetBlockAttrStep(9, BlockAttr.Align, (int)TextAlign.Center);
        var result = step.Apply(MixedSample.Doc);

        Assert.True(result.IsOk, result.Failure);

        var paragraph = (ParagraphNode)result.Doc!.Resolve(10).Parent;
        Assert.Equal(TextAlign.Center, paragraph.Align);
    }

    [Fact]
    public void AListChangesKindAndStart()
    {
        var kind = new SetBlockAttrStep(7, BlockAttr.ListKind, (int)ListKind.Ordered)
            .Apply(MixedSample.Doc);

        Assert.True(kind.IsOk, kind.Failure);

        var start = new SetBlockAttrStep(7, BlockAttr.ListStart, 5).Apply(kind.Doc!);

        Assert.True(start.IsOk, start.Failure);

        var list = (ListNode)start.Doc!.Children[1];
        Assert.Equal(ListKind.Ordered, list.Kind);
        Assert.Equal(5, list.Start);
    }

    [Fact]
    public void UndoingPutsTheOldValueBack()
    {
        var step = new SetBlockAttrStep(0, BlockAttr.ParagraphKind, (int)ParagraphKind.Heading1);
        var changed = step.Apply(MixedSample.Doc).Doc!;
        var undone = step.Invert(MixedSample.Doc).Apply(changed);

        Assert.True(undone.IsOk, undone.Failure);
        Assert.Equal(MixedSample.Doc, undone.Doc);
    }

    [Fact]
    public void AnAttributeTheBlockDoesNotHaveFails()
    {
        // A list has no indent level.
        Assert.False(new SetBlockAttrStep(7, BlockAttr.IndentLevel, 2)
            .Apply(MixedSample.Doc).IsOk);
    }

    [Fact]
    public void AValueOutsideTheEnumFails()
    {
        Assert.False(new SetBlockAttrStep(0, BlockAttr.ParagraphKind, 9999)
            .Apply(MixedSample.Doc).IsOk);
    }

    [Fact]
    public void ANegativeIndentFails()
    {
        Assert.False(new SetBlockAttrStep(0, BlockAttr.IndentLevel, -1)
            .Apply(MixedSample.Doc).IsOk);
    }

    [Fact]
    public void APositionThatIsNotBeforeABlockFails()
    {
        // Position 3 is inside the first paragraph's text.
        Assert.False(new SetBlockAttrStep(3, BlockAttr.ParagraphKind, (int)ParagraphKind.Body)
            .Apply(MixedSample.Doc).IsOk);
    }

    [Fact]
    public void APositionPastTheEndOfTheDocumentFails()
    {
        Assert.False(new SetBlockAttrStep(
            MixedSample.Doc.ContentSize,
            BlockAttr.ParagraphKind,
            (int)ParagraphKind.Body).Apply(MixedSample.Doc).IsOk);
    }

    [Fact]
    public void ThePositionShiftsWithTheDocument()
    {
        var mapped = Assert.IsType<SetBlockAttrStep>(
            new SetBlockAttrStep(7, BlockAttr.ListKind, (int)ListKind.Ordered)
                .Map(Mapping.Of(new StepMap(1, 0, 4))));

        Assert.Equal(11, mapped.Pos);
    }

    [Fact]
    public void ABlockThatWasDeletedLeavesNothingToSet()
    {
        Assert.Null(new SetBlockAttrStep(7, BlockAttr.ListKind, (int)ListKind.Ordered)
            .Map(Mapping.Of(new StepMap(0, 18, 0))));
    }

    [Fact]
    public void TheStepFollowsItsBlockWhenSomethingIsInsertedInFrontOfIt()
    {
        // The position names the block by sitting immediately before it, so an insertion at
        // exactly that spot has to push it along. Leaving it where it was would silently
        // re-aim the step at whatever block the insertion put there.
        var mapped = Assert.IsType<SetBlockAttrStep>(
            new SetBlockAttrStep(7, BlockAttr.ListKind, (int)ListKind.Ordered)
                .Map(Mapping.Of(new StepMap(7, 0, 4))));

        Assert.Equal(11, mapped.Pos);
    }

    [Fact]
    public void AnOrderedListCanStartAtZero()
    {
        // Numbering from zero is unusual but legal, and ListNode already accepts it. Rejecting
        // it here would make a start of zero reachable by construction but not by editing.
        var doc = new DocumentNode(
            [new ListNode([new ListItemNode([ParagraphNode.FromText("x")])], ListKind.Ordered)]);

        var result = new SetBlockAttrStep(0, BlockAttr.ListStart, 0).Apply(doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.Equal(0, ((ListNode)result.Doc!.Children[0]).Start);
    }

    [Fact]
    public void EveryBlockAttributeSurvivesARoundTrip()
    {
        var doc = new DocumentNode(
        [
            ParagraphNode.FromText("text"),
            new ListNode([new ListItemNode([ParagraphNode.FromText("item")])]),
        ]);

        var cases = ImmutableArray.Create(
            (0, BlockAttr.ParagraphKind, (int)ParagraphKind.Quote),
            (0, BlockAttr.Align, (int)TextAlign.Right),
            (0, BlockAttr.IndentLevel, 3),
            (6, BlockAttr.ListKind, (int)ListKind.Ordered),
            (6, BlockAttr.ListStart, 7));

        foreach (var (pos, attr, value) in cases)
        {
            var step = new SetBlockAttrStep(pos, attr, value);
            var applied = step.Apply(doc);

            Assert.True(applied.IsOk, $"{attr}: {applied.Failure}");
            Assert.NotEqual(doc, applied.Doc);

            var undone = step.Invert(doc).Apply(applied.Doc!);

            Assert.True(undone.IsOk, $"{attr}: {undone.Failure}");
            Assert.Equal(doc, undone.Doc);
        }
    }
}
