using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Tests for <see cref="ParagraphView"/>.
/// </summary>
/// <remarks>
/// Geometry assertions are relative — one rectangle against another, a rectangle inside a line —
/// because absolute pixel values depend on whichever font the machine falls back to. The harness
/// runs on real Skia text shaping (see <c>TestAppBuilder</c>), so the relationships are real.
/// </remarks>
public class ParagraphViewTests
{
    private static TextStyleResolver Styles() => new(new Typeface("Segoe UI"), 14, Brushes.Black);

    private static ParagraphView View(ParagraphNode paragraph) => new(paragraph, Styles());

    private static ParagraphView Measured(string text, double width = 400)
    {
        var view = View(ParagraphNode.FromText(text));
        view.Measure(width);
        return view;
    }

    [AvaloniaFact]
    public void GeometryBeforeMeasureIsAMistakeNotAnEmptyResult()
    {
        // Silently returning an empty rect would turn "the host forgot to measure" into a caret
        // drawn at the origin, which looks like a layout bug anywhere but here.
        var view = View(ParagraphNode.FromText("hello"));

        Assert.Throws<InvalidOperationException>(() => view.GetCaretRect(0));
    }

    [AvaloniaFact]
    public void MeasuringReportsTheSpaceTheTextNeeds()
    {
        var view = Measured("hello world");

        Assert.True(view.Size.Width > 0);
        Assert.True(view.Size.Height > 0);
    }

    [AvaloniaFact]
    public void ANonPositiveWidthIsRejected()
    {
        var view = View(ParagraphNode.FromText("hello"));

        Assert.Throws<ArgumentOutOfRangeException>(() => view.Measure(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Measure(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Measure(double.NaN));
    }

    [AvaloniaFact]
    public void TextWrapsToTheWidthItIsGiven()
    {
        var text = string.Join(' ', Enumerable.Repeat("wrap", 40));

        Assert.True(Measured(text, 1000).Layout.TextLines.Count < Measured(text, 120).Layout.TextLines.Count);
    }

    [AvaloniaFact]
    public void MeasuringTwiceAtTheSameWidthReusesTheLayout()
    {
        var view = Measured("hello world");
        var first = view.Layout;

        view.Measure(400);

        Assert.Same(first, view.Layout);
    }

    [AvaloniaFact]
    public void MeasuringAtANewWidthRelaysOut()
    {
        var view = Measured("hello world");
        var first = view.Layout;

        view.Measure(200);

        Assert.NotSame(first, view.Layout);
    }

    [AvaloniaFact]
    public void UpdatingToAnEqualParagraphChangesNothing()
    {
        var view = Measured("hello world");
        var layout = view.Layout;

        Assert.False(view.Update(ParagraphNode.FromText("hello world")));
        Assert.Same(layout, view.Layout);
    }

    [AvaloniaFact]
    public void UpdatingTheTextDiscardsTheLayout()
    {
        var view = Measured("hello world");

        Assert.True(view.Update(ParagraphNode.FromText("hello worlds")));
        Assert.Throws<InvalidOperationException>(() => view.GetCaretRect(0));
    }

    [AvaloniaFact]
    public void AnEditIsReflectedInTheGeometryAfterRemeasuring()
    {
        // This is the guard the Increment 0 finding demands. TextRunCache.InvalidateFrom(k>0)
        // returns the pre-edit layout without ever calling the text source, so an editor that
        // drove the cache by offset would keep drawing the old paragraph. Asserting that the
        // width actually grows after appending text is what proves the cache is being driven by
        // lifetime instead. See docs/increment-0-findings.md section 2.
        var view = Measured("hello", 1000);
        var before = view.Size.Width;

        view.Update(ParagraphNode.FromText("hello and then a good deal more text besides"));
        view.Measure(1000);

        Assert.True(
            view.Size.Width > before * 1.5,
            $"width was {before} before the edit and {view.Size.Width} after; the layout is stale");
    }

    [AvaloniaFact]
    public void RewrappingUnchangedTextKeepsTheRunCache()
    {
        // The other half of the same finding: a width-only change is the case where keeping the
        // cache is both correct and worth roughly 3.5x, so it must not be thrown away. The
        // cache is not observable directly, so this asserts the condition that governs it -
        // Update is what replaces it, and a width change does not go through Update.
        var view = Measured("hello world", 1000);
        var cache = RunCacheOf(view);

        view.Measure(120);

        Assert.Same(cache, RunCacheOf(view));
    }

    [AvaloniaFact]
    public void EditingReplacesTheRunCache()
    {
        var view = Measured("hello world");
        var cache = RunCacheOf(view);

        view.Update(ParagraphNode.FromText("hello worlds"));

        Assert.NotSame(cache, RunCacheOf(view));
    }

    [AvaloniaFact]
    public void ChangingOnlyAnAttributeKeepsTheRunCache()
    {
        // Alignment rearranges shaped runs; it does not reshape them.
        var view = Measured("hello world");
        var cache = RunCacheOf(view);

        Assert.True(view.Update(view.Paragraph.WithAlign(TextAlign.Center)));
        Assert.Same(cache, RunCacheOf(view));
    }

    [AvaloniaFact]
    public void TheCaretAdvancesAcrossTheText()
    {
        var view = Measured("hello");

        var first = view.GetCaretRect(0);
        var later = view.GetCaretRect(3);

        Assert.Equal(0, first.Width);
        Assert.True(later.X > first.X);
        Assert.True(first.Height > 0);
    }

    [AvaloniaFact]
    public void TheCaretIsAvailableAtEveryPositionIncludingTheEnd()
    {
        var view = Measured("hello");

        for (var i = 0; i <= view.ContentSize; i++)
        {
            Assert.True(view.GetCaretRect(i).Height > 0);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => view.GetCaretRect(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.GetCaretRect(view.ContentSize + 1));
    }

    [AvaloniaFact]
    public void AnEmptySelectionHasNoRectangles()
    {
        Assert.Empty(Measured("hello").GetSelectionRects(2, 2));
    }

    [AvaloniaFact]
    public void ASelectionIsTheSameEitherWayRound()
    {
        var view = Measured("hello world");

        Assert.Equal(view.GetSelectionRects(2, 7), view.GetSelectionRects(7, 2));
    }

    [AvaloniaFact]
    public void ASelectionCoversTheTextItSpansAndNoMore()
    {
        var view = Measured("hello world");
        var part = view.GetSelectionRects(0, 5);
        var whole = view.GetSelectionRects(0, view.ContentSize);

        Assert.Single(part);
        Assert.Single(whole);
        Assert.True(part[0].Width < whole[0].Width);
    }

    [AvaloniaFact]
    public void AWrappedSelectionProducesOneRectanglePerLine()
    {
        var text = string.Join(' ', Enumerable.Repeat("wrap", 40));
        var view = Measured(text, 120);

        var rects = view.GetSelectionRects(0, view.ContentSize);

        Assert.Equal(view.Layout.TextLines.Count, rects.Count);
        Assert.True(rects.Count > 1);
    }

    [AvaloniaFact]
    public void GetTextBoundsClampsALengthThatOverrunsTheLine()
    {
        // Characterization, and the reason ParagraphView's own clipping of a selection to each
        // line is belt-and-braces rather than load-bearing: asking a line for more than it holds
        // does not throw and does not reach into the next line. Recorded because removing the
        // clip is otherwise an invisible change, and because a future Avalonia that stops
        // clamping should fail here rather than start drawing selection rectangles over the
        // wrong lines.
        var text = string.Join(' ', Enumerable.Repeat("wrap", 40));
        var view = Measured(text, 120);
        var line = view.Layout.TextLines[0];

        var clamped = line.GetTextBounds(0, line.Length);
        var overrun = line.GetTextBounds(0, view.ContentSize);

        Assert.Equal(
            clamped.Sum(b => b.Rectangle.Width),
            overrun.Sum(b => b.Rectangle.Width),
            3);
    }

    [AvaloniaFact]
    public void ASelectionAcrossABidiBoundaryIsDisjoint()
    {
        // Increment 0 measured this: a logically contiguous range that crosses a direction
        // change is visually two pieces, and a highlight that drew one rectangle from the first
        // character to the last would cover text that is not selected.
        var view = Measured("hello \u05e9\u05dc\u05d5\u05dd world", 1000);

        var rects = view.GetSelectionRects(3, 9);

        Assert.True(rects.Count > 1, $"expected disjoint rectangles, got {rects.Count}");
    }

    [AvaloniaFact]
    public void HitTestingRoundTripsWithTheCaret()
    {
        var view = Measured("hello world");

        for (var i = 0; i <= view.ContentSize; i++)
        {
            var caret = view.GetCaretRect(i);

            Assert.Equal(i, view.HitTest(new Point(caret.X, caret.Y + (caret.Height / 2))));
        }
    }

    [AvaloniaFact]
    public void HitTestingOutsideTheTextClampsIntoIt()
    {
        var view = Measured("hello");

        Assert.Equal(0, view.HitTest(new Point(-500, -500)));
        Assert.Equal(view.ContentSize, view.HitTest(new Point(5000, 5000)));
    }

    [AvaloniaFact]
    public void CaretMovementStepsOverAWholeSurrogatePair()
    {
        // U+1D160 is astral, so it is two code units and one caret step.
        var view = Measured("a\U0001D160b");

        Assert.Equal(1, view.NextCaretPosition(0));
        Assert.Equal(3, view.NextCaretPosition(1));
        Assert.Equal(1, view.PreviousCaretPosition(3));
    }

    [AvaloniaFact]
    public void CaretMovementStepsOverAZwjSequence()
    {
        var family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        var view = Measured("a" + family + "b");

        Assert.Equal(1 + family.Length, view.NextCaretPosition(1));
        Assert.Equal(1, view.PreviousCaretPosition(1 + family.Length));
    }

    [AvaloniaFact]
    public void CaretMovementStepsOverACombiningMark()
    {
        var view = Measured("ae\u0301b");

        Assert.Equal(3, view.NextCaretPosition(1));
        Assert.Equal(1, view.PreviousCaretPosition(3));
    }

    [AvaloniaFact]
    public void BackspacePeelsAComponentWhereTheArrowKeyStepsOverTheCluster()
    {
        // The behaviour the user chose in Increment 0: Backspace removes the combining mark and
        // leaves the base letter, which is what every other editor does, whereas Left steps over
        // the whole cluster.
        var view = Measured("ae\u0301b");

        Assert.Equal(2, view.BackspacePosition(3));
        Assert.Equal(1, view.PreviousCaretPosition(3));
    }

    [AvaloniaFact]
    public void CaretMovementIsClampedAtBothEnds()
    {
        var view = Measured("ab");

        Assert.Equal(0, view.PreviousCaretPosition(0));
        Assert.Equal(0, view.BackspacePosition(0));
        Assert.Equal(view.ContentSize, view.NextCaretPosition(view.ContentSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.NextCaretPosition(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.NextCaretPosition(view.ContentSize + 1));
    }

    [AvaloniaFact]
    public void CaretMovementCrossesAWrapBoundary()
    {
        // A wrap boundary is one position owned by two lines, and TextLine's caret methods
        // refuse to leave the line they are asked on. Move left off the start of a wrapped line
        // and the answer has to come from the line above, or the caret is stuck there forever.
        var text = string.Join(' ', Enumerable.Repeat("wrap", 40));
        var view = Measured(text, 120);
        var secondLineStart = view.Layout.TextLines[0].Length;

        var back = view.PreviousCaretPosition(secondLineStart);

        Assert.True(back < secondLineStart, $"moving left from {secondLineStart} stayed at {back}");
        Assert.Equal(secondLineStart, view.NextCaretPosition(back));
    }

    [AvaloniaFact]
    public void TheCaretCanWalkAWrappedParagraphEndToEnd()
    {
        // The stronger form of the same guard: every position must be reachable going forward
        // and the walk must come back the same way. A caret pinned at any line start shows up
        // here as a walk that never terminates.
        var text = string.Join(' ', Enumerable.Repeat("wrap", 40));
        var view = Measured(text, 120);

        Assert.True(view.Layout.TextLines.Count > 3);

        var forward = new List<int> { 0 };

        for (var p = 0; p < view.ContentSize;)
        {
            var next = view.NextCaretPosition(p);

            Assert.True(next > p, $"forward walk stalled at {p} of {view.ContentSize}");

            forward.Add(next);
            p = next;
        }

        var backward = new List<int> { view.ContentSize };

        for (var p = view.ContentSize; p > 0;)
        {
            var previous = view.PreviousCaretPosition(p);

            Assert.True(previous < p, $"backward walk stalled at {p}");

            backward.Add(previous);
            p = previous;
        }

        backward.Reverse();

        Assert.Equal(forward, backward);
    }

    [AvaloniaFact]
    public void AnEmbedIsOneCaretStep()
    {
        var content = InlineContent.FromText("ab")
            .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 30, 10)))
            .Concat(InlineContent.FromText("cd"));
        var view = View(new ParagraphNode(content));
        view.Measure(1000);

        Assert.Equal(3, view.NextCaretPosition(2));
        Assert.Equal(2, view.PreviousCaretPosition(3));
    }

    [AvaloniaFact]
    public void AnIndentLevelShiftsTheTextRight()
    {
        // The model carries IndentLevel, so dropping it on the floor would render a document
        // wrongly and silently. Every coordinate the view reports has to move with it.
        var plain = Measured("hello world", 1000);
        var indented = View(ParagraphNode.FromText("hello world").WithIndentLevel(2));
        indented.Measure(1000);

        var step = 2 * ParagraphView.DefaultIndentStep;

        Assert.Equal(step, indented.Indent);
        Assert.Equal(plain.GetCaretRect(3).X + step, indented.GetCaretRect(3).X, 3);
        Assert.Equal(plain.GetSelectionRects(0, 5)[0].X + step, indented.GetSelectionRects(0, 5)[0].X, 3);
        Assert.Equal(plain.Size.Width + step, indented.Size.Width, 3);
    }

    [AvaloniaFact]
    public void AnIndentLevelShiftsHitTestingToo()
    {
        var indented = View(ParagraphNode.FromText("hello world").WithIndentLevel(2));
        indented.Measure(1000);

        for (var i = 0; i <= indented.ContentSize; i++)
        {
            var caret = indented.GetCaretRect(i);

            Assert.Equal(i, indented.HitTest(new Point(caret.X, caret.Y + (caret.Height / 2))));
        }
    }

    [AvaloniaFact]
    public void AnIndentLevelNarrowsTheWidthTextWrapsTo()
    {
        var text = string.Join(' ', Enumerable.Repeat("wrap", 40));
        var plain = Measured(text, 300);
        var indented = View(ParagraphNode.FromText(text).WithIndentLevel(4));
        indented.Measure(300);

        Assert.True(
            indented.Layout.TextLines.Count > plain.Layout.TextLines.Count,
            "an indented paragraph has less room and must wrap sooner");
    }

    [AvaloniaFact]
    public void ANegativeIndentStepIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ParagraphView(ParagraphNode.FromText("x"), Styles(), null, null, -1));
    }

    [AvaloniaFact]
    public void AnIndentWiderThanTheAvailableWidthStillLaysOut()
    {
        var view = new ParagraphView(ParagraphNode.FromText("hello").WithIndentLevel(10), Styles());

        view.Measure(50);

        Assert.NotEmpty(view.Layout.TextLines);
    }

    [AvaloniaFact]
    public void AlignmentIsTakenFromTheParagraph()
    {
        var left = View(ParagraphNode.FromText("hi").WithAlign(TextAlign.Left));
        var right = View(ParagraphNode.FromText("hi").WithAlign(TextAlign.Right));
        left.Measure(500);
        right.Measure(500);

        Assert.True(right.GetCaretRect(0).X > left.GetCaretRect(0).X);
    }

    [AvaloniaFact]
    public void AnEmptyParagraphStillHasACaret()
    {
        var view = View(ParagraphNode.Empty);
        view.Measure(400);

        Assert.Equal(0, view.ContentSize);
        Assert.True(view.GetCaretRect(0).Height > 0);
    }

    [AvaloniaFact]
    public void RenderingDrawsSomething()
    {
        var view = Measured("hello world");
        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 60));

        using (var context = bitmap.CreateDrawingContext())
        {
            view.Render(context, new Point(0, 0));
        }

        Assert.True(NonBlankPixels(bitmap) > 0);
    }

    [AvaloniaFact]
    public void RenderingHonoursTheIndent()
    {
        // Proves the indent reaches the actual drawing, not only the reported geometry.
        var plain = Measured("hello", 400);
        var indented = View(ParagraphNode.FromText("hello").WithIndentLevel(4));
        indented.Measure(400);

        Assert.True(LeftmostInkedColumn(indented) > LeftmostInkedColumn(plain));
    }

    private static TextRunCache RunCacheOf(ParagraphView view)
    {
        var field = typeof(ParagraphView).GetField(
            "_runCache",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);

        return (TextRunCache)field.GetValue(view)!;
    }

    private static int LeftmostInkedColumn(ParagraphView view)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 60));

        using (var context = bitmap.CreateDrawingContext())
        {
            view.Render(context, new Point(0, 0));
        }

        return FirstInkedColumn(bitmap);
    }

    private static int FirstInkedColumn(RenderTargetBitmap bitmap)
    {
        var (pixels, width, height) = Read(bitmap);

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (pixels[(y * width) + x] != 0)
                {
                    return x;
                }
            }
        }

        return int.MaxValue;
    }

    private static int NonBlankPixels(RenderTargetBitmap bitmap)
    {
        var (pixels, _, _) = Read(bitmap);

        return pixels.Count(p => p != 0);
    }

    private static (uint[] Pixels, int Width, int Height) Read(RenderTargetBitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var pixels = new uint[size.Width * size.Height];

        unsafe
        {
            fixed (uint* buffer = pixels)
            {
                bitmap.CopyPixels(
                    new PixelRect(size),
                    (nint)buffer,
                    pixels.Length * sizeof(uint),
                    size.Width * sizeof(uint));
            }
        }

        return (pixels, size.Width, size.Height);
    }
}
