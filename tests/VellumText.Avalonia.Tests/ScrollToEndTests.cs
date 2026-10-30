using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A deliberate request for the end of a document — Ctrl+End, or the offset taken to the end of
/// the scroll range, which is what the scroll bar's end and <see cref="ScrollViewer.ScrollToEnd"/>
/// hand the view — ends at the document's end, even when the extent the request was measured
/// against was far too small because the rows not yet seen hold most of the document.
/// </summary>
/// <remarks>
/// The offset is set on the view itself: the headless tests run without a theme, so a
/// <see cref="ScrollViewer"/> has no template to hand it on.
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

    private static void AskForTheEnd(string how, bool endsWithTable, bool atBottom)
    {
        var table = new TableNode(Enumerable.Range(0, 40).Select(r => new TableRowNode(r < 5
            ? [TableCellNode.FromText($"short {r}"), TableCellNode.FromText($"side {r}")]
            : [new TableCellNode([NestedTableVirtualizationTests.Uniform(200, tag: $"t{r}")]), TableCellNode.FromText($"side {r}")])));
        var doc = endsWithTable ? new DocumentNode([table]) : new DocumentNode([table, ParagraphNode.FromText("The end.")]);
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        GeometryTransactionTests.Layout(window);

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

        var last = view.Blocks.Count - 1;
        var bottom = view.BlockOrigin(last).Y + view.Blocks[last].Size.Height;

        Assert.True(
            !atBottom || Math.Abs(bottom - view.Viewport.Height) <= 1,
            $"the document ends at {bottom:F1} on screen, not at the bottom of the viewport, {view.Viewport.Height:F1}; the extent is {view.Extent.Height:F0} of {truth:F0}");
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);

        if (how == "ctrl+end")
        {
            Assert.InRange(view.CaretRect().Bottom, 0, view.Viewport.Height + 1);
        }

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }
}