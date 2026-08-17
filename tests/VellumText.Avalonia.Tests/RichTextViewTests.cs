using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Tests for <see cref="RichTextView"/> — what the control draws and where it says things are.
/// </summary>
public class RichTextViewTests
{
    private static RichTextView Laid(string text, double width = 400)
    {
        var view = new RichTextView
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(text)),
        };

        view.Measure(new Size(width, 200));
        view.Arrange(new Rect(0, 0, width, 200));

        return view;
    }

    [AvaloniaFact]
    public void AParagraphsContentStartsAtDocumentPositionOne()
    {
        // The single additive offset between block-local and document positions. Everything the
        // control converts leans on it, so it is worth asserting rather than assuming.
        Assert.Equal(1, Laid("hello").BlockStart);
    }

    [AvaloniaFact]
    public void MeasuringLaysOutTheBlock()
    {
        var view = Laid("hello world");

        Assert.NotNull(view.Block);
        Assert.True(view.DesiredSize.Width > 0);
        Assert.True(view.DesiredSize.Height > 0);
    }

    [AvaloniaFact]
    public void TheDesiredWidthLeavesRoomForTheCaret()
    {
        // A caret at the end of the longest line otherwise sits exactly on the edge and is
        // clipped to a sliver.
        var view = Laid("hello world");

        Assert.True(view.DesiredSize.Width > view.Block!.Size.Width);
    }

    [AvaloniaFact]
    public void AnUnconstrainedWidthDoesNotProduceAnEndlessLine()
    {
        var view = new RichTextView
        {
            State = EditorState.Create(
                DocumentNode.FromParagraphs(string.Join(' ', Enumerable.Repeat("wrap", 200)))),
        };

        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(view.Block!.Layout.TextLines.Count > 1);
    }

    [AvaloniaFact]
    public void ReplacingTheStateRelaysOutTheBlock()
    {
        var view = Laid("hello");
        var before = view.Block!.Size.Width;

        view.State = EditorState.Create(
            DocumentNode.FromParagraphs("hello and a good deal more text besides"));
        view.Measure(new Size(1000, 200));

        Assert.True(view.Block!.Size.Width > before);
    }

    [AvaloniaFact]
    public void SettingTheSameStateChangesNothing()
    {
        var view = Laid("hello");
        var block = view.Block;
        var state = view.State;

        view.State = state;

        Assert.Same(block, view.Block);
    }

    [AvaloniaFact]
    public void ANullStateIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new RichTextView().State = null!);
    }

    [AvaloniaFact]
    public void PositionsAreReportedInDocumentSpace()
    {
        var view = Laid("hello world");
        var caret = view.Block!.GetCaretRect(4);

        Assert.Equal(
            view.BlockStart + 4,
            view.PositionAt(new Point(caret.X, caret.Y + (caret.Height / 2))));
    }

    [AvaloniaFact]
    public void TextStylesComeFromTheControlsOwnFontProperties()
    {
        var view = new RichTextView();
        TextElement.SetFontSize(view, 40);

        Assert.Equal(40, view.TextStyles.Default.FontRenderingEmSize);
    }

    [AvaloniaFact]
    public void ChangingAFontPropertyRelaysOut()
    {
        // A resolver derived from the control's properties is only as current as they are.
        var view = Laid("hello world");
        var before = view.Block!.Size.Width;

        TextElement.SetFontSize(view, 40);
        view.Measure(new Size(1000, 400));

        Assert.True(
            view.Block!.Size.Width > before,
            $"width stayed at {view.Block!.Size.Width} after tripling the font size");
    }

    [AvaloniaFact]
    public void AnExplicitResolverOverridesTheControlsFontProperties()
    {
        var view = Laid("hello world");

        view.TextStyles = new TextStyleResolver(new Typeface("Segoe UI"), 40, Brushes.Black);
        var explicitWidth = Remeasure(view);

        TextElement.SetFontSize(view, 8);

        Assert.Equal(explicitWidth, Remeasure(view), 3);
    }

    [AvaloniaFact]
    public void ClearingTheResolverGoesBackToTheControlsFontProperties()
    {
        var view = Laid("hello world");
        var derived = Remeasure(view);

        view.TextStyles = new TextStyleResolver(new Typeface("Segoe UI"), 40, Brushes.Black);

        Assert.NotEqual(derived, Remeasure(view), 3);

        view.TextStyles = null!;

        Assert.Equal(derived, Remeasure(view), 3);
    }

    [AvaloniaFact]
    public void TheCaretIsNotDrawnWhenTheViewIsUnfocused()
    {
        var view = Laid("hello");

        Assert.False(view.IsCaretVisible);
        Assert.Equal(0, InkedPixels(view));
    }

    [AvaloniaFact]
    public void TheCaretFollowsTheTextColourWhenNoneIsSetForIt()
    {
        // Reported as "no indicator of where your caret is when typing or moving". The caret
        // defaulted to black, the demo is dark-themed, and a black caret on a near-black
        // background reads as the control ignoring the keyboard. An empty paragraph is used so
        // that the only thing drawn in the whole bitmap is the caret.
        var view = Focused(string.Empty);

        TextElement.SetForeground(view, Brushes.Red);

        Assert.Null(view.CaretBrush);

        var drawn = CaretColours(view);

        Assert.NotEmpty(drawn);
        Assert.All(drawn, p => Assert.True(
            Red(p) > 200 && Green(p) < 60 && Blue(p) < 60,
            $"the caret drew {p:X8} rather than the red the text is in"));
    }

    [AvaloniaFact]
    public void AnExplicitCaretColourStillWins()
    {
        var view = Focused(string.Empty);

        TextElement.SetForeground(view, Brushes.Red);
        view.CaretBrush = Brushes.Lime;

        var drawn = CaretColours(view);

        Assert.NotEmpty(drawn);
        Assert.All(drawn, p => Assert.True(
            Green(p) > 200 && Red(p) < 60,
            $"the caret drew {p:X8} rather than the colour it was given"));
    }

    [AvaloniaFact]
    public void TheCaretIsDrawnForAnEmptySelection()
    {
        var view = Focused("hello");

        Assert.True(view.IsCaretVisible);
        Assert.True(InkedPixels(view) > 0);
    }

    [AvaloniaFact]
    public void TheCaretIsNotDrawnForARangeSelection()
    {
        // Every editor hides the caret while text is selected; a caret plus a highlight reads
        // as two cursors.
        var view = Focused("hello");

        view.State = EditorState.Create(view.State.Doc, new TextSelection(1, 4));

        Assert.Equal(0, CaretPixels(view));
    }

    [AvaloniaFact]
    public void TheCaretMovesWithTheSelection()
    {
        var view = Focused("hello world");
        var atStart = CaretColumn(view);

        view.State = EditorState.Create(view.State.Doc, TextSelection.Cursor(view.BlockStart + 8));

        Assert.True(CaretColumn(view) > atStart);
    }

    [AvaloniaFact]
    public void TheSelectionHighlightIsDrawnBehindTheText()
    {
        var view = Laid("hello world");
        var unselected = InkedPixels(view, includeText: true);

        view.State = EditorState.Create(view.State.Doc, new TextSelection(1, view.State.Doc.ContentSize - 1));

        Assert.True(InkedPixels(view, includeText: true) > unselected);
    }

    [AvaloniaFact]
    public void NoHighlightIsDrawnForAnEmptySelection()
    {
        var view = Laid("hello world");
        var before = InkedPixels(view, includeText: true);

        view.State = EditorState.Create(view.State.Doc, TextSelection.Cursor(3));

        Assert.Equal(before, InkedPixels(view, includeText: true));
    }

    [AvaloniaFact]
    public void TheCaretIsSnappedToWholeDevicePixels()
    {
        // TextPresenter's trick. Without it the caret is a blurred two-pixel smear wherever the
        // layout puts it on a fractional coordinate.
        var view = Focused("hello world");

        // Read before drawing: blanking the foreground to isolate the caret changes a font
        // property, which correctly discards the block until the next measure.
        var last = view.Block!.ContentSize;

        for (var i = 0; i <= last; i++)
        {
            view.State = EditorState.Create(view.State.Doc, TextSelection.Cursor(view.BlockStart + i));

            var columns = InkedColumns(view);

            Assert.True(columns.Count <= 1, $"caret at {i} spanned {columns.Count} columns");
        }
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ASnappedCaretIsExactlyOneDevicePixelOnTheGrid(double scale)
    {
        // The architecture's recipe, PixelRect.FromRect(rect, scale).ToRect(scale), is a
        // covering conversion: it floors the left edge and ceilings the right, so a one-wide
        // rectangle at a fractional x comes back two pixels wide. Measured:
        // FromRect(new Rect(10.7, 4.2, 1, 18.6), 1) is 10, 4, 2, 19. This asserts the property
        // that was actually wanted instead of the recipe that was assumed to give it.
        var snapped = RichTextView.SnapCaret(new Rect(10.7, 4.2, 1, 18.6), scale);

        Assert.Equal(1, snapped.Width * scale, 6);
        Assert.Equal(Math.Round(snapped.X * scale), snapped.X * scale, 6);
        Assert.Equal(Math.Round(snapped.Y * scale), snapped.Y * scale, 6);
        Assert.Equal(Math.Round(snapped.Height * scale), snapped.Height * scale, 6);
        Assert.True(snapped.Height > 0);
    }

    [AvaloniaFact]
    public void TheArchitecturesSnappingRecipeWouldHaveGivenATwoPixelCaret()
    {
        // Kept as evidence for the deviation, and as a tripwire: if a future Avalonia changes
        // FromRect to round rather than cover, this fails and the deviation can be revisited.
        var covering = PixelRect.FromRect(new Rect(10.7, 4.2, 1, 18.6), 1).ToRect(1);

        Assert.Equal(2, covering.Width);
        Assert.Equal(1, RichTextView.SnapCaret(new Rect(10.7, 4.2, 1, 18.6), 1).Width);
    }

    [AvaloniaFact]
    public void ANonsenseRenderScalingIsRefused()
    {
        var caret = new Rect(1, 1, 1, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => RichTextView.SnapCaret(caret, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RichTextView.SnapCaret(caret, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RichTextView.SnapCaret(caret, double.NaN));
    }

    [AvaloniaFact]
    public void ASelectionSpanningTheWholeDocumentRenders()
    {
        // A document range includes the block's open and close tokens, so it runs past both
        // ends of the block's own content — a naive "select all" is exactly this. Without
        // clamping, the block is asked for a range it does not have and throws while painting,
        // which in a real app is a crash on the render thread from a selection the model
        // considers perfectly valid.
        var view = Laid("hello world");
        var whole = new TextSelection(0, view.State.Doc.ContentSize);

        Assert.True(whole.To > view.BlockStart + view.Block!.ContentSize);

        view.State = EditorState.Create(view.State.Doc, whole);

        Assert.True(InkedPixels(view, includeText: true) > 0);
    }

    [AvaloniaFact]
    public void MovingTheCaretMakesItSolidAgain()
    {
        // A caret moved just before the blink tick would otherwise vanish on arrival, which
        // reads as a dropped keystroke.
        var view = Focused("hello world");

        typeof(RichTextView)
            .GetField("_caretVisible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(view, false);

        view.State = EditorState.Create(view.State.Doc, TextSelection.Cursor(view.BlockStart + 3));

        Assert.True(view.IsCaretVisible);
    }

    [AvaloniaFact]
    public void LeavingTheTreeStopsTheBlinkTimer()
    {
        // The timer's Tick handler captures the view, so a view that leaves without stopping it
        // is kept alive repainting nothing.
        //
        // Focus is deliberately not used to start the timer here. Detaching a focused control
        // also blurs it, and the blur alone stops the timer, so a test that focuses first
        // passes whether or not the detach handler exists. Starting the timer directly is what
        // makes this test able to fail.
        var root = new Window { Width = 400, Height = 200 };
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("hi")) };

        root.Content = view;
        root.Show();

        Timer(view).Start();

        Assert.True(Timer(view).IsEnabled);

        root.Content = null;

        Assert.False(Timer(view).IsEnabled);
    }

    [AvaloniaFact]
    public void LosingFocusStopsTheBlinkTimerAndHidesTheCaret()
    {
        var root = new Window { Width = 400, Height = 200 };
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var other = new Button();
        var panel = new StackPanel();

        panel.Children.Add(view);
        panel.Children.Add(other);
        root.Content = panel;
        root.Show();
        view.Focus();

        Assert.True(Timer(view).IsEnabled);
        Assert.True(view.IsCaretVisible);

        other.Focus();

        Assert.False(Timer(view).IsEnabled);
        Assert.False(view.IsCaretVisible);
    }

    private static global::Avalonia.Threading.DispatcherTimer Timer(RichTextView view) =>
        (global::Avalonia.Threading.DispatcherTimer)typeof(RichTextView)
            .GetField("_blink", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(view)!;

    private static RichTextView Focused(string text, double width = 400)
    {
        var root = new Window { Width = width, Height = 200 };
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(text)) };

        root.Content = view;
        root.Show();
        view.Focus();

        view.Measure(new Size(width, 200));
        view.Arrange(new Rect(0, 0, width, 200));

        return view;
    }

    private static double Remeasure(RichTextView view)
    {
        view.Measure(new Size(1000, 400));

        return view.Block!.Size.Width;
    }

    private static int CaretPixels(RichTextView view) => InkedPixels(view);

    /// <summary>
    /// The distinct opaque colours the view draws, for a document with nothing in it but a caret.
    /// </summary>
    /// <remarks>
    /// Deliberately does not blank the text, because blanking works by making the foreground
    /// transparent and the caret's whole contract is that it follows that foreground. An empty
    /// paragraph has no glyphs, so the caret is the only thing left to draw.
    /// </remarks>
    private static List<uint> CaretColours(RichTextView view) =>
        [.. Draw(view, includeText: true).Where(p => Alpha(p) > 200).Distinct()];

    private static uint Alpha(uint p) => (p >> 24) & 0xFF;

    private static uint Red(uint p) => (p >> 16) & 0xFF;

    private static uint Green(uint p) => (p >> 8) & 0xFF;

    private static uint Blue(uint p) => p & 0xFF;

    private static int CaretColumn(RichTextView view)
    {
        var columns = InkedColumns(view);

        Assert.NotEmpty(columns);

        return columns[0];
    }

    private static int InkedPixels(RichTextView view, bool includeText = false)
    {
        return Draw(view, includeText).Count(p => p != 0);
    }

    private static List<int> InkedColumns(RichTextView view)
    {
        var size = new PixelSize(400, 200);
        var pixels = Draw(view, includeText: false);
        var columns = new List<int>();

        for (var x = 0; x < size.Width; x++)
        {
            for (var y = 0; y < size.Height; y++)
            {
                if (pixels[(y * size.Width) + x] != 0)
                {
                    columns.Add(x);
                    break;
                }
            }
        }

        return columns;
    }

    /// <summary>
    /// Renders the view and returns its pixels, optionally with the text blanked out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Blanking the text is how a caret assertion is made without the glyphs drowning it out:
    /// the foreground and the selection fill are both made transparent, so what is left is
    /// exactly the caret. Leaving the selection fill in would count the highlight as caret ink
    /// and every "the caret is hidden" assertion would be measuring the wrong thing.
    /// </para>
    /// <para>
    /// Changing the foreground is a font-property change, so it correctly discards the block.
    /// Anything read off <see cref="RichTextView.Block"/> must therefore be read before calling
    /// this, or re-read after a measure.
    /// </para>
    /// </remarks>
    private static uint[] Draw(RichTextView view, bool includeText)
    {
        var foreground = TextElement.GetForeground(view);
        var selection = view.SelectionBrush;
        var caret = view.CaretBrush;

        if (!includeText)
        {
            TextElement.SetForeground(view, Brushes.Transparent);
            view.SelectionBrush = null;

            // The caret follows the text colour unless it is told otherwise, so blanking the
            // text would blank the caret with it and every assertion below would measure an
            // empty bitmap. Pinning it is what makes the caret the only thing left drawing.
            view.CaretBrush = Brushes.Black;
        }

        try
        {
            view.Measure(new Size(400, 200));
            view.Arrange(new Rect(0, 0, 400, 200));

            using var bitmap = new RenderTargetBitmap(new PixelSize(400, 200));

            using (var context = bitmap.CreateDrawingContext())
            {
                view.Render(context);
            }

            var pixels = new uint[400 * 200];

            unsafe
            {
                fixed (uint* buffer = pixels)
                {
                    bitmap.CopyPixels(
                        new PixelRect(new PixelSize(400, 200)),
                        (nint)buffer,
                        pixels.Length * sizeof(uint),
                        400 * sizeof(uint));
                }
            }

            return pixels;
        }
        finally
        {
            TextElement.SetForeground(view, foreground);
            view.SelectionBrush = selection;
            view.CaretBrush = caret;
        }
    }
}
