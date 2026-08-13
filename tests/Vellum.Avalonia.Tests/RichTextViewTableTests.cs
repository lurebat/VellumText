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
   [AvaloniaFact]
    public void BackspaceAtTheStartOfACellDoesNotJoinItToTheOneBeforeIt()
    {
        // Two cells of a row hold their paragraphs at the same depth, so joining them is a
        // well-formed splice as far as the schema is concerned: it merges the paragraphs, then
        // the cells, then the rows. A 2x2 table is caught by the occupancy check afterwards, but
        // a single row stays rectangular and the join succeeds -- the user sees two lines merge
        // and what actually happened is that a column disappeared out of the model.
        var doc = new DocumentNode([
            ParagraphNode.FromText("before"),
            new TableNode([new TableRowNode([
                TableCellNode.FromText("a1"),
                TableCellNode.FromText("b1"),
            ])]),
            ParagraphNode.FromText("after"),
        ]);

        var (_, view) = Shown(doc);

        view.Focus();
        view.MoveTo(view.Slots[2].Start, extend: false);

        Assert.False(view.Backspace());

        var table = (TableNode)view.State.Doc.Blocks[1];

        Assert.Equal(2, Assert.Single(table.Rows).Cells.Length);
    }

    [AvaloniaFact]
    public void DeleteAtTheEndOfACellDoesNotJoinItToTheOneAfterIt()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([TableCellNode.FromText("a1")]),
                new TableRowNode([TableCellNode.FromText("a2")]),
            ]),
        ]);

        var (_, view) = Shown(doc);

        view.Focus();
        view.MoveTo(view.Slots[0].End, extend: false);

        Assert.False(view.Delete());
        Assert.Equal(2, ((TableNode)view.State.Doc.Blocks[0]).Rows.Length);
    }

    [AvaloniaFact]
    public void BackspaceAtTheStartOfTheFirstCellDoesNotSwallowTheParagraphBeforeTheTable()
    {
        var (_, view) = Shown(WithTable());

        view.Focus();
        view.MoveTo(view.Slots[1].Start, extend: false);

        Assert.False(view.Backspace());
        Assert.Equal("before", ((ParagraphNode)view.State.Doc.Blocks[0]).Content.Text);
        Assert.IsType<TableNode>(view.State.Doc.Blocks[1]);
    }

    [AvaloniaFact]
    public void BackspaceStillJoinsTwoParagraphsInsideOneCell()
    {
        // Nesting the two blocks share is not what a join would merge, so a cell holding two
        // paragraphs behaves like anywhere else. Refusing this too would be the easy overshoot.
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([
                    ParagraphNode.FromText("one"),
                    ParagraphNode.FromText("two"),
                ]),
            ])]),
        ]);

        var (_, view) = Shown(doc);

        view.Focus();
        view.MoveTo(view.Slots[1].Start, extend: false);

        Assert.True(view.Backspace());

        var cell = Assert.Single(Assert.Single(((TableNode)view.State.Doc.Blocks[0]).Rows).Cells);

        Assert.Equal("onetwo", ((ParagraphNode)Assert.Single(cell.Blocks)).Content.Text);
    }
   [AvaloniaFact]
    public void DeletingASelectionThatSpansTwoCellsDoesNotMergeThem()
    {
        // The same corruption as Backspace at a cell boundary, reached by dragging instead. It
        // is refused in the step rather than in the view precisely so that every way of asking
        // for it -- Backspace, Delete, typing over it, pasting over it -- is refused at once.
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                TableCellNode.FromText("a1"),
                TableCellNode.FromText("b1"),
            ])]),
        ]);

        var (_, view) = Shown(doc);

        view.Focus();
        view.MoveTo(view.Slots[0].Start + 1, extend: false);
        view.MoveTo(view.Slots[1].Start + 1, extend: true);

        Assert.False(view.Delete());
        Assert.Equal(2, Assert.Single(((TableNode)view.State.Doc.Blocks[0]).Rows).Cells.Length);
    }

    [AvaloniaFact]
    public void TypingOverASelectionThatSpansTwoCellsChangesNothing()
    {
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                TableCellNode.FromText("a1"),
                TableCellNode.FromText("b1"),
            ])]),
        ]);

        var (_, view) = Shown(doc);

        view.Focus();
        view.MoveTo(view.Slots[0].Start + 1, extend: false);
        view.MoveTo(view.Slots[1].Start + 1, extend: true);

        Assert.False(view.InsertText("x"));

        var cells = Assert.Single(((TableNode)view.State.Doc.Blocks[0]).Rows).Cells;

        Assert.Equal(2, cells.Length);
        Assert.Equal("a1", ((ParagraphNode)cells[0].Blocks[0]).Content.Text);
        Assert.Equal("b1", ((ParagraphNode)cells[1].Blocks[0]).Content.Text);
    }
}