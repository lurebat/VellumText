using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A deliberate request for the end of a document — Ctrl+End, <see cref="RichTextView.ScrollToEnd"/>,
/// or the scroll bar taken to its end — ends at the document's end, even when the extent the
/// request was measured against was far too small because the rows not yet seen hold most of the
/// document. The wheel stays a plain scroll.
/// </summary>
/// <remarks>
/// Most tests work on the view itself: the headless tests run without a theme, so a
/// <see cref="ScrollViewer"/> has no template to hand an offset on. The scroll bar tests need its
/// template and load the Fluent theme into their own window, never into the shared application.
/// </remarks>
public class ScrollToEndTests
{
    private const double Width = 500;
    private const double Height = 400;

    /// <summary>
    /// Forty rows, the first five short and each of the rest holding a 200-row table, so the extent
    /// after the first layout is a tenth of the document, and a paragraph after the table; then
    /// the end asked for once. Before Ctrl+End meant the end, the caret was aimed at where the
    /// estimates put it, the offset clamped at the too-small extent, and the document ended 8,389
    /// pixels below the bottom of the viewport, the caret with it.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("ctrl+end")]
    [InlineData("offset")]
    public void AskingForTheEndOnceShowsTheLastLineAtTheBottom(string how)
    {
        AskForTheEnd(how, endsWithTable: false, atBottom: true);
    }

    /// <summary>
    /// The same document with the table the last thing in it: the last place the caret can sit is
    /// the text beside the last row's table, at that row's top, and Ctrl+End shows it — after the
    /// geometry around the end has been laid out from the end up.
    /// </summary>
    [AvaloniaFact]
    public void CtrlEndBesideALongTableInTheLastRowShowsTheCaret()
    {
        AskForTheEnd("ctrl+end", endsWithTable: true, atBottom: false);
    }

    /// <summary>
    /// <see cref="RichTextView.ScrollToEnd"/> on the same documents: one call and one layout pass
    /// put the document's end at the bottom of the viewport, with the screen laid out, and leave
    /// the caret and the selection where they were — that is the difference from Ctrl+End. Ending
    /// in the table is the case an offset at the end of the scroll range stops short in.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollToEndShowsTheEndAndLeavesTheSelectionAlone(bool endsWithTable)
    {
        var doc = Nested(endsWithTable);
        var from = GeometryTransactionTests.PositionOf(doc, "short 1");
        var view = new RichTextView { State = EditorState.Create(doc, new TextSelection(from, from + 3)) };
        var window = Shown(view);
        var truth = GeometryTransactionTests.FullyLaidOut(doc, Width).DesiredSize.Height;
        var state = view.State;

        Assert.True(view.Extent.Height < truth / 4, $"the extent {view.Extent.Height:F0} is not far below the truth {truth:F0}");

        view.ScrollToEnd();
        GeometryTransactionTests.Layout(window);

        AssertEndAtBottom(view, truth);
        Assert.Equal(new TextSelection(from, from + 3), view.State.Selection);
        Assert.Same(state, view.State);

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }

    /// <summary>A long run of plain paragraphs, nothing nested: the end at the bottom all the same.</summary>
    [AvaloniaFact]
    public void ScrollToEndOnPlainParagraphsShowsTheEnd()
    {
        var doc = DocumentNode.FromParagraphs([.. Enumerable.Range(0, 3000).Select(i => $"Paragraph {i}, with words enough in it to wrap at least once in a narrow window.")]);
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = Shown(view);
        var state = view.State;

        view.ScrollToEnd();
        GeometryTransactionTests.Layout(window);

        AssertEndAtBottom(view, truth: double.NaN);
        Assert.Same(state, view.State);
    }

    /// <summary>
    /// Nothing to scroll: an empty document, one that fits, and a view nobody has laid out yet. No
    /// throw, and the view stays at the top.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("A single short line.")]
    public void ScrollToEndOnADocumentThatFitsDoesNothing(string text)
    {
        var doc = text.Length == 0 ? DocumentNode.Empty : DocumentNode.FromParagraphs(text);

        new RichTextView { State = EditorState.Create(doc) }.ScrollToEnd();

        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = Shown(view);
        var state = view.State;

        view.ScrollToEnd();
        GeometryTransactionTests.Layout(window);

        Assert.Equal(0, view.Offset.Y);
        Assert.Same(state, view.State);
    }

    /// <summary>
    /// A scroll after the request and before the layout pass that would have carried it out is
    /// the later word: the view goes where it was scrolled to, and the request does not come back
    /// to yank it to the end on a later pass.
    /// </summary>
    [AvaloniaFact]
    public void AScrollBeforeTheLayoutCancelsScrollToEnd()
    {
        var view = new RichTextView { State = EditorState.Create(Nested(endsWithTable: false)) };
        var window = Shown(view);

        view.ScrollToEnd();
        view.Offset = new Vector(0, 1000);
        GeometryTransactionTests.Layout(window);
        view.InvalidateMeasure();
        GeometryTransactionTests.Layout(window);

        var end = EndOnScreen(view);

        Assert.True(end > 2 * view.Viewport.Height, $"the document ends at {end:F1} on screen: the cancelled request took the view to the end");
        Assert.InRange(view.Offset.Y, 500, 1500);
    }

    /// <summary>
    /// Once carried out, the request is spent: the next layout pass keeps the line at the top of
    /// the viewport where it is, as any pass does (owner decision Q3), rather than going to the end
    /// again. A narrower window is such a pass.
    /// </summary>
    [AvaloniaFact]
    public void AfterScrollToEndTheLineAtTheTopIsHeldAsEver()
    {
        var view = new RichTextView { State = EditorState.Create(Nested(endsWithTable: false)) };
        var window = Shown(view);

        view.ScrollToEnd();
        GeometryTransactionTests.Layout(window);

        var line = view.PositionAt(new Point(10, 1));
        var top = GeometryTransactionTests.ScreenTop(view, line);

        window.Width = Width - 120;
        GeometryTransactionTests.Layout(window);

        var after = GeometryTransactionTests.ScreenTop(view, line);

        Assert.True(Math.Abs(after - top) <= 1, $"the line at the top moved from {top:F1} to {after:F1} when the window narrowed: the spent request was carried out again");
    }

    /// <summary>
    /// The scroll bar's thumb dragged to the bottom, through the Fluent theme's own scroll bar and
    /// a real pointer: the document's end at the bottom of the viewport. Before the scroll bar's end
    /// was taken as a request for the end, the offset clamped at the too-small extent and the
    /// document ending in the long table stopped thousands of pixels short.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void DraggingTheScrollBarToTheBottomShowsTheEnd(bool endsWithTable)
    {
        var doc = Nested(endsWithTable);
        var (window, _, view, bar) = ShownInEditor(doc);
        var truth = GeometryTransactionTests.FullyLaidOut(doc, view.Bounds.Width).DesiredSize.Height;
        var state = view.State;

        Assert.True(view.Extent.Height < truth / 4, $"the extent {view.Extent.Height:F0} is not far below the truth {truth:F0}");

        DragThumb(window, bar, Height * 2);

        AssertEndAtBottom(view, truth);
        Assert.Equal(bar.Maximum, bar.Value);
        Assert.Same(state, view.State);

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }

    /// <summary>
    /// The scroll bar's own steps — a page, as a click on the track makes, or a line, as an arrow
    /// makes — show the end when one lands at the bottom. Started where the wheel leaves a document
    /// ending in the long table, short of the end, and stepped down until a step lands there. A line
    /// gets there in one step (the arrow's step is three estimated blocks, and this document is one
    /// block); pages take dozens, laying the end out on the way, so that case is a guard.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("page")]
    [InlineData("line")]
    public void AScrollBarStepThatReachesTheBottomShowsTheEnd(string step)
    {
        var doc = Nested(endsWithTable: true);
        var (window, _, view, bar) = ShownInEditor(doc);
        var landed = false;

        Assert.IsType<ScrollViewer>(view.Parent).Offset = new Vector(0, double.PositiveInfinity);
        GeometryTransactionTests.Layout(window);

        bar.Scroll += (_, e) => landed |= e.NewValue >= bar.Maximum;

        for (var steps = 0; steps < 500 && !landed; steps++)
        {
            if (step == "page")
            {
                bar.PageDown();
            }
            else
            {
                bar.LineDown();
            }

            Dispatcher.UIThread.RunJobs();
            GeometryTransactionTests.Layout(window);
        }

        Assert.True(landed, "no step reached the bottom of the scroll bar");
        AssertEndAtBottom(view, GeometryTransactionTests.FullyLaidOut(doc, view.Bounds.Width).DesiredSize.Height);
    }

    /// <summary>The thumb dragged part of the way is a plain scroll to where it was dragged.</summary>
    [AvaloniaFact]
    public void DraggingTheScrollBarPartWayIsAPlainScroll()
    {
        var (window, _, view, bar) = ShownInEditor(Nested(endsWithTable: true));

        DragThumb(window, bar, Height / 4);

        var end = EndOnScreen(view);

        Assert.InRange(bar.Value, 1, bar.Maximum / 2);
        Assert.Equal(bar.Value, view.Offset.Y, 0.5);
        Assert.True(end > 2 * view.Viewport.Height, $"the document ends at {end:F1} on screen: a scroll part of the way went to the end");
    }

    /// <summary>
    /// The wheel run to the bottom of the scroll range stays a plain scroll: it goes to the end of
    /// the range, which for a document ending in the long table is short of the document's end,
    /// rather than jumping screens past where the wheel was taking it. Pins that the end request is
    /// the scroll bar's and not every offset at the end of the range.
    /// </summary>
    [AvaloniaFact]
    public void TheWheelToTheBottomStaysAPlainScroll()
    {
        var (window, _, view, _) = ShownInEditor(Nested(endsWithTable: true));
        var over = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), window)!.Value;

        window.MouseWheel(over, new Vector(0, -100_000));
        Dispatcher.UIThread.RunJobs();
        GeometryTransactionTests.Layout(window);

        var end = EndOnScreen(view);

        Assert.True(view.Offset.Y > view.Extent.Height / 2, $"the wheel left the view at {view.Offset.Y:F0} of {view.Extent.Height:F0}");
        Assert.True(end > 2 * view.Viewport.Height, $"the document ends at {end:F1} on screen: the wheel jumped to the end");
    }

    /// <summary>
    /// Given new chrome, the editor listens to the new scroll viewer's bar, and no longer to the
    /// discarded one: that bar taken to its end does nothing to the view now shown.
    /// </summary>
    [AvaloniaFact]
    public void ARetemplatedEditorListensToItsNewScrollBarOnly()
    {
        var doc = Nested(endsWithTable: true);
        var (window, editor, _, discarded) = ShownInEditor(doc);

        editor.Template = RetemplateTests.Chrome();
        GeometryTransactionTests.Layout(window);

        var view = editor.View!;
        var bar = VerticalBarOf(view);

        Assert.NotSame(discarded, bar);

        discarded.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        GeometryTransactionTests.Layout(window);

        Assert.Equal(0, view.Offset.Y);

        DragThumb(window, bar, Height * 2);

        AssertEndAtBottom(view, GeometryTransactionTests.FullyLaidOut(doc, view.Bounds.Width).DesiredSize.Height);
    }
    /// <summary>
    /// Forty rows, the first five short and each of the rest holding a 200-row table, then — unless
    /// <paramref name="endsWithTable"/> — a paragraph.
    /// </summary>
    private static DocumentNode Nested(bool endsWithTable)
    {
        var table = new TableNode(Enumerable.Range(0, 40).Select(r => new TableRowNode(r < 5
            ? [TableCellNode.FromText($"short {r}"), TableCellNode.FromText($"side {r}")]
            : [new TableCellNode([NestedTableVirtualizationTests.Uniform(200, tag: $"t{r}")]), TableCellNode.FromText($"side {r}")])));

        return endsWithTable ? new DocumentNode([table]) : new DocumentNode([table, ParagraphNode.FromText("The end.")]);
    }

    private static Window Shown(RichTextView view)
    {
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        GeometryTransactionTests.Layout(window);

        return window;
    }

    /// <summary>An editor in a window with the Fluent theme, so its scroll viewer has a scroll bar.</summary>
    private static (Window Window, RichTextEditor Editor, RichTextView View, ScrollBar Bar) ShownInEditor(DocumentNode doc)
    {
        var editor = new RichTextEditor { State = EditorState.Create(doc) };
        var window = new Window { Content = editor, Width = Width, Height = Height };

        // Into the window rather than the application, which every test in the assembly shares.
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        window.Show();
        GeometryTransactionTests.Layout(window);

        return (window, editor, editor.View!, VerticalBarOf(editor.View!));
    }

    private static ScrollBar VerticalBarOf(RichTextView view) =>
        Assert.IsType<ScrollViewer>(view.Parent).GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Name == "PART_VerticalScrollBar");

    /// <summary>Presses on the thumb, moves the pointer down by <paramref name="by"/> in four steps, and lets go.</summary>
    private static void DragThumb(Window window, ScrollBar bar, double by)
    {
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var from = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);

        for (var step = 1; step <= 4; step++)
        {
            window.MouseMove(from + new Vector(0, by * step / 4));
            Dispatcher.UIThread.RunJobs();
            GeometryTransactionTests.Layout(window);
        }

        window.MouseUp(from + new Vector(0, by), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        GeometryTransactionTests.Layout(window);
    }

    /// <summary>Where the last block ends, in view coordinates.</summary>
    private static double EndOnScreen(RichTextView view)
    {
        var last = view.Blocks.Count - 1;

        return view.BlockOrigin(last).Y + view.Blocks[last].Size.Height;
    }

    private static void AssertEndAtBottom(RichTextView view, double truth)
    {
        var end = EndOnScreen(view);

        Assert.True(
            Math.Abs(end - view.Viewport.Height) <= 1,
            $"the document ends at {end:F1} on screen, not at the bottom of the viewport, {view.Viewport.Height:F1}; the extent is {view.Extent.Height:F0} of {truth:F0}");
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);
    }

    private static void AskForTheEnd(string how, bool endsWithTable, bool atBottom)
    {
        var doc = Nested(endsWithTable);
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = Shown(view);
        var truth = GeometryTransactionTests.FullyLaidOut(doc, Width).DesiredSize.Height;

        Assert.True(view.Extent.Height < truth / 4, $"the extent {view.Extent.Height:F0} is not far below the truth {truth:F0}");

        if (how == "ctrl+end")
        {
            // What Ctrl+End does (RichTextView.Input): the caret to the document's last position.
            view.MoveTo(view.State.Doc.ContentSize, extend: false);
        }
        else
        {
            view.Offset = new Vector(0, view.Extent.Height - view.Viewport.Height);
        }

        GeometryTransactionTests.Layout(window);

        if (atBottom)
        {
            AssertEndAtBottom(view, truth);
        }
        else
        {
            NestedTableVirtualizationTests.AssertScreenLaidOut(view);
        }

        if (how == "ctrl+end")
        {
            Assert.InRange(view.CaretRect().Bottom, 0, view.Viewport.Height + 1);
        }

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }
}