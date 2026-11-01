using System.Collections.Immutable;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Colouring code blocks, and the line highlighting must not cross.
/// </summary>
public class SyntaxHighlightingTests
{
    private static DocumentNode Doc(string code, ParagraphKind kind = ParagraphKind.Code) =>
        new([new ParagraphNode(InlineContent.FromText(code), kind)]);

    private static ParagraphNode First(DocumentNode doc) => (ParagraphNode)doc.Blocks[0];

    [AvaloniaFact]
    public void HighlightingNeverReachesTheDocument()
    {
        // The load-bearing property. Colour in the document would put a palette in every export,
        // put highlighting in the undo stack, and make changing theme an edit.
        var doc = Doc("""{"a": 1}""");
        var view = new RichTextView { State = EditorState.Create(doc) };

        view.SyntaxHighlighter = JsonHighlighter.Instance;

        Assert.Same(doc, view.State.Doc);
        Assert.True(First(view.State.Doc).Content.Marks.IsEmpty);
        Assert.Equal(doc, view.State.Doc);
    }

    [AvaloniaFact]
    public void ChangingTheThemeIsNotAnEdit()
    {
        var doc = Doc("""{"a": 1}""");
        var view = new RichTextView { State = EditorState.Create(doc) };

        view.SyntaxHighlighter = JsonHighlighter.Instance;
        view.SyntaxTheme = SyntaxTheme.Light;

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.CanUndo);
    }

    [AvaloniaFact]
    public void ACodeParagraphGetsColouredRuns()
    {
        var highlighted = SyntaxHighlighting.Apply(
            First(Doc("""{"a": 1}""")), JsonHighlighter.Instance, SyntaxTheme.Dark);

        Assert.False(highlighted.Content.Marks.IsEmpty);

        // The key, the number and the braces are each coloured, and differently.
        var key = highlighted.Content.MarkAt(1).Foreground;
        var number = highlighted.Content.MarkAt(6).Foreground;

        Assert.NotNull(key);
        Assert.NotNull(number);
        Assert.NotEqual(key, number);
    }

    [AvaloniaFact]
    public void OnlyCodeParagraphsAreTouched()
    {
        // Prose that happens to look like JSON is still prose.
        foreach (var kind in new[]
        {
            ParagraphKind.Body, ParagraphKind.Heading1, ParagraphKind.Quote,
        })
        {
            var paragraph = First(Doc("""{"a": 1}""", kind));

            Assert.Same(
                paragraph,
                SyntaxHighlighting.Apply(paragraph, JsonHighlighter.Instance, SyntaxTheme.Dark));
        }
    }

    [AvaloniaFact]
    public void NoHighlighterMeansNoChange()
    {
        var paragraph = First(Doc("""{"a": 1}"""));

        Assert.Same(paragraph, SyntaxHighlighting.Apply(paragraph, null, SyntaxTheme.Dark));
    }

    [AvaloniaFact]
    public void AColourTheUserChoseOutranksOneTheTokenizerGuessed()
    {
        var chosen = new Rgba(255, 0, 255, 255);
        var paragraph = new ParagraphNode(
            InlineContent.Create(
                """{"a": 1}""",
                [new ValueSpan<MarkSet>(0, 8, new MarkSet { Foreground = chosen })],
                []),
            ParagraphKind.Code);

        var highlighted = SyntaxHighlighting.Apply(
            paragraph, JsonHighlighter.Instance, SyntaxTheme.Dark);

        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(chosen, highlighted.Content.MarkAt(i).Foreground);
        }
    }

    [AvaloniaFact]
    public void AHighlighterThatThrowsCostsTheColourAndNotTheDocument()
    {
        // The contract says it does not throw, so reaching this is a bug in one - but losing a
        // layout pass over it would be worse than losing the colour.
        var paragraph = First(Doc("""{"a": 1}"""));

        Assert.Same(paragraph, SyntaxHighlighting.Apply(paragraph, new Throwing(), SyntaxTheme.Dark));
    }

    [AvaloniaFact]
    public void ASpanRunningPastTheTextIsSkippedRatherThanThrowing()
    {
        // A third-party highlighter reporting nonsense must not take a render down. Note the span
        // is one ValueSpan will actually construct: it forbids a negative start, so overrunning
        // the end is the only way a token can be out of range - an earlier version of this test
        // used a negative start, which threw inside Tokenize and passed through the catch instead,
        // proving nothing about the bounds check at all.
        var paragraph = First(Doc("abc"));

        var result = SyntaxHighlighting.Apply(paragraph, new Liar(), SyntaxTheme.Dark);

        Assert.Equal("abc", result.Content.Text);
    }

    [AvaloniaFact]
    public void AnEmptyCodeParagraphIsLeftAlone()
    {
        var paragraph = First(Doc(string.Empty));

        Assert.Same(paragraph, SyntaxHighlighting.Apply(paragraph, JsonHighlighter.Instance, SyntaxTheme.Dark));
    }

    [AvaloniaFact]
    public void TheViewDefaultsToNoHighlighting()
    {
        // A highlighter is a claim about what language the code is in, and nothing in a document
        // says. Guessing would colour prose that happens to parse.
        Assert.Null(new RichTextView().SyntaxHighlighter);
        Assert.Null(new RichTextViewer().SyntaxHighlighter);
        Assert.NotNull(new RichTextView().SyntaxTheme);
    }

    [AvaloniaFact]
    public void ClearingTheThemeRestoresTheDefaultRatherThanBreakingColour()
    {
        var view = new RichTextView { SyntaxTheme = SyntaxTheme.Light };

        view.SyntaxTheme = null;

        Assert.Same(SyntaxTheme.Dark, view.SyntaxTheme);
    }

    [AvaloniaFact]
    public void TheTwoPalettesDifferAndCoverEveryMeaningfulKind()
    {
        foreach (var kind in new[]
        {
            TokenKind.Keyword, TokenKind.String, TokenKind.Number,
            TokenKind.Comment, TokenKind.Identifier, TokenKind.Operator, TokenKind.Invalid,
        })
        {
            var light = SyntaxTheme.Light.For(kind);
            var dark = SyntaxTheme.Dark.For(kind);

            Assert.NotNull(light);
            Assert.NotNull(dark);
            Assert.NotEqual(light, dark);
        }

        // Plain text is deliberately unstyled: it inherits the block's own colour.
        Assert.Null(SyntaxTheme.Dark.For(TokenKind.Text));
    }

    [AvaloniaFact]
    public void TheThemeChecksItsArgument()
    {
        Assert.Throws<ArgumentNullException>(() => new SyntaxTheme(null!));
    }

    [AvaloniaFact]
    public void HighlightingActuallyReachesThePixels()
    {
        // Everything above tests the marks. This tests that the marks become colour on screen -
        // a highlighter wired to a layout that never redraws would pass all of it and show nothing.
        var doc = Doc("""{"key": 12345, "flag": true}""");

        var plain = Render(doc, highlighter: null);
        var coloured = Render(doc, JsonHighlighter.Instance);

        Assert.NotEmpty(plain);
        Assert.NotEqual(
            Convert.ToBase64String(plain),
            Convert.ToBase64String(coloured));

        // And more colours are present once it is on, rather than merely different pixels.
        Assert.True(
            Distinct(coloured) > Distinct(plain),
            $"highlighted render had {Distinct(coloured)} colours against {Distinct(plain)} plain");
    }

    private static byte[] Render(DocumentNode doc, ISyntaxHighlighter? highlighter)
    {
        const int width = 320;
        const int height = 80;

        var view = new RichTextView { State = EditorState.Create(doc), SyntaxHighlighter = highlighter };
        var window = new global::Avalonia.Controls.Window { Width = width, Height = height, Content = view };

        window.Show();
        window.Measure(new global::Avalonia.Size(width, height));
        window.Arrange(new global::Avalonia.Rect(0, 0, width, height));

        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new global::Avalonia.PixelSize(width, height));

        bitmap.Render(view);

        using var stream = new MemoryStream();

        bitmap.Save(stream, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    private static int Distinct(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new global::Avalonia.Media.Imaging.Bitmap(stream);

        return RenderedPixels.Argb(bitmap).Distinct().Count();
    }

    private sealed class Throwing : ISyntaxHighlighter
    {
        public string Language => "broken";

        public ImmutableArray<ValueSpan<TokenKind>> Tokenize(string text) =>
            throw new InvalidOperationException("bug");
    }

    private sealed class Liar : ISyntaxHighlighter
    {
        public string Language => "liar";

        public ImmutableArray<ValueSpan<TokenKind>> Tokenize(string text) =>
        [
            // Constructible, and past the end of any short text.
            new ValueSpan<TokenKind>(0, 9999, TokenKind.Keyword),
            new ValueSpan<TokenKind>(1, 0, TokenKind.Number),
        ];
    }
}
