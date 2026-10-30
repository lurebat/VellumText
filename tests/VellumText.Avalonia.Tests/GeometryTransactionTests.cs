using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Questions about geometry far from the viewport are answered against what is really there and
/// leave the screen as it was; painting a frame lays nothing out (rubber-duck review finding 10.4,
/// design amendments on point queries and paint purity).
/// </summary>
public class GeometryTransactionTests
{
    private const double Width = 500;
    private const double Height = 400;

    /// <summary>
    /// A focused caret in a row far below the viewport, through a width change: the frame that
    /// follows draws without laying out a row, a paragraph or a height.
    /// </summary>
    /// <remarks>
    /// Drawing the caret asks where the caret is. Asked during the frame, a table laid out the
    /// caret's row there and then — after the width change forgot every row's height — which moved
    /// the table's height, the extent and everything below in the middle of drawing.
    /// </remarks>
    [AvaloniaFact]
    public void AFocusedOffScreenCaretSurvivesAResizeWithoutLayingOutMidFrame()
    {
        var table = new TableNode(Enumerable.Range(0, 3_000).Select(r => new TableRowNode([
            TableCellNode.FromText($"r{r}c0"),
            TableCellNode.FromText($"r{r}c1"),
        ])));
        var doc = new DocumentNode([table, ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var block = (TableBlockView)view.Blocks[0];

        view.MoveTo(PositionOf(doc, "r2900c1") + 2, extend: false);
        Layout(window);
        view.Offset = default;
        Layout(window);

        window.Width = Width - 120;
        Layout(window);

        AssertFrameLaysNothingOut(window, view, block);
    }

    /// <summary>
    /// A point far below the viewport lands on the row that is really that far below what the
    /// reader sees — the row a fully laid-out copy of the document has there — not on the row an
    /// estimate put there.
    /// </summary>
    /// <remarks>
    /// The rows on the first screen are short and the rest are ten lines tall, so the estimate the
    /// short ones teach is far off by the time the point is reached.
    /// </remarks>
    [AvaloniaFact]
    public void APointFarBelowTheViewportLandsOnTheRowReallyThere()
    {
        var doc = new DocumentNode([TallAfterShort(2_000), ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var twin = FullyLaidOut(doc);
        var point = new Point(30, 30_000);

        var hit = view.PositionAt(point);

        Assert.Equal(twin.PositionAt(point), hit);

        // Asked again, the same answer, and nothing laid out to give it.
        WorkCounters.TableCellsMeasured = 0;

        Assert.Equal(hit, view.PositionAt(point));
        Assert.Equal(0, WorkCounters.TableCellsMeasured);
    }

    /// <summary>The same, far above a viewport scrolled deep into the table.</summary>
    [AvaloniaFact]
    public void APointFarAboveTheViewportLandsOnTheRowReallyThere()
    {
        var doc = new DocumentNode([TallAfterShort(2_000), ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var twin = FullyLaidOut(doc);

        view.Offset = new Vector(0, view.Extent.Height * 0.8);
        Layout(window);

        // What is at the top of the viewport, and where the copy has it.
        var reference = view.PositionAt(new Point(30, 2));
        var here = ScreenTop(view, reference);
        var there = ScreenTop(twin, reference);
        var point = new Point(30, -20_000);

        var hit = view.PositionAt(point);

        Assert.Equal(twin.PositionAt(new Point(point.X, point.Y - here + there)), hit);

        // The reader saw nothing move while it was answered.
        Assert.Equal(here, ScreenTop(view, reference), 6);
    }

    /// <summary>
    /// A column-boundary question exactly at the top of a tall row far below the viewport, in a
    /// table whose rows above the viewport nobody has laid out: everything the answer reads is
    /// laid out first — the row starting at the point too, not only the one ending there — so the
    /// answer lays nothing out, matches a fully laid-out copy, and the text on screen stays put.
    /// </summary>
    /// <remarks>
    /// Every row but one is a single line, so the estimate is exact and the far row's top is where
    /// the point is however much is measured on the way; the one tall row, measured while
    /// answering, moved the estimate pricing the rows above the viewport.
    /// </remarks>
    [AvaloniaFact]
    public void AColumnBoundaryQuestionExactlyAtAFarRowsTopLaysNothingOutWhileAnswering()
    {
        const int Tall = 1_500;
        var doc = new DocumentNode([
            new TableNode(Enumerable.Range(0, 2_000).Select(r => new TableRowNode([
                r == Tall
                    ? new TableCellNode(Enumerable.Range(0, 40).Select(i => (BlockNode)ParagraphNode.FromText($"tall {i}")))
                    : TableCellNode.FromText($"r{r}c0"),
                TableCellNode.FromText($"r{r}c1"),
            ]))),
            ParagraphNode.FromText("After."),
        ]);
        var (window, view) = Shown(doc);
        var twin = FullyLaidOut(doc);
        var table = (TableBlockView)view.Blocks[0];

        view.Offset = new Vector(0, table.RowTopOf(1_000));
        Layout(window);

        Assert.False(table.IsRowMeasured(500));
        Assert.False(table.IsRowMeasured(Tall));

        var reference = view.PositionAt(new Point(30, Height / 2));
        var here = ScreenTop(view, reference);
        var there = ScreenTop(twin, reference);
        var point = new Point(view.BlockOrigin(0).X + (table.Size.Width / 2), view.BlockOrigin(0).Y + table.RowTopOf(Tall));
        var measured = -1L;

        view.BeforeAnswer = () => (WorkCounters.TableCellsMeasured, WorkCounters.CharactersLaidOut) = (0, 0);

        var boundary = view.ColumnBoundaryAt(point, 4);

        measured = WorkCounters.TableCellsMeasured + WorkCounters.CharactersLaidOut;
        view.BeforeAnswer = null;

        Assert.Equal(0, measured);
        Assert.Equal(here, ScreenTop(view, reference), 1);
        Assert.Equal(twin.ColumnBoundaryAt(new Point(point.X, point.Y - here + there), 4)?.Boundary, boundary?.Boundary);
        Assert.NotNull(boundary);
    }

    /// <summary>
    /// The same for the position at a point exactly at the top of a tall paragraph far below the
    /// viewport, in a document whose paragraphs above the viewport nobody has laid out.
    /// </summary>
    [AvaloniaFact]
    public void APositionQuestionExactlyAtAFarBlocksTopLaysNothingOutWhileAnswering()
    {
        const int Tall = 1_500;
        var doc = new DocumentNode([.. Enumerable.Range(0, 2_000).Select(i => (BlockNode)ParagraphNode.FromText(i == Tall
            ? string.Concat(Enumerable.Repeat("a tall paragraph far below the viewport ", 200))
            : $"p{i}"))]);
        var (window, view) = Shown(doc);
        var twin = FullyLaidOut(doc);
        var first = view.Blocks[0].Size.Height;

        view.Offset = new Vector(0, first * 1_000);
        Layout(window);

        var reference = view.PositionAt(new Point(30, Height / 2));
        var here = ScreenTop(view, reference);
        var there = ScreenTop(twin, reference);
        var point = new Point(30, view.BlockOrigin(Tall).Y);
        var measured = -1L;

        view.BeforeAnswer = () => (WorkCounters.TableCellsMeasured, WorkCounters.CharactersLaidOut) = (0, 0);

        var hit = view.PositionAt(point);

        measured = WorkCounters.TableCellsMeasured + WorkCounters.CharactersLaidOut;
        view.BeforeAnswer = null;

        Assert.Equal(0, measured);
        Assert.Equal(here, ScreenTop(view, reference), 1);
        // On the edge the point belongs to the block starting there; half a pixel down, the copy
        // says the same without depending on how the two round the edge.
        Assert.Equal(twin.PositionAt(new Point(point.X, point.Y - here + there + 0.5)), hit);
    }

    /// <summary>
    /// Dragging a move onto a rule near the end of a long run of them far below the viewport:
    /// the drop caret goes to the nearest text, which is after the run — past the point, which is
    /// as far as the point's own question laid out — and the frame that draws it lays nothing out.
    /// </summary>
    [AvaloniaFact]
    public void DraggingAMoveFarDownPreparesTheDropTargetBeforeTheFrame()
    {
        var blocks = new List<BlockNode> { ParagraphNode.FromText("drag me somewhere far") };

        blocks.AddRange(Enumerable.Range(0, 300).Select(i => (BlockNode)ParagraphNode.FromText($"filler {i}")));

        blocks.AddRange(Enumerable.Repeat((BlockNode)RuleNode.Instance, 300));

        var rule = blocks.Count - 5;
        blocks.Add(ParagraphNode.FromText("after the rules"));
        blocks.AddRange(Enumerable.Range(0, 50).Select(i => (BlockNode)ParagraphNode.FromText($"tail {i}")));

        var doc = new DocumentNode(blocks);
        var (window, view) = Shown(doc);
        var twin = FullyLaidOut(doc);

        view.MoveTo(1, extend: false);
        view.MoveTo(5, extend: true);
        Layout(window);

        var from = new Point(4, view.BlockOrigin(0).Y + 4);
        var to = new Point(4, twin.BlockOrigin(rule).Y + 1);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X + 20, from.Y + 20));
        window.MouseMove(to);

        // No layout pass comes between the move and the frame, so the frame finds the drop target
        // laid out only if the move laid it out.
        Assert.True(view.IsMeasureValid);
        Assert.True(view.IsMovingSelection);

        WorkCounters.CharactersLaidOut = 0;

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        Assert.Equal(0, WorkCounters.CharactersLaidOut);

        window.MouseUp(to, MouseButton.Left);
    }

    internal static void AssertFrameLaysNothingOut(Window window, RichTextView view, TableBlockView block)
    {
        var height = block.Size.Height;
        var extent = view.Extent;
        var offset = view.Offset;

        WorkCounters.TableCellsMeasured = 0;
        WorkCounters.TableCellsBuilt = 0;
        WorkCounters.CharactersLaidOut = 0;

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        Assert.Equal(0, WorkCounters.TableCellsMeasured);
        Assert.Equal(0, WorkCounters.TableCellsBuilt);
        Assert.Equal(0, WorkCounters.CharactersLaidOut);
        Assert.Equal(height, block.Size.Height);
        Assert.Equal(extent, view.Extent);
        Assert.Equal(offset, view.Offset);
    }

    /// <summary>Rows of one line for a screenful, then rows ten lines tall.</summary>
    internal static TableNode TallAfterShort(int rows) =>
        new(Enumerable.Range(0, rows).Select(r => new TableRowNode([
            r < 20
                ? TableCellNode.FromText($"short {r}")
                : new TableCellNode(Enumerable.Range(0, 10).Select(i => (BlockNode)ParagraphNode.FromText($"row {r} line {i}"))),
            TableCellNode.FromText($"r{r}c1"),
        ])));

    /// <summary>The same document laid out whole, with nothing estimated, as the oracle.</summary>
    internal static RichTextView FullyLaidOut(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };

        view.Measure(new Size(Width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, Width, view.DesiredSize.Height));

        return view;
    }

    internal static double ScreenTop(RichTextView view, int position)
    {
        var block = view.BlockIndexOf(position);

        return view.BlockOrigin(block).Y
            + view.Blocks[block].GetCaretRect(position - view.Slots[block].Start).Top;
    }

    internal static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        Layout(window);
        view.Focus();

        return (window, view);
    }

    internal static void Layout(Window window)
    {
        window.UpdateLayout();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
    }

    internal static int PositionOf(DocumentNode doc, string text)
    {
        return Find(doc.Blocks, 0) ?? throw new InvalidOperationException($"No paragraph reads {text}.");

        int? Find(IReadOnlyList<Node> nodes, int start)
        {
            var position = start;

            foreach (var node in nodes)
            {
                if (node is ParagraphNode paragraph && paragraph.Content.Text == text)
                {
                    return position + 1;
                }

                if (node.Children.Length > 0 && Find(node.Children, position + 1) is { } found)
                {
                    return found;
                }

                position += node.NodeSize;
            }

            return null;
        }
    }
}
