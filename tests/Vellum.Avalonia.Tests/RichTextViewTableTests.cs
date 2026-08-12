using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// What the view does with the blocks it cannot yet draw properly.
/// </summary>
/// <remarks>
/// The HTML importer produces a <see cref="TableNode"/> from any pasted web page, so a document
/// holding one is not a hypothetical: it is what arrives from an ordinary copy out of a browser.
/// Before these, that threw on the way to the screen.
/// </remarks>
public class RichTextViewTableTests
{
    private static TableNode Table() =>
        new([
            new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
            new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
        ]);

    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc), MinHeight = 300 };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();
        window.Measure(new Size(500, 400));
        window.Arrange(new Rect(0, 0, 500, 400));

        return (window, view);
    }

    private static DocumentNode WithTable() =>
        new([ParagraphNode.FromText("before"), Table(), ParagraphNode.FromText("after")]);

    [AvaloniaFact]
    public void ADocumentHoldingATableReachesTheScreen()
    {
        var (_, view) = Shown(WithTable());

        Assert.Equal(6, view.Slots.Length);
    }

    [AvaloniaFact]
    public void EveryCellKeepsItsText()
    {
        var (_, view) = Shown(WithTable());

        Assert.Equal(
            ["before", "a1", "b1", "a2", "b2", "after"],
            view.Slots.Select(s => ((ParagraphNode)s.Node).Content.Text));
    }

    /// <summary>
    /// The invariant that makes flattening safe, checked against the document's own resolver
    /// rather than against the arithmetic that produced the slots. If a cell's start were off by
    /// even one, the caret would edit the wrong node while the text still looked right.
    /// </summary>
    [AvaloniaFact]
    public void EverySlotStartResolvesToTheNodeTheSlotClaims()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        Assert.All(view.Slots, slot => Assert.Same(slot.Node, doc.Resolve(slot.Start).Parent));
    }

    [AvaloniaFact]
    public void ACellIsIndentedPastTheParagraphsAroundIt()
    {
        var (_, view) = Shown(WithTable());

        Assert.Equal(0, view.Slots[0].Depth);
        Assert.All(view.Slots[1..5], slot => Assert.Equal(1, slot.Depth));
        Assert.Equal(0, view.Slots[5].Depth);
    }

    /// <summary>
    /// Flattening is a drawing decision, not an editing one: the document still holds a real
    /// table, so an exporter or a round-trip back to HTML has lost nothing.
    /// </summary>
    [AvaloniaFact]
    public void TheDocumentStillHoldsARealTable()
    {
        var (_, view) = Shown(WithTable());

        var table = Assert.IsType<TableNode>(view.State.Doc.Blocks[1]);

        Assert.Equal(2, table.Rows.Length);
        Assert.Equal(2, table.Rows[0].Cells.Length);
    }

    [AvaloniaFact]
    public void TypingInACellEditsThatCellAndLeavesTheOthersAlone()
    {
        var (_, view) = Shown(WithTable());

        view.MoveTo(view.Slots[2].End, extend: false);

        Assert.True(view.InsertText("!"));

        var table = (TableNode)view.State.Doc.Blocks[1];

        Assert.Equal("b1!", ((ParagraphNode)table.Rows[0].Cells[1].Blocks[0]).Content.Text);
        Assert.Equal("a1", ((ParagraphNode)table.Rows[0].Cells[0].Blocks[0]).Content.Text);
        Assert.Equal("a2", ((ParagraphNode)table.Rows[1].Cells[0].Blocks[0]).Content.Text);
    }

    [AvaloniaFact]
    public void ACaretCanWalkFromBeforeATableIntoIt()
    {
        var (_, view) = Shown(WithTable());

        view.MoveTo(view.Slots[0].End, extend: false);

        Assert.Equal(0, view.CaretBlockIndex);

        view.MoveTo(view.Slots[1].Start, extend: false);

        Assert.Equal(1, view.CaretBlockIndex);
    }

    [AvaloniaFact]
    public void ABlockImageIsDrawnRatherThanRefused()
    {
        var doc = new DocumentNode([
            ParagraphNode.FromText("above"),
            new BlockImageNode(new ImageEmbed("http://example.invalid/a.png", width: 40, height: 20)),
            ParagraphNode.FromText("below"),
        ]);

        var (_, view) = Shown(doc);

        Assert.Equal(3, view.Slots.Length);

        var image = Assert.IsType<BlockImageView>(view.Blocks[1]);

        Assert.Equal(40, image.Size.Width);
        Assert.Equal(20, image.Size.Height);
    }

    [AvaloniaFact]
    public void AnImageWiderThanTheColumnIsBoxedIntoItKeepingItsShape()
    {
        var doc = new DocumentNode([
            new BlockImageNode(new ImageEmbed("http://example.invalid/a.png", width: 2000, height: 1000)),
        ]);

        var (_, view) = Shown(doc);
        var image = (BlockImageView)view.Blocks[0];

        Assert.True(image.Size.Width <= 500, $"width was {image.Size.Width}");
        Assert.Equal(image.Size.Width / 2, image.Size.Height, 3);
    }
}
