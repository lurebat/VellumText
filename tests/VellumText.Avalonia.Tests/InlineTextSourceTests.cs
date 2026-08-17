using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The source that presents a paragraph's inline content to Avalonia's text formatter.
/// </summary>
public class InlineTextSourceTests
{
    private static readonly MarkSet Bold = new() { Style = TextStyle.Bold };
    private static readonly MarkSet Italic = new() { Style = TextStyle.Italic };

    private static TextStyleResolver Styles() => new(Typeface.Default, 14, Brushes.Black);

    private static InlineTextSource Source(InlineContent content) => new(content, Styles());

    private static TextParagraphProperties Paragraph(TextStyleResolver styles) =>
        new GenericTextParagraphProperties(styles.Default, textWrapping: TextWrapping.NoWrap);

    /// <summary>Reads every run the formatter would, in order.</summary>
    private static List<TextRun> RunsOf(InlineTextSource source)
    {
        var runs = new List<TextRun>();

        for (var i = 0; i < source.Length;)
        {
            var run = source.GetTextRun(i);

            Assert.NotNull(run);
            Assert.True(run.Length > 0, $"run at {i} reported length {run.Length}; the formatter would spin");

            runs.Add(run);
            i += run.Length;
        }

        return runs;
    }

    [AvaloniaFact]
    public void UnformattedTextIsOneRun()
    {
        var runs = RunsOf(Source(InlineContent.FromText("hello")));

        Assert.Single(runs);
        Assert.Equal(5, runs[0].Length);
    }

    [AvaloniaFact]
    public void PastTheEndTheSourceEndsTheParagraph()
    {
        // Without this the formatter treats the end of the text as the end of the source and
        // an empty trailing line never forms.
        var source = Source(InlineContent.FromText("hi"));

        Assert.IsType<TextEndOfParagraph>(source.GetTextRun(2));
        Assert.IsType<TextEndOfParagraph>(source.GetTextRun(99));
    }

    [AvaloniaFact]
    public void AnEmptyParagraphIsNothingButItsEnding()
    {
        Assert.IsType<TextEndOfParagraph>(Source(InlineContent.Empty).GetTextRun(0));
    }

    [AvaloniaFact]
    public void RunsBreakAtEveryMarkBoundary()
    {
        // "abcdef" with "cd" bold is three runs: plain, bold, plain. A source that returned
        // the whole string would format the entire paragraph in one style.
        var content = InlineContent.Create(
            "abcdef",
            [new ValueSpan<MarkSet>(2, 2, Bold)],
            []);

        var runs = RunsOf(Source(content));

        Assert.Equal(3, runs.Count);
        Assert.Equal([2, 2, 2], runs.Select(r => r.Length).ToArray());
    }

    [AvaloniaFact]
    public void AdjacentDifferentMarksAreSeparateRuns()
    {
        var content = InlineContent.Create(
            "abcd",
            [new ValueSpan<MarkSet>(0, 2, Bold), new ValueSpan<MarkSet>(2, 2, Italic)],
            []);

        var runs = RunsOf(Source(content));

        Assert.Equal(2, runs.Count);
        Assert.Equal([2, 2], runs.Select(r => r.Length).ToArray());
    }

    [AvaloniaFact]
    public void EachRunCarriesThePropertiesItsMarksResolveTo()
    {
        var content = InlineContent.Create(
            "abcd",
            [new ValueSpan<MarkSet>(2, 2, Bold)],
            []);

        var runs = RunsOf(Source(content));

        Assert.Equal(FontWeight.Normal, runs[0].Properties!.Typeface.Weight);
        Assert.Equal(FontWeight.Bold, runs[1].Properties!.Typeface.Weight);
    }

    [AvaloniaFact]
    public void AnEmbedIsItsOwnRunOfLengthOne()
    {
        var content = InlineContent.FromText("ab")
            .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 10, 10)))
            .Concat(InlineContent.FromText("cd"));

        var runs = RunsOf(Source(content));

        Assert.Equal(3, runs.Count);
        Assert.Equal(2, runs[0].Length);

        var embed = Assert.IsType<EmbedRun>(runs[1]);

        Assert.Equal(1, embed.Length);
        Assert.Equal(2, runs[2].Length);
    }

    [AvaloniaFact]
    public void AnEmbedRunNeverLeavesItsPropertiesNull()
    {
        // A run with null Properties formats fine and only throws when drawn, from deep
        // inside the text stack. Measured in Increment 0; this is the guard for it.
        var content = InlineContent.FromEmbed(new ImageEmbed("x.png", 10, 10));
        var run = Assert.IsType<EmbedRun>(Source(content).GetTextRun(0));

        Assert.NotNull(run.Properties);
    }

    [AvaloniaFact]
    public void AnEmbedTakesTheSizeItAsksFor()
    {
        var content = InlineContent.FromEmbed(new ImageEmbed("x.png", 40, 20));
        var run = Assert.IsType<EmbedRun>(Source(content).GetTextRun(0));

        Assert.Equal(new Size(40, 20), run.Size);
    }

    [AvaloniaFact]
    public void AnEmbedWithNoSizeStillOccupiesABox()
    {
        var content = InlineContent.FromEmbed(new ImageEmbed("x.png"));
        var run = Assert.IsType<EmbedRun>(Source(content).GetTextRun(0));

        Assert.True(run.Size.Width > 0);
        Assert.True(run.Size.Height > 0);
    }

    [AvaloniaFact]
    public void ANegativeIndexIsNotTextRatherThanACrash()
    {
        Assert.Null(Source(InlineContent.FromText("abc")).GetTextRun(-1));
    }

    [AvaloniaFact]
    public void TheFormatterCanLayOutWhatTheSourceProduces()
    {
        // The point of all of the above. Increment 0 validated that a custom ITextSource with
        // a DrawableTextRun lays out; this is that path running against the real model.
        var styles = Styles();
        var content = InlineContent.Create(
            "abcdef",
            [new ValueSpan<MarkSet>(0, 3, Bold)],
            []);

        var layout = new TextLayout(new InlineTextSource(content, styles), Paragraph(styles));

        Assert.Single(layout.TextLines);
        Assert.True(layout.Width > 0);
        Assert.Equal(0, layout.TextLines[0].FirstTextSourceIndex);
    }

    [AvaloniaFact]
    public void TheParagraphEndingCostsOnePositionInTheLine()
    {
        // Worth pinning because it is an off-by-one waiting to happen: TextEndOfParagraph has
        // a text source length of 1, so a six character paragraph reports a line length of
        // seven. The extra position is the caret position at the end of the paragraph, which
        // the model also has, so the two still correspond - but any code that treats
        // TextLine.Length as a character count is wrong.
        var styles = Styles();
        var layout = new TextLayout(
            new InlineTextSource(InlineContent.FromText("abcdef"), styles),
            Paragraph(styles));

        Assert.Equal(7, layout.TextLines[0].Length);
    }

    [AvaloniaFact]
    public void AnEmbedParticipatesInTheLineItIsOn()
    {
        var styles = Styles();
        var withEmbed = InlineContent.FromText("ab")
            .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 50, 10)))
            .Concat(InlineContent.FromText("cd"));

        var without = InlineContent.FromText("abcd");

        var wide = new TextLayout(new InlineTextSource(withEmbed, styles), Paragraph(styles));
        var narrow = new TextLayout(new InlineTextSource(without, styles), Paragraph(styles));

        // A 50 wide embed has to make the line wider than the same text without it. If the
        // formatter were ignoring the embed the two would measure the same.
        Assert.True(
            wide.Width > narrow.Width + 40,
            $"embed did not take up space: {wide.Width} vs {narrow.Width}");
    }

    [AvaloniaFact]
    public void AnEmbedTakesPartInWrapping()
    {
        var styles = Styles();
        var content = InlineContent.FromText("aaa ")
            .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 200, 10)))
            .Concat(InlineContent.FromText(" bbb"));

        var layout = new TextLayout(
            new InlineTextSource(content, styles),
            new GenericTextParagraphProperties(styles.Default, textWrapping: TextWrapping.Wrap),
            maxWidth: 100);

        Assert.True(layout.TextLines.Count > 1, "an oversized embed did not force a line break");
    }

    [AvaloniaFact]
    public void SelectionBoundsSpanningAnEmbedAreSane()
    {
        var styles = Styles();
        var content = InlineContent.FromText("ab")
            .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 30, 10)))
            .Concat(InlineContent.FromText("cd"));

        var layout = new TextLayout(new InlineTextSource(content, styles), Paragraph(styles));
        var line = layout.TextLines[0];

        var acrossEmbed = line.GetTextBounds(1, 3);
        var beforeEmbed = line.GetTextBounds(0, 2);

        Assert.NotEmpty(acrossEmbed);
        Assert.True(
            acrossEmbed.Sum(b => b.Rectangle.Width) > beforeEmbed.Sum(b => b.Rectangle.Width),
            "a selection covering the embed was no wider than one that stopped before it");
    }

    [AvaloniaFact]
    public void HitTestingLandsOnThePlaceholderIndex()
    {
        // The placeholder is why model positions and formatter positions differ by a single
        // additive offset. An embed occupies one index, so its two caret positions are its own
        // index and the one after it, chosen by which half of the box was hit - exactly as a
        // character behaves. Testing both halves is what proves the embed is one position
        // wide rather than zero or two.
        var styles = Styles();
        var content = InlineContent.FromText("ab")
            .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 30, 10)))
            .Concat(InlineContent.FromText("cd"));

        var layout = new TextLayout(new InlineTextSource(content, styles), Paragraph(styles));
        var box = layout.TextLines[0].GetTextBounds(2, 1)[0].Rectangle;
        var y = box.Center.Y;

        Assert.Equal(2, layout.HitTestPoint(new Point(box.Left + (box.Width * 0.1), y)).TextPosition);
        Assert.Equal(3, layout.HitTestPoint(new Point(box.Right - (box.Width * 0.1), y)).TextPosition);
    }

    [AvaloniaFact]
    public void StyleOverridesDescribeTheSameFormattingAsTheRuns()
    {
        // The C1 fallback from Increment 0.
        var content = InlineContent.Create(
            "abcdef",
            [new ValueSpan<MarkSet>(2, 2, Bold)],
            []);

        var overrides = Source(content).StyleOverrides();
        var span = Assert.Single(overrides);

        Assert.Equal(2, span.Start);
        Assert.Equal(2, span.Length);
        Assert.Equal(FontWeight.Bold, span.Value.Typeface.Weight);
    }
}
