using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Resizing a table column by dragging the boundary between it and the next.
/// </summary>
/// <remarks>
/// <para>
/// Every boundary here is found by asking the view where its boundaries are rather than by
/// working out where they ought to be from the window width. The arithmetic that places them is
/// the thing under test, so a test that repeated it would agree with the view about a layout they
/// were both wrong about.
/// </para>
/// <para>
/// The widths a drag writes are ratios. What these tests therefore assert is the proportion
/// between columns and the total the table lays out to, never a stored number that happens to be
/// a count of pixels.
/// </para>
/// </remarks>
public class RichTextViewColumnResizeTests
{
    [AvaloniaFact]
    public void ThePointerSaysWhenItIsOverABoundary()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);
        var (at, _) = BoundaryOn(view, 1);

        window.MouseMove(at);
        Assert.Same(RichTextView.ColumnResizeCursorShape, view.Cursor);

        window.MouseMove(at + new Vector(30, 0));
        Assert.NotSame(RichTextView.ColumnResizeCursorShape, view.Cursor);
    }

    /// <summary>
    /// The band, not the line. A one-pixel line cannot be aimed at, and a test that only ever
    /// clicked the exact line would pass with no tolerance at all.
    /// </summary>
    /// <remarks>
    /// The distances are literal rather than read from
    /// <see cref="RichTextView.ColumnBoundaryTolerance"/>, which is the thing being pinned: a test
    /// stated in terms of the constant agrees with any value the constant takes. Eight pixels out
    /// is past a cell's six pixels of padding and therefore over its text, which must stay
    /// clickable as text.
    /// </remarks>
    [AvaloniaFact]
    public void TheBandAroundTheLineIsFourPixelsEitherSide()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);
        var (at, _) = BoundaryOn(view, 1);

        window.MouseMove(at - new Vector(4, 0));
        Assert.Same(RichTextView.ColumnResizeCursorShape, view.Cursor);

        window.MouseMove(at + new Vector(4, 0));
        Assert.Same(RichTextView.ColumnResizeCursorShape, view.Cursor);

        window.MouseMove(at + new Vector(8, 0));
        Assert.NotSame(RichTextView.ColumnResizeCursorShape, view.Cursor);

        window.MouseMove(at - new Vector(8, 0));
        Assert.NotSame(RichTextView.ColumnResizeCursorShape, view.Cursor);
    }

    /// <summary>
    /// The two columns either side swap width and nothing else moves, so the table lays out to
    /// exactly the width it did before.
    /// </summary>
    [AvaloniaFact]
    public void ADragMovesTheBoundaryAndLeavesTheTableTheWidthItWas()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);
        var (at, before) = BoundaryOn(view, 1);

        Drag(window, at, 40);

        var (moved, after) = BoundaryOn(view, 1);

        Assert.Equal(Total(before), Total(after), 6);
        Assert.Equal(at.X + 40, moved.X, 6);
        Assert.Equal(before.Widths[0] + 40, after.Widths[0], 6);
        Assert.Equal(before.Widths[1] - 40, after.Widths[1], 6);
    }

    [AvaloniaFact]
    public void TheWrittenProportionsSumToOne()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);
        var (at, _) = BoundaryOn(view, 1);

        Drag(window, at, -35);

        var widths = Widths(view);

        Assert.Equal(2, widths.Length);
        Assert.Equal(1.0, widths.Sum(), 9);
    }

    /// <summary>
    /// An automatic table states nothing at all, so the first drag has to invent the whole vector
    /// from the widths the view laid out — otherwise a freshly inserted table cannot be resized.
    /// </summary>
    [AvaloniaFact]
    public void AnAutomaticTableGetsAFullVectorFromItsFirstDrag()
    {
        var doc = WithTable(columns: 3);
        var (window, view) = Shown(doc);

        Assert.True(Table(view).ColumnWidths.IsEmpty);

        var (at, before) = BoundaryOn(view, 1);

        Drag(window, at, 25);

        var widths = Widths(view);

        Assert.Equal(3, widths.Length);
        Assert.Equal(1.0, widths.Sum(), 9);

        // The column the drag did not touch keeps the share an even split gave it.
        Assert.Equal(before.Widths[2] / Total(before), widths[2], 6);
    }

    [AvaloniaFact]
    public void UndoTakesTheWholeGestureBackAtOnce()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);
        var (at, _) = BoundaryOn(view, 1);

        Drag(window, at, 40);

        Assert.NotEqual(doc, view.State.Doc);
        Assert.True(view.Undo());
        Assert.Equal(doc, view.State.Doc);
        Assert.False(view.CanUndo);
    }

    /// <summary>
    /// Three moves, one transaction. A step per move would still end at the right widths and would
    /// need three undos to get back, which is the failure this is watching for.
    /// </summary>
    [AvaloniaFact]
    public void ADragOfSeveralMovesIsOneTransaction()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);
        var (at, _) = BoundaryOn(view, 1);

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at + new Vector(10, 0));
        window.MouseMove(at + new Vector(25, 0));
        window.MouseMove(at + new Vector(40, 0));

        // Nothing is written until the button comes up: the preview is view-local.
        Assert.Equal(doc, view.State.Doc);
        Assert.True(view.IsResizingColumn);

        window.MouseUp(at + new Vector(40, 0), MouseButton.Left);

        Assert.False(view.IsResizingColumn);
        Assert.NotEqual(doc, view.State.Doc);
        Assert.True(view.Undo());
        Assert.Equal(doc, view.State.Doc);
    }

    [AvaloniaFact]
    public void APressAndReleaseWithNoMovementCommitsNothing()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);
        var (at, _) = BoundaryOn(view, 1);

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.CanUndo);
    }

    /// <summary>
    /// A drag past the floor stops at it rather than collapsing the column. A column with no width
    /// has no boundary left to grab, so the gesture would not be recoverable by the same gesture.
    /// </summary>
    [AvaloniaFact]
    public void AColumnCannotBeDraggedBelowTheMinimumShare()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);
        var (at, before) = BoundaryOn(view, 1);

        Drag(window, at, -Total(before));

        var widths = Widths(view);

        Assert.Equal(RichTextView.MinimumColumnShare, widths[0], 6);
        Assert.Equal(1.0, widths.Sum(), 9);
    }

    /// <summary>
    /// With enough columns every column is already below the minimum share, so the floor gives way
    /// to a quarter of the pair either side. Without that cap the floor would reach half the pair
    /// and pin the boundary exactly where it is, and the gesture would silently do nothing on any
    /// table of more than twenty columns.
    /// </summary>
    [AvaloniaFact]
    public void ABoundaryBetweenTwoAlreadyNarrowColumnsStillClamps()
    {
        var doc = WithTable(columns: 30);
        var (window, view) = Shown(doc);
        var (at, before) = BoundaryOn(view, 1);
        var pair = before.Widths[0] + before.Widths[1];

        Assert.True(
            pair < 2 * RichTextView.MinimumColumnShare * Total(before),
            "the pair is not narrow enough for this test to be about the degenerate case");

        Drag(window, at, -Total(before));

        var widths = Widths(view);

        Assert.Equal(30, widths.Length);
        Assert.Equal(1.0, widths.Sum(), 9);
        Assert.Equal(pair / 4 / Total(before), widths[0], 6);
    }

    /// <summary>
    /// A boundary under a cell that spans it is still a boundary of the grid, which is what the
    /// widths index. The spanning cell is drawn exactly as wide as it was, because the pair either
    /// side of the boundary keeps its total.
    /// </summary>
    [AvaloniaFact]
    public void ABoundaryUnderAMergedCellMovesTheSlotAndNotTheCell()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 3)]),
                new TableRowNode([
                    TableCellNode.FromText("a"),
                    TableCellNode.FromText("b"),
                    TableCellNode.FromText("c")]),
            ]),
        ]);

        var (window, view) = Shown(doc);
        var spanning = SpanningCellWidth(view);
        var (at, before) = BoundaryOn(view, 1);

        Drag(window, at, 30);

        var (moved, after) = BoundaryOn(view, 1);

        Assert.Equal(at.X + 30, moved.X, 6);
        Assert.Equal(before.Widths[0] + 30, after.Widths[0], 6);
        Assert.Equal(spanning, SpanningCellWidth(view), 6);
        Assert.Equal(1.0, Widths(view).Sum(), 9);
    }

    /// <summary>
    /// The whole gesture, not only the write. A read-only view that offered the cursor and took
    /// the capture would advertise an edit it then refused, which architecture 10.2 records this
    /// codebase getting wrong five separate times.
    /// </summary>
    [AvaloniaFact]
    public void AReadOnlyViewOffersNothingAndChangesNothing()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc);

        view.IsReadOnly = true;

        var (at, _) = BoundaryOn(view, 1);

        window.MouseMove(at);
        Assert.NotSame(RichTextView.ColumnResizeCursorShape, view.Cursor);

        window.MouseDown(at, MouseButton.Left);
        Assert.False(view.IsResizingColumn);

        window.MouseMove(at + new Vector(40, 0));
        Assert.False(view.IsResizingColumn);

        window.MouseUp(at + new Vector(40, 0), MouseButton.Left);

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.CanUndo);
    }

    /// <summary>
    /// The outer edges are not boundaries. Dragging one would change the table's total width, and
    /// the widths are shares of whatever width the table is given, so there is nothing it could
    /// have meant.
    /// </summary>
    [AvaloniaFact]
    public void TheTablesOwnEdgesAreNotGrabbable()
    {
        var doc = WithTable();
        var (_, view) = Shown(doc);
        var (_, boundary) = BoundaryOn(view, 1);
        var y = BoundaryY(view);

        Assert.Null(view.ColumnBoundaryAt(new Point(boundary.Left, y), 4));
        Assert.Null(
            view.ColumnBoundaryAt(new Point(boundary.Left + Total(boundary), y), 4));
    }

    /// <summary>
    /// The band is vertical but not infinite. A point below the last block resolves to that block,
    /// so a table at the end of the document would otherwise offer its boundaries down the whole
    /// empty remainder of the control.
    /// </summary>
    [AvaloniaFact]
    public void ABoundaryStopsAtTheTablesOwnTopAndBottom()
    {
        var doc = new DocumentNode([new TableNode([Row(2, "1"), Row(2, "2")])]);
        var (_, view) = Shown(doc);
        var (at, _) = BoundaryOn(view, 1);
        var table = (TableBlockView)view.Blocks[0];
        var top = view.BlockOrigin(0).Y;

        Assert.NotNull(view.ColumnBoundaryAt(at, 4));
        Assert.Null(view.ColumnBoundaryAt(new Point(at.X, top - 1), 4));
        Assert.Null(view.ColumnBoundaryAt(new Point(at.X, top + table.Size.Height + 1), 4));
    }

    /// <summary>
    /// A table inside a cell answers for its own boundaries. Its columns are its own, so a drag
    /// there must not be read as a drag on the outer table's grid.
    /// </summary>
    /// <remarks>
    /// The inner table is asked first for the same reason cell rectangles are collected that way:
    /// its boundaries lie inside one of the outer table's cells, so it is the more specific answer,
    /// and an outer boundary can never be inside a cell to be confused with it.
    /// </remarks>
    [AvaloniaFact]
    public void ANestedTableAnswersForItsOwnBoundaries()
    {
        var inner = new TableNode([
            new TableRowNode([TableCellNode.FromText("x"), TableCellNode.FromText("y")])]);
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode([inner]),
                    TableCellNode.FromText("outer"),
                ]),
            ]),
        ]);

        var (window, view) = Shown(doc);
        var outer = (TableNode)view.State.Doc.Blocks[0];
        var y = BoundaryY(view);
        var found = Scan(view, y, b => b.TablePos != 0);

        Assert.NotNull(found);

        Drag(window, found.Value.At, 12);

        var after = (TableNode)view.State.Doc.Blocks[0];

        Assert.True(outer.ColumnWidths.IsEmpty);
        Assert.True(after.ColumnWidths.IsEmpty, "the outer table was resized instead");

        var resized = (TableNode)after.Rows[0].Cells[0].Blocks[0];

        Assert.Equal(2, resized.ColumnWidths.Length);
        Assert.Equal(1.0, resized.ColumnWidths.Sum(), 9);
    }

    private static void Drag(Window window, Point at, double by)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at + new Vector(by, 0));
        window.MouseUp(at + new Vector(by, 0), MouseButton.Left);
    }

    /// <summary>The table's own column widths, as the document states them.</summary>
    private static ImmutableArray<double> Widths(RichTextView view) => Table(view).ColumnWidths;

    private static TableNode Table(RichTextView view)
    {
        foreach (var block in view.State.Doc.Blocks)
        {
            if (block is TableNode table)
            {
                return table;
            }
        }

        throw new InvalidOperationException("The document holds no table.");
    }

    private static double Total(ColumnBoundary boundary) => boundary.Widths.Sum();

    /// <summary>How wide the cell spanning the whole first row is drawn.</summary>
    private static double SpanningCellWidth(RichTextView view)
    {
        var table = (TableBlockView)view.Blocks[0];

        // 1 is the position before the first cell of the first row: 0 opens the table and the
        // row's own opening boundary is at 0 in the table's local coordinates.
        return table.GetCellRects([1], 0)[0].Width;
    }

    /// <summary>
    /// Finds a boundary by asking the view, and returns the point exactly on the line.
    /// </summary>
    /// <remarks>
    /// The scan lands somewhere in the grab band, then reads the exact position out of the edges
    /// the view reported. Scanning for the line itself would need a step finer than any rounding
    /// the layout does and would be a search for a coincidence. The step is a fixed pixel rather
    /// than the tolerance, which this is meant to be able to observe: a step read from the constant
    /// under test spins forever the moment that constant is broken to zero.
    /// </remarks>
    private static (Point At, ColumnBoundary Boundary) BoundaryOn(RichTextView view, int index) =>
        Scan(view, BoundaryY(view), boundary => boundary.Boundary == index)
        ?? throw new InvalidOperationException($"No column boundary {index} across the table.");

    /// <summary>The first boundary across the control that matches, with the point on its line.</summary>
    private static (Point At, ColumnBoundary Boundary)? Scan(
        RichTextView view, double y, Func<ColumnBoundary, bool> wanted)
    {
        for (var x = 0d; x <= view.Bounds.Width; x++)
        {
            if (view.ColumnBoundaryAt(new Point(x, y), 4) is not { } found || !wanted(found))
            {
                continue;
            }

            var exact = found.Left;

            for (var i = 0; i < found.Boundary; i++)
            {
                exact += found.Widths[i];
            }

            return (new Point(exact, y), found);
        }

        return null;
    }

    /// <summary>A vertical offset inside the table, in the control's coordinates.</summary>
    private static double BoundaryY(RichTextView view)
    {
        for (var index = 0; index < view.Blocks.Count; index++)
        {
            if (view.Blocks[index] is TableBlockView table)
            {
                return view.BlockOrigin(index).Y + (table.Size.Height / 2);
            }
        }

        throw new InvalidOperationException("The view holds no table.");
    }

    private static DocumentNode WithTable(int columns = 2) =>
        new([
            ParagraphNode.FromText("before"),
            new TableNode([Row(columns, "1"), Row(columns, "2")]),
            ParagraphNode.FromText("after"),
        ]);

    private static TableRowNode Row(int columns, string suffix)
    {
        var cells = new List<TableCellNode>(columns);

        for (var i = 0; i < columns; i++)
        {
            cells.Add(TableCellNode.FromText($"{(char)('a' + i)}{suffix}"));
        }

        return new TableRowNode(cells);
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
}
