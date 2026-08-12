using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Tests for input method composition on <see cref="RichTextView"/>.
/// </summary>
public class RichTextViewImeTests
{
    [AvaloniaFact]
    public void NothingIsBeingComposedToBeginWith()
    {
        var view = Focused("hello");

        Assert.False(view.IsComposing);
        Assert.Null(view.Preedit);
    }

    [AvaloniaFact]
    public void ComposingNeverReachesTheDocumentOrTheHistory()
    {
        // The whole point of the type. If this ever fails, Ctrl+Z replays half-composed text.
        var view = Focused("hello");
        var before = view.State;

        Caret(view, 5);

        var afterCaret = view.State;

        Assert.True(view.SetPreedit("にほ"));
        Assert.True(view.IsComposing);
        Assert.Same(afterCaret, view.State);
        Assert.Equal("hello", Text(view));
        Assert.False(view.CanUndo);
        Assert.Equal(before.Doc, view.State.Doc);
    }

    [AvaloniaFact]
    public void ComposedTextIsDrawnAndTakesUpRoom()
    {
        var view = Focused("hello");

        Caret(view, 5);

        var before = Measured(view);

        Assert.True(view.SetPreedit("composing"));

        var after = Measured(view);

        Assert.True(after > before, $"composed text should widen the paragraph: {before} -> {after}");
    }

    [AvaloniaFact]
    public void ClearingTheCompositionPutsTheParagraphBack()
    {
        var view = Focused("hello");

        Caret(view, 5);

        var before = Measured(view);

        view.SetPreedit("composing");

        Assert.True(view.SetPreedit(null));
        Assert.False(view.IsComposing);
        Assert.Equal(before, Measured(view));
    }

    [AvaloniaFact]
    public void ComposingTheSameThingTwiceChangesNothing()
    {
        var view = Focused("hello");

        Caret(view, 5);

        Assert.True(view.SetPreedit("ab", 1));
        Assert.False(view.SetPreedit("ab", 1));
        Assert.True(view.SetPreedit("ab", 2));
    }

    [AvaloniaFact]
    public void EmptyComposedTextMeansNoComposition()
    {
        var view = Focused("hello");

        Caret(view, 5);

        Assert.False(view.SetPreedit(string.Empty));
        Assert.False(view.IsComposing);
    }

    [AvaloniaFact]
    public void TheCompositionIsAnchoredAtTheStartOfTheSelection()
    {
        var view = Focused("hello world");

        Select(view, 6, 11);
        view.SetPreedit("x");

        Assert.Equal(6, view.Preedit!.Offset);
    }

    [AvaloniaFact]
    public void TheCaretFollowsTheCompositionCursor()
    {
        var view = Focused("hello");

        Caret(view, 5);

        var atEnd = view.CaretRect().X;

        view.SetPreedit("abcd", 4);

        var afterAll = view.CaretRect().X;

        view.SetPreedit("abcd", 0);

        var beforeAll = view.CaretRect().X;

        Assert.True(afterAll > beforeAll, $"cursor 4 should be right of cursor 0: {afterAll} vs {beforeAll}");
        Assert.Equal(atEnd, beforeAll, 3);
    }

    [AvaloniaFact]
    public void AnAbsentCompositionCursorSitsAtTheEnd()
    {
        var view = Focused("hello");

        Caret(view, 5);
        view.SetPreedit("abcd", 4);

        var explicitEnd = view.CaretRect().X;

        view.SetPreedit("abcd");

        Assert.Equal(explicitEnd, view.CaretRect().X, 3);
    }

    [AvaloniaFact]
    public void ACompositionCursorFromAMisbehavingInputMethodIsClamped()
    {
        // A client that throws at a stray report from somebody else's IME crashes the app.
        var view = Focused("hello");

        Caret(view, 5);

        Assert.True(view.SetPreedit("ab", 99));
        Assert.Equal(2, view.Preedit!.CursorPosition);

        Assert.True(view.SetPreedit("ab", -3));
        Assert.Equal(0, view.Preedit!.CursorPosition);
    }

    [AvaloniaFact]
    public void TheCaretIsDrawnWhileComposingOverASelection()
    {
        // Normally a non-empty selection hides the caret. While composing it must not, because
        // the caret is the input method's and marks progress through the composed text.
        var view = Focused("hello world");

        Select(view, 0, 5);

        Assert.Empty(Caret(view));

        view.SetPreedit("ab");

        Assert.NotEmpty(Caret(view));
    }

    [AvaloniaFact]
    public void EditingTheDocumentAbandonsTheComposition()
    {
        var view = Focused("hello");

        Caret(view, 5);
        view.SetPreedit("ab");

        view.InsertText("!");

        Assert.False(view.IsComposing);
        Assert.Equal("hello!", Text(view));
    }

    [AvaloniaFact]
    public void ClickingComposedTextDoesNotMoveTheCaretIntoIt()
    {
        // Positions inside the composition have no document counterpart, so they collapse onto
        // the anchor rather than landing somewhere the model cannot represent.
        var view = Focused("ab");

        Caret(view, 1);
        view.SetPreedit("WWWWWWWW");

        var rect = view.CaretRect();

        Assert.Equal(view.BlockStart + 1, view.PositionAt(new Point(rect.X - 2, rect.Y + 2)));
    }

    [AvaloniaFact]
    public void ASelectionAcrossTheCompositionStillCoversTheRealText()
    {
        var view = Focused("hello world");

        Caret(view, 5);

        var before = Highlight(view, 0, 11);

        view.SetPreedit("XX");

        var during = Highlight(view, 0, 11);

        Assert.True(during > before, $"the highlight should span the composed text too: {before} -> {during}");
    }

    [AvaloniaFact]
    public void TheInputMethodIsOfferedThisViewsClient()
    {
        var view = Focused("hello");

        var args = new global::Avalonia.Input.TextInput.TextInputMethodClientRequestedEventArgs
        {
            RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
            Source = view,
        };

        view.RaiseEvent(args);

        Assert.NotNull(args.Client);
        Assert.Same(view, args.Client!.TextViewVisual);
        Assert.True(args.Client.SupportsPreedit);
        Assert.True(args.Client.SupportsSurroundingText);
    }

    [AvaloniaFact]
    public void TheInputMethodSeesTheParagraphAndTheSelection()
    {
        var view = Focused("hello world");
        var client = Client(view);

        Select(view, 2, 7);

        Assert.Equal("hello world", client.SurroundingText);
        Assert.Equal(2, client.Selection.Start);
        Assert.Equal(7, client.Selection.End);
    }

    [AvaloniaFact]
    public void TheSurroundingTextExcludesTheComposition()
    {
        var view = Focused("hello");
        var client = Client(view);

        Caret(view, 5);
        client.SetPreeditText("abc", null);

        Assert.True(view.IsComposing);
        Assert.Equal("hello", client.SurroundingText);
    }

    [AvaloniaFact]
    public void TheInputMethodCanMoveTheSelection()
    {
        var view = Focused("hello world");
        var client = Client(view);

        client.Selection = new global::Avalonia.Input.TextInput.TextSelection(1, 4);

        Assert.Equal(view.BlockStart + 1, view.State.Selection.From);
        Assert.Equal(view.BlockStart + 4, view.State.Selection.To);
    }

    [AvaloniaFact]
    public void ASelectionFromOutsideTheParagraphIsClamped()
    {
        var view = Focused("hello");
        var client = Client(view);

        client.Selection = new global::Avalonia.Input.TextInput.TextSelection(-5, 99);

        Assert.Equal(view.BlockStart, view.State.Selection.From);
        Assert.Equal(view.BlockStart + 5, view.State.Selection.To);
        Assert.Equal(0, client.Selection.Start);
        Assert.Equal(5, client.Selection.End);
    }

    [AvaloniaFact]
    public void TheCursorRectangleFollowsTheCaret()
    {
        var view = Focused("hello world");
        var client = Client(view);

        Caret(view, 0);

        var atStart = client.CursorRectangle;

        Caret(view, 11);

        Assert.True(client.CursorRectangle.X > atStart.X);
    }

    [AvaloniaFact]
    public void TheClientTellsThePlatformWhenTheStateMoves()
    {
        var view = Focused("hello");
        var client = Client(view);
        var surrounding = 0;
        var selection = 0;
        var cursor = 0;

        client.SurroundingTextChanged += (_, _) => surrounding++;
        client.SelectionChanged += (_, _) => selection++;
        client.CursorRectangleChanged += (_, _) => cursor++;

        Caret(view, 3);

        Assert.True(surrounding > 0);
        Assert.True(selection > 0);
        Assert.True(cursor > 0);
    }

    [AvaloniaFact]
    public void APreeditWithNoTextIsRefused() =>
        Assert.Throws<ArgumentException>(() => new Preedit(string.Empty, 0));

    [AvaloniaFact]
    public void APreeditCannotBeAnchoredBeforeTheParagraph() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Preedit("a", -1));

    [AvaloniaFact]
    public void APreeditCursorOutsideItsTextIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Preedit("ab", 0, 3));

    [AvaloniaFact]
    public void APreeditCannotBeAnchoredPastTheParagraph()
    {
        var view = Focused("hello");
        var block = view.Block!;

        Assert.Throws<ArgumentOutOfRangeException>(() => block.SetPreedit(new Preedit("a", 99)));
    }

    [AvaloniaFact]
    public void AskingForACompositionCaretWithNoCompositionThrows()
    {
        var view = Focused("hello");

        Assert.Throws<InvalidOperationException>(() => view.Block!.GetPreeditCaretRect());
    }

    [AvaloniaFact]
    public void TextAfterTheCompositionIsPushedAlongWithIt()
    {
        // The document position 5 is still the end of "hello"; the composed text sits before it
        // and must displace it. Without the forward mapping the caret for position 5 would be
        // drawn inside the composition, which is not a place the document has.
        var view = Focused("hello");

        Caret(view, 2);
        view.SetPreedit("MMMM");

        var endOfComposition = view.CaretRect().X;
        var endOfText = view.Block!.GetCaretRect(5).X;

        Assert.True(
            endOfText > endOfComposition,
            $"the tail of the paragraph should follow the composition: {endOfText} vs {endOfComposition}");
    }

    [AvaloniaFact]
    public void TheCompositionIsLaidOutWhereItIsAnchored()
    {
        var view = Focused("hello");

        Caret(view, 2);
        view.SetPreedit("XY");

        Assert.Equal("heXYllo", LaidOut(view));
    }

    [AvaloniaFact]
    public void ComposedTextIsUnderlinedAndTheRestIsNot()
    {
        // Underlining is how a user tells composed text from committed text. It goes through
        // the mark set, so a host that replaced the style resolver still gets whatever that
        // resolver does with an underline.
        var view = Focused("hello");

        Caret(view, 2);
        view.SetPreedit("XY");

        var underlined = Runs(view)
            .Where(r => r.Properties?.TextDecorations is not null)
            .Select(r => r.Text.ToString())
            .ToList();

        Assert.Equal(["XY"], underlined);
    }

    [AvaloniaFact]
    public void ComposedTextInheritsTheFormattingItIsComposedInto()
    {
        var bold = InlineContent.FromText("hello")
            .ApplyMarks(0, 5, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold);
        var view = Shown(EditorState.Create(new DocumentNode([new ParagraphNode(bold)])));

        Caret(view, 2);
        view.SetPreedit("XY");

        var composed = Runs(view).Single(r => r.Text.ToString() == "XY");

        Assert.Equal(FontWeight.Bold, composed.Properties!.Typeface.Weight);
    }

    [AvaloniaFact]
    public void ComposingIntoAnEmptyParagraphWorks()
    {
        // The common way a composition actually starts: an empty document and a user typing
        // Japanese. There is no text to inherit formatting from and no position to anchor
        // against but zero.
        var view = Focused(string.Empty);

        Assert.True(view.SetPreedit("にほ"));
        Assert.Equal(0, view.Preedit!.Offset);
        Assert.Equal("にほ", LaidOut(view));
        Assert.True(view.CaretRect().X > 0);
    }

    [AvaloniaFact]
    public void AComposionCanWrapTheParagraphOntoMoreLines()
    {
        var view = Focused(string.Join(' ', Enumerable.Repeat("wrapping", 12)));
        var size = view.Block!.ContentSize;

        Caret(view, size);

        var before = view.Block!.LineCount;

        view.SetPreedit(new string('W', 40));

        Assert.True(view.Block!.LineCount > before);

        // Every line still reports document positions, so the lines the composition spills onto
        // all answer with the end of the real text rather than with a position that does not
        // exist.
        for (var i = 0; i < view.Block!.LineCount; i++)
        {
            Assert.InRange(view.Block!.LineStart(i), 0, size);
            Assert.InRange(view.Block!.LineEnd(i), 0, size);
        }
    }

    [AvaloniaFact]
    public void ClickingInsideComposedTextPutsTheCaretAtTheAnchor()
    {
        // Composed text has no position in the document, so a click that lands in the middle of
        // it has to collapse back onto the anchor. Anything else hands the model a position that
        // does not exist, and — because layout positions run ahead of document ones — would land
        // *before* the anchor.
        var view = Focused("hello world");

        Caret(view, 5);
        view.SetPreedit("XXXXXXXX");

        var caret = view.CaretRect();
        var mid = view.Block!.HitTest(new Point(caret.X - 4, caret.Y + 2));

        Assert.Equal(5, mid);

        // And a click beyond the composition maps back to real text after the anchor.
        var after = view.Block!.HitTest(new Point(caret.X + 200, caret.Y + 2));

        Assert.InRange(after, 5, 11);
    }

    private static string LaidOut(RichTextView view) =>
        string.Concat(Runs(view).Select(r => r.Text.ToString()));

    private static IEnumerable<global::Avalonia.Media.TextFormatting.TextRun> Runs(RichTextView view)
    {
        view.Measure(new Size(400, 200));

        return view.Block!.Layout.TextLines.SelectMany(line => line.TextRuns);
    }

    private static double Measured(RichTextView view)
    {
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        return view.DesiredSize.Width;
    }

    private static string Text(RichTextView view) =>
        ((ParagraphNode)view.State.Doc.Blocks[0]).Content.Text;

    private static global::Avalonia.Input.TextInput.TextInputMethodClient Client(RichTextView view)
    {
        var args = new global::Avalonia.Input.TextInput.TextInputMethodClientRequestedEventArgs
        {
            RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
            Source = view,
        };

        view.RaiseEvent(args);

        return args.Client!;
    }

    /// <summary>The rectangles the caret was drawn as, by drawing the view and reading pixels.</summary>
    private static IReadOnlyList<int> Caret(RichTextView view)
    {
        view.CaretBrush = Brushes.Red;

        return Columns(view, r: 200, g: 60, b: 60);
    }

    private static int Highlight(RichTextView view, int from, int to)
    {
        Select(view, from, to);
        view.SelectionBrush = Brushes.Lime;

        return Columns(view, r: 60, g: 200, b: 60).Count;
    }

    /// <summary>
    /// The columns in which the view drew a pixel of a distinctive colour.
    /// </summary>
    /// <remarks>
    /// Colour is what separates the caret from the highlight from the glyphs here, rather than
    /// the blank-the-foreground trick the other suites use. Blanking the foreground is a font
    /// property change, and a font property change discards the block — along with any
    /// composition on it, which is precisely what these tests are about.
    /// </remarks>
    private static List<int> Columns(RichTextView view, int r, int g, int b)
    {
        const int width = 400;
        const int height = 200;

        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.ResetCaretBlink();

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));

        using (var context = bitmap.CreateDrawingContext())
        {
            view.Render(context);
        }

        var pixels = new uint[width * height];

        unsafe
        {
            fixed (uint* buffer = pixels)
            {
                bitmap.CopyPixels(
                    new PixelRect(new PixelSize(width, height)),
                    (nint)buffer,
                    pixels.Length * sizeof(uint),
                    width * sizeof(uint));
            }
        }

        var columns = new List<int>();

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var pixel = pixels[(y * width) + x];
                var pr = (int)((pixel >> 16) & 0xFF);
                var pg = (int)((pixel >> 8) & 0xFF);
                var pb = (int)(pixel & 0xFF);

                var match = r > 100 ? pr >= r && pg <= g && pb <= b
                    : g > 100 ? pg >= g && pr <= r && pb <= b
                    : pb >= b && pr <= r && pg <= g;

                if (match)
                {
                    columns.Add(x);
                    break;
                }
            }
        }

        return columns;
    }

    private static RichTextView Focused(string text) =>
        Shown(EditorState.Create(DocumentNode.FromParagraphs(text)));

    private static RichTextView Shown(EditorState state)
    {
        var window = new Window { Width = 400, Height = 200 };
        var view = new RichTextView { State = state };

        window.Content = view;
        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));
        view.Focus();

        return view;
    }

    private static void Caret(RichTextView view, int offset) =>
        view.MoveTo(view.BlockStart + offset, extend: false);

    private static void Select(RichTextView view, int from, int to)
    {
        view.MoveTo(view.BlockStart + from, extend: false);
        view.MoveTo(view.BlockStart + to, extend: true);
    }
}
