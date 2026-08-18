using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What the editing commands do to a rectangle of table cells.
/// </summary>
/// <remarks>
/// The failure these defend against is not cosmetic. A rectangle's <c>From</c> and <c>To</c> only
/// bound it, so any command that treats them as the affected range removes or reformats cells
/// beside the ones selected — and deleting that range takes whole cells out of their rows rather
/// than emptying them. Several tests below therefore assert on the surviving <em>structure</em>,
/// not only on the text.
/// </remarks>
public class RichTextViewCellCommandTests
{
    [AvaloniaFact]
    public void DeletingARectangleEmptiesItsCellsAndKeepsTheRest()
    {
        var doc = Grid();
        var (_, view) = Shown(doc);

        // The left column only. The right one must come through untouched.
        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "a2"), extend: true);

        Assert.True(view.Delete());

        var rows = ((TableNode)view.State.Doc.Blocks[0]).Rows;

        Assert.Equal(2, rows.Length);
        Assert.Equal([string.Empty, "b1"], Texts(rows[0]));
        Assert.Equal([string.Empty, "b2"], Texts(rows[1]));
    }

    [AvaloniaFact]
    public void DeletingARectangleLeavesTheCaretInItsFirstCell()
    {
        var doc = Grid();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        Assert.True(view.Delete());
        Assert.IsNotType<CellSelection>(view.State.Selection);
        Assert.True(view.State.Selection.IsEmpty);

        // Inside the first cell's now empty paragraph, which is where typing should resume.
        Assert.True(view.InsertText("x"));
        Assert.Equal("x", Texts(((TableNode)view.State.Doc.Blocks[0]).Rows[0])[0]);
    }

    [AvaloniaFact]
    public void DeletingARectangleOfAlreadyEmptyCellsDoesNothing()
    {
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([ParagraphNode.FromText(string.Empty)]),
                new TableCellNode([ParagraphNode.FromText(string.Empty)]),
            ])]),
        ]);

        var (_, view) = Shown(doc);
        var grid = TableGrid.Build(0, (TableNode)doc.Blocks[0]);

        view.State = EditorState.Create(
            doc,
            CellSelection.Create(doc, grid.At(0, 0)!.Value.Pos, grid.At(0, 1)!.Value.Pos)!);

        // Every cell is already one empty paragraph, so there is nothing to clear. Reporting a
        // change would push an undo entry that restores a document identical to this one.
        Assert.False(view.Delete());
        Assert.Same(doc, view.State.Doc);
    }

    [AvaloniaFact]
    public void UndoingADeletedRectangleBringsEveryCellBack()
    {
        var doc = Grid();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        Assert.True(view.Delete());
        Assert.True(view.Undo());

        var rows = ((TableNode)view.State.Doc.Blocks[0]).Rows;

        Assert.Equal(["a1", "b1"], Texts(rows[0]));
        Assert.Equal(["a2", "b2"], Texts(rows[1]));
    }

    [AvaloniaFact]
    public void BoldingARectangleBoldsOnlyItsCells()
    {
        var doc = Grid();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "a2"), extend: true);

        Assert.True(view.SetStyle(TextStyle.Bold, on: true));

        var rows = ((TableNode)view.State.Doc.Blocks[0]).Rows;

        Assert.True(IsAllBold(rows[0].Cells[0]));
        Assert.True(IsAllBold(rows[1].Cells[0]));
        Assert.False(IsAllBold(rows[0].Cells[1]));
        Assert.False(IsAllBold(rows[1].Cells[1]));
    }

    [AvaloniaFact]
    public void BoldingARectangleKeepsItSelected()
    {
        var doc = Grid();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        Assert.True(view.SetStyle(TextStyle.Bold, on: true));

        var rectangle = Assert.IsType<CellSelection>(view.State.Selection);

        Assert.Equal(4, rectangle.Cells(view.State.Doc).Length);
        Assert.True(view.IsActive(TextStyle.Bold));
    }

    [AvaloniaFact]
    public void ARectangleWhereOnlyOneCellIsBoldReportsNotBold()
    {
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([new ParagraphNode(
                    Half("a1"))]),
                TableCellNode.FromText("b1"),
            ])]),
        ]);

        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b1"), extend: true);

        Assert.IsType<CellSelection>(view.State.Selection);
        Assert.False(view.IsActive(TextStyle.Bold));
    }

    [AvaloniaFact]
    public void CopyingARectangleTakesOnlyItsCells()
    {
        var doc = Grid();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "a2"), extend: true);

        var copied = (TableNode)Assert.Single(view.SelectedDocument().Blocks);

        Assert.Equal(2, copied.Rows.Length);
        Assert.Equal(["a1"], Texts(copied.Rows[0]));
        Assert.Equal(["a2"], Texts(copied.Rows[1]));
        Assert.Equal("a1\na2", view.SelectedText().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [AvaloniaTheory]
    [InlineData("insert")]
    [InlineData("split")]
    [InlineData("paste")]
    [InlineData("pasteText")]
    [InlineData("heading")]
    [InlineData("indent")]
    [InlineData("list")]
    [InlineData("preedit")]
    public void CommandsARectangleCannotExpressAreRefused(string command)
    {
        // One row of two cells, deliberately. A rectangle covering a whole table has bounds the
        // schema refuses to replace anyway, so it would pass whether or not these commands
        // checked anything. Within a single row the bounds are a replaceable range, and treating
        // them as one takes both cells out of the row -- which is the bug these guards exist for.
        var doc = Row();
        var (_, view) = Shown(doc);

        view.Focus();
        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b1"), extend: true);

        Assert.IsType<CellSelection>(view.State.Selection);

        var before = view.State.Doc;

        var changed = command switch
        {
            "insert" => view.InsertText("x"),
            "split" => view.Split(),
            "paste" => view.PasteDocument(new DocumentNode([ParagraphNode.FromText("p")])),
            "pasteText" => view.PasteText("p"),
            "heading" => view.SetParagraphKind(ParagraphKind.Heading1),
            "indent" => view.Indent(),
            "list" => view.ToggleList(ListKind.Unordered),
            "preedit" => view.SetPreedit("x"),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        Assert.False(changed);
        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void DeletingTheSelectionOfARectangleEmptiesItsCells()
    {
        var doc = Row();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b1"), extend: true);

        Assert.True(view.DeleteSelection());

        var row = Assert.Single(((TableNode)view.State.Doc.Blocks[0]).Rows);

        Assert.Equal([string.Empty, string.Empty], Texts(row));
    }

    [AvaloniaFact]
    public void FormattingARectangleWithinOneRowLeavesBothCellsThere()
    {
        var doc = Row();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b1"), extend: true);

        Assert.True(view.SetStyle(TextStyle.Bold, on: true));

        var row = Assert.Single(((TableNode)view.State.Doc.Blocks[0]).Rows);

        Assert.Equal(["a1", "b1"], Texts(row));
        Assert.True(IsAllBold(row.Cells[0]));
        Assert.True(IsAllBold(row.Cells[1]));
    }

    /// <summary>
    /// A cell merged across two columns fills both of the grid's slots. Taken from each of them
    /// it would be copied twice, giving a row wider than the one that was selected.
    /// </summary>
    [AvaloniaFact]
    public void CopyingARectangleTakesAMergedCellOnce()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2)]),
                new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
            ]),
        ]);

        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "wide"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        var copied = (TableNode)Assert.Single(view.SelectedDocument().Blocks);

        Assert.Equal(2, copied.Rows.Length);
        Assert.Equal(["wide"], Texts(copied.Rows[0]));
        Assert.Equal(["a2", "b2"], Texts(copied.Rows[1]));
    }

    [AvaloniaFact]
    public void ARectangleShowsNoTouchHandles()
    {
        var doc = Grid();
        var (_, view) = Shown(doc);

        view.ShowTouchHandles();
        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        Assert.IsType<CellSelection>(view.State.Selection);
        Assert.Empty(view.TouchHandles);
    }

    /// <summary>Text whose first two characters are bold and whose rest is not.</summary>
    /// <summary>
    /// Enter in an empty bullet leaves the list, and that path runs before the selection is
    /// removed — so it is the one way a rectangle could reach a mutation without passing the
    /// choke point in <c>RemoveSelection</c>.
    /// </summary>
    [AvaloniaFact]
    public void SplittingARectangleWhoseCellHoldsAnEmptyBulletChangesNothing()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode([new ListNode([new ListItemNode([ParagraphNode.Empty])])]),
                    TableCellNode.FromText("b1"),
                ]),
            ]),
        ]);

        var (_, view) = Shown(doc);
        var grid = TableGrid.Build(0, (TableNode)doc.Blocks[0]);

        // Head is the bullet's cell: that is what decides which paragraph the command looks at.
        view.State = EditorState.Create(
            doc,
            CellSelection.Create(doc, grid.At(0, 1)!.Value.Pos, grid.At(0, 0)!.Value.Pos)!);

        Assert.False(view.Split());
        Assert.Same(doc, view.State.Doc);
    }

    private static InlineContent Half(string text) =>
        InlineContent.Create(
            text,
            [new ValueSpan<MarkSet>(0, 2, new MarkSet { Style = TextStyle.Bold })],
            []);

    private static bool IsAllBold(TableCellNode cell)
    {
        var content = ((ParagraphNode)cell.Blocks[0]).Content;

        for (var i = 0; i < content.Text.Length; i++)
        {
            if (!content.MarkAt(i).Has(TextStyle.Bold))
            {
                return false;
            }
        }

        return content.Text.Length > 0;
    }

    private static string[] Texts(TableRowNode row) =>
        [.. row.Cells.Select(cell => ((ParagraphNode)cell.Blocks[0]).Content.Text)];

    /// <summary>One row of two cells — a rectangle here has replaceable bounds.</summary>
    private static DocumentNode Row() =>
        new([
            new TableNode([
                new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
            ]),
        ]);

    private static DocumentNode Grid() =>
        new([
            new TableNode([
                new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
                new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
            ]),
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
