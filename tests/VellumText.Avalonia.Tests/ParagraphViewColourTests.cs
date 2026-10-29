using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// That a foreground mark actually reaches the screen, asserted on pixels rather than on the
/// properties handed to the layout. Every layer in between was individually correct while the
/// reported symptom was that colour never appeared.
/// </summary>
public class ParagraphViewColourTests
{
    [AvaloniaFact]
    public void RedTextRendersRed()
    {
        // Exactly the shape the RTF importer produces for red text, verified by probing it.
        var marks = new[] { new ValueSpan<MarkSet>(0, 3, new MarkSet { Foreground = Rgba.FromRgb(255, 0, 0) }) };
        var paragraph = new ParagraphNode(InlineContent.Create("RED", marks, []));
        var doc = new DocumentNode([paragraph]);

        var view = new RichTextView { State = EditorState.Create(doc) };

        TextElement.SetFontSize(view, 40);

        var window = new Window { Width = 300, Height = 120, Content = view };

        window.Show();
        window.Measure(new Size(300, 120));
        window.Arrange(new Rect(0, 0, 300, 120));

        using var bitmap = new RenderTargetBitmap(new PixelSize(300, 120));

        bitmap.Render(view);

        var pixels = Read(bitmap);
        var reds = pixels.Count(p => Red(p) > 200 && Green(p) < 80 && Blue(p) < 80 && Alpha(p) > 200);
        var seen = string.Join(", ", pixels.Where(p => Alpha(p) > 200).Distinct().Take(8).Select(p => p.ToString("X8")));

        Assert.True(reds > 0, $"no red pixels. opaque colours seen: {seen}");
    }

    [AvaloniaFact]
    public void RedAndBlueRunsAmongPlainRunsBothRender()
    {
        // The exact span shape RtfImporter produced for "plain then <red> then <blue> end."
        var marks = new[]
        {
            new ValueSpan<MarkSet>(10, 9, new MarkSet { Foreground = Rgba.FromRgb(255, 0, 0), Style = TextStyle.Bold }),
            new ValueSpan<MarkSet>(24, 5, new MarkSet { Foreground = Rgba.FromRgb(0, 0, 255) }),
        };
        var paragraph = new ParagraphNode(InlineContent.Create("plain then red bold then blue end.", marks, []));
        var view = new RichTextView { State = EditorState.Create(new DocumentNode([paragraph])) };

        TextElement.SetFontSize(view, 28);

        var window = new Window { Width = 900, Height = 200, Content = view };

        window.Show();
        window.Measure(new Size(900, 200));
        window.Arrange(new Rect(0, 0, 900, 200));

        using var bitmap = new RenderTargetBitmap(new PixelSize(900, 200));

        bitmap.Render(view);

        var pixels = Read(bitmap);
        var reds = pixels.Count(p => Red(p) > 200 && Green(p) < 80 && Blue(p) < 80 && Alpha(p) > 200);
        var blues = pixels.Count(p => Blue(p) > 200 && Green(p) < 80 && Red(p) < 80 && Alpha(p) > 200);
        var seen = string.Join(", ", pixels.Where(p => Alpha(p) > 200).Distinct().Take(10).Select(p => p.ToString("X8")));

        Assert.True(reds > 0 && blues > 0, $"reds={reds} blues={blues}. opaque colours seen: {seen}");
    }
    [AvaloniaFact]
    public void ColourSurvivesReplacingTheDocumentOnARealizedView()
    {
        // What the demo does on paste: the view is already on screen showing something else.
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("plain white text")) };

        TextElement.SetFontSize(view, 40);

        var window = new Window { Width = 400, Height = 150, Content = view };

        window.Show();
        window.Measure(new Size(400, 150));
        window.Arrange(new Rect(0, 0, 400, 150));

        using (var first = new RenderTargetBitmap(new PixelSize(400, 150)))
        {
            first.Render(view);
        }

        var marks = new[] { new ValueSpan<MarkSet>(0, 3, new MarkSet { Foreground = Rgba.FromRgb(255, 0, 0) }) };

        view.State = EditorState.Create(new DocumentNode([new ParagraphNode(InlineContent.Create("RED", marks, []))]));

        window.Measure(new Size(400, 150));
        window.Arrange(new Rect(0, 0, 400, 150));

        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 150));

        bitmap.Render(view);

        var pixels = Read(bitmap);
        var reds = pixels.Count(p => Red(p) > 200 && Green(p) < 80 && Blue(p) < 80 && Alpha(p) > 200);
        var seen = string.Join(", ", pixels.Where(p => Alpha(p) > 200).Distinct().Take(10).Select(p => p.ToString("X8")));

        Assert.True(reds > 0, $"no red pixels after replacing the document. opaque colours seen: {seen}");
    }
    private static uint Alpha(uint p) => (p >> 24) & 0xFF;

    private static uint Red(uint p) => (p >> 16) & 0xFF;

    private static uint Green(uint p) => (p >> 8) & 0xFF;

    private static uint Blue(uint p) => p & 0xFF;

    private static uint[] Read(RenderTargetBitmap bitmap) => RenderedPixels.Argb(bitmap);
}