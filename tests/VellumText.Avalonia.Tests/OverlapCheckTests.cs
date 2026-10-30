using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.TextFormatting;
using static VellumText.Tests.Avalonia.ToolbarHarness;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The overlap check the toolbar outcome tests lean on: it must fail a layout that really draws
/// text over the line below, and pass one that is only the font being itself.
/// </summary>
/// <remarks>
/// It compared the ink of whole lines with no allowance, but a font may draw past its own ascent
/// and descent. macOS CI's default font does by about a pixel at 32 px bold, so a correct layout —
/// line boxes stacked, natural height, nothing forced — failed there (run 41033451740: line 1's
/// glyphs began at 31.64, line 0's ended at 32.64). The rule is checked on measured lines here,
/// with that run's numbers, because no font on every CI machine reliably overhangs on demand.
/// </remarks>
public class OverlapCheckTests
{
    /// <summary>
    /// The macOS failure: line boxes stacked, and the only overlap is ink the font itself draws
    /// above its ascent. That is the font, not overlap.
    /// </summary>
    [AvaloniaFact]
    public void InkTheFontDrawsBeyondItsOwnAscentIsNotOverlap() =>
        AssertNoOverlap([
            new("line 0", Top: 0, Bottom: 32, InkTop: 0.6, InkBottom: 32.64, OverhangAbove: 0, OverhangBelow: 0.64),
            new("line 1", Top: 32, Bottom: 64, InkTop: 31.64, InkBottom: 64.64, OverhangAbove: 1.4, OverhangBelow: 0.64),
        ]);

    /// <summary>Overlap beyond what the fonts overhang is still overlap: the allowance is exact.</summary>
    [AvaloniaFact]
    public void InkOverlapBeyondTheFontsOwnOverhangIsOverlap() =>
        Assert.Throws<global::Xunit.Sdk.TrueException>(() => AssertNoOverlap([
            new("line 0", Top: 0, Bottom: 32, InkTop: 0.6, InkBottom: 33, OverhangAbove: 0, OverhangBelow: 0.5),
            new("line 1", Top: 32, Bottom: 64, InkTop: 30.5, InkBottom: 64, OverhangAbove: 1, OverhangBelow: 0),
        ]));

    /// <summary>Line boxes that overlap are overlap, whatever the ink does.</summary>
    [AvaloniaFact]
    public void OverlappingLineBoxesAreOverlap() =>
        Assert.Throws<global::Xunit.Sdk.TrueException>(() => AssertNoOverlap([
            new("line 0", Top: 0, Bottom: 32, InkTop: 4, InkBottom: 28, OverhangAbove: 0, OverhangBelow: 0),
            new("line 1", Top: 31, Bottom: 63, InkTop: 35, InkBottom: 59, OverhangAbove: 0, OverhangBelow: 0),
        ]));

    /// <summary>
    /// Text larger than a line forced to a smaller height is drawn over the next line: the bug the
    /// check exists for (found live: a 28 pt pick overlapped the lines below). Measured from a real
    /// layout, so the measuring half is covered too.
    /// </summary>
    [AvaloniaFact]
    public void TextTallerThanItsForcedLineIsOverlap()
    {
        var run = new GenericTextRunProperties(global::Avalonia.Media.Typeface.Default, fontRenderingEmSize: 40);
        var properties = new GenericTextParagraphProperties(
            global::Avalonia.Media.FlowDirection.LeftToRight,
            global::Avalonia.Media.TextAlignment.Left,
            firstLineInParagraph: true,
            alwaysCollapsible: false,
            run,
            global::Avalonia.Media.TextWrapping.Wrap,
            lineHeight: 16,
            indent: 0,
            letterSpacing: 0);

        var lines = new TextLayout(
            new SimpleSource("gyp jag hold fly gyp jag hold fly gyp jag hold fly gyp jag hold fly", run),
            properties,
            maxWidth: 200).TextLines;

        Assert.True(lines.Count > 1);
        Assert.Throws<global::Xunit.Sdk.TrueException>(() => AssertNoOverlappingLines(lines));
    }

    /// <summary>The same text at its natural height is not overlap.</summary>
    [AvaloniaFact]
    public void TheSameTextAtItsNaturalHeightIsNotOverlap()
    {
        var run = new GenericTextRunProperties(global::Avalonia.Media.Typeface.Default, fontRenderingEmSize: 40);
        var properties = new GenericTextParagraphProperties(
            global::Avalonia.Media.FlowDirection.LeftToRight,
            global::Avalonia.Media.TextAlignment.Left,
            firstLineInParagraph: true,
            alwaysCollapsible: false,
            run,
            global::Avalonia.Media.TextWrapping.Wrap,
            lineHeight: 0,
            indent: 0,
            letterSpacing: 0);

        var lines = new TextLayout(
            new SimpleSource("gyp jag hold fly gyp jag hold fly gyp jag hold fly gyp jag hold fly", run),
            properties,
            maxWidth: 200).TextLines;

        Assert.True(lines.Count > 1);
        AssertNoOverlappingLines(lines);
    }

    private sealed class SimpleSource(string text, TextRunProperties properties) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex) =>
            textSourceIndex >= text.Length ? null : new TextCharacters(text[textSourceIndex..], properties);
    }
}
