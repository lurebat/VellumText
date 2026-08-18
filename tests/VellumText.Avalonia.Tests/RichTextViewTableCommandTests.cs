using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The table structure commands, and Tab between cells.
/// </summary>
/// <remarks>
/// These assert the whole shape of the table and where the caret ended up, because both are the
/// command: a row inserted correctly with the caret left in the row above it is a command the
/// reader has to finish by hand, and a caret left in a cell that no longer exists is worse.
/// </remarks>
public class RichTextViewTableCommandTests
{
    [AvaloniaFact]
    public void ARowGoesInAboveTheCaretAndTakesIt()
    {
        var view = At(Grid(), "a2");

        Assert.True(view.InsertRowAbove());
        Assert.Equal(["a1|b1", "|", "a2|b2"], Shape(view));

        // The caret is in the new row, so typing carries straight on into it.
        Assert.True(view.InsertText("x"));
        Assert.Equal(["a1|b1", "x|", "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void ARowGoesInBelowTheCaretAndTakesIt()
    {
        var view = At(Grid(), "a1");

        Assert.True(view.InsertRowBelow());
        Assert.True(view.InsertText("x"));
        Assert.Equal(["a1|b1", "x|", "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void AColumnGoesInOnEitherSideOfTheCaretAndTakesIt()
    {
        var left = At(Grid(), "b1");

        Assert.True(left.InsertColumnLeft());
        Assert.True(left.InsertText("x"));
        Assert.Equal(["a1|x|b1", "a2||b2"], Shape(left));

        var right = At(Grid(), "a1");

        Assert.True(right.InsertColumnRight());
        Assert.True(right.InsertText("y"));
        Assert.Equal(["a1|y|b1", "a2||b2"], Shape(right));
    }

    [AvaloniaFact]
    public void ARowGoesAndTheCaretLandsInWhatMovedUp()
    {
        var view = At(Grid(), "a1");

        Assert.True(view.DeleteRow());
        Assert.Equal(["a2|b2"], Shape(view));

        Assert.True(view.InsertText("x"));
        Assert.Equal(["xa2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void AColumnGoesAndTheCaretLandsInWhatMovedAcross()
    {
        var view = At(Grid(), "a1");

        Assert.True(view.DeleteColumn());
        Assert.Equal(["b1", "b2"], Shape(view));

        Assert.True(view.InsertText("x"));
        Assert.Equal(["xb1", "b2"], Shape(view));
    }

    [AvaloniaFact]
    public void TheLastRowAndTheLastColumnStay()
    {
        var view = At(
            new DocumentNode([
                new TableNode([new TableRowNode([TableCellNode.FromText("only")])]),
            ]),
            "only");

        var before = view.State.Doc;

        Assert.False(view.DeleteRow());
        Assert.False(view.DeleteColumn());
        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void ARectangleMergesAndLeavesTheCaretPastEverythingItAbsorbed()
    {
        var view = At(Grid(), "a1");

        view.MoveTo(StartOf(view.State.Doc, "b1"), extend: true);
        Assert.IsType<CellSelection>(view.State.Selection);

        Assert.True(view.MergeCells());
        Assert.Equal(["a1 b1(1x2)", "a2|b2"], Shape(view));

        // At the end of the merged cell, which is where a reader carries on.
        Assert.True(view.InsertText("!"));
        Assert.Equal(["a1 b1!(1x2)", "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void OneCellHasNothingToMerge()
    {
        var view = At(Grid(), "a1");
        var before = view.State.Doc;

        Assert.False(view.MergeCells());
        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void AMergedCellSplitsBackAndKeepsTheCaret()
    {
        var view = At(Grid(), "a1");

        view.MoveTo(StartOf(view.State.Doc, "b2"), extend: true);

        Assert.True(view.MergeCells());
        Assert.True(view.SplitCell());
        Assert.Equal(["a1 b1 a2 b2|", "|"], Shape(view));

        Assert.True(view.InsertText("x"));
        Assert.Equal(["a1 b1 a2 b2x|", "|"], Shape(view));
    }

    [AvaloniaFact]
    public void APlainCellHasNothingToSplit()
    {
        var view = At(Grid(), "a1");
        var before = view.State.Doc;

        Assert.False(view.SplitCell());
        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void OutsideATableEveryOneOfThemDeclines()
    {
        var view = At(DocumentNode.FromParagraphs("plain"), "plain");
        var before = view.State.Doc;

        Assert.False(view.IsInTable);
        Assert.False(view.InsertRowAbove());
        Assert.False(view.InsertRowBelow());
        Assert.False(view.DeleteRow());
        Assert.False(view.InsertColumnLeft());
        Assert.False(view.InsertColumnRight());
        Assert.False(view.DeleteColumn());
        Assert.False(view.MergeCells());
        Assert.False(view.SplitCell());

        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void AReadOnlyViewRefusesThemAll()
    {
        var view = At(Grid(), "a1");
        var before = view.State.Doc;

        view.IsReadOnly = true;

        Assert.False(view.InsertRowBelow());
        Assert.False(view.InsertColumnRight());
        Assert.False(view.DeleteRow());
        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void EachOneIsASingleUndoStep()
    {
        var view = At(Grid(), "a1");
        var before = view.State.Doc;

        Assert.True(view.InsertRowBelow());
        Assert.True(view.InsertColumnRight());

        var after = Shape(view);

        Assert.True(view.Undo());
        Assert.True(view.Undo());
        Assert.Equal(before, view.State.Doc);

        Assert.True(view.Redo());
        Assert.True(view.Redo());
        Assert.Equal(after, Shape(view));
        Assert.Equal(["a1||b1", "||", "a2||b2"], after);
    }

    [AvaloniaFact]
    public void UndoingAMergeBringsBackEveryCell()
    {
        var view = At(Grid(), "a1");

        view.MoveTo(StartOf(view.State.Doc, "b2"), extend: true);

        var before = view.State.Doc;

        Assert.True(view.MergeCells());
        Assert.True(view.Undo());
        Assert.Equal(before, view.State.Doc);
    }

    // ---- Tab between cells -----------------------------------------------------------------

    [AvaloniaFact]
    public void TabMovesToTheNextCellAndShiftTabBack()
    {
        var view = At(Grid(), "a1");

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.True(view.InsertText("x"));
        Assert.Equal(["a1|xb1", "a2|b2"], Shape(view));

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: true));
        Assert.True(view.InsertText("y"));
        Assert.Equal(["ya1|xb1", "a2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void TabWrapsOntoTheNextRow()
    {
        var view = At(Grid(), "b1");

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.True(view.InsertText("x"));
        Assert.Equal(["a1|b1", "xa2|b2"], Shape(view));
    }

    [AvaloniaFact]
    public void TabInTheLastCellAddsARowAndGoesIntoIt()
    {
        var view = At(Grid(), "b2");

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.Equal(["a1|b1", "a2|b2", "|"], Shape(view));

        Assert.True(view.InsertText("x"));
        Assert.Equal(["a1|b1", "a2|b2", "x|"], Shape(view));
    }

    [AvaloniaFact]
    public void ShiftTabInTheFirstCellIsConsumedAndChangesNothing()
    {
        // Reporting it unhandled would send Tab on to focus navigation, so the key would take
        // focus out of the editor from the middle of a table.
        var view = At(Grid(), "a1");
        var before = view.State;

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: true));
        Assert.Same(before, view.State);
    }

    [AvaloniaFact]
    public void TabStillNestsAListInsideACell()
    {
        // The list is the more specific context and is nested deeper than the cell. Taking Tab
        // away from it would leave a list in a cell with no way to nest at all.
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode([
                        new ListNode([
                            ListItemNode.FromText("one"),
                            ListItemNode.FromText("two"),
                        ]),
                    ]),
                    TableCellNode.FromText("b1"),
                ]),
            ]),
        ]);

        var view = At(doc, "two");

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: false));

        var cell = ((TableNode)view.State.Doc.Blocks[0]).Rows[0].Cells[0];
        var list = (ListNode)cell.Blocks[0];

        Assert.Single(list.Items);
        Assert.Contains(list.Items[0].Blocks, block => block is ListNode);
    }

    [AvaloniaFact]
    public void TabTurnedOffLeavesTheTableAlone()
    {
        var view = At(Grid(), "a1");
        var before = view.State;

        view.AcceptsTab = false;

        Assert.False(view.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.Same(before, view.State);
    }

    [AvaloniaFact]
    public void TabOutOfARectangleCarriesOnFromItsHeadCell()
    {
        var view = At(Grid(), "a1");

        view.MoveTo(StartOf(view.State.Doc, "a2"), extend: true);
        Assert.IsType<CellSelection>(view.State.Selection);

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.True(view.InsertText("x"));
        Assert.Equal(["a1|b1", "a2|xb2"], Shape(view));
    }

    // ---- the context menu ------------------------------------------------------------------

    [AvaloniaFact]
    public void TheTableMenuAppearsOnlyInsideATable()
    {
        var view = At(
            new DocumentNode([
                ParagraphNode.FromText("outside"),
                new TableNode([
                    new TableRowNode([TableCellNode.FromText("a1")]),
                ]),
            ]),
            "outside");

        view.SyncContextFlyout();
        Assert.False(TableMenu(view).IsVisible);

        view.MoveTo(StartOf(view.State.Doc, "a1"), extend: false);
        view.SyncContextFlyout();

        Assert.True(TableMenu(view).IsVisible);
    }

    [AvaloniaFact]
    public void TheTableMenuRunsTheCommands()
    {
        var view = At(Grid(), "a1");

        view.SyncContextFlyout();

        var items = ((IEnumerable<object>)TableMenu(view).ItemsSource!).OfType<MenuItem>().ToArray();

        Assert.Equal(
            ["Insert Row Above", "Insert Row Below", "Insert Column Left", "Insert Column Right",
             "Delete Row", "Delete Column", "Merge Cells", "Split Cell"],
            items.Select(item => (string)item.Header!));

        // Clicked rather than called, so that the wiring is what is under test and not the
        // command it is wired to.
        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(["a1|b1", "|", "a2|b2"], Shape(view));
    }

    private static MenuItem TableMenu(RichTextView view) =>
        ((IEnumerable<object>)((MenuFlyout)view.ContextFlyout!).ItemsSource!)
            .OfType<MenuItem>()
            .Single(item => (string?)item.Header == "Table");

    // ---- helpers ---------------------------------------------------------------------------

    private static DocumentNode Grid() =>
        new([
            new TableNode([
                new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
                new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
            ]),
        ]);

    /// <summary>A shown view with the caret at the start of a named paragraph.</summary>
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

    /// <summary>Each row's cells, joined by a bar, with a span where a cell covers more than one slot.</summary>
    private static string[] Shape(RichTextView view) =>
        [.. ((TableNode)view.State.Doc.Blocks[0]).Rows.Select(row => string.Join(
            "|",
            row.Cells.Select(cell =>
                string.Join(
                    " ",
                    cell.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text))
                + (cell.RowSpan == 1 && cell.ColumnSpan == 1
                    ? string.Empty
                    : $"({cell.RowSpan}x{cell.ColumnSpan})"))))];

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
