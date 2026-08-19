using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What the table commands do at a nesting boundary — a table inside a cell of another table.
/// </summary>
/// <remarks>
/// <para>
/// A nested table has always rendered and always updated in place. What was undefined until these
/// tests is which of the two tables an edit lands on, and every one of them turns on the same
/// rule: <see cref="TableGrid.CellContaining"/> answers with the <em>innermost</em> cell, so a
/// command means the grid the caret is actually in. These pin that rule at each command rather
/// than at the resolver, because the rule is only worth anything if every command goes through it.
/// </para>
/// <para>
/// Each assertion names the outer table's rows as well as the inner one's, since a command that
/// acted on the wrong table would still produce a valid document — the difference is only visible
/// in which rows moved.
/// </para>
/// </remarks>
public class RichTextViewNestedTableTests
{
    [AvaloniaFact]
    public void TabInTheLastCellOfANestedTableAddsARowToTheNestedTable()
    {
        // Word's answer, and the one that follows from Tab meaning "the next cell of this grid":
        // the grid the caret is in is the inner one.
        var view = At(Nested(), "y2");

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.Equal(["a1|{x1|y1/x2|y2/|}", "a2|b2"], Shape(view));

        // In the new inner row. Stepping out to the outer table instead would leave the shape
        // above unchanged and put this character at the front of a2.
        Assert.True(view.InsertText("z"));
        Assert.Equal(["a1|{x1|y1/x2|y2/z|}", "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void ShiftTabInTheFirstCellOfANestedTableStaysWhereItIs()
    {
        // The same rule read backwards. There is a cell before this one in the outer table, and
        // Tab does not reach it: a nested table is a grid of its own, and the key that walks a
        // grid stops at its edge in both directions.
        var view = At(Nested(), "x1");
        var before = view.State;

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: true));
        Assert.Same(before, view.State);
    }

    [AvaloniaFact]
    public void TabIntoAnOuterCellThatHoldsATableLandsInsideThatTable()
    {
        // The way in, and the reason the two tests above cost a reader nothing: the caret cannot
        // be parked on a cell that holds a table, only in the first place text can sit inside it.
        var view = At(Nested(), "a1");

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.True(view.InsertText("!"));
        Assert.Equal(["a1|{!x1|y1/x2|y2}", "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void DeletingAnOuterRowTakesTheTableInsideItWithIt()
    {
        var view = At(Nested(), "a1");

        Assert.True(view.DeleteRow());
        Assert.Equal(["a2|b2"], Shape(view));

        // And nothing of it is left loose in the document, which is what a row deletion that
        // lifted the cell's blocks out before removing the row would produce.
        Assert.DoesNotContain("x1", DocumentText.Of(view.State.Doc), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every structural command acts on the table the caret is in, leaving the one around it
    /// exactly as it was.
    /// </summary>
    /// <remarks>
    /// The outer row is asserted alongside the inner ones because it is the whole discrimination:
    /// each of these edits produces a perfectly valid table whichever grid it lands on, and only
    /// the untouched <c>a2|b2</c> says it landed on the right one.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(TableCommand.InsertRowAbove, "a1|{|/x1|y1/x2|y2}")]
    [InlineData(TableCommand.InsertRowBelow, "a1|{x1|y1/|/x2|y2}")]
    [InlineData(TableCommand.DeleteRow, "a1|{x2|y2}")]
    [InlineData(TableCommand.InsertColumnLeft, "a1|{|x1|y1/|x2|y2}")]
    [InlineData(TableCommand.InsertColumnRight, "a1|{x1||y1/x2||y2}")]
    [InlineData(TableCommand.DeleteColumn, "a1|{y1/y2}")]
    public void AStructureCommandInsideANestedTableActsOnTheNestedTable(
        TableCommand command, string expected)
    {
        var view = At(Nested(), "x1");

        Assert.True(Run(view, command));
        Assert.Equal([expected, "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void MergingOuterCellsKeepsTheTableInsideOneOfThem()
    {
        var view = At(Nested(), "a1");

        view.MoveTo(StartOf(view.State.Doc, "x1"), extend: true);

        // Dragging from a cell of the outer table into one of the inner one resolves at the
        // deepest table they share, which is the outer one.
        Assert.IsType<CellSelection>(view.State.Selection);
        Assert.True(view.MergeCells());
        Assert.Equal(["a1 {x1|y1/x2|y2}(1x2)", "a2|b2"], Shape(view));

        // The caret goes to the last place text can sit in the merged cell, which here is inside
        // the nested table's last cell rather than after it.
        Assert.True(view.InsertText("!"));
        Assert.Equal(["a1 {x1|y1/x2|y2!}(1x2)", "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void SplittingAMergedOuterCellNeedsTheCaretOutsideTheNestedTable()
    {
        var view = At(Nested(), "a1");

        view.MoveTo(StartOf(view.State.Doc, "x1"), extend: true);

        Assert.True(view.MergeCells());

        // The merge leaves the caret at the last place text can sit in the merged cell, which is
        // inside the nested table — so the next command means the inner grid, and that cell was
        // never merged. Nothing else the merge could do here is honest: there is no text position
        // in the merged cell after the nested table for the caret to rest on.
        Assert.False(view.SplitCell());

        // From the merged cell's own text it splits, and the nested table stays in the cell that
        // absorbed it rather than being handed back to the slot the split frees.
        view.MoveTo(StartOf(view.State.Doc, "a1"), extend: false);

        Assert.True(view.SplitCell());
        Assert.Equal(["a1 {x1|y1/x2|y2}|", "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void ExtendingARectangleAnchoredOnACellHoldingATableMeasuresFromInsideThatCell()
    {
        var view = At(Nested(), "a1");
        var doc = view.State.Doc;
        var grid = TableGrid.Build(0, (TableNode)doc.Blocks[0]);
        var holder = grid.At(0, 1)!.Value.Pos;

        // A rectangle set on the state directly, as an application may, so there is no remembered
        // drag origin and the anchor cell has to supply one.
        view.State = EditorState.Create(doc, new CellSelection(holder, holder));
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        var rectangle = Assert.IsType<CellSelection>(view.State.Selection);

        // The right-hand column. Anchoring at the nearest text position instead would put the
        // origin at the end of a1 and select all four cells.
        Assert.Equal(
            [holder, grid.At(1, 1)!.Value.Pos],
            rectangle.Cells(doc).AsEnumerable());
    }

    [AvaloniaFact]
    public void SteppingRightOutOfANestedTableLandsInTheOuterTablesNextCell()
    {
        var view = At(Nested(), "y2");

        view.MoveTo(StartOf(view.State.Doc, "y2") + 2, extend: false);
        Press(view, Key.Right);

        Assert.True(view.InsertText("!"));
        Assert.Equal(["a1|{x1|y1/x2|y2}", "!a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void ARectangleInsideANestedTableIsHighlightedOverTheNestedTable()
    {
        var view = At(Nested(), "x1");

        view.MoveTo(StartOf(view.State.Doc, "y1"), extend: true);

        var rectangle = Assert.IsType<CellSelection>(view.State.Selection);
        var table = Assert.IsType<TableBlockView>(view.Blocks[0]);
        var grid = TableGrid.Build(0, (TableNode)view.State.Doc.Blocks[0]);
        var start = view.Slots[0].Start;

        var rects = table.GetCellRects(rectangle.Cells(view.State.Doc), start);
        var holder = table.GetCellRects([grid.At(0, 1)!.Value.Pos], start)[0];

        // Two boxes, one per inner cell, side by side and both strictly inside the outer cell the
        // nested table sits in. Highlighting the outer cell instead would give one box the size of
        // `holder`, and failing to recurse at all would give none.
        Assert.Equal(2, rects.Count);
        Assert.All(rects, rect => Assert.True(
            holder.Contains(rect), $"{rect} is not inside the outer cell {holder}"));
        Assert.True(
            rects[0].Right <= rects[1].Left,
            $"{rects[0]} and {rects[1]} are not two cells side by side");
        Assert.True(
            rects[0].Width + rects[1].Width < holder.Width,
            "the highlight is as wide as the outer cell, so it is not the nested table's");
    }

    [AvaloniaFact]
    public void ANestedTableIsNotJoinedToWhatSurroundsItInsideItsOwnCell()
    {
        // The cell boundary refusal that keeps Backspace from eating a cell has to hold at the
        // block above a nested table too: the position before the table's first cell and the
        // position after the paragraph are adjacent, and joining them would swallow the table.
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode([
                        ParagraphNode.FromText("lead"),
                        new TableNode([
                            new TableRowNode([
                                TableCellNode.FromText("x1"),
                                TableCellNode.FromText("y1"),
                            ]),
                        ]),
                    ]),
                    TableCellNode.FromText("b1"),
                ]),
            ]),
        ]);

        var back = At(doc, "x1");
        var beforeBack = back.State.Doc;

        Press(back, Key.Back);
        Assert.Same(beforeBack, back.State.Doc);

        var forward = At(doc, "lead");
        var beforeForward = forward.State.Doc;

        forward.MoveTo(StartOf(doc, "lead") + 4, extend: false);
        Press(forward, Key.Delete);

        Assert.Same(beforeForward, forward.State.Doc);
    }

    /// <summary>Which structural command a theory case runs.</summary>
    public enum TableCommand
    {
        InsertRowAbove,
        InsertRowBelow,
        DeleteRow,
        InsertColumnLeft,
        InsertColumnRight,
        DeleteColumn,
    }

    private static bool Run(RichTextView view, TableCommand command) => command switch
    {
        TableCommand.InsertRowAbove => view.InsertRowAbove(),
        TableCommand.InsertRowBelow => view.InsertRowBelow(),
        TableCommand.DeleteRow => view.DeleteRow(),
        TableCommand.InsertColumnLeft => view.InsertColumnLeft(),
        TableCommand.InsertColumnRight => view.InsertColumnRight(),
        TableCommand.DeleteColumn => view.DeleteColumn(),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// A two by two table whose top-right cell holds a two by two table of its own.
    /// </summary>
    /// <remarks>
    /// The nested table is in the second column so that the cell before it is reachable, which is
    /// what makes "Tab into a cell holding a table" a question that can be asked at all.
    /// </remarks>
    private static DocumentNode Nested() =>
        new([
            new TableNode([
                new TableRowNode([
                    TableCellNode.FromText("a1"),
                    new TableCellNode([
                        new TableNode([
                            new TableRowNode([
                                TableCellNode.FromText("x1"),
                                TableCellNode.FromText("y1"),
                            ]),
                            new TableRowNode([
                                TableCellNode.FromText("x2"),
                                TableCellNode.FromText("y2"),
                            ]),
                        ]),
                    ]),
                ]),
                new TableRowNode([
                    TableCellNode.FromText("a2"),
                    TableCellNode.FromText("b2"),
                ]),
            ]),
        ]);

    /// <summary>Each outer row's cells, with a nested table written out in braces.</summary>
    private static string[] Shape(RichTextView view) =>
        [.. ((TableNode)view.State.Doc.Blocks[0]).Rows.Select(Row)];

    private static string Row(TableRowNode row) =>
        row.Cells.Length == 0 ? "-" : string.Join("|", row.Cells.Select(Cell));

    private static string Cell(TableCellNode cell) =>
        string.Join(" ", cell.Blocks.Select(Block))
        + (cell.RowSpan == 1 && cell.ColumnSpan == 1
            ? string.Empty
            : $"({cell.RowSpan}x{cell.ColumnSpan})");

    private static string Block(BlockNode block) => block switch
    {
        ParagraphNode paragraph => paragraph.Content.Text,
        TableNode table => "{" + string.Join("/", table.Rows.Select(Row)) + "}",
        _ => block.TypeName,
    };

    private static void Press(RichTextView view, Key key) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.None,
        });

    private static RichTextView At(DocumentNode doc, string text)
    {
        var view = new RichTextView { State = EditorState.Create(doc), MinHeight = 300 };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();
        window.Measure(new Size(500, 400));
        window.Arrange(new Rect(0, 0, 500, 400));

        view.MoveTo(StartOf(doc, text), extend: false);

        return view;
    }

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

            if (node.Children.Length > 0 && Find(node.Children, pos + 1, text) is { } found)
            {
                return found;
            }

            pos += node.NodeSize;
        }

        return null;
    }
}
