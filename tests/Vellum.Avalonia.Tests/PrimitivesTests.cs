using Avalonia.Headless.XUnit;
using Vellum.Avalonia;
using AvColor = Avalonia.Media.Color;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// The conversions between the primitives Vellum.Core owns and Avalonia's equivalents.
/// </summary>
public class PrimitivesTests
{
    [AvaloniaTheory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 255, 255, 255)]
    [InlineData(1, 2, 3, 4)]
    [InlineData(255, 0, 128, 7)]
    public void AColourSurvivesARoundTrip(byte r, byte g, byte b, byte a)
    {
        var colour = new Rgba(r, g, b, a);

        Assert.Equal(colour, colour.ToAvalonia().ToVellum());
    }

    [AvaloniaFact]
    public void AColourKeepsItsChannelsInTheRightOrder()
    {
        // The whole point of the two layouts matching is that nothing is reinterpreted. A
        // round trip alone would pass even if red and blue were consistently swapped.
        var converted = new Rgba(1, 2, 3, 4).ToAvalonia();

        Assert.Equal(1, converted.R);
        Assert.Equal(2, converted.G);
        Assert.Equal(3, converted.B);
        Assert.Equal(4, converted.A);
    }

    [AvaloniaFact]
    public void AnAvaloniaColourKeepsItsChannelsInTheRightOrder()
    {
        var converted = AvColor.FromArgb(4, 1, 2, 3).ToVellum();

        Assert.Equal(new Rgba(1, 2, 3, 4), converted);
    }

    [AvaloniaFact]
    public void ASpanSurvivesARoundTrip()
    {
        var span = new ValueSpan<MarkSet>(3, 7, new MarkSet { Style = TextStyle.Bold });
        var back = span.ToAvalonia().ToVellum();

        Assert.Equal(span.Start, back.Start);
        Assert.Equal(span.Length, back.Length);
        Assert.Equal(span.Value, back.Value);
    }

    [AvaloniaFact]
    public void ASpanKeepsStartAndLengthApart()
    {
        // Start and Length are both ints in the same order on both sides, so a transposition
        // would round-trip cleanly and only surface as mangled formatting much later.
        var converted = new ValueSpan<int>(3, 7, 0).ToAvalonia();

        Assert.Equal(3, converted.Start);
        Assert.Equal(7, converted.Length);
    }

    [AvaloniaFact]
    public void AnEmptySpanStaysEmpty()
    {
        var converted = new ValueSpan<int>(5, 0, 0).ToAvalonia();

        Assert.Equal(5, converted.Start);
        Assert.Equal(0, converted.Length);
    }
}
