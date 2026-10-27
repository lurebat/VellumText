using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A table view brought up to date row by row, and drawn row band by row band, answers and draws
/// exactly what a table view built afresh does (F-35).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TableBlockView.Update"/> now walks only the rows whose nodes an edit replaced, with
/// every position kept relative to its row and the row starts moved in one pass; and
/// <c>Render</c> draws only the rows meeting what is visible. Both are the kind of change that is
/// right in the common case and wrong by one somewhere, so every test here compares against a
/// fresh view of the same table: every position's caret, a grid of hit tests, every paragraph and
/// cell, selection rectangles, and pixels.
/// </para>
/// </remarks>
public class TableRowUpdateTests
{
    private const double Width = 420;

    [AvaloniaFact]
    public void AnEditThatGrowsARowMovesEveryRowAfterIt()
    {
        var table = Grid(30, 3);
        var view = Built(table);

        // More text in a middle cell, and a second paragraph in it: the row is taller and longer,
        // so every row after it starts later in both senses.
        var grown = Replace(table, 10, 1, new TableCellNode([
            ParagraphNode.FromText("a cell that now holds a good deal more text than it did, enough to wrap"),
            ParagraphNode.FromText("and a second paragraph"),
        ]));

        Assert.True(view.Update(grown));
        view.Measure(Width);

        AssertSameAs(Fresh(grown), view);
    }

    [AvaloniaFact]
    public void TwoRowsTradingHeightInOneEditMoveEveryRowBetweenThem()
    {
        // Row 3 grows by a paragraph and row 9 loses one, in the same update, so the table is as
        // tall as it was and the mean row height is unchanged — and every row between them is a
        // paragraph lower than it was.
        var rows = Grid(14, 3).Rows.ToArray();

        rows[9] = new TableRowNode([new TableCellNode([ParagraphNode.FromText("a"), ParagraphNode.FromText("b")]), rows[9].Cells[1], rows[9].Cells[2]]);

        var table = new TableNode(rows);
        var view = Built(table);
        var traded = rows.ToArray();

        traded[3] = new TableRowNode([new TableCellNode([ParagraphNode.FromText("a"), ParagraphNode.FromText("b")]), rows[3].Cells[1], rows[3].Cells[2]]);
        traded[9] = new TableRowNode([TableCellNode.FromText("a"), rows[9].Cells[1], rows[9].Cells[2]]);

        var changed = new TableNode(traded);

        Assert.True(view.Update(changed));
        view.Measure(Width);

        AssertSameAs(Fresh(changed), view);
    }
    [AvaloniaFact]
    public void EditsInManyRowsInTurnMatchAFreshView()
    {
        var table = Grid(24, 4);
        var view = Built(table);
        var random = new Random(5);

        for (var i = 0; i < 15; i++)
        {
            var row = random.Next(table.Rows.Length);
            var column = random.Next(4);
            var text = i % 3 == 0 ? "x" : string.Concat(Enumerable.Repeat("longer text ", random.Next(1, 6)));

            table = Replace(table, row, column, TableCellNode.FromText(text));

            Assert.True(view.Update(table));
            view.Measure(Width);

            AssertSameAs(Fresh(table), view);
        }
    }

    [AvaloniaFact]
    public void AnUntouchedRowKeepsItsCellViews()
    {
        var table = Grid(12, 3);
        var view = Built(table);
        var before = view.ParagraphViews.ToArray();
        var edited = Replace(table, 5, 2, TableCellNode.FromText("edited"));

        WorkCounters.TableCellsBuilt = 0;
        view.Update(edited);

        var after = view.ParagraphViews.ToArray();

        Assert.Equal(3, WorkCounters.TableCellsBuilt);
        Assert.Equal(before.Length, after.Length);

        for (var i = 0; i < before.Length; i++)
        {
            Assert.Same(before[i], after[i]);
        }
    }

    [AvaloniaFact]
    public void ResizingAColumnWalksNoCellAndMatchesAFreshView()
    {
        var table = Grid(10, 3);
        var view = Built(table);
        var resized = new TableNode(table.Rows, [1, 2, 1]);

        WorkCounters.TableCellsBuilt = 0;

        Assert.True(view.Update(resized));
        Assert.Equal(0, WorkCounters.TableCellsBuilt);

        view.Measure(Width);

        AssertSameAs(Fresh(resized), view);
    }

    [AvaloniaTheory]
    [InlineData("insert a row")]
    [InlineData("remove a row")]
    [InlineData("span a row")]
    [InlineData("add a cell")]
    public void AShapeChangeIsWalkedWholeAndMatchesAFreshView(string change)
    {
        var table = Grid(10, 3);
        var view = Built(table);
        var rows = table.Rows.ToList();

        TableNode changed;

        switch (change)
        {
            case "insert a row":
                rows.Insert(4, new TableRowNode([TableCellNode.FromText("n0"), TableCellNode.FromText("n1"), TableCellNode.FromText("n2")]));
                changed = new TableNode(rows);
                break;
            case "remove a row":
                rows.RemoveAt(4);
                changed = new TableNode(rows);
                break;
            case "span a row":
                // The cell at row 3, column 0 spans down into row 4, which gives up its first cell.
                rows[3] = new TableRowNode([new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2), rows[3].Cells[1], rows[3].Cells[2]]);
                rows[4] = new TableRowNode([rows[4].Cells[1], rows[4].Cells[2]]);
                changed = new TableNode(rows);
                break;
            default:
                rows[6] = new TableRowNode([.. rows[6].Cells, TableCellNode.FromText("extra")]);
                changed = new TableNode(rows);
                break;
        }

        WorkCounters.TableCellsBuilt = 0;

        Assert.True(view.Update(changed));
        view.Measure(Width);

        // Every cell placed again and given its views again, whether in the update or, for a row
        // whose cells take the place of none that were, when it is next laid out.
        Assert.Equal(changed.Rows.Sum(row => row.Cells.Length), WorkCounters.TableCellsBuilt);

        AssertSameAs(Fresh(changed), view);
    }

    [AvaloniaFact]
    public void DrawingOnlyTheVisibleRowsDrawsWhatTheWholeTableDrawsThere()
    {
        var table = Spanned(40);
        var view = Fresh(table);

        // A band that starts inside the cell spanning rows 10 to 16, so that cell reaches into it
        // from a row above the band and must still be drawn.
        var top = view.GetCaretRect(PositionIn(table, 13, 1)).Top - 5;
        var visible = new Rect(0, top, Width, 220);

        WorkCounters.TableCellsRendered = 0;

        var hinted = Draw(view, visible, hint: true);
        var drawn = WorkCounters.TableCellsRendered;
        var whole = Draw(view, visible, hint: false);

        Assert.Equal(whole, hinted);

        // Exactly the cells whose boxes, border included, meet the band — the spanning one among
        // them — and none of the cells above it that the band does not reach.
        var meeting = view.GetCellRects(Enumerable.Range(0, view.ContentSize).ToHashSet(), 1)
            .Count(cell => cell.Intersects(visible.Inflate(1)));

        Assert.Equal(meeting, drawn);
    }

    [AvaloniaFact]
    public void DrawingABandThatStartsAndEndsMidRowMatchesTheWholeTable()
    {
        var view = Fresh(Grid(30, 3));
        var row = view.GetCaretRect(PositionIn(Grid(30, 3), 1, 0)).Height;

        // Bands beginning and ending a little way into a row, on a row edge, and a pixel either
        // side of one, so that a row only just on screen at either end is still drawn.
        foreach (var top in new[] { 100.0, 133.3, 160 - 0.5, 160, 160 + 0.5, 271.9 })
        {
            foreach (var height in new[] { row / 3, row * 2.5, 300 })
            {
                var visible = new Rect(0, top, Width, height);

                Assert.Equal(Draw(view, visible, hint: false), Draw(view, visible, hint: true));
            }
        }
    }

    [AvaloniaFact]
    public void AVisibleAreaBelowOrAboveTheTableDrawsNoCell()
    {
        var view = Fresh(Grid(10, 3));

        WorkCounters.TableCellsRendered = 0;

        Draw(view, new Rect(0, view.Size.Height + 50, Width, 100), hint: true);
        Draw(view, new Rect(0, -300, Width, 100), hint: true);

        Assert.Equal(0, WorkCounters.TableCellsRendered);
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
        var view = new TableBlockView(table, Create, Brushes.Gray, Brushes.LightGray);

        view.Measure(Width);

        return view;
    }

    private static TableBlockView Fresh(TableNode table) => Built(table);

    private static TableNode Grid(int rows, int columns) =>
        new(Enumerable.Range(0, rows).Select(r =>
            new TableRowNode(Enumerable.Range(0, columns).Select(c =>
                TableCellNode.FromText($"row {r} column {c}")))));

    /// <summary>A grid with a cell spanning rows 10 to 16 in its first column.</summary>
    private static TableNode Spanned(int rows) =>
        new(Enumerable.Range(0, rows).Select(r => new TableRowNode(r switch
        {
            10 => [new TableCellNode([ParagraphNode.FromText("spanning seven rows")], rowSpan: 7), Cell(r, 1), Cell(r, 2)],
            > 10 and < 17 => [Cell(r, 1), Cell(r, 2)],
            _ => [Cell(r, 0), Cell(r, 1), Cell(r, 2)],
        })));

    private static TableCellNode Cell(int row, int column) => TableCellNode.FromText($"row {row} column {column}");

    private static TableNode Replace(TableNode table, int row, int column, TableCellNode cell)
    {
        var rows = table.Rows.ToArray();
        var cells = rows[row].Cells.ToArray();

        cells[column] = cell;
        rows[row] = new TableRowNode(cells);

        return new TableNode(rows, table.ColumnWidths);
    }

    /// <summary>A local position inside the first paragraph of a cell, by its row and index in the row.</summary>
    private static int PositionIn(TableNode table, int row, int index)
    {
        var position = 0;

        for (var r = 0; r < row; r++)
        {
            position += table.Rows[r].NodeSize;
        }

        // The row's and the cell's opening boundaries, the cells before it, and the paragraph's.
        position += 1;

        for (var c = 0; c < index; c++)
        {
            position += table.Rows[row].Cells[c].NodeSize;
        }

        return position + 3;
    }

    private static void AssertSameAs(TableBlockView expected, TableBlockView actual)
    {
        Assert.Equal(expected.ContentSize, actual.ContentSize);
        Assert.Equal(expected.Size, actual.Size);

        for (var p = 0; p <= expected.ContentSize; p++)
        {
            Assert.True(expected.GetCaretRect(p) == actual.GetCaretRect(p), $"caret at {p}");
            Assert.True(Same(expected.ParagraphAt(p), actual.ParagraphAt(p)), $"paragraph at {p}");
        }

        for (var y = -10.0; y < expected.Size.Height + 20; y += 7.5)
        {
            for (var x = -10.0; x < Width + 20; x += 23)
            {
                Assert.True(expected.HitTest(new Point(x, y)) == actual.HitTest(new Point(x, y)), $"hit at {x}, {y}");
            }
        }

        var size = expected.ContentSize;

        foreach (var (from, to) in new[] { (0, size), (size / 3, size / 2), (size / 2, size - 7), (5, 60) })
        {
            Assert.Equal(expected.GetSelectionRects(from, to), actual.GetSelectionRects(from, to));
        }

        var cells = Enumerable.Range(0, size).ToHashSet();

        Assert.Equal(expected.GetCellRects(cells, 1), actual.GetCellRects(cells, 1));
        Assert.Equal(Draw(expected, null, hint: false), Draw(actual, null, hint: false));
    }

    private static bool Same(TextAt? a, TextAt? b) =>
        a is null
            ? b is null
            : b is not null
                && a.Value.Start == b.Value.Start
                && a.Value.Origin == b.Value.Origin
                && a.Value.View.Paragraph.Equals(b.Value.View.Paragraph);

    private static uint[] Draw(TableBlockView view, Rect? visible, bool hint)
    {
        var size = new PixelSize((int)Width + 20, (int)Math.Ceiling(view.Size.Height) + 20);

        using var bitmap = new RenderTargetBitmap(size);

        using (var context = bitmap.CreateDrawingContext())
        {
            var origin = new Point(5, 5);

            if (visible is { } area)
            {
                using (context.PushClip(area.Translate(new Vector(origin.X, origin.Y))))
                {
                    if (hint)
                    {
                        view.Render(context, origin, area);
                    }
                    else
                    {
                        view.Render(context, origin);
                    }
                }
            }
            else
            {
                view.Render(context, origin);
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

        return pixels;
    }
}
