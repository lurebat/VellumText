using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

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

    [AvaloniaFact]
    public void EachWrappedLineOfASelectionIsHighlightedOnItsOwnLine()
    {
        // Reported as "selecting text when there is text wrap is broken". Avalonia measures a
        // TextBounds rectangle from the top of its own line, not from the top of the layout, so
        // translating by the paragraph origin alone stacked every line's highlight on the first
        // one: a ten-line selection painted one bar across line one and nothing below it.
        var text = string.Join(" ", Enumerable.Repeat("wrapping", 40));
        var view = View(ParagraphNode.FromText(text));

        view.Measure(300);

        Assert.True(view.Layout.TextLines.Count > 5, "the paragraph has to actually wrap");

        // Cross-checked against the caret, which reads its own geometry from the layout by a
        // different route and was never wrong. The two have to agree line for line.
        foreach (var line in view.Layout.TextLines)
        {
            var end = Math.Min(line.FirstTextSourceIndex + line.Length, text.Length);
            var rects = view.GetSelectionRects(line.FirstTextSourceIndex, end);

            Assert.NotEmpty(rects);
            Assert.All(
                rects,
                r => Assert.Equal(view.GetCaretRect(line.FirstTextSourceIndex).Y, r.Y, 3));
        }
    }


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

    /// <summary>
    /// Every public geometry method rejects the two ways a host can ask a question the paragraph
    /// cannot answer, so that the exception documentation is checked rather than asserted in prose.
    /// </summary>
    /// <remarks>
    /// Written because the docs claimed neither. A host reading them would have believed these
    /// methods total, and both failures are ones a host provokes by ordinary mistakes: forgetting
    /// to measure, or holding a position from before an edit.
    /// </remarks>
    [AvaloniaFact]
    public void GeometryRejectsAnUnmeasuredParagraphAndAnOutOfRangePosition()
    {
        var unmeasured = View(ParagraphNode.FromText("hello"));
        var measured = Measured("hello");
        var past = measured.ContentSize + 1;

        var byPosition = new Action<ParagraphView, int>[]
        {
            (v, p) => _ = v.GetCaretRect(p),
            (v, p) => _ = v.NextCaretPosition(p),
            (v, p) => _ = v.PreviousCaretPosition(p),
            (v, p) => _ = v.BackspacePosition(p),
            (v, p) => _ = v.LineIndexAt(p, forward: true),
        };

        foreach (var call in byPosition)
        {
            Assert.Throws<InvalidOperationException>(() => call(unmeasured, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => call(measured, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => call(measured, past));
        }

        // GetSelectionRects checks both ends, but an empty range is answered with no rectangles
        // before the layout is consulted at all — so it is only unmeasured-hostile when there is
        // something to measure. Documented that way because a caller collapsing a selection to a
        // caret legitimately asks this question before the first measure.
        Assert.Throws<ArgumentOutOfRangeException>(() => measured.GetSelectionRects(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => measured.GetSelectionRects(0, past));
        Assert.Empty(unmeasured.GetSelectionRects(0, 0));
        Assert.Throws<InvalidOperationException>(() => unmeasured.GetSelectionRects(0, 1));

        var byLine = new Action<ParagraphView, int>[]
        {
            (v, i) => _ = v.LineStart(i),
            (v, i) => _ = v.LineEnd(i),
            (v, i) => _ = v.PositionAtLineX(i, 0),
        };

        foreach (var call in byLine)
        {
            Assert.Throws<InvalidOperationException>(() => call(unmeasured, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => call(measured, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => call(measured, measured.LineCount));
        }

        // Not in either table: these take no index, so only the unmeasured failure applies.
        Assert.Throws<InvalidOperationException>(() => unmeasured.LineCount);
        Assert.Throws<InvalidOperationException>(() => unmeasured.HitTest(default));
        Assert.Throws<InvalidOperationException>(() => unmeasured.GetImageRect(0));
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

    [AvaloniaFact]
    public void ACodeBlocksFillLeavesTheSpaceAboveAndBelowItClear()
    {
        // The fill is the block's content box. SpaceBefore and SpaceAfter sit outside it, or two
        // code blocks in a row would close the gap between them and read as one.
        var view = new ParagraphView(
            ParagraphNode.FromText("code").WithKind(ParagraphKind.Code),
            Styles(),
            kinds: new ParagraphStyleResolver(codeBackground: Brushes.Red, spacing: 10));

        view.Measure(200);

        var height = (int)Math.Ceiling(view.Size.Height);

        using var bitmap = new RenderTargetBitmap(new PixelSize(200, height + 10));

        using (var context = bitmap.CreateDrawingContext())
        {
            view.Render(context, new Point(0, 0));
        }

        var (pixels, width, _) = Read(bitmap);

        // Down a column the text never reaches, clear of the rounded corners.
        bool Filled(int y) => pixels[(y * width) + 190] == 0xFFFF0000;

        Assert.All(Enumerable.Range(0, 9), y => Assert.False(Filled(y), $"row {y}, above the block, is filled"));
        Assert.All(Enumerable.Range(12, height - 24), y => Assert.True(Filled(y), $"row {y}, inside the block, is not filled"));
        Assert.All(Enumerable.Range(height - 9, 9), y => Assert.False(Filled(y), $"row {y}, below the block, is filled"));
    }

    [AvaloniaFact]
    public void AListMarkerEndsAGapShortOfTheText()
    {
        // Right-aligned into the gutter the depth opens up: a number of any width ends MarkerGap
        // before the text begins, rather than starting there and running into it.
        var view = View(ParagraphNode.Empty);

        view.SetLead(2, new ListMarker(ListKind.Ordered, 12));
        view.Measure(400);

        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 60));

        using (var context = bitmap.CreateDrawingContext())
        {
            view.Render(context, new Point(0, 0));
        }

        var (pixels, width, height) = Read(bitmap);

        // An empty paragraph draws nothing of its own, so every inked column is the marker's.
        var inked = Enumerable.Range(0, width)
            .Where(x => Enumerable.Range(0, height).Any(y => pixels[(y * width) + x] != 0))
            .ToArray();
        var gap = view.Indent - ParagraphView.MarkerGap;

        Assert.NotEmpty(inked);
        Assert.True(inked[^1] < gap, $"the marker is inked out to x={inked[^1]}, past the gap at {gap}");
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
        var pixels = RenderedPixels.Argb(bitmap);

        return (pixels, size.Width, size.Height);
    }
}
