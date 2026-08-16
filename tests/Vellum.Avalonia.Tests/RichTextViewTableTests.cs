using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Editing inside a table.
/// </summary>
/// <remarks>
/// <para>
/// The HTML importer produces a <see cref="TableNode"/> from any pasted web page, so a document
/// holding one is not a hypothetical: it is what arrives from an ordinary copy out of a browser.
/// </para>
/// <para>
/// Every position here is found by walking the document for the paragraph it belongs to, never
/// by indexing into <c>Slots</c>. Slot indices are a property of how the view chooses to flatten
/// the document, and they moved when tables stopped being flattened — but a test that indexed
/// into them did not fail, it silently started exercising a different position. Two of these
/// tests were passing that way. Deriving the position from the document is what makes them
/// about editing rather than about flattening.
/// </para>
/// </remarks>
public class RichTextViewTableTests
{
    /// <summary>
    /// Down enters the table rather than stepping over it, and moves between rows rather than
    /// along the row. Document order and visual order disagree here: the paragraph after the one
    /// in the top-left cell is the one to its right, not the one below it.
    /// </summary>
    [AvaloniaFact]
    public void DownMovesIntoTheTableAndThenDownItsColumn()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "before"), extend: false);

        Press(view, Key.Down);
        Assert.Equal(StartOf(doc, "a1"), view.State.Selection.Head);

        Press(view, Key.Down);
        Assert.Equal(StartOf(doc, "a2"), view.State.Selection.Head);
    }

    /// <summary>
    /// Up out of the top row and down out of the bottom row both leave, so the table cannot trap
    /// the caret. Landing keeps the goal column, so leaving the indented first cell lands just
    /// inside the paragraph below rather than exactly at its start.
    /// </summary>
    [AvaloniaFact]
    public void TheCaretCanLeaveTheTableInBothDirections()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);
        var below = StartOf(doc, "after");

        view.MoveTo(StartOf(doc, "a2"), extend: false);
        Press(view, Key.Down);
        Assert.InRange(view.State.Selection.Head, below, below + "after".Length);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        Press(view, Key.Up);

        var above = StartOf(doc, "before");

        Assert.InRange(view.State.Selection.Head, above, above + "before".Length);
    }

    /// <summary>
    /// The goal column follows the caret between cells. A paragraph-local goal would slide back to
    /// the left edge of each cell it entered, so a column of cells would drift the caret sideways.
    /// </summary>
    /// <remarks>
    /// The cells in the right-hand column hold enough text that the landing is not clamped to
    /// their end. With two-character cells both the right answer and an unconverted goal column
    /// land on the last character, and the test cannot tell them apart.
    /// </remarks>
    [AvaloniaFact]
    public void TheGoalColumnSurvivesMovingBetweenCells()
    {
        var doc = new DocumentNode([new TableNode([
            new TableRowNode([
                TableCellNode.FromText("a1"),
                TableCellNode.FromText("wide enough to land inside"),
            ]),
            new TableRowNode([
                TableCellNode.FromText("a2"),
                TableCellNode.FromText("wide enough to land inside"),
            ]),
        ])]);

        var (_, view) = Shown(doc);
        var top = StartOf(doc, "wide enough to land inside");

        view.MoveTo(top + 4, extend: false);
        Press(view, Key.Down);

        // The same table cell text appears twice, so the one below is found by asking where the
        // caret ended up rather than by searching the document for the text again.
        var landed = view.State.Selection.Head;

        Assert.True(landed > top + 20, $"expected a position in the row below, got {landed}");
        Assert.Equal(4, view.State.Selection.Head - view.BlockStart);
    }

    /// <summary>
    /// A list has no view of its own — it is drawn as its paragraphs wearing a marker — so a cell
    /// that handed its blocks straight to the view factory threw the moment the user pressed the
    /// bullet button with the caret inside a table.
    /// </summary>
    [AvaloniaFact]
    public void MakingACellsParagraphABulletDrawsItAsOne()
    {
        var doc = new DocumentNode([Table()]);
        var (window, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);

        var before = view.CaretRect();

        Assert.True(view.ToggleList(ListKind.Unordered));

        window.Measure(new Size(500, 400));
        window.Arrange(new Rect(0, 0, 500, 400));

        // Indented behind a bullet, which is the observable difference between a list item and
        // the paragraph it was: without the marker the text would start where it always did.
        var after = view.CaretRect();

        Assert.True(
            after.X > before.X,
            $"expected the bullet to indent the text, got {after.X} from {before.X}");

        // And the bullet is drawn, not merely reserved for. It lives in the gap the indent
        // opened up, which is the one place the caret is not, so any ink there is the marker.
        Assert.True(
            Ink(window, before.X, after.X, after.Y, after.Bottom) > 0,
            "expected the bullet glyph to be drawn in the space the indent opened up");
    }

    /// <summary>Counts pixels that are neither white nor transparent in a region.</summary>
    private static int Ink(Window window, double left, double right, double top, double bottom)
    {
        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!;

        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];

        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var count = 0;

        for (var y = (int)top; y < (int)bottom && y < locked.Size.Height; y++)
        {
            for (var x = (int)left; x < (int)right && x < locked.Size.Width; x++)
            {
                var i = (y * locked.RowBytes) + (x * 4);

                // Rgba8888, not BGRA: reading it the other way round finds nothing at all.
                if (bytes[i + 3] > 0 && (bytes[i] < 200 || bytes[i + 1] < 200 || bytes[i + 2] < 200))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// A paragraph inside a table cell is not a top-level block, so reading the shared formatting
    /// off the top-level blocks alone finds no characters and falls back to the formatting at the
    /// selection's start — reporting a half-bold cell as bold, and making Ctrl+B unbold it.
    /// </summary>
    [AvaloniaFact]
    public void AHalfBoldSelectionInsideOneCellReportsNotBold()
    {
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([new ParagraphNode(InlineContent.Create(
                    "abcd",
                    [new ValueSpan<MarkSet>(0, 2, new MarkSet { Style = TextStyle.Bold })],
                    []))]),
            ])]),
        ]);

        var (_, view) = Shown(doc);
        var start = StartOf(doc, "abcd");

        view.MoveTo(start, extend: false);
        view.MoveTo(start + 4, extend: true);

        Assert.False(view.IsActive(TextStyle.Bold));
    }

    private static void Press(RichTextView view, Key key) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = view,
        });

    private static TableNode Table() =>        new([
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

    /// <summary>The document position just inside the paragraph holding some text.</summary>
    private static int StartOf(DocumentNode doc, string text) =>
        Find(doc.Blocks, 0, text)
        ?? throw new InvalidOperationException($"No paragraph reads '{text}'.");

    private static int? Find(IReadOnlyList<Node> nodes, int start, string text)
    {
        var pos = start;

        foreach (var node in nodes)
        {
            if (node is ParagraphNode paragraph && paragraph.Content.Text == text)
            {
                return pos + 1;
            }

            if (!node.IsLeaf && Find(node.Children, pos + 1, text) is { } found)
            {
                return found;
            }

            pos += node.NodeSize;
        }

        return null;
    }

    [AvaloniaFact]
    public void ADocumentHoldingATableReachesTheScreen()
    {
        var (_, view) = Shown(WithTable());

        Assert.Equal(3, view.Slots.Length);
        Assert.IsType<TableBlockView>(view.Blocks[1]);
    }

    /// <summary>
    /// A table is one block to the flat run, not the paragraphs inside it. That is the whole
    /// difference between a table that draws as a grid and one that draws as a stack.
    /// </summary>
    [AvaloniaFact]
    public void ATableIsOneSlotAndSpansEveryPositionInsideIt()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);
        var slot = view.Slots[1];

        Assert.IsType<TableNode>(slot.Node);
        Assert.InRange(StartOf(doc, "a1"), slot.Start, slot.End);
        Assert.InRange(StartOf(doc, "b2"), slot.Start, slot.End);
    }

    /// <summary>
    /// The invariant that makes flattening safe, checked against the document's own resolver
    /// rather than against the arithmetic that produced the slots. If a slot's start were off by
    /// even one, the caret would edit the wrong node while the text still looked right.
    /// </summary>
    [AvaloniaFact]
    public void EverySlotStartResolvesToTheNodeTheSlotClaims()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        Assert.All(view.Slots, slot => Assert.Same(slot.Node, doc.Resolve(slot.Start).Parent));
    }

    /// <summary>
    /// Drawing is a view decision: the document still holds a real table, so an exporter or a
    /// round-trip back to HTML has lost nothing.
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
        var doc = WithTable();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "b1") + 2, extend: false);

        Assert.True(view.InsertText("!"));

        var table = (TableNode)view.State.Doc.Blocks[1];

        Assert.Equal("b1!", ((ParagraphNode)table.Rows[0].Cells[1].Blocks[0]).Content.Text);
        Assert.Equal("a1", ((ParagraphNode)table.Rows[0].Cells[0].Blocks[0]).Content.Text);
        Assert.Equal("a2", ((ParagraphNode)table.Rows[1].Cells[0].Blocks[0]).Content.Text);
    }

    [AvaloniaFact]
    public void ACaretCanWalkFromBeforeATableIntoIt()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        view.MoveTo(view.Slots[0].End, extend: false);

        Assert.Equal(0, view.CaretBlockIndex);

        view.MoveTo(StartOf(doc, "a1"), extend: false);

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
        view.MoveTo(StartOf(doc, "b1"), extend: false);

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
        view.MoveTo(StartOf(doc, "a1") + 2, extend: false);

        Assert.False(view.Delete());
        Assert.Equal(2, ((TableNode)view.State.Doc.Blocks[0]).Rows.Length);
    }

    [AvaloniaFact]
    public void BackspaceAtTheStartOfTheFirstCellDoesNotSwallowTheParagraphBeforeTheTable()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        view.Focus();
        view.MoveTo(StartOf(doc, "a1"), extend: false);

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
        view.MoveTo(StartOf(doc, "two"), extend: false);

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
        view.MoveTo(StartOf(doc, "a1") + 1, extend: false);
        view.MoveTo(StartOf(doc, "b1") + 1, extend: true);

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
        view.MoveTo(StartOf(doc, "a1") + 1, extend: false);
        view.MoveTo(StartOf(doc, "b1") + 1, extend: true);

        Assert.False(view.InsertText("x"));

        var cells = Assert.Single(((TableNode)view.State.Doc.Blocks[0]).Rows).Cells;

        Assert.Equal(2, cells.Length);
        Assert.Equal("a1", ((ParagraphNode)cells[0].Blocks[0]).Content.Text);
        Assert.Equal("b1", ((ParagraphNode)cells[1].Blocks[0]).Content.Text);
    }
}
