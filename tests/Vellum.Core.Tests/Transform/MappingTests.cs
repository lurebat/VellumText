namespace Vellum.Tests;

public class StepMapTests
{
    private static readonly StepMap Insert3At5 = new(5, 0, 3);
    private static readonly StepMap Delete5To10 = new(5, 5, 0);
    private static readonly StepMap Replace5To10With2 = new(5, 5, 2);

    [Fact]
    public void AStepThatMovesNothingIsIdentity()
    {
        Assert.True(StepMap.Identity.IsIdentity);
        Assert.Equal(0, StepMap.Identity.SizeDelta);

        for (var pos = 0; pos < 10; pos++)
        {
            Assert.Equal(pos, StepMap.Identity.Map(pos));
        }
    }

    [Fact]
    public void PositionsBeforeTheEditNeverMove()
    {
        Assert.Equal(4, Insert3At5.Map(4));
        Assert.Equal(4, Delete5To10.Map(4));
        Assert.Equal(0, Replace5To10With2.Map(0));
    }

    [Fact]
    public void AMapThatCouldOverflowIsRefusedWhenItIsMade()
    {
        // Mapping is addition without a bounds check, because it runs for every held position
        // on every keystroke. A map whose range runs off the end of the position space would
        // send positions to large negative numbers, which read as valid to everything they
        // reach. Refusing it at construction costs nothing on the hot path.
        Assert.Throws<ArgumentOutOfRangeException>(() => new StepMap(1, 0, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StepMap(1, int.MaxValue, 0));

        // The largest honest map is still allowed.
        Assert.Equal(int.MaxValue, new StepMap(0, 0, int.MaxValue).NewSize);
    }

    [Fact]
    public void PositionsAfterTheEditShiftByTheSizeChange()
    {
        Assert.Equal(9, Insert3At5.Map(6));
        Assert.Equal(6, Delete5To10.Map(11));
        Assert.Equal(9, Replace5To10With2.Map(12));
    }

    [Fact]
    public void AtAPureInsertionThePositionChoosesWhetherToMove()
    {
        // This is the caret question: type at the caret and does the caret end up before or
        // after what you typed. Both answers have to be reachable.
        Assert.Equal(5, Insert3At5.Map(5, Assoc.Before));
        Assert.Equal(8, Insert3At5.Map(5, Assoc.After));
    }

    [Fact]
    public void AnInsertionDeletesNothingWhicheverSideIsAsked()
    {
        Assert.False(Insert3At5.MapWithResult(5, Assoc.Before).Deleted);
        Assert.False(Insert3At5.MapWithResult(5, Assoc.After).Deleted);
    }

    [Fact]
    public void PositionsInsideADeletedRangeCollapseToItsStart()
    {
        Assert.Equal(5, Delete5To10.Map(6));
        Assert.Equal(5, Delete5To10.Map(7));
        Assert.Equal(5, Delete5To10.Map(9));
    }

    [Fact]
    public void PositionsInsideADeletedRangeAreReportedDeleted()
    {
        Assert.True(Delete5To10.MapWithResult(7, Assoc.Before).Deleted);
        Assert.True(Delete5To10.MapWithResult(7, Assoc.After).Deleted);
    }

    [Fact]
    public void TheEdgesOfADeletedRangeSurviveOnTheSideFacingAway()
    {
        // Position 5 bound to what precedes it is fine; bound to what follows it is not,
        // because what follows it is exactly what was deleted.
        Assert.False(Delete5To10.MapWithResult(5, Assoc.Before).Deleted);
        Assert.True(Delete5To10.MapWithResult(5, Assoc.After).Deleted);

        Assert.True(Delete5To10.MapWithResult(10, Assoc.Before).Deleted);
        Assert.False(Delete5To10.MapWithResult(10, Assoc.After).Deleted);
    }

    [Fact]
    public void TheEdgesOfAReplacedRangeIgnoreTheRequestedSide()
    {
        // A position in front of replaced content cannot be pushed past the replacement, and
        // one behind it cannot be pulled in front, whatever the caller asked for.
        Assert.Equal(5, Replace5To10With2.Map(5, Assoc.Before));
        Assert.Equal(5, Replace5To10With2.Map(5, Assoc.After));

        Assert.Equal(7, Replace5To10With2.Map(10, Assoc.Before));
        Assert.Equal(7, Replace5To10With2.Map(10, Assoc.After));
    }

    [Fact]
    public void InsideAReplacementTheRequestedSidePicksAnEdge()
    {
        Assert.Equal(5, Replace5To10With2.Map(7, Assoc.Before));
        Assert.Equal(7, Replace5To10With2.Map(7, Assoc.After));
    }

    [Fact]
    public void InvertingSwapsTheSizes()
    {
        var inverted = Replace5To10With2.Invert();

        Assert.Equal(5, inverted.Start);
        Assert.Equal(2, inverted.OldSize);
        Assert.Equal(5, inverted.NewSize);
        Assert.Equal(3, inverted.SizeDelta);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void ARangeWithNegativeNumbersIsRefused(int start, int oldSize, int newSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StepMap(start, oldSize, newSize));
    }

    [Fact]
    public void ANegativePositionIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Insert3At5.Map(-1));
    }
}

public class StepMapRoundTripTests
{
    public static TheoryData<int, int, int> Edits()
    {
        var data = new TheoryData<int, int, int>();

        foreach (var start in new[] { 0, 3, 7 })
        {
            foreach (var oldSize in new[] { 0, 1, 4 })
            {
                foreach (var newSize in new[] { 0, 1, 6 })
                {
                    data.Add(start, oldSize, newSize);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public void UndeletedPositionsSurviveTheRoundTrip(int start, int oldSize, int newSize)
    {
        var map = new StepMap(start, oldSize, newSize);
        var back = map.Invert();

        for (var pos = 0; pos <= 20; pos++)
        {
            foreach (var assoc in new[] { Assoc.Before, Assoc.After })
            {
                var forward = map.MapWithResult(pos, assoc);

                if (forward.Deleted)
                {
                    // Content that no longer exists has no position to come back to. That is
                    // exactly what the flag is for.
                    continue;
                }

                Assert.Equal(pos, back.Map(forward.Pos, assoc));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public void MappingNeverGoesBackwardsOrOutOfOrder(int start, int oldSize, int newSize)
    {
        var map = new StepMap(start, oldSize, newSize);
        var previous = 0;

        for (var pos = 0; pos <= 20; pos++)
        {
            var mapped = map.Map(pos);

            Assert.True(mapped >= 0);
            Assert.True(mapped >= previous, $"{pos} mapped to {mapped}, behind {previous}");

            previous = mapped;
        }
    }
}

public class MappingTests
{
    [Fact]
    public void AnEmptyMappingMovesNothing()
    {
        Assert.Equal(0, Mapping.Empty.Count);
        Assert.True(Mapping.Empty.IsIdentity);
        Assert.Equal(7, Mapping.Empty.Map(7));
    }

    [Fact]
    public void MapsApplyInOrder()
    {
        // Insert 3 at 5, then insert 2 at 0. Position 6 becomes 9, then 11.
        var mapping = Mapping.Of(new StepMap(5, 0, 3), new StepMap(0, 0, 2));

        Assert.Equal(11, mapping.Map(6));
    }

    [Fact]
    public void OrderMatters()
    {
        // Insert 3 at position 2 and delete [2, 4). Doing the insertion first puts the caret
        // past it, and the deletion then only shifts it; doing the deletion first collapses
        // the caret onto position 2, where the insertion then pushes it along.
        var insertThenDelete = Mapping.Of(new StepMap(2, 0, 3), new StepMap(2, 2, 0));
        var deleteThenInsert = Mapping.Of(new StepMap(2, 2, 0), new StepMap(2, 0, 3));

        Assert.Equal(3, insertThenDelete.Map(2));
        Assert.Equal(5, deleteThenInsert.Map(2));
    }

    [Fact]
    public void DeletionIsStickyAcrossLaterSteps()
    {
        // The first step removes what position 7 pointed at; the second merely shifts it.
        // A caller asking about identity must still hear that its content is gone.
        var mapping = Mapping.Of(new StepMap(5, 5, 0), new StepMap(0, 0, 3));

        var result = mapping.MapWithResult(7);

        Assert.Equal(8, result.Pos);
        Assert.True(result.Deleted);
    }

    [Fact]
    public void IdentityMapsAreStillCountedSoIndicesLineUpWithSteps()
    {
        var mapping = Mapping.Of(StepMap.Identity, new StepMap(5, 0, 3), StepMap.Identity);

        Assert.Equal(3, mapping.Count);
        Assert.False(mapping.IsIdentity);
        Assert.Equal(9, mapping.Map(6));
    }

    [Fact]
    public void AppendingBuildsUpTheSameMapping()
    {
        var built = Mapping.Empty
            .Append(new StepMap(5, 0, 3))
            .Append(new StepMap(0, 0, 2));

        Assert.Equal(Mapping.Of(new StepMap(5, 0, 3), new StepMap(0, 0, 2)).Map(6), built.Map(6));
    }

    [Fact]
    public void AppendingAMappingConcatenatesIt()
    {
        var first = Mapping.Of(new StepMap(5, 0, 3));
        var second = Mapping.Of(new StepMap(0, 0, 2));

        var joined = first.AppendMapping(second);

        Assert.Equal(2, joined.Count);
        Assert.Equal(11, joined.Map(6));
    }

    [Fact]
    public void ASubrangeAppliesOnlyTheMapsAskedFor()
    {
        var mapping = Mapping.Of(new StepMap(5, 0, 3), new StepMap(0, 0, 2), new StepMap(0, 0, 4));

        Assert.Equal(6, mapping.MapRange(6, 0, 0).Pos);
        Assert.Equal(9, mapping.MapRange(6, 0, 1).Pos);
        Assert.Equal(11, mapping.MapRange(6, 0, 2).Pos);
        Assert.Equal(15, mapping.MapRange(6, 0, 3).Pos);
        Assert.Equal(12, mapping.MapRange(6, 1, 3).Pos);
    }

    [Fact]
    public void ASubrangeOutsideTheMappingIsRefused()
    {
        var mapping = Mapping.Of(new StepMap(5, 0, 3));

        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.MapRange(0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.MapRange(0, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.MapRange(0, 1, 0));
    }

    [Fact]
    public void InvertingReversesBothTheOrderAndEachMap()
    {
        var mapping = Mapping.Of(new StepMap(5, 0, 3), new StepMap(0, 0, 2));

        var inverted = mapping.Invert();

        Assert.Equal(2, inverted.Count);
        Assert.Equal(new StepMap(0, 2, 0), inverted.Maps[0]);
        Assert.Equal(new StepMap(5, 3, 0), inverted.Maps[1]);
    }

    [Fact]
    public void UndeletedPositionsSurviveAMappingRoundTrip()
    {
        var mapping = Mapping.Of(
            new StepMap(5, 0, 3),
            new StepMap(0, 2, 0),
            new StepMap(4, 3, 1));

        var back = mapping.Invert();

        for (var pos = 0; pos <= 20; pos++)
        {
            var forward = mapping.MapWithResult(pos, Assoc.Before);

            if (forward.Deleted)
            {
                continue;
            }

            Assert.Equal(pos, back.Map(forward.Pos, Assoc.Before));
        }
    }
}
