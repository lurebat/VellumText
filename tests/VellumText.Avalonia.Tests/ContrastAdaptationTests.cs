using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Text whose colour the document states is drawn readable on the background it ends up on,
/// without the document changing.
/// </summary>
/// <remarks>
/// The report was black text pasted from Word into the dark-themed demo: perfectly correct, and
/// invisible. The colour is the document's and copy and export have to keep it; only what is
/// drawn adapts.
/// </remarks>
public class ContrastAdaptationTests
{
    private static readonly Color Dark = Color.FromRgb(0x1E, 0x1E, 0x1E);

    // ------------------------------------------------------------------ drawing

    [AvaloniaFact]
    public void BlackTextOnADarkBackgroundIsDrawnLight()
    {
        var (window, _) = Shown(Coloured(Rgba.Black), Dark);

        Assert.True(Brightest(window) > 200, $"the text was drawn no brighter than {Brightest(window)}");
    }

    [AvaloniaFact]
    public void TurnedOffTheColourIsDrawnAsWritten()
    {
        var (window, view) = Shown(Coloured(Rgba.Black), Dark);

        view.IsContrastAdaptationEnabled = false;

        Assert.True(Brightest(window) < 60, $"the text was drawn as bright as {Brightest(window)}");
    }

    [AvaloniaFact]
    public void ItIsOnByDefault()
    {
        Assert.True(new RichTextView().IsContrastAdaptationEnabled);
        Assert.True(new RichTextViewer().IsContrastAdaptationEnabled);
    }

    [AvaloniaFact]
    public void ColourWithEnoughContrastIsLeftAlone()
    {
        // Dark red on white is 10:1. Adapting it would be the control overruling the author.
        var red = Rgba.FromRgb(0x8B, 0, 0);
        var (window, _) = Shown(Coloured(red), Colors.White);

        Assert.Contains(Colours(window), c => Near(c, Color.FromRgb(0x8B, 0, 0)));
    }

    [AvaloniaFact]
    public void TextInTheThemesOwnColourIsNotTouched()
    {
        // Only colours the document states adapt. The theme's colour is the host's choice, and a
        // host that paints black text on black has a bug this would hide.
        var (window, view) = Shown(DocumentNode.FromParagraphs("WWWW WWWW"), Dark);

        TextElement.SetForeground(view, Brushes.Black);
        window.UpdateLayout();

        Assert.True(Brightest(window) < 60);
    }

    [AvaloniaFact]
    public void AHighlightIsWhatTheTextIsReadAgainst()
    {
        // Black on yellow is perfectly readable, whatever the editor behind the highlight is.
        var marks = new MarkSet(foreground: Rgba.Black, highlight: Rgba.FromRgb(0xFF, 0xF0, 0x40));
        var (window, _) = Shown(Marked(marks), Dark);

        Assert.Contains(Colours(window), c => Near(c, Colors.Black));
        Assert.DoesNotContain(Colours(window), c => c.R > 200 && c.G > 200 && c.B > 200);
    }

    [AvaloniaFact]
    public void ACellsOwnBackgroundIsWhatItsTextIsReadAgainst()
    {
        // Word tables pasted into a dark editor bring their white cells with them; their black text
        // is fine where it is and must not be lightened onto white.
        var cell = new TableCellNode(
            [new ParagraphNode(InlineContent.FromText("WWWW WWWW", new MarkSet(foreground: Rgba.Black)))],
            background: Rgba.White);
        var doc = new DocumentNode([new TableNode([new TableRowNode([cell])])]);
        var (window, _) = Shown(doc, Dark);

        Assert.Contains(Colours(window), c => Near(c, Colors.Black));
    }

    [AvaloniaFact]
    public void ChangingTheBackgroundAdaptsAgain()
    {
        var (window, view) = Shown(Coloured(Rgba.Black), Colors.White);

        Assert.Contains(Colours(window), c => Near(c, Colors.Black));

        view.Background = new SolidColorBrush(Dark);
        window.UpdateLayout();

        Assert.Contains(Colours(window), c => Near(c, Dark));
        Assert.True(Brightest(window) > 200);
    }

    [AvaloniaFact]
    public void SwitchingTheThemeAdaptsAgain()
    {
        // The editor's background is a theme resource, so a theme switch changes it without
        // anything about the view itself changing.
        var view = new RichTextView { State = EditorState.Create(Coloured(Rgba.Black)), FontSize = 30 };
        var border = new Border { Child = view };

        border.Resources.ThemeDictionaries[ThemeVariant.Light] =
            new ResourceDictionary { ["Paper"] = new SolidColorBrush(Colors.White) };
        border.Resources.ThemeDictionaries[ThemeVariant.Dark] =
            new ResourceDictionary { ["Paper"] = new SolidColorBrush(Dark) };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("Paper"));

        var window = new Window
        {
            Width = 400,
            Height = 120,
            Content = border,
            RequestedThemeVariant = ThemeVariant.Light,
        };

        window.Show();
        window.UpdateLayout();

        Assert.Contains(Colours(window), c => Near(c, Colors.Black));

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.True(Brightest(window) > 200, $"the text was drawn no brighter than {Brightest(window)}");
    }

    [AvaloniaFact]
    public void TheBackgroundIsFoundOnAnAncestorWhenTheViewHasNone()
    {
        // The editor's template paints its background on a Border around the view, so the view's
        // own Background is transparent in the common case.
        var view = new RichTextView { State = EditorState.Create(Coloured(Rgba.Black)), FontSize = 30 };
        var window = new Window
        {
            Width = 400,
            Height = 120,
            Content = new Border { Background = new SolidColorBrush(Dark), Child = view },
        };

        window.Show();
        window.UpdateLayout();

        Assert.True(Brightest(window) > 200);
    }

    [AvaloniaFact]
    public void WithNoBackgroundAnywhereNothingIsAdapted()
    {
        // Unknown is not dark. Guessing would lighten black text on whatever the window is.
        var view = new RichTextView { State = EditorState.Create(Coloured(Rgba.Black)), FontSize = 30 };
        // A window paints white unless told otherwise, so it is told otherwise.
        var window = new Window { Width = 400, Height = 120, Content = view, Background = null };

        window.Show();
        window.UpdateLayout();

        // Black as written: nothing anywhere was drawn light.
        Assert.DoesNotContain(Colours(window), c => c.R > 200 && c.G > 200 && c.B > 200);
    }

    // ------------------------------------------------------------------ the document is untouched

    [AvaloniaFact]
    public void TheDocumentCopyAndExportKeepTheColourAsWritten()
    {
        var doc = Coloured(Rgba.Black);
        var htmlBefore = HtmlExporter.Instance.Export(doc);
        var (window, view) = Shown(doc, Dark);

        view.Formats.Add(HtmlFormat.Instance);
        Assert.True(Brightest(window) > 200);

        Assert.Same(doc, view.State.Doc);

        var run = ((ParagraphNode)view.State.Doc.Blocks[0]).Content.MarkAt(0);

        Assert.Equal(Rgba.Black, run.Foreground);
        Assert.Equal(htmlBefore, HtmlExporter.Instance.Export(view.State.Doc));

        view.State = EditorState.Create(
            view.State.Doc, TextSelection.Create(view.State.Doc, 1, view.State.Doc.ContentSize - 1));

        IDataTransferItem copied = view.BuildCopyData(view.SelectedText()).Items.Single();
        var html = Assert.IsType<byte[]>(copied.TryGetValue(RichTextClipboard.FlavorFor("html").DataFormat));
        var text = System.Text.Encoding.UTF8.GetString(html);

        Assert.Contains("#000000", text, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ the colour arithmetic

    [AvaloniaTheory]
    [InlineData(0x000000u, 0x1E1E1Eu)]
    [InlineData(0x1A237Eu, 0x1E1E1Eu)]
    [InlineData(0xFFFFFFu, 0xFFFFFFu)]
    [InlineData(0xFFFF66u, 0xFFFFFFu)]
    [InlineData(0x404040u, 0x202020u)]
    public void AnAdaptedColourIsReadable(uint text, uint background)
    {
        var adapted = TextContrast.Adapt(Rgb(text), Rgb(background));

        Assert.True(
            TextContrast.Ratio(adapted, Rgb(background)) >= TextContrast.Target,
            $"{adapted} on {Rgb(background)} is {TextContrast.Ratio(adapted, Rgb(background)):0.00}:1");
    }

    [AvaloniaTheory]
    [InlineData(0x777777u, 0xFFFFFFu)]
    [InlineData(0x8B0000u, 0xFFFFFFu)]
    [InlineData(0xFFFFFFu, 0x1E1E1Eu)]
    [InlineData(0x4FC1FFu, 0x1E1E1Eu)]
    public void ColourThatIsAlreadyReadableIsReturnedAsIs(uint text, uint background)
    {
        Assert.Equal(Rgb(text), TextContrast.Adapt(Rgb(text), Rgb(background)));
    }

    [AvaloniaFact]
    public void AdaptingKeepsTheHue()
    {
        // Dark navy on a dark editor becomes a light blue, not white: the author chose blue.
        var adapted = TextContrast.Adapt(Rgb(0x1A237E), Rgb(0x1E1E1E));
        var hsl = adapted.ToHsl();

        Assert.InRange(hsl.H, 225, 245);
        Assert.True(hsl.S > 0.3);
    }

    [AvaloniaFact]
    public void WhereMirroringIsNotEnoughTheColourIsLightenedFurtherNotWhitened()
    {
        // Indigo on a mid-dark grey: its mirror is only 2.9:1, so it has to go lighter still - and
        // stay indigo, rather than giving up and turning white.
        var background = Rgb(0x3A3A3A);
        var adapted = TextContrast.Adapt(Rgb(0x283593), background);
        var hsl = adapted.ToHsl();

        Assert.True(TextContrast.Ratio(adapted, background) >= TextContrast.Target);
        Assert.InRange(hsl.H, 225, 245);
        Assert.True(hsl.S > 0.3, $"{adapted} lost its colour");
    }

    [AvaloniaFact]
    public void AdaptingKeepsTheAlpha()
    {
        var adapted = TextContrast.Adapt(Color.FromArgb(0x80, 0, 0, 0), Rgb(0x1E1E1E));

        Assert.Equal(0x80, adapted.A);
    }

    // ------------------------------------------------------------------ helpers

    private static Color Rgb(uint rgb) => Color.FromUInt32(0xFF000000 | rgb);

    private static DocumentNode Coloured(Rgba colour) => Marked(new MarkSet(foreground: colour));

    private static DocumentNode Marked(MarkSet marks) =>
        new([new ParagraphNode(InlineContent.FromText("WWWW WWWW", marks))]);

    private static (Window Window, RichTextView View) Shown(DocumentNode doc, Color background)
    {
        var view = new RichTextView
        {
            State = EditorState.Create(doc),
            FontSize = 30,
            Background = new SolidColorBrush(background),
        };
        var window = new Window { Width = 400, Height = 120, Content = view };

        window.Show();
        window.UpdateLayout();

        return (window, view);
    }

    private static List<Color> Colours(Window window)
    {
        // A captured frame can trail the latest commit by a tick.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");

        var pixels = RenderedPixels.Rgba(frame);
        var colours = new List<Color>(pixels.Length / 4);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            colours.Add(Color.FromArgb(pixels[i + 3], pixels[i], pixels[i + 1], pixels[i + 2]));
        }

        return colours;
    }

    /// <summary>The brightest pixel in the frame, as the mean of its channels.</summary>
    private static double Brightest(Window window) =>
        Colours(window)
            .Select(c => (c.R + c.G + c.B) / 3.0)
            .DefaultIfEmpty(0)
            .Max();

    private static bool Near(Color a, Color b) =>
        Math.Abs(a.R - b.R) <= 6 && Math.Abs(a.G - b.G) <= 6 && Math.Abs(a.B - b.B) <= 6
        && Math.Abs(a.A - b.A) <= 6;
}
