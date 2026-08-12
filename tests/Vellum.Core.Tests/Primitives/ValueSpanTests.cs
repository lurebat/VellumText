namespace Vellum.Tests.Primitives;

public class ValueSpanTests
{
    [Fact]
    public void Carries_its_range_and_value()
    {
        var span = new ValueSpan<string>(3, 4, "bold");

        Assert.Equal(3, span.Start);
        Assert.Equal(4, span.Length);
        Assert.Equal(7, span.End);
        Assert.Equal("bold", span.Value);
        Assert.False(span.IsEmpty);
    }

    [Fact]
    public void Deconstructs()
    {
        var (start, length, value) = new ValueSpan<int>(1, 2, 42);

        Assert.Equal(1, start);
        Assert.Equal(2, length);
        Assert.Equal(42, value);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(-5, -5)]
    public void Rejects_negative_bounds(int start, int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ValueSpan<int>(start, length, 0));
    }

    [Fact]
    public void Rejects_a_range_whose_end_would_overflow()
    {
        // End is used in arithmetic throughout position mapping. Wrapping to a negative
        // offset would corrupt mapping silently, so construction must refuse it.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ValueSpan<int>(int.MaxValue, 1, 0));

        var largest = new ValueSpan<int>(int.MaxValue - 1, 1, 0);
        Assert.Equal(int.MaxValue, largest.End);
    }

    [Fact]
    public void Contains_is_half_open()
    {
        var span = new ValueSpan<int>(2, 3, 0); // [2..5)

        Assert.False(span.Contains(1));
        Assert.True(span.Contains(2));
        Assert.True(span.Contains(4));
        Assert.False(span.Contains(5));
    }

    [Fact]
    public void An_empty_span_contains_nothing()
    {
        var empty = new ValueSpan<int>(4, 0, 0);

        Assert.True(empty.IsEmpty);
        Assert.False(empty.Contains(3));
        Assert.False(empty.Contains(4));
    }

    [Theory]
    [InlineData(0, 2, false)]  // strictly before, touching at the boundary
    [InlineData(0, 3, true)]   // overlaps by one
    [InlineData(3, 1, true)]   // fully inside
    [InlineData(5, 2, false)]  // strictly after, touching at the boundary
    [InlineData(0, 10, true)]  // fully contains
    public void OverlapsWith_treats_touching_ranges_as_disjoint(int start, int length, bool expected)
    {
        var span = new ValueSpan<int>(2, 3, 0); // [2..5)
        var other = new ValueSpan<int>(start, length, 0);

        Assert.Equal(expected, span.OverlapsWith(other));
        Assert.Equal(expected, other.OverlapsWith(span));
    }

    [Fact]
    public void An_empty_span_overlaps_nothing_even_itself()
    {
        var empty = new ValueSpan<int>(3, 0, 0);
        var covering = new ValueSpan<int>(0, 10, 0);

        Assert.False(empty.OverlapsWith(empty));
        Assert.False(empty.OverlapsWith(covering));
        Assert.False(covering.OverlapsWith(empty));
    }

    [Fact]
    public void WithValue_keeps_the_range()
    {
        var span = new ValueSpan<string>(3, 4, "bold").WithValue("italic");

        Assert.Equal(new ValueSpan<string>(3, 4, "italic"), span);
    }

    [Fact]
    public void WithRange_keeps_the_value()
    {
        var span = new ValueSpan<string>(3, 4, "bold").WithRange(10, 1);

        Assert.Equal(new ValueSpan<string>(10, 1, "bold"), span);
    }

    [Fact]
    public void WithRange_validates_like_the_constructor()
    {
        var span = new ValueSpan<int>(0, 1, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => span.WithRange(-1, 1));
    }

    [Fact]
    public void Equality_is_structural_over_all_three_components()
    {
        Assert.Equal(new ValueSpan<int>(1, 2, 3), new ValueSpan<int>(1, 2, 3));
        Assert.NotEqual(new ValueSpan<int>(1, 2, 3), new ValueSpan<int>(1, 2, 4));
        Assert.NotEqual(new ValueSpan<int>(1, 2, 3), new ValueSpan<int>(1, 9, 3));
        Assert.NotEqual(new ValueSpan<int>(1, 2, 3), new ValueSpan<int>(9, 2, 3));

        Assert.Equal(
            new ValueSpan<int>(1, 2, 3).GetHashCode(),
            new ValueSpan<int>(1, 2, 3).GetHashCode());
    }

    [Fact]
    public void ToString_shows_the_half_open_range()
    {
        Assert.Equal("[3..7) = bold", new ValueSpan<string>(3, 4, "bold").ToString());
    }
}
