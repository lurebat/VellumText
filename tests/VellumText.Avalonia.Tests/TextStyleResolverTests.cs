using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The mapping from a mark set to the properties the text formatter consumes.
/// </summary>
public class TextStyleResolverTests
{
    private const double BaseSize = 14;

    private static TextStyleResolver Resolver() =>
        new(Typeface.Default, BaseSize, Brushes.Black);

    [AvaloniaFact]
    public void AnEmptyMarkSetInheritsEveryDefault()
    {
        var resolved = Resolver().Resolve(MarkSet.Empty);

        Assert.Equal(BaseSize, resolved.FontRenderingEmSize);
        Assert.Equal(FontWeight.Normal, resolved.Typeface.Weight);
        Assert.Equal(FontStyle.Normal, resolved.Typeface.Style);
        Assert.Equal(Brushes.Black, resolved.ForegroundBrush);
        Assert.Null(resolved.BackgroundBrush);
        Assert.Null(resolved.TextDecorations);
        Assert.Equal(BaselineAlignment.Baseline, resolved.BaselineAlignment);
    }

    [AvaloniaFact]
    public void BoldAndItalicReachTheTypeface()
    {
        var resolved = Resolver().Resolve(new MarkSet { Style = TextStyle.Bold | TextStyle.Italic });

        Assert.Equal(FontWeight.Bold, resolved.Typeface.Weight);
        Assert.Equal(FontStyle.Italic, resolved.Typeface.Style);
    }

    [AvaloniaFact]
    public void AMarkSetOverridesOnlyWhatItNames()
    {
        // Setting a size must not quietly reset the weight, which is the classic way a
        // resolver built from a fresh default rather than from the base loses formatting.
        var resolved = Resolver().Resolve(new MarkSet { Style = TextStyle.Bold, FontSize = 30 });

        Assert.Equal(30, resolved.FontRenderingEmSize);
        Assert.Equal(FontWeight.Bold, resolved.Typeface.Weight);
        Assert.Equal(Brushes.Black, resolved.ForegroundBrush);
    }

    [AvaloniaFact]
    public void AForegroundBecomesABrushOfTheSameColour()
    {
        var resolved = Resolver().Resolve(new MarkSet { Foreground = Rgba.FromRgb(10, 20, 30) });
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resolved.ForegroundBrush);

        Assert.Equal(Color.FromArgb(255, 10, 20, 30), brush.Color);
    }

    [AvaloniaFact]
    public void AHighlightBecomesABackgroundBrush()
    {
        var resolved = Resolver().Resolve(new MarkSet { Highlight = Rgba.FromRgb(1, 2, 3) });
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resolved.BackgroundBrush);

        Assert.Equal(Color.FromArgb(255, 1, 2, 3), brush.Color);
    }

    [AvaloniaFact]
    public void UnderlineAndStrikethroughBothSurvive()
    {
        var resolved = Resolver()
            .Resolve(new MarkSet { Style = TextStyle.Underline | TextStyle.Strikethrough });

        Assert.NotNull(resolved.TextDecorations);
        Assert.Contains(resolved.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
        Assert.Contains(resolved.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    }

    [AvaloniaFact]
    public void ALinkIsUnderlinedWithoutBeingAskedTo()
    {
        var resolved = Resolver().Resolve(new MarkSet { Link = new LinkMark("https://example.com") });

        Assert.NotNull(resolved.TextDecorations);
        Assert.Contains(resolved.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
    }

    [AvaloniaTheory]
    [InlineData(TextStyle.Sub, BaselineAlignment.Subscript)]
    [InlineData(TextStyle.Super, BaselineAlignment.Superscript)]
    public void ScriptsShiftTheBaselineAndShrinkTheText(TextStyle style, BaselineAlignment expected)
    {
        var resolved = Resolver().Resolve(new MarkSet { Style = style });

        Assert.Equal(expected, resolved.BaselineAlignment);
        Assert.True(
            resolved.FontRenderingEmSize < BaseSize,
            $"script text was not shrunk: {resolved.FontRenderingEmSize} vs {BaseSize}");
    }

    [AvaloniaFact]
    public void ScriptsShrinkTheSizeTheMarkSetAsksFor()
    {
        // The shrink has to compose with an explicit size rather than replace it.
        var resolved = Resolver().Resolve(new MarkSet { Style = TextStyle.Super, FontSize = 100 });

        Assert.True(resolved.FontRenderingEmSize < 100);
        Assert.True(resolved.FontRenderingEmSize > BaseSize);
    }

    [AvaloniaFact]
    public void TheSameMarkSetResolvesToTheSameInstance()
    {
        // The formatter compares run properties constantly. Memoizing is why a paragraph of
        // one format does not allocate a properties object per run.
        var resolver = Resolver();
        var marks = new MarkSet { Style = TextStyle.Bold, FontSize = 20 };

        Assert.Same(resolver.Resolve(marks), resolver.Resolve(marks));
    }

    [AvaloniaFact]
    public void AnEmptyMarkSetResolvesToTheSharedDefault()
    {
        var resolver = Resolver();

        Assert.Same(resolver.Default, resolver.Resolve(MarkSet.Empty));
    }

    [AvaloniaFact]
    public void DifferentMarkSetsResolveToDifferentInstances()
    {
        // Guards the cache key. Keying on something coarser than the whole mark set would
        // hand one format's properties to another.
        var resolver = Resolver();

        Assert.NotSame(
            resolver.Resolve(new MarkSet { Style = TextStyle.Bold }),
            resolver.Resolve(new MarkSet { Style = TextStyle.Italic }));
    }

    [AvaloniaFact]
    public void EveryFieldOfAMarkSetParticipatesInTheCacheKey()
    {
        // A cache keyed on, say, style alone would return the 12pt properties for the 13pt
        // mark set. Walking the fields one at a time is what catches that.
        var resolver = Resolver();
        var baseline = resolver.Resolve(new MarkSet { Style = TextStyle.Bold });

        MarkSet[] variations =
        [
            new() { Style = TextStyle.Bold, FontSize = 13 },
            new() { Style = TextStyle.Bold, FontFamily = "Courier New" },
            new() { Style = TextStyle.Bold, Foreground = Rgba.FromRgb(9, 9, 9) },
            new() { Style = TextStyle.Bold, Highlight = Rgba.FromRgb(9, 9, 9) },
            new() { Style = TextStyle.Bold, Link = new LinkMark("https://example.com") },
        ];

        Assert.All(variations, m => Assert.NotSame(baseline, resolver.Resolve(m)));
    }

    [AvaloniaFact]
    public void CodeIsVisiblyDifferentFromUnmarkedText()
    {
        // Deliberately not an instance comparison. Resolve(Empty) returns the shared default
        // while any other mark set builds a fresh object, so NotSame passes even when the two
        // are pixel-identical. Compare what actually reaches the formatter.
        var resolver = Resolver();
        var plain = resolver.Resolve(MarkSet.Empty);
        var code = resolver.Resolve(new MarkSet { Style = TextStyle.Code });

        Assert.NotEqual(plain.Typeface.FontFamily.Name, code.Typeface.FontFamily.Name);
    }

    [AvaloniaFact]
    public void CodeUsesTheMonospaceFamilyItWasGiven()
    {
        var resolver = new TextStyleResolver(
            Typeface.Default,
            BaseSize,
            Brushes.Black,
            new FontFamily("Courier New"));

        Assert.Equal(
            "Courier New",
            resolver.Resolve(new MarkSet { Style = TextStyle.Code }).Typeface.FontFamily.Name);
    }

    [AvaloniaFact]
    public void AnExplicitFontFamilyBeatsCode()
    {
        // A mark set that names a family is being specific on purpose; the code default is
        // only a default.
        var resolved = Resolver()
            .Resolve(new MarkSet { Style = TextStyle.Code, FontFamily = "Courier New" });

        Assert.Equal("Courier New", resolved.Typeface.FontFamily.Name);
    }

    [AvaloniaFact]
    public void CodeStillHonoursTheOtherStyles()
    {
        var resolved = Resolver().Resolve(new MarkSet { Style = TextStyle.Code | TextStyle.Bold });

        Assert.Equal(FontWeight.Bold, resolved.Typeface.Weight);
    }

    [AvaloniaFact]
    public void AFontFamilyOnAMarkSetReachesTheTypeface()
    {
        var resolved = Resolver().Resolve(new MarkSet { FontFamily = "Courier New" });

        Assert.Equal("Courier New", resolved.Typeface.FontFamily.Name);
    }

    [AvaloniaFact]
    public void ADefaultTypefaceWithAWeightIsNotOverwrittenByAnUnboldMarkSet()
    {
        // The document default may itself be bold; a mark set that says nothing about weight
        // must inherit it rather than reset to Normal.
        var resolver = new TextStyleResolver(
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Light),
            BaseSize,
            Brushes.Black);

        Assert.Equal(FontWeight.Light, resolver.Resolve(new MarkSet { FontSize = 20 }).Typeface.Weight);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonsenseDefaultSizeIsRefused(double size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TextStyleResolver(Typeface.Default, size, Brushes.Black));
    }

    [AvaloniaFact]
    public void ANullDefaultForegroundIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TextStyleResolver(Typeface.Default, BaseSize, null!));
    }
}
