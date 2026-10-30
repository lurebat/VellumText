using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A paragraph's forced line height against the embeds it contains.
/// </summary>
/// <remarks>
/// A forced line height is <em>absolute</em> in Avalonia, not a minimum: a line holding a drawable
/// run taller than it keeps the forced height and the drawable spills out of the paragraph and
/// over whatever is drawn below. Measured on 12.1.1 — a 40pt image on a 19.2pt line reported
/// <c>Height</c> 19.2 with <c>Baseline</c> 40, which cannot describe a real box. These tests pin
/// both halves of the fix: a paragraph gives the leading up when it has to, and not otherwise.
/// </remarks>
public class ParagraphViewLineHeightTests
{
    private const double Width = 400;
    private const double Height = 300;

    [AvaloniaFact]
    public void AParagraphIsTallEnoughForTheImageInIt()
    {
        var view = Shown(Inline(60, 40));

        Assert.True(
            view.Block!.Size.Height >= 40,
            $"a 40pt image was put in a {view.Block.Size.Height}pt paragraph");
    }

    [AvaloniaFact]
    public void ATallImageDoesNotOverlapTheBlockBelowIt()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromText("a")
                .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 60, 80)))),
            new ParagraphNode(InlineContent.FromText("below"))]);
        var view = Shown(doc);

        var top = view.BlockOrigin(0).Y;
        var next = view.BlockOrigin(1).Y;

        Assert.True(
            next - top >= 80,
            $"an 80pt image was given {next - top}pt before the next block starts");
    }

    /// <remarks>
    /// The leading is only given up when it has to be. An embed that fits inside the forced line
    /// height must not change the paragraph's height at all, or every list bullet with a small
    /// icon in it would re-space the page.
    /// </remarks>
    [AvaloniaFact]
    public void ASmallImageDoesNotChangeTheLeading()
    {
        var plain = Shown(DocumentNode.FromParagraphs("a"));
        var small = Shown(Inline(4, 4));
        var tall = Shown(Inline(60, 40));

        Assert.Equal(plain.Block!.Size.Height, small.Block!.Size.Height, 3);
        Assert.True(tall.Block!.Size.Height > plain.Block.Size.Height);
    }

    private static DocumentNode Inline(double width, double height) =>
        new([new ParagraphNode(
            InlineContent.FromText("a")
                .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", width, height)))
                .Concat(InlineContent.FromText("b")))]);

    /// <summary>
    /// Text set larger than the paragraph's own size gets lines tall enough for it. The forced
    /// line height is worked out from the paragraph's size, and it took no account of larger text:
    /// every line kept the body pitch, so 48-pixel text wrapped onto lines 25.6 pixels apart and
    /// each line was drawn over the next (found live, picking a size from the toolbar and pasting
    /// a wrapped heading).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ParagraphKind.Body, 48.0)]
    [InlineData(ParagraphKind.Body, 24.0)]
    [InlineData(ParagraphKind.Heading1, 64.0)]
    public void LargerTextGetsLinesTallEnoughForIt(ParagraphKind kind, double size)
    {
        var text = string.Join(" ", Enumerable.Range(0, 30).Select(i => "word" + i));
        var view = Shown(new DocumentNode([new ParagraphNode(
            InlineContent.Create(text, [new ValueSpan<MarkSet>(0, text.Length, new MarkSet { FontSize = size })], []),
            kind)]));
        var lines = ((ParagraphView)view.Blocks[0]).Layout.TextLines;

        Assert.True(lines.Count > 2, $"the paragraph was meant to wrap; it is {lines.Count} lines");

        // A line is at least as tall as the text on it: otherwise its glyphs reach into the next.
        Assert.All(lines, line => Assert.True(
            line.Height >= size,
            $"{size}px text was put on lines {line.Height}px tall"));
        Assert.True(view.Blocks[0].Size.Height >= lines.Count * size);
    }

    /// <summary>
    /// A paragraph holding only one larger word gives the leading up only for itself: plain text
    /// and text no larger than the paragraph's own size keep the leading they had.
    /// </summary>
    [AvaloniaFact]
    public void TextNoLargerThanTheParagraphKeepsTheLeading()
    {
        var plain = Shown(DocumentNode.FromParagraphs("a b"));
        var smaller = Shown(new DocumentNode([new ParagraphNode(
            InlineContent.Create("a b", [new ValueSpan<MarkSet>(0, 1, new MarkSet { FontSize = 10 })], []))]));
        var larger = Shown(new DocumentNode([new ParagraphNode(
            InlineContent.Create("a b", [new ValueSpan<MarkSet>(0, 1, new MarkSet { FontSize = 40 })], []))]));

        Assert.Equal(plain.Blocks[0].Size.Height, smaller.Blocks[0].Size.Height, 3);
        Assert.True(larger.Blocks[0].Size.Height >= 40);
    }

    private static RichTextView Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        return view;
    }
}
