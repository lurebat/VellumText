using Vellum.Avalonia;

namespace Vellum.Avalonia.Tests;

public class BlockHeightIndexTests
{
    [Fact]
    public void AnEmptyIndexHasNoHeightAndCoversNothing()
    {
        var index = new BlockHeightIndex();

        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.TotalHeight);
        Assert.Throws<InvalidOperationException>(() => index.IndexAt(0));
    }

    [Fact]
    public void EveryBlockStartsOutEstimated()
    {
        var index = Of(4, estimate: 10);

        Assert.Equal(40, index.TotalHeight);
        Assert.All(Enumerable.Range(0, 4), i => Assert.False(index.Measured(i)));
        Assert.Equal(30, index.OffsetOf(3));
    }

    [Fact]
    public void OffsetsAreThePrefixSumsOfTheHeights()
    {
        var index = Of(4, estimate: 10);

        index.SetHeight(0, 5);
        index.SetHeight(1, 25);
        index.SetHeight(2, 12);
        index.SetHeight(3, 8);

        Assert.Equal(0, index.OffsetOf(0));
        Assert.Equal(5, index.OffsetOf(1));
        Assert.Equal(30, index.OffsetOf(2));
        Assert.Equal(42, index.OffsetOf(3));
        Assert.Equal(50, index.OffsetOf(4));
        Assert.Equal(50, index.TotalHeight);
    }

    [Fact]
    public void ABlockCoversItsOwnRangeAndNotItsNeighboursFirstPixel()
    {
        var index = Of(3, estimate: 10);

        index.SetHeight(0, 10);
        index.SetHeight(1, 20);
        index.SetHeight(2, 30);

        Assert.Equal(0, index.IndexAt(0));
        Assert.Equal(0, index.IndexAt(9.9));
        Assert.Equal(1, index.IndexAt(10));
        Assert.Equal(1, index.IndexAt(29.9));
        Assert.Equal(2, index.IndexAt(30));
        Assert.Equal(2, index.IndexAt(59.9));
    }

    [Fact]
    public void APositionOutsideTheDocumentClampsToAnEnd()
    {
        var index = Of(3, estimate: 10);

        // A drag above the document selects to its start and one below it to its end, so
        // neither may report nothing.
        Assert.Equal(0, index.IndexAt(-1000));
        Assert.Equal(2, index.IndexAt(1000));
    }

    [Fact]
    public void AZeroHeightBlockIsSkippedRatherThanReported()
    {
        var index = Of(3, estimate: 10);

        index.SetHeight(0, 10);
        index.SetHeight(1, 0);
        index.SetHeight(2, 10);

        // Block 1 occupies no pixel, so no pixel may report it. Reporting it would let a
        // click land on a block that is not visible anywhere.
        Assert.Equal(2, index.IndexAt(10));
    }

    [Fact]
    public void MeasuringOneBlockRevisesTheGuessForTheRest()
    {
        var index = Of(4, estimate: 10);

        index.SetHeight(0, 50);

        Assert.Equal(50, index.Estimate);
        Assert.Equal(200, index.TotalHeight);

        index.SetHeight(1, 30);

        Assert.Equal(40, index.Estimate);
        Assert.Equal(50 + 30 + 40 + 40, index.TotalHeight);
    }

    [Fact]
    public void RemeasuringABlockReplacesItsContributionRatherThanAddingToIt()
    {
        var index = Of(2, estimate: 10);

        index.SetHeight(0, 100);
        index.SetHeight(0, 20);

        Assert.Equal(20, index.Estimate);
        Assert.Equal(40, index.TotalHeight);
    }

    [Fact]
    public void AMeasuredBlockKeepsItsHeightWhenTheEstimateMoves()
    {
        var index = Of(3, estimate: 10);

        index.SetHeight(1, 7);
        index.SetHeight(0, 100);

        Assert.Equal(7, index.HeightOf(1));
        Assert.True(index.Measured(1));
        Assert.False(index.Measured(2));
        Assert.Equal(53.5, index.HeightOf(2));
    }

    [Fact]
    public void InsertedBlocksTakeTheCurrentEstimateAndPushTheRestDown()
    {
        var index = Of(2, estimate: 10);

        index.SetHeight(0, 40);
        index.SetHeight(1, 40);
        index.Insert(1, 2);

        Assert.Equal(4, index.Count);
        Assert.Equal(40, index.HeightOf(1));
        Assert.False(index.Measured(1));
        Assert.True(index.Measured(3));
        Assert.Equal(120, index.OffsetOf(3));
    }

    [Fact]
    public void InsertingAtTheEndAppends()
    {
        var index = Of(2, estimate: 10);

        index.Insert(2, 1);

        Assert.Equal(3, index.Count);
        Assert.Equal(30, index.TotalHeight);
    }

    [Fact]
    public void RemovedBlocksStopCountingTowardsTheEstimate()
    {
        var index = Of(3, estimate: 10);

        index.SetHeight(0, 10);
        index.SetHeight(1, 100);
        index.SetHeight(2, 10);
        index.Remove(1, 1);

        Assert.Equal(2, index.Count);
        Assert.Equal(10, index.Estimate);
        Assert.Equal(20, index.TotalHeight);
    }

    [Fact]
    public void AResetForgetsMeasurementsButNotWhatWasLearnedFromThem()
    {
        var index = Of(2, estimate: 10);

        index.SetHeight(0, 60);
        index.SetHeight(1, 40);
        index.Reset(3);

        Assert.Equal(3, index.Count);
        Assert.All(Enumerable.Range(0, 3), i => Assert.False(index.Measured(i)));

        // The seed is what was learned, not what was originally guessed: a control whose blocks
        // measured 50 tall should not go back to guessing 10 for the next document.
        Assert.Equal(50, index.Estimate);
        Assert.Equal(150, index.TotalHeight);
    }

    [Fact]
    public void AnIndexRefusesHeightsAndPositionsItCannotHold()
    {
        var index = Of(2, estimate: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => new BlockHeightIndex(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlockHeightIndex(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.SetHeight(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.SetHeight(0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.SetHeight(2, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.HeightOf(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.OffsetOf(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Insert(3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Remove(1, 2));
    }

    [Fact]
    public void OffsetOfTheCountIsTheTotalSoAScrollExtentNeedsNoSpecialCase()
    {
        var index = Of(5, estimate: 13);

        Assert.Equal(index.TotalHeight, index.OffsetOf(index.Count));
    }

    [Fact]
    public void TheTreeAgreesWithTheObviousLinearAnswerUnderRandomEditing()
    {
        // The Fenwick tree is the whole point of the class and the one thing that could be
        // subtly wrong at sizes no hand-written example reaches. This keeps a plain list beside
        // it and insists they never disagree.
        var random = new Random(20260812);
        var index = new BlockHeightIndex(10);
        var plain = new List<double>();
        var measured = new List<bool>();

        for (var step = 0; step < 4000; step++)
        {
            switch (random.Next(4))
            {
                case 0:
                {
                    var at = random.Next(plain.Count + 1);
                    var count = random.Next(1, 4);

                    index.Insert(at, count);
                    plain.InsertRange(at, Enumerable.Repeat(Estimate(plain, measured), count));
                    measured.InsertRange(at, Enumerable.Repeat(false, count));
                    break;
                }

                case 1 when plain.Count > 0:
                {
                    var at = random.Next(plain.Count);
                    var count = Math.Min(random.Next(1, 4), plain.Count - at);

                    index.Remove(at, count);
                    plain.RemoveRange(at, count);
                    measured.RemoveRange(at, count);
                    break;
                }

                case 2 when plain.Count > 0:
                {
                    var at = random.Next(plain.Count);
                    var height = Math.Round(random.NextDouble() * 40, 3);

                    index.SetHeight(at, height);
                    plain[at] = height;
                    measured[at] = true;
                    break;
                }
            }

            // Unmeasured blocks all carry the current estimate, which the index rewrites as
            // measurements arrive; mirroring that here is what makes the comparison fair.
            var estimate = Estimate(plain, measured);

            for (var i = 0; i < plain.Count; i++)
            {
                if (!measured[i])
                {
                    plain[i] = estimate;
                }
            }

            Assert.Equal(plain.Count, index.Count);

            // Compared to a tolerance rather than to a rounded number of decimals: a Fenwick
            // tree adds the same heights in a different order from a linear sum, so the two
            // disagree in the last bit or two of a double. Measured drift here is around
            // 1e-15 on values near 20 — a millionth of a device pixel at worst, and it does
            // not accumulate, because every sum is taken afresh from the stored heights.
            Assert.Equal(plain.Sum(), index.TotalHeight, 1e-6);

            if (plain.Count == 0)
            {
                continue;
            }

            var probe = random.Next(plain.Count);

            Assert.Equal(plain.Take(probe).Sum(), index.OffsetOf(probe), 1e-6);
            Assert.Equal(plain[probe], index.HeightOf(probe), 1e-6);

            var y = random.NextDouble() * Math.Max(1, plain.Sum());

            Assert.Equal(Covering(plain, y), index.IndexAt(y));
        }
    }

    private static double Estimate(List<double> heights, List<bool> measured)
    {
        var taken = heights.Where((_, i) => measured[i]).ToList();

        return taken.Count == 0 ? 10 : taken.Average();
    }

    private static int Covering(List<double> heights, double y)
    {
        var running = 0d;

        for (var i = 0; i < heights.Count; i++)
        {
            running += heights[i];

            if (y < running)
            {
                return i;
            }
        }

        return heights.Count - 1;
    }

    private static BlockHeightIndex Of(int count, double estimate)
    {
        var index = new BlockHeightIndex(estimate);

        index.Insert(0, count);

        return index;
    }
}
