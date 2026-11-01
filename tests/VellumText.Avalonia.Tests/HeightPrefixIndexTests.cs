using VellumText.Avalonia;

namespace VellumText.Avalonia.Tests;

public class HeightPrefixIndexTests
{
    /// <summary>Fully measured geometry must depend on the heights, not the edit or measurement order.</summary>
    [Fact]
    public void ReplacingHeightsHasExactlyTheSamePrefixesAsAFreshIndex()
    {
        var heights = Enumerable.Range(0, 400).Select(i => 44.4 + (i % 7)).ToArray();
        var measured = Enumerable.Repeat(true, heights.Length).ToArray();
        var fresh = new HeightPrefixIndex();
        var edited = new HeightPrefixIndex();

        fresh.Rebuild(heights, measured);
        edited.Reset(heights.Length);

        for (var i = heights.Length - 1; i >= 0; i--)
        {
            edited.SetHeight(i, double.NaN, heights[i]);
        }

        for (var i = 0; i < heights.Length; i += 3)
        {
            edited.SetHeight(i, heights[i], heights[i] * 17);
            edited.SetHeight(i, heights[i] * 17, heights[i]);
        }

        for (var i = 0; i <= heights.Length; i++)
        {
            Assert.Equal(fresh.OffsetOf(i, 32), edited.OffsetOf(i, 32));
        }
    }
}
