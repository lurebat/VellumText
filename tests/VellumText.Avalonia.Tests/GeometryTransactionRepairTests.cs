using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What happens when a geometry question asked outside a layout pass leaves the viewport not laid
/// out — which the passes are built never to do, so it is forced through a test seam.
/// </summary>
public class GeometryTransactionRepairTests
{
    /// <summary>
    /// The next frame is made to wait for a layout pass that fills the hole, through the ordinary
    /// dispatcher scheduling, rather than drawing it blank.
    /// </summary>
    [AvaloniaFact]
    public void AQuestionThatLeavesTheViewportUncoveredGetsALayoutPassBeforeTheNextFrame()
    {
        // The hole is left in the table on screen; the question is about another table, far down.
        var shown = new TableNode(Enumerable.Range(0, 20).Select(r => new TableRowNode([
            TableCellNode.FromText($"a{r}c0"),
            TableCellNode.FromText($"a{r}c1"),
        ])));
        var far = new TableNode(Enumerable.Range(0, 2_000).Select(r => new TableRowNode([
            TableCellNode.FromText($"r{r}c0"),
            TableCellNode.FromText($"r{r}c1"),
        ])));
        var doc = new DocumentNode([shown, far, ParagraphNode.FromText("After.")]);
        var (window, view) = GeometryTransactionTests.Shown(doc);
        var block = (TableBlockView)view.Blocks[0];
        var onScreen = GeometryTransactionTests.PositionOf(doc, "a3c1") + 1 - view.Slots[0].Start;

        view.AfterPrepare = () =>
        {
            view.AfterPrepare = null;
            block.ParagraphChanged(block.ParagraphAt(onScreen)!.Value.View, onScreen);
        };

        WorkCounters.CoverageRepairs = 0;

        // A far position's question, outside any layout pass.
        _ = view.ImageRect(GeometryTransactionTests.PositionOf(doc, "r1900c0"));

        Assert.Equal(1, WorkCounters.CoverageRepairs);
        Assert.False(block.IsRowMeasured(3));

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        Assert.True(block.IsRowMeasured(3));
    }
}
