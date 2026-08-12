using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Checks the test harness itself before anything is built on top of it.
/// </summary>
/// <remarks>
/// <para>
/// A headless Avalonia app will happily run with a drawing stack that invents text metrics.
/// Every later assertion about carets, hit tests and selection rectangles would then be
/// measuring the stub rather than Avalonia.
/// </para>
/// <para>
/// <see cref="TheHarnessShapesTextWithRealGlyphWidths"/> is the sentinel for that, and it is
/// the only one: measured by flipping <c>UseHeadlessDrawing</c> back to <c>true</c>, it is the
/// single test of these four that goes red. The stub still hands back shaped runs with
/// positive advances, so the other three pass against it and are smoke tests rather than
/// guards. If that sentinel is ever weakened the whole suite quietly stops testing Avalonia.
/// </para>
/// <para>
/// Nothing here asserts an absolute pixel value, and the suite deliberately does not bundle a
/// font. Which face the platform resolves is not this project's business, and pinning one buys
/// reproducibility only until the next revision of that font. Every assertion is instead
/// relative - one width larger than another, a rectangle inside a line - which holds in any
/// proportional face on any machine.
/// </para>
/// </remarks>
public class HarnessTests
{
    private static readonly Typeface Face = Typeface.Default;

    [AvaloniaFact]
    public void TheHarnessShapesTextWithRealGlyphWidths()
    {
        // A stub metrics provider gives every glyph the same advance. In any proportional face
        // an 'i' is far narrower than a 'W', so this is the cheapest assertion that
        // distinguishes real shaping from invented numbers.
        var narrow = Layout("iiiiii");
        var wide = Layout("WWWWWW");

        Assert.True(narrow.Width > 0, "text measured to zero width; no font was resolved");
        Assert.True(
            wide.Width > narrow.Width * 1.5,
            $"'WWWWWW' ({wide.Width}) is not meaningfully wider than 'iiiiii' ({narrow.Width}); " +
            "text is probably not being shaped");
    }

    [AvaloniaFact]
    public void TheHarnessProducesShapedRunsWithPositiveAdvances()
    {
        // A smoke test, not a guard: the headless stub passes this too. It confirms a glyph
        // typeface was resolved at all, without caring which family the platform chose.
        var line = Layout("Hg").TextLines[0];
        var run = Assert.IsAssignableFrom<ShapedTextRun>(line.TextRuns[0]);

        Assert.NotNull(run.GlyphRun.GlyphTypeface);
        Assert.Equal(2, run.GlyphRun.GlyphInfos.Count);
        Assert.All(run.GlyphRun.GlyphInfos, g => Assert.True(g.GlyphAdvance > 0));
    }

    [AvaloniaFact]
    public void TheHarnessWrapsTextRatherThanReportingOneEndlessLine()
    {
        // Wrapping is the behaviour every paragraph assertion depends on. TextLayout defaults
        // to TextWrapping.NoWrap, so a maxWidth on its own changes nothing.
        var wrapped = new TextLayout(
            "the quick brown fox jumps over the lazy dog and keeps on running",
            Face,
            fontSize: 14,
            foreground: Brushes.Black,
            textWrapping: TextWrapping.Wrap,
            maxWidth: 80);

        Assert.True(
            wrapped.TextLines.Count > 3,
            $"expected several wrapped lines, got {wrapped.TextLines.Count}");
    }

    [AvaloniaFact]
    public void TheHarnessReportsSelectionGeometryInsideTheLine()
    {
        // Hit testing and text bounds are the other half of what every later view assertion
        // leans on.
        var layout = Layout("hello world");
        var line = layout.TextLines[0];
        var bounds = line.GetTextBounds(0, 5);

        Assert.NotEmpty(bounds);

        var rect = bounds[0].Rectangle;

        Assert.True(rect.Width > 0, "a five character selection measured to zero width");
        Assert.True(rect.Width < layout.Width, "selecting 'hello' covered the whole of 'hello world'");
    }

    private static TextLayout Layout(string text) =>
        new(text, Face, fontSize: 14, foreground: Brushes.Black);
}
