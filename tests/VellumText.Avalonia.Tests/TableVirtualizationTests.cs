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
        windowed.SetWindow(0, 100);
        windowed.Measure(Width);

        // A window starting halfway down the cell that spans rows 200 to 209.
        var top = RowTop(whole, table, 205);

        windowed.SetWindow(top, top + 250);
        windowed.Measure(Width);

        Assert.InRange(windowed.RealizedRows, 10, 40);
        Assert.Equal(whole.Size, windowed.Size);

        var visible = new Rect(0, top, Width, 250);

        Assert.Equal(Draw(whole, visible), Draw(windowed, visible));

        // Positions in the rows the window holds, and in the spanning cell above its top.
        for (var p = PositionIn(table, 199, 0); p < PositionIn(table, 215, 0); p++)
        {
            Assert.True(whole.GetCaretRect(p) == windowed.GetCaretRect(p), $"caret at {p}");
        }

        for (var y = top; y < top + 250; y += 5)
        {
            for (var x = 2.0; x < Width; x += 37)
            {
                Assert.True(whole.HitTest(new Point(x, y)) == windowed.HitTest(new Point(x, y)), $"hit at {x}, {y}");
            }
        }

        Assert.InRange(windowed.RealizedRows, 10, 40);

        // A band the window never reached is realized when it is drawn rather than drawn blank.
        var far = new Rect(0, RowTop(whole, table, 330), Width, 200);

        Assert.Equal(Draw(whole, far), Draw(windowed, far));
    }

    [AvaloniaFact]
    public void ARowOutsideTheWindowIsRealizedWhenItIsAskedAbout()
    {
        var table = Uniform(400);
        var whole = Built(table);
        var windowed = new TableBlockView(table, Create, Brushes.Gray);

        windowed.SetWindow(0, 200);
        windowed.Measure(Width);

        var before = windowed.RealizedRows;
        var far = PositionIn(table, 350, 2);

        Assert.True(before < 20);
        Assert.Equal(whole.GetCaretRect(far), windowed.GetCaretRect(far));
        Assert.Equal(whole.ParagraphAt(far)!.Value.Origin, windowed.ParagraphAt(far)!.Value.Origin);
        Assert.True(windowed.RealizedRows > before);
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

    private static uint[] Draw(TableBlockView view, Rect visible)
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

        var pixels = new uint[size.Width * size.Height];

        unsafe
        {
            fixed (uint* buffer = pixels)
            {
                bitmap.CopyPixels(new PixelRect(size), (nint)buffer, pixels.Length * sizeof(uint), size.Width * sizeof(uint));
            }
        }

        Assert.Contains(pixels, p => p != 0);

        return pixels;
    }
}
