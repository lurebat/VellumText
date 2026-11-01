using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Row virtualization of tables (F-35): a long table realizes the row bands near the viewport, and
/// everything that worked on a table still works on one — inside the window, across its edges, and
/// for rows that are not realized until they are asked about.
/// </summary>
/// <remarks>
/// <para>
/// The equivalence checks use tables whose rows are all one line tall. There the estimate a row
/// nobody has measured is priced at is exactly the height it turns out to be, so a table realized
/// only in a window is geometrically the same table as one realized whole, and any difference is
/// a fault in what was realized rather than in an estimate.
/// </para>
/// </remarks>
public class TableVirtualizationTests
{
    private const double Width = 420;

    [AvaloniaFact]
    public void ATableRealizedInAWindowAnswersAsTheWholeTableDoes()
    {
        var table = Uniform(400, span: (200, 10));
        var whole = Built(table);
        var windowed = new TableBlockView(table, Create, Brushes.Gray);

        // Laid out at the top first, so that its estimate is the height every row really is and the
        // window below names the rows it names in the whole table.
        windowed.SetWindow(0, 0, 100);
        windowed.Measure(Width);

        // A window starting halfway down the cell that spans rows 200 to 209.
        var top = RowTop(whole, table, 205);

        windowed.SetWindow(top, 50, 250);
        windowed.Measure(Width);

        Assert.InRange(windowed.RealizedRows, 10, 40);
        Assert.Equal(whole.Size.Width, windowed.Size.Width);
        Assert.Equal(whole.Size.Height, windowed.Size.Height, 8);

        var visible = new Rect(0, top, Width, 250);

        Assert.Equal(Draw(whole, visible), Draw(windowed, visible));

        // Positions in the rows the window holds, and in the spanning cell above its top.
        for (var p = PositionIn(table, 199, 0); p < PositionIn(table, 215, 0); p++)
        {
            AssertSameGeometry(whole.GetCaretRect(p), windowed.GetCaretRect(p));
        }

        for (var y = top; y < top + 250; y += 5)
        {
            var windowY = EquivalentEdgePoint(whole, windowed, y);

            for (var x = 2.0; x < Width; x += 37)
            {
                Assert.True(whole.HitTest(new Point(x, y)) == windowed.HitTest(new Point(x, windowY)), $"hit at {x}, {y}");
            }
        }

        Assert.InRange(windowed.RealizedRows, 10, 40);

        // A band the window never reached is not laid out by drawing it — that would move the
        // estimate and the table's height in the middle of a frame — and is drawn once a window
        // has reached it.
        var farTop = RowTop(whole, table, 330);
        var far = new Rect(0, farTop, Width, 200);
        var height = windowed.Size.Height;

        WorkCounters.TableCellsRendered = 0;
        Draw(windowed, far, inked: false);

        Assert.Equal(0, WorkCounters.TableCellsRendered);
        Assert.Equal(height, windowed.Size.Height);

        windowed.SetWindow(farTop, 50, 200);
        windowed.Measure(Width);

        Assert.Equal(Draw(whole, far), Draw(windowed, far));
    }

    [AvaloniaFact]
    public void ARowOutsideTheWindowIsRealizedWhenItIsAskedAbout()
    {
        var table = Uniform(400);
        var whole = Built(table);
        var windowed = new TableBlockView(table, Create, Brushes.Gray);

        windowed.SetWindow(0, 0, 200);
        windowed.Measure(Width);

        var before = windowed.RealizedRows;
        var far = PositionIn(table, 350, 2);

        Assert.True(before < 20);
        AssertSameGeometry(whole.GetCaretRect(far), windowed.GetCaretRect(far));
        Assert.Equal(whole.ParagraphAt(far)!.Value.Origin.X, windowed.ParagraphAt(far)!.Value.Origin.X);
        Assert.Equal(whole.ParagraphAt(far)!.Value.Origin.Y, windowed.ParagraphAt(far)!.Value.Origin.Y, 8);
        Assert.True(windowed.RealizedRows > before);
    }

    /// <summary>
    /// Geometry may differ in the last bits when sums are grouped, but a row's exact top must
    /// still name that row, and the immediately preceding double must name its predecessor.
    /// </summary>
    [AvaloniaFact]
    public void ExactRowBoundariesKeepTheirHalfOpenMeaningAfterMeasurementAndResize()
    {
        var table = Uniform(400, span: (200, 10));
        var view = new TableBlockView(table, Create, Brushes.Gray);

        view.SetWindow(0, 0, 200);
        view.Measure(Width);
        Check();

        view.SetWindow(view.RowTopOf(205), 50, 250);
        view.Measure(Width);
        Check();

        view.Measure(Width - 120);
        Check();

        view.SetWindow(0, 0, double.PositiveInfinity);
        view.Measure(Width - 120);
        Check();

        void Check()
        {
            for (var row = 1; row < view.RowCount; row++)
            {
                var top = view.RowTopOf(row);

                Assert.Equal((row, 0d), view.RowAnchor(top));
                Assert.Equal(row - 1, view.RowAnchor(Math.BitDecrement(top)).Row);
                Assert.Equal(row, view.RowAnchor(Math.BitIncrement(top)).Row);
            }
        }
    }

    // Grouping height prefixes changes roundoff, not geometry. Keep the tolerance far below a
    // visible DIP; boundary classification above is deliberately exact rather than tolerant.
    private static void AssertSameGeometry(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y, 8);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height, 8);
    }

    private static double EquivalentEdgePoint(TableBlockView whole, TableBlockView windowed, double y)
    {
        var row = whole.RowAnchor(y).Row;

        // At an edge, compare the same side of each layout's boundary rather than letting
        // permitted summation roundoff put the same raw double on opposite sides.
        foreach (var edge in new[] { row, row + 1 })
        {
            var from = whole.RowTopOf(edge);

            if (Math.Abs(y - from) > 1e-8)
            {
                continue;
            }

            var to = windowed.RowTopOf(edge);

            Assert.InRange(Math.Abs(to - from), 0, 1e-8);

            return y < from ? Math.BitDecrement(to) : y > from ? Math.BitIncrement(to) : to;
        }

        return y;
    }

    [AvaloniaFact]
    public void ScrollingIntoTheMiddleKeepsTheRowTheScrollBarPointedAt()
    {
        // Every fifth row is three lines tall, so the rows measured on the way in are not the
        // height the estimate guessed and every row above the viewport moves as they are measured.
        var (window, view) = Shown(Mixed(3_000), height: 400, lead: true);
        var table = (TableBlockView)view.Blocks[1];
        var tableTop = view.BlockOrigin(1).Y + view.Offset.Y;
        var offset = tableTop + (table.Size.Height / 2);
        var expected = table.RowAnchor(offset - tableTop);

        view.Offset = new Vector(0, offset);
        Layout(window);

        var after = table.RowAnchor(view.Offset.Y - (view.BlockOrigin(1).Y + view.Offset.Y));

        // The same row, the same way into it: the rows above it changed height as they were
        // measured, and the row itself did too, so the distance into it is kept as a share of it.
        Assert.Equal(expected.Row, after.Row);
        Assert.Equal(expected.Fraction, after.Fraction, 9);
        Assert.InRange(table.RealizedRows, 1, 80);
    }

    [AvaloniaFact]
    public void TheWindowCoversTheViewportWhereRowsAreShorterThanTheEstimate()
    {
        // Fifty tall rows at the top teach the estimate that rows are tall; the thousands after
        // them are one line. Scrolled among the short rows, the window must still reach the bottom
        // of the screen in real rows, not in estimated ones.
        var table = new TableNode(Enumerable.Range(0, 3_000).Select(r => new TableRowNode([
            r < 50 ? new TableCellNode(Enumerable.Range(0, 8).Select(i => (BlockNode)ParagraphNode.FromText($"line {i}"))) : Cell(r, 0, false),
            Cell(r, 1, false),
            Cell(r, 2, false),
        ])));
        var (window, view) = Shown(table, height: 400);
        var block = (TableBlockView)view.Blocks[0];

        ToMiddleOf(window, view, 0);

        var top = view.Offset.Y - (view.BlockOrigin(0).Y + view.Offset.Y);
        var first = block.RowAnchor(top).Row;
        var last = block.RowAnchor(top + view.Viewport.Height).Row;

        Assert.True(last - first > 5, $"rows {first} to {last} fill the viewport");

        for (var r = first; r <= last; r++)
        {
            Assert.True(block.IsRowMeasured(r), $"row {r} is on screen but not measured ({first} to {last})");
        }
    }

    [AvaloniaFact]
    public void ScrollingAmongRowsTallerThanTheEstimateMeasuresAndDrawsTheRowsOnScreen()
    {
        // The mirror of the test above: fifty one-line rows at the top teach the estimate that rows
        // are short, and the thousands after them are eight paragraphs tall. The window placed by
        // the estimate reaches only a few real rows, and the rows the viewport is anchored to must
        // still be measured before anything is drawn.
        var table = new TableNode(Enumerable.Range(0, 3_000).Select(r => new TableRowNode([
            r >= 50 ? new TableCellNode(Enumerable.Range(0, 8).Select(i => (BlockNode)ParagraphNode.FromText($"line {i}"))) : Cell(r, 0, false),
            Cell(r, 1, false),
            Cell(r, 2, false),
        ])));
        var (window, view) = Shown(table, height: 400);
        var block = (TableBlockView)view.Blocks[0];
        var target = block.Size.Height / 2;
        var expected = block.RowAnchor(target);

        view.Offset = new Vector(0, view.BlockOrigin(0).Y + view.Offset.Y + target);
        Layout(window);

        var top = view.Offset.Y - (view.BlockOrigin(0).Y + view.Offset.Y);
        var first = block.RowAnchor(top).Row;
        var last = block.RowAnchor(top + view.Viewport.Height).Row;

        Assert.Equal(expected.Row, first);

        for (var r = first; r <= last; r++)
        {
            Assert.True(block.IsRowMeasured(r), $"row {r} is on screen but not measured ({first} to {last})");
        }

        var height = block.Size.Height;

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        // Drawing lays nothing out: the table is the height the presenter last recorded for it.
        Assert.Equal(height, block.Size.Height);
        Assert.Equal(block.Size.Height, view.BlockOrigin(1).Y - view.BlockOrigin(0).Y, 6);
    }
    [AvaloniaFact]
    public void AWindowIsMeasuredFromWhereItsAnchorFallsInsideARow()
    {
        // Every row eight paragraphs tall. Anchored nine tenths of the way down row 40, a window
        // reaching 30px further must measure row 41 too: the viewport starts near the bottom of
        // row 40, not at its top.
        var table = new TableNode(Enumerable.Range(0, 200).Select(r => new TableRowNode([
            new TableCellNode(Enumerable.Range(0, 8).Select(i => (BlockNode)ParagraphNode.FromText($"line {i}"))),
            Cell(r, 1, false),
        ])));
        var view = new TableBlockView(table, Create, Brushes.Gray);

        view.SetWindow(0, 0, 100);
        view.Measure(Width);

        var top = view.RowOffset(40, 0);
        var height = view.RowOffset(41, 0) - top;

        view.SetWindow(top + (0.9 * height), 0, 30);
        view.Measure(Width);

        Assert.True(view.IsRowMeasured(40) && view.IsRowMeasured(41));
    }

    [AvaloniaFact]
    public void ARowRealizedButNotMeasuredAtTheCurrentWidthIsNotDrawn()
    {
        var table = Uniform(300);
        var view = Built(table);
        var far = new Rect(0, RowTop(view, table, 250), Width, 100);

        // Narrower, with a window at the top: the rows further down keep their views, laid out
        // for the old width, and no height at this one.
        view.SetWindow(0, 0, 200);
        view.Measure(Width - 100);

        Assert.False(view.IsRowMeasured(250));

        WorkCounters.TableCellsRendered = 0;
        Draw(view, far, inked: false);

        Assert.Equal(0, WorkCounters.TableCellsRendered);
    }

    [AvaloniaFact]
    public void ARowEditedSinceItWasMeasuredIsNotDrawnAndTheRowsAroundItAre()
    {
        var table = Uniform(50);
        var view = Built(table);
        var rows = table.Rows.ToList();

        // An edit in row 11 that nothing has laid out yet: its first cell's paragraph has no
        // layout to draw, and the rows either side of it are measured as they were.
        rows[11] = new TableRowNode([TableCellNode.FromText("edited"), rows[11].Cells[1], rows[11].Cells[2]]);
        Assert.True(view.Update(new TableNode(rows)));
        Assert.False(view.IsRowMeasured(11));

        // Rows 9 to 13, then 11 to 13: whether the walk starts on a row that may be drawn or on
        // one that may not, each row is drawn by what its own band is.
        foreach (var (first, drawn) in new[] { (9, 4), (11, 2) })
        {
            var top = view.RowOffset(first, 0);
            var area = new Rect(0, top + 2, Width, view.RowOffset(14, 0) - top - 4);

            WorkCounters.TableCellsRendered = 0;
            Draw(view, area, inked: false);

            Assert.Equal(3 * drawn, WorkCounters.TableCellsRendered);
        }
    }

    [AvaloniaFact]
    public void AShapeChangeLeavesRowsRealizedButNotMeasuredUnmeasured()
    {
        var table = Uniform(300);
        var view = Built(table);

        // Narrower, with a window at the top: the rows further down keep their views and lose
        // their heights. Then a row goes in above them all.
        view.SetWindow(0, 0, 200);
        view.Measure(Width - 100);

        Assert.False(view.IsRowMeasured(250));

        var rows = table.Rows.ToList();

        rows.Insert(0, new TableRowNode([Cell(-1, 0, false), Cell(-1, 1, false), Cell(-1, 2, false)]));
        Assert.True(view.Update(new TableNode(rows)));
        view.Measure(Width - 100);

        Assert.False(view.IsRowMeasured(251));
        Assert.True(double.IsFinite(view.Size.Height) && view.Size.Height > 0, $"{view.Size.Height}");
        Assert.True(double.IsFinite(view.RowOffset(300, 0)), $"{view.RowOffset(300, 0)}");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void InsertingOrDeletingARowLeavesWhatIsOnScreenWhereItWas(bool insert)
    {
        // Fifty eight-paragraph rows at the top and short rows after them. The tall rows are
        // measured on the way down; changing the grid's shape must not forget them, or the offset
        // the viewport is at names a row a hundred further on.
        var (window, view, block) = TallRowsThenShortOnesScrolledTo500();

        var top = block.RowAnchor(view.Offset.Y - (view.BlockOrigin(0).Y + view.Offset.Y));
        var caret = view.CaretRect();
        var onScreen = view.BlockOrigin(0).Y + block.RowOffset(504, 0);

        Assert.InRange(top.Row, 490, 505);
        Assert.InRange(caret.Y, 50, 350);

        Assert.True(insert ? view.InsertRowBelow() : view.DeleteRow());
        Layout(window);

        var after = block.RowAnchor(view.Offset.Y - (view.BlockOrigin(0).Y + view.Offset.Y));

        Assert.Equal(top.Row, after.Row);
        Assert.Equal(top.Fraction, after.Fraction, 6);

        // The row before the one changed is drawn where it was; the caret is still on screen.
        Assert.Equal(onScreen, view.BlockOrigin(0).Y + block.RowOffset(504, 0), 3);
        Assert.InRange(view.CaretRect().Y, 0, 400);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARowInsertedOrDeletedAboveTheViewportLeavesWhatIsOnScreenWhereItWas(bool insert)
    {
        // An edit that does not come from the caret — a collaborator's, or a host's — far above
        // the viewport. The rows on screen are rows further down the table now, and stay put.
        var (window, view, block) = TallRowsThenShortOnesScrolledTo500();
        var top = block.RowAnchor(-view.BlockOrigin(0).Y);
        var onScreen = view.BlockOrigin(0).Y + block.RowOffset(504, 0);
        var caret = view.CaretRect().Y;
        var cell = TableGrid.Build(0, (TableNode)view.State.Doc.Blocks[0]).At(5, 0) is { } slot ? slot.Pos : -1;
        var step = insert
            ? TableEdits.InsertRows(view.State.Doc, cell, cell, below: false)
            : TableEdits.DeleteRows(view.State.Doc, cell, cell);

        Assert.True(view.Apply(view.State.Transaction().Step(step!)));
        Layout(window);

        var moved = insert ? 1 : -1;
        var after = block.RowAnchor(-view.BlockOrigin(0).Y);

        Assert.Equal(2_000 + moved, block.RowCount);
        Assert.Equal(top.Row + moved, after.Row);
        Assert.Equal(top.Fraction, after.Fraction, 6);
        Assert.Equal(onScreen, view.BlockOrigin(0).Y + block.RowOffset(504 + moved, 0), 3);
        Assert.Equal(caret, view.CaretRect().Y, 3);
    }

    /// <summary>
    /// Fifty eight-paragraph rows at the top of a two-thousand-row table and short rows after
    /// them, walked down to row 500 so that the tall rows are measured, with the caret in row 505.
    /// </summary>
    private static (Window Window, RichTextView View, TableBlockView Block) TallRowsThenShortOnesScrolledTo500()
    {
        var table = new TableNode(Enumerable.Range(0, 2_000).Select(r => new TableRowNode([
            r < 50 ? new TableCellNode(Enumerable.Range(0, 8).Select(i => (BlockNode)ParagraphNode.FromText($"line {i}"))) : Cell(r, 0, false),
            Cell(r, 1, false),
            Cell(r, 2, false),
        ])));
        var (window, view) = Shown(table, height: 400);
        var block = (TableBlockView)view.Blocks[0];

        for (var row = 0; row <= 500; row += 10)
        {
            view.Offset = new Vector(0, view.BlockOrigin(0).Y + view.Offset.Y + block.RowOffset(row, 0));
            Layout(window);
        }

        view.MoveTo(PositionIn(view.State.Doc, 0, 505, 1), false);
        Layout(window);

        return (window, view, block);
    }

    /// <summary>
    /// One giant row does not price every other row like itself — the table form of the giant
    /// paragraph (Phase 9, found live). Rows nobody has measured were priced at the plain mean
    /// of those that had been, so a cell of thousands of words near the top made each of
    /// thousands of one-line rows look dozens of lines tall.
    /// </summary>
    [AvaloniaFact]
    public void OneGiantRowDoesNotInflateTheRowsNobodyHasMeasured()
    {
        var giant = string.Join(' ', Enumerable.Range(0, 2_000).Select(i => $"word{i}"));
        var rows = Uniform(3_000).Rows.ToArray();

        rows[5] = new TableRowNode([TableCellNode.FromText(giant), Cell(5, 1, false), Cell(5, 2, false)]);

        var (_, view) = Shown(new TableNode(rows), height: 400);
        var table = (TableBlockView)view.Blocks[0];

        Assert.True(table.IsRowMeasured(1));
        Assert.True(table.IsRowMeasured(5));
        Assert.False(table.IsRowMeasured(2_000));

        var shortRow = table.RowOffset(2, 0) - table.RowOffset(1, 0);
        var unmeasured = table.RowOffset(2_001, 0) - table.RowOffset(2_000, 0);

        Assert.True(
            unmeasured < shortRow * 4,
            $"An unmeasured row was priced at {unmeasured:F1} beside measured one-line rows of {shortRow:F1}.");
    }

    /// <summary>
    /// A row measured again replaces its own share of the estimate rather than adding a second
    /// one. Editing a row up to three lines and back must leave unmeasured rows priced exactly
    /// as before; counting the old height as well would drift the guess with every edit.
    /// </summary>
    [AvaloniaFact]
    public void ARowMeasuredAgainReplacesItsShareOfTheEstimate()
    {
        var table = Uniform(3_000);
        var view = new TableBlockView(table, Create, Brushes.Gray);

        view.SetWindow(0, 0, 200);
        view.Measure(Width);

        Assert.True(view.IsRowMeasured(1));
        Assert.False(view.IsRowMeasured(2_000));

        double Price() => view.RowOffset(2_001, 0) - view.RowOffset(2_000, 0);

        var before = Price();
        var rows = table.Rows.ToArray();

        rows[1] = new TableRowNode([
            new TableCellNode([ParagraphNode.FromText("one"), ParagraphNode.FromText("two"), ParagraphNode.FromText("three")]),
            Cell(1, 1, false),
            Cell(1, 2, false)]);

        Assert.True(view.Update(new TableNode(rows)));
        view.Measure(Width);

        Assert.NotEqual(before, Price(), 6);

        Assert.True(view.Update(table));
        view.Measure(Width);

        Assert.Equal(before, Price(), 6);
    }

    [AvaloniaFact]
    public void EveryRowNobodyHasMeasuredIsPricedAtTheSameEstimate()
    {
        var (window, view) = Shown(Mixed(3_000), height: 400, lead: true);
        var table = (TableBlockView)view.Blocks[1];

        ToMiddleOf(window, view, 1);

        var unmeasured = Enumerable.Range(0, table.RowCount - 1)
            .Where(r => !table.IsRowMeasured(r))
            .Select(r => Math.Round(table.RowOffset(r + 1, 0) - table.RowOffset(r, 0), 6))
            .Distinct()
            .ToArray();

        // Rows above the measured window and rows below it alike: the estimate is one number.
        Assert.True(unmeasured.Length == 1, string.Join(", ", unmeasured.Take(5)));
    }

    [AvaloniaFact]
    public void AClickBesideARaggedRowLandsInTheNearestCellWhateverRowItIsIn()
    {
        // Row 6 has a fourth cell no other row has. A click in that column a row or two away is
        // nearer to it than to anything in its own row.
        var rows = Uniform(12).Rows.ToList();

        rows[6] = new TableRowNode([.. rows[6].Cells, TableCellNode.FromText("extra")]);

        var view = Built(new TableNode(rows));
        var cells = view.GetCellRects(Enumerable.Range(0, view.ContentSize).ToHashSet(), 1);
        var extra = cells.Single(cell => cell.Right > Width - 1 && cell.Left > Width * 0.7);

        foreach (var y in new[] { extra.Top - 30, extra.Top - 3, extra.Bottom + 3, extra.Bottom + 30 })
        {
            var point = new Point(extra.Center.X, y);
            var expected = Nearest(cells, point);
            var hit = view.ParagraphAt(view.HitTest(point));
            var inside = view.ParagraphAt(view.HitTest(expected.Center));

            Assert.Equal(inside!.Value.Start, hit!.Value.Start);
        }
    }

    /// <summary>The first cell at the least distance from a point, by looking at every one.</summary>
    private static Rect Nearest(IReadOnlyList<Rect> cells, Point point)
    {
        var best = double.PositiveInfinity;
        var found = default(Rect);

        foreach (var cell in cells)
        {
            var dx = Math.Max(0, Math.Max(cell.X - point.X, point.X - cell.Right));
            var dy = Math.Max(0, Math.Max(cell.Y - point.Y, point.Y - cell.Bottom));

            if ((dx * dx) + (dy * dy) < best)
            {
                best = (dx * dx) + (dy * dy);
                found = cell;
            }
        }

        return found;
    }
    [AvaloniaFact]
    public void PaintingASelectionOfTheWholeTableRealizesOnlyTheRowsOnScreen()
    {
        var (window, view) = Shown(Uniform(2_000, wrapped: true), height: 400);
        var table = (TableBlockView)view.Blocks[0];

        ToMiddleOf(window, view, 0);

        var realized = table.RealizedRows;

        // Selecting everything scrolls to its end, where the rest of the selection is nothing;
        // back in the middle, the selection runs on for a thousand rows below the screen.
        Assert.True(view.SelectAll());
        Layout(window);
        ToMiddleOf(window, view, 0);

        realized = table.RealizedRows;

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        Assert.NotNull(view.SelectionBounds());
        Assert.InRange(table.RealizedRows, realized, realized + 10);

        var doc = view.State.Doc;
        var cells = CellSelection.Across(doc, PositionIn(doc, 0, 0, 0), PositionIn(doc, 0, 1_999, 2));

        Assert.NotNull(cells);

        view.State = view.State.Apply(view.State.Transaction().SetSelection(cells));
        Layout(window);
        ToMiddleOf(window, view, 0);

        realized = table.RealizedRows;

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        Assert.NotNull(view.SelectionBounds());
        Assert.InRange(table.RealizedRows, realized, realized + 10);
    }

    [AvaloniaFact]
    public void AColumnOfALongTableCanBeDraggedFromTheMiddleOfIt()
    {
        var (window, view) = Shown(Uniform(2_000, wrapped: true), height: 400);
        var table = (TableBlockView)view.Blocks[0];

        ToMiddleOf(window, view, 0);

        var y = 200.0;
        var at = Enumerable.Range(0, (int)view.Bounds.Width)
            .Select(x => new Point(x, y))
            .First(point => view.ColumnBoundaryAt(point, 1) is { Boundary: 1 });

        WorkCounters.TableCellsMeasured = 0;

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at + new Vector(40, 0));
        window.MouseUp(at + new Vector(40, 0), MouseButton.Left);
        Layout(window);

        var widths = ((TableNode)view.State.Doc.Blocks[0]).ColumnWidths;

        Assert.Equal(3, widths.Length);
        Assert.True(widths[0] > widths[2], string.Join(", ", widths));
        Assert.True(view.ColumnBoundaryAt(at + new Vector(40, 0), 1) is { Boundary: 1 });

        // Every row's height is forgotten by the resize, but only the window is measured again.
        Assert.InRange(WorkCounters.TableCellsMeasured, 1, 3 * 80);
        Assert.InRange(table.RealizedRows, 1, 120);
    }

    [AvaloniaFact]
    public void TheCaretWalksDownALongTablePastTheRealizedWindow()
    {
        var (window, view) = Shown(Uniform(300), height: 300);
        var doc = view.State.Doc;

        view.Focus();
        view.MoveTo(PositionIn(doc, 0, 0, 1) + 1, false);
        Layout(window);

        for (var i = 0; i < 120; i++)
        {
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        }

        Layout(window);

        var row = doc.Resolve(view.State.Selection.Head).Parent;

        Assert.Equal("r120c1", Assert.IsType<ParagraphNode>(row).Content.Text);

        // Scrolled with it: the caret is on screen.
        var caret = view.CaretRect();

        Assert.True(caret.Y >= 0 && caret.Bottom <= view.Bounds.Height + 1e-6, $"caret {caret} in {view.Bounds}");
    }

    [AvaloniaFact]
    public void AClickOnTheVisiblePartOfACellSpanningIntoTheViewportFindsIt()
    {
        var (window, view) = Shown(Uniform(600, span: (300, 12)), height: 300);
        var table = (TableBlockView)view.Blocks[0];
        var doc = view.State.Doc;

        // The top of the viewport halfway down the spanning cell: the rows it starts in are above
        // the screen, and only its lower half shows.
        var spanning = PositionIn(doc, 0, 300, 0);

        view.MoveTo(spanning, false);
        Layout(window);

        var cellTop = view.BlockOrigin(0).Y + view.Offset.Y + table.RowOffset(300, 0);

        view.Offset = new Vector(0, cellTop + (6 * (table.RowOffset(301, 0) - table.RowOffset(300, 0))));
        Layout(window);

        var clicked = view.PositionAt(new Point(20, 4));

        Assert.Equal(
            "spanning twelve rows",
            Assert.IsType<ParagraphNode>(doc.Resolve(clicked).Parent).Content.Text);
    }

    [AvaloniaFact]
    public void ATableAskedAboutAPositionBeforeItsFirstMeasureMeasuresNothing()
    {
        // With no width yet there is nothing to wrap a cell to. A question realizes the row's
        // views, and laying them out waits for the measure that brings the width.
        var view = new TableBlockView(Uniform(10), Create, Brushes.Gray);

        // The row's opening boundary is in no paragraph.
        Assert.Null(view.ParagraphAt(0));
        Assert.Equal(1, view.RealizedRows);
        Assert.False(view.IsRowMeasured(0));

        view.Measure(Width);

        Assert.True(view.IsRowMeasured(0));
    }

    [AvaloniaFact]
    public void TheOverscanAboveAWindowReachesTheFirstRow()
    {
        var view = new TableBlockView(Uniform(300), Create, Brushes.Gray);

        // Anchored halfway down row 1, with room above for more than row 0.
        view.SetWindow(view.RowOffset(1, 0.5), 200, 100);
        view.Measure(Width);

        Assert.True(view.IsRowMeasured(1));
        Assert.True(view.IsRowMeasured(0), "row 0 is inside the overscan above the window and was not measured");
    }

    [AvaloniaFact]
    public void TheRowsJustAboveTheViewportAreMeasuredWithTheWindow()
    {
        // The overscan above the viewport reaches into a table as it does between blocks, so that
        // a small scroll up shows rows already laid out.
        var (window, view) = Shown(Uniform(600), height: 300);
        var table = (TableBlockView)view.Blocks[0];

        ToMiddleOf(window, view, 0);

        var top = table.RowAnchor(-view.BlockOrigin(0).Y).Row;

        Assert.InRange(top, 100, 500);
        Assert.True(table.IsRowMeasured(top - 1) && table.IsRowMeasured(top - 3), $"the rows just above row {top} are not measured");
    }

    [AvaloniaFact]
    public void ARowHeldInOneTableIsNotKeptInAnotherThatAnEditBringsUnderTheViewport()
    {
        // The viewport halfway down the second of two tables, and a third put in above both. The
        // offset stays where it was and now falls sixteen rows into the first table, a different
        // table from the one whose row was held: the row held there names nothing in it. Sixteen
        // since e9a9273: the inserted table nobody has laid out is priced by what is known of the
        // tables measured, not by their heights estimates and all, so it is shorter than it was
        // priced before (ten rows).
        var view = new RichTextView
        {
            State = EditorState.Create(new DocumentNode([Uniform(20), Uniform(100), ParagraphNode.FromText("After.")])),
        };
        var window = new Window { Content = view, Width = 600, Height = 300 };

        window.Show();
        Layout(window);
        view.Focus();

        // The caret where it is on screen with the first table's row 10 at the top, so keeping it
        // there scrolls nothing.
        view.MoveTo(PositionIn(view.State.Doc, 0, 16, 1), false);
        Layout(window);

        var second = (TableBlockView)view.Blocks[1];

        view.Offset = new Vector(0, view.BlockOrigin(1).Y + view.Offset.Y);
        Layout(window);
        view.Offset = new Vector(0, view.BlockOrigin(1).Y + view.Offset.Y + second.RowOffset(50, 0.5));
        Layout(window);

        Assert.Equal(50, second.RowAnchor(-view.BlockOrigin(1).Y).Row);

        var inserted = Uniform(100);
        var caret = view.State.Selection.Head + inserted.NodeSize;

        view.State = EditorState.Create(
            new DocumentNode([inserted, .. view.State.Doc.Blocks]), TextSelection.Cursor(caret));
        Layout(window);

        var first = (TableBlockView)view.Blocks[1];

        // The top is in the old first table, not in the table whose row 50 was held.
        Assert.Equal(1, view.BlockIndexAt(0));
        Assert.NotEqual(2, view.BlockIndexAt(0));
        Assert.InRange(first.RowAnchor(-view.BlockOrigin(1).Y).Row, 14, 18);
    }

    [AvaloniaTheory]
    [InlineData("image")]
    [InlineData("click")]
    [InlineData("boundary")]
    [InlineData("paragraph")]
    public void AFarRowRealizedByAGeometryQuestionIsInTheHeightThePresenterRecords(string question)
    {
        // The last quarter of the rows are six lines tall, far taller than the one-line rows the
        // window at the top measures, so realizing one of them moves the table's height. Their
        // last cell is empty, so selecting its paragraph finds the paragraph and selects nothing.
        var table = new TableNode(Enumerable.Range(0, 2_000).Select(r => new TableRowNode([
            r >= 1_500
                ? new TableCellNode(Enumerable.Range(0, 6).Select(i => (BlockNode)ParagraphNode.FromText($"line {i}")))
                : Cell(r, 0, false),
            Cell(r, 1, false),
            r >= 1_500 ? TableCellNode.FromText(string.Empty) : Cell(r, 2, false),
        ])));
        var (window, view) = Shown(table, height: 300);
        var tableView = (TableBlockView)view.Blocks[0];
        var height = tableView.Size.Height;

        Assert.Equal(height, view.BlockOrigin(1).Y + view.Offset.Y, 6);
        Assert.False(tableView.IsRowMeasured(1_900));

        // Asked without a layout pass in between, as a hover or an image lookup is. A point is
        // answered against real heights from the top of the viewport to it — the row really there,
        // which is not the row the estimate put there — and a position by the row holding it.
        var y = view.BlockOrigin(0).Y + tableView.RowOffset(1_900, 0.5);

        switch (question)
        {
            case "image":
                Assert.Null(view.ImageRect(PositionIn(view.State.Doc, 0, 1_900, 0)));
                break;
            case "click":
                view.PositionAt(new Point(20, y));
                break;
            case "boundary":
                view.ColumnBoundaryAt(new Point(20, y), 1);
                break;
            default:
                Assert.False(view.SelectParagraphAt(PositionIn(view.State.Doc, 0, 1_900, 2) - 1));
                break;
        }

        Assert.True(tableView.IsRowMeasured(question is "click" or "boundary"
            ? tableView.RowAnchor(y - view.BlockOrigin(0).Y).Row
            : 1_900));
        Assert.NotEqual(height, tableView.Size.Height);

        // The block after the table starts where the table now ends, and the scroll extent is
        // already what the next layout pass makes it.
        Assert.Equal(tableView.Size.Height, view.BlockOrigin(1).Y + view.Offset.Y, 6);

        var recorded = view.Extent.Height;

        view.InvalidateMeasure();
        Layout(window);

        Assert.Equal(view.Extent.Height, recorded, 6);
    }

    private static readonly TextStyleResolver Styles = new(new Typeface("Segoe UI"), 14, Brushes.Black);

    private static BlockView Create(BlockNode block) => block switch
    {
        ParagraphNode paragraph => new ParagraphView(paragraph, Styles),
        TableNode table => new TableBlockView(table, Create, Brushes.Gray),
        _ => throw new NotSupportedException(),
    };

    private static TableBlockView Built(TableNode table)
    {
        var view = new TableBlockView(table, Create, Brushes.Gray);

        view.Measure(Width);

        return view;
    }

    /// <summary>
    /// Rows of three one-line cells, with an optional cell spanning rows in the first column, and
    /// optionally the text of the bench's table so that cells are as long as there.
    /// </summary>
    private static TableNode Uniform(int rows, (int Top, int Span)? span = null, bool wrapped = false) =>
        new(Enumerable.Range(0, rows).Select(r => new TableRowNode(
            span is { } s && r == s.Top
                ? [new TableCellNode([ParagraphNode.FromText("spanning twelve rows")], rowSpan: s.Span), Cell(r, 1, wrapped), Cell(r, 2, wrapped)]
                : span is { } t && r > t.Top && r < t.Top + t.Span
                    ? [Cell(r, 1, wrapped), Cell(r, 2, wrapped)]
                    : [Cell(r, 0, wrapped), Cell(r, 1, wrapped), Cell(r, 2, wrapped)])));

    private static TableNode Mixed(int rows) =>
        new(Enumerable.Range(0, rows).Select(r => new TableRowNode([
            r % 5 == 0
                ? new TableCellNode([ParagraphNode.FromText("one"), ParagraphNode.FromText("two"), ParagraphNode.FromText("three")])
                : Cell(r, 0, false),
            Cell(r, 1, false),
            Cell(r, 2, false),
        ])));

    private static TableCellNode Cell(int row, int column, bool wrapped) =>
        TableCellNode.FromText(wrapped ? $"Row {row} column {column} value {row * column}" : $"r{row}c{column}");

    private static (Window Window, RichTextView View) Shown(TableNode table, double height, bool lead = false)
    {
        var doc = lead
            ? new DocumentNode([ParagraphNode.FromText("Before."), table, ParagraphNode.FromText("After.")])
            : new DocumentNode([table, ParagraphNode.FromText("After.")]);
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = 600, Height = height };

        window.Show();
        Layout(window);

        return (window, view);
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
    }

    /// <summary>Scrolls to the middle of a table, which is not the middle of the extent.</summary>
    /// <remarks>
    /// A block nobody has measured is priced at the mean of those that have been, and a table is
    /// one enormous block, so the paragraph after it is estimated at the table's height.
    /// </remarks>
    private static void ToMiddleOf(Window window, RichTextView view, int block)
    {
        var top = view.BlockOrigin(block).Y + view.Offset.Y;

        view.Offset = new Vector(0, top + (view.Blocks[block].Size.Height / 2));
        Layout(window);
    }

    /// <summary>A position inside the first paragraph of a cell, local to the table.</summary>
    private static int PositionIn(TableNode table, int row, int index)
    {
        var position = 0;

        for (var r = 0; r < row; r++)
        {
            position += table.Rows[r].NodeSize;
        }

        position += 1;

        for (var c = 0; c < index; c++)
        {
            position += table.Rows[row].Cells[c].NodeSize;
        }

        return position + 3;
    }

    /// <summary>The document position inside the first paragraph of a cell of a top-level table.</summary>
    private static int PositionIn(DocumentNode doc, int block, int row, int index)
    {
        var start = 0;

        for (var b = 0; b < block; b++)
        {
            start += doc.Blocks[b].NodeSize;
        }

        return start + 1 + PositionIn((TableNode)doc.Blocks[block], row, index);
    }

    private static double RowTop(TableBlockView view, TableNode table, int row) =>
        view.GetCaretRect(PositionIn(table, row, 1)).Top - 6;

    private static uint[] Draw(TableBlockView view, Rect visible, bool inked = true)
    {
        var size = new PixelSize((int)Width + 20, (int)Math.Ceiling(visible.Height) + 20);

        using var bitmap = new RenderTargetBitmap(size);

        using (var context = bitmap.CreateDrawingContext())
        {
            var origin = new Point(5, 5 - visible.Top);

            using (context.PushClip(visible.Translate(new Vector(origin.X, origin.Y))))
            {
                view.Render(context, origin, visible);
            }
        }

        var pixels = RenderedPixels.Argb(bitmap);

        if (inked)
        {
            Assert.Contains(pixels, p => p != 0);
        }

        return pixels;
    }
}
