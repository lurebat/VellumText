using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Selecting a rectangle of table cells by dragging across them.
/// </summary>
/// <remarks>
/// <para>
/// The claim that matters here is that a rectangle is <em>not</em> a range. Selecting the left
/// column of a two column table skips the right one in every row, so anything that highlights
/// <c>From</c> to <c>To</c> covers cells the reader did not select. Several tests below assert
/// geometry rather than the selection object for exactly that reason: a correct
/// <see cref="CellSelection"/> that is drawn as a range still looks wrong on screen.
/// </para>
/// <para>
/// Positions are found by walking the document for the paragraph they belong to, never by
/// indexing into <c>Slots</c> — slot indices are a property of how the view flattens the
/// document, and a test that indexes into them silently starts exercising a different position
/// when that changes.
/// </para>
/// </remarks>
public class RichTextViewCellSelectionTests
{
    [AvaloniaFact]
    public void DraggingAcrossCellsSelectsARectangle()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        var rectangle = Assert.IsType<CellSelection>(view.State.Selection);

        Assert.Equal(4, rectangle.Cells(doc).Length);
    }

    [AvaloniaFact]
    public void DraggingWithinOneCellStaysATextSelection()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);
        var a1 = StartOf(doc, "a1");

        view.MoveTo(a1, extend: false);
        view.MoveTo(a1 + 2, extend: true);

        Assert.IsNotType<CellSelection>(view.State.Selection);
        Assert.Equal(a1, view.State.Selection.From);
        Assert.Equal(a1 + 2, view.State.Selection.To);
    }

    [AvaloniaFact]
    public void DraggingOutOfTheTableIsATextSelection()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "after"), extend: true);

        Assert.IsNotType<CellSelection>(view.State.Selection);
    }

    /// <summary>
    /// The heart of it. A column selection must not reach the column beside it, which is exactly
    /// what drawing the range between the two cells would do.
    /// </summary>
    [AvaloniaFact]
    public void SelectingAColumnDoesNotReachTheColumnBesideIt()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        var both = view.SelectionBounds()!.Value;

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "a2"), extend: true);

        var left = Assert.IsType<CellSelection>(view.State.Selection);

        Assert.Equal(2, left.Cells(doc).Length);

        var column = view.SelectionBounds()!.Value;

        // The left column alone must be appreciably narrower than both columns. Drawing the
        // range from a1 to a2 would sweep through b1 and make these two equal.
        Assert.True(
            column.Width < both.Width * 0.75,
            $"one column measured {column.Width:F1} against {both.Width:F1} for both, so the "
                + "selection is being drawn as a range rather than as a rectangle");

        // And it must stay on the left: a range would start at a1 and run right.
        Assert.True(
            column.Right < both.Right - 1,
            $"the column selection reaches x={column.Right:F1}, nearly the {both.Right:F1} of "
                + "the whole table");
    }

    /// <summary>
    /// A cell with nothing in it holds no text to highlight, so a range-based highlight leaves a
    /// hole in the rectangle. Whole cells are filled instead.
    /// </summary>
    [AvaloniaFact]
    public void AnEmptyCellInTheRectangleIsStillFilled()
    {
        var doc = new DocumentNode(
        [
            new TableNode(
            [
                new TableRowNode(
                [
                    TableCellNode.FromText("a1"),
                    new TableCellNode([ParagraphNode.FromText("")]),
                ]),
            ]),
        ]);

        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(EmptyCellPosition(doc), extend: true);

        Assert.IsType<CellSelection>(view.State.Selection);

        var bounds = view.SelectionBounds()!.Value;

        // The empty cell is the right half of the table. If only text were highlighted the
        // bounds would stop at the end of "a1", well short of halfway.
        Assert.True(
            bounds.Width > 200,
            $"the selection is only {bounds.Width:F1} wide, so the empty cell was not filled");
    }

    [AvaloniaFact]
    public void TheRectangleIsActuallyPainted()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);

        view.SelectionBrush = new SolidColorBrush(Color.FromRgb(255, 0, 0));

        var before = Red(window);

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        Assert.IsType<CellSelection>(view.State.Selection);

        var after = Red(window);

        // Bounds are computed from the same walk as painting, so a geometry-only assertion
        // cannot tell a rectangle that is drawn from one that is merely calculated.
        Assert.True(
            after > before + 1000,
            $"only {after - before} red pixels appeared, so the rectangle was computed but not "
                + "drawn");
    }

    /// <summary>
    /// Dragging back into the cell it started in has to give up the rectangle. The anchor of a
    /// cell selection is the position before a cell, which is not in any cell's text — so a view
    /// that measured the fall back from it would resolve the anchor to nothing and collapse.
    /// </summary>
    [AvaloniaFact]
    public void DraggingBackIntoTheStartingCellReturnsToATextSelection()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);
        var a1 = StartOf(doc, "a1");

        view.MoveTo(a1, extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        Assert.IsType<CellSelection>(view.State.Selection);

        view.MoveTo(a1 + 2, extend: true);

        Assert.IsNotType<CellSelection>(view.State.Selection);
        Assert.Equal(a1, view.State.Selection.From);
        Assert.Equal(a1 + 2, view.State.Selection.To);
    }

    /// <summary>
    /// A rectangle an application set itself never went through a drag, so there is no remembered
    /// text position to extend from. Extending from the stale one reaches out of the table
    /// entirely and selects everything between wherever it points and the pointer.
    /// </summary>
    [AvaloniaFact]
    public void ExtendingARectangleThatWasSetProgrammaticallyStaysInsideTheTable()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);
        var table = (TableNode)doc.Blocks[1];
        var grid = TableGrid.Build(doc.Blocks[0].NodeSize, table);

        view.State = EditorState.Create(
            doc,
            CellSelection.Create(doc, grid.At(0, 0)!.Value.Pos, grid.At(0, 1)!.Value.Pos)!);

        view.MoveTo(StartOf(doc, "a2"), extend: true);

        var rectangle = Assert.IsType<CellSelection>(view.State.Selection);

        // Measured from inside the anchor cell, so the new head replaces the old one: the
        // anchor column, both rows. Before the recovery this was a text selection from 0.
        Assert.Equal(grid.At(0, 0)!.Value.Pos, rectangle.AnchorCell);
        Assert.Equal([grid.At(0, 0)!.Value.Pos, grid.At(1, 0)!.Value.Pos], rectangle.Cells(doc));
    }

    [AvaloniaFact]
    public void ACaretInsideACellIsNotARectangle()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);

        view.MoveTo(StartOf(doc, "a1"), extend: false);

        Assert.IsNotType<CellSelection>(view.State.Selection);
        Assert.True(view.State.Selection.IsEmpty);
    }

    /// <summary>
    /// A drag that leaves a cell and comes back is one drag: it still runs from the character it
    /// started on, not from the start of the cell that character happens to be in.
    /// </summary>
    [AvaloniaFact]
    public void DraggingOutOfACellAndBackKeepsTheOffsetTheDragStartedAt()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);
        var a1 = StartOf(doc, "a1");

        // Between the two characters, so the start of the cell is a different position.
        view.MoveTo(a1 + 1, extend: false);
        view.MoveTo(StartOf(doc, "b1"), extend: true);
        view.MoveTo(a1 + 2, extend: true);

        var text = Assert.IsType<TextSelection>(view.State.Selection);

        Assert.Equal(a1 + 1, text.Anchor);
    }

    /// <summary>
    /// The remembered origin is only worth anything inside the rectangle's own anchor cell. A
    /// position in some other cell of the same table is not the drag this rectangle came from,
    /// and extending from it selects backwards across cells the user never touched.
    /// </summary>
    [AvaloniaFact]
    public void ARectangleIgnoresARememberedOriginFromADifferentCell()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);
        var grid = TableGrid.Build(doc.Blocks[0].NodeSize, (TableNode)doc.Blocks[1]);
        var a1 = StartOf(doc, "a1");

        // A click in b1 is what leaves the remembered origin there.
        view.MoveTo(StartOf(doc, "b1") + 1, extend: false);

        view.State = EditorState.Create(
            doc,
            CellSelection.Create(doc, grid.At(0, 0)!.Value.Pos, grid.At(0, 0)!.Value.Pos)!);

        view.MoveTo(a1 + 2, extend: true);

        var text = Assert.IsType<TextSelection>(view.State.Selection);

        Assert.Equal(a1, text.Anchor);
    }

    private static int Red(Window window)
    {
        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!;

        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];

        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var count = 0;

        // Rgba8888, not BGRA: reading it the other way round counts nothing and passes vacuously.
        for (var i = 0; i + 3 < bytes.Length; i += 4)
        {
            if (bytes[i] > 150 && bytes[i + 1] < 100 && bytes[i + 2] < 100 && bytes[i + 3] > 0)
            {
                count++;
            }
        }

        return count;
    }

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
        new(
        [
            ParagraphNode.FromText("before"),
            new TableNode(
            [
                new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
                new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
            ]),
            ParagraphNode.FromText("after"),
        ]);

    /// <summary>The position inside the one cell whose paragraph is empty.</summary>
    private static int EmptyCellPosition(DocumentNode doc)
    {
        var table = (TableNode)doc.Blocks[0];
        var grid = TableGrid.Build(0, table);

        // The second cell of the first row, one past the cell boundary and one past the
        // paragraph's, which is where its (absent) text would start.
        return grid.At(0, 1)!.Value.Pos + 2;
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
