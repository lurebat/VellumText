using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Table layout: the grid, and the positions inside it.
/// </summary>
/// <remarks>
/// A table is the only block that hosts other blocks, so it is the only one where a position is
/// two levels of arithmetic away from a pixel. Everything here checks the two against each
/// other rather than against a number written down once — a caret rectangle has to land in the
/// cell whose text it belongs to, and hit-testing that rectangle has to give the position back.
/// </remarks>
public class TableBlockViewTests
{
    private static TableBlockView Measured(TableNode table, double width = 400)
    {
        var view = new RichTextView { State = EditorState.Create(new DocumentNode([table])) };

        view.Measure(new Size(width, 800));

        return (TableBlockView)view.Blocks[0];
    }

    private static TableNode Grid() =>
        new([
            new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
            new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
        ]);

    /// <summary>The local position just inside the paragraph reading some text.</summary>
    private static int StartOf(TableNode table, string text)
    {
        var pos = 0;

        foreach (var row in table.Rows)
        {
            var cellPos = pos + 1;

            foreach (var cell in row.Cells)
            {
                var blockPos = cellPos + 1;

                foreach (var block in cell.Blocks)
                {
                    if (block is ParagraphNode paragraph && paragraph.Content.Text == text)
                    {
                        return blockPos + 1;
                    }

                    blockPos += block.NodeSize;
                }

                cellPos += cell.NodeSize;
            }

            pos += row.NodeSize;
        }

        throw new InvalidOperationException($"No cell reads '{text}'.");
    }

    [AvaloniaFact]
    public void ATableFillsTheWidthItIsGiven()
    {
        Assert.Equal(400, Measured(Grid()).Size.Width);
    }

    [AvaloniaFact]
    public void ARowSpansingCellCountsAsOneColumnPerSpan()
    {
        var table = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2),
            ]),
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
        ]);

        Assert.Equal(2, Measured(table).Columns);
    }

    /// <summary>
    /// Not <c>Rows[0].Cells.Length</c>: a cell spanning rows occupies a slot in the row below,
    /// so that row carries one cell while the table is still two columns wide.
    /// </summary>
    [AvaloniaFact]
    public void ACellSpanningRowsStillLeavesTheTableTwoColumnsWide()
    {
        var table = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                TableCellNode.FromText("b1"),
            ]),
            new TableRowNode([TableCellNode.FromText("b2")]),
        ]);

        Assert.Equal(2, Measured(table).Columns);
    }

    [AvaloniaFact]
    public void TwoColumnsWithNoStatedWidthsSplitTheSpaceEvenly()
    {
        var table = Grid();
        var view = Measured(table);

        var left = view.GetCaretRect(StartOf(table, "a1"));
        var right = view.GetCaretRect(StartOf(table, "b1"));

        // The gap between the two carets is the first column's width, whatever the padding is.
        Assert.Equal(200, right.X - left.X, 1);
    }

    [AvaloniaFact]
    public void StatedColumnWidthsAreHonouredInProportion()
    {
        // Three times as wide as the second, so the first column takes three quarters of 400.
        var table = new TableNode(Grid().Rows, [300.0, 100.0]);
        var view = Measured(table, 400);

        var left = view.GetCaretRect(StartOf(table, "a1"));
        var right = view.GetCaretRect(StartOf(table, "b1"));

        Assert.Equal(300, right.X - left.X, 1);
    }

    /// <summary>
    /// Widths totalling less than the space are scaled <em>up</em>, which is what makes them
    /// proportions rather than measurements.
    /// </summary>
    /// <remarks>
    /// The discriminating case, and the only one. <c>StatedColumnWidthsAreHonouredInProportion</c>
    /// states 300 and 100 into 400, where the proportional answer and the literal-pixel answer are
    /// the same number, and <c>StatedWidthsWiderThanTheSpaceAreScaledDownToFit</c> states more than
    /// fits, which a pixel semantics that clamped would also scale down. Neither can tell the two
    /// readings apart. Only a table asking for less than it is given can: pixels would leave the
    /// remainder empty.
    /// </remarks>
    [AvaloniaFact]
    public void StatedWidthsNarrowerThanTheSpaceAreScaledUpToFill()
    {
        var table = new TableNode(Grid().Rows, [100.0, 100.0]);
        var view = Measured(table, 400);

        var left = view.GetCaretRect(StartOf(table, "a1"));
        var right = view.GetCaretRect(StartOf(table, "b1"));

        // Half of 400, not the 100 the number says.
        Assert.Equal(200, right.X - left.X, 1);
    }

    /// <summary>
    /// A table wider than the space is scaled to fit rather than drawn off the edge, because a
    /// pasted table's widths were written for someone else's window.
    /// </summary>
    [AvaloniaFact]
    public void StatedWidthsWiderThanTheSpaceAreScaledDownToFit()
    {
        var table = new TableNode(Grid().Rows, [900.0, 300.0]);
        var view = Measured(table, 400);

        var left = view.GetCaretRect(StartOf(table, "a1"));
        var right = view.GetCaretRect(StartOf(table, "b1"));

        Assert.Equal(400, view.Size.Width);
        Assert.Equal(300, right.X - left.X, 1);
    }

    [AvaloniaFact]
    public void EachRowIsBelowTheOneBeforeIt()
    {
        var table = Grid();
        var view = Measured(table);

        var top = view.GetCaretRect(StartOf(table, "a1"));
        var bottom = view.GetCaretRect(StartOf(table, "a2"));

        Assert.True(bottom.Y >= top.Bottom, $"row two started at {bottom.Y}, row one ended at {top.Bottom}");
    }

    [AvaloniaFact]
    public void EveryCaretRectangleIsInsideTheTable()
    {
        var table = Grid();
        var view = Measured(table);
        var bounds = new Rect(view.Size);

        foreach (var text in (string[])["a1", "b1", "a2", "b2"])
        {
            var rect = view.GetCaretRect(StartOf(table, text));

            Assert.True(bounds.Contains(rect.TopLeft), $"{text} caret at {rect} escaped {bounds}");
            Assert.True(bounds.Contains(rect.BottomRight), $"{text} caret at {rect} escaped {bounds}");
        }
    }

    /// <summary>
    /// The round trip that would catch an off-by-one in either direction of the mapping. A
    /// caret rectangle and hit-testing it are computed by different code paths through the same
    /// arithmetic, so agreeing is evidence rather than a tautology.
    /// </summary>
    [AvaloniaFact]
    public void HitTestingACaretRectangleGivesThePositionBack()
    {
        var table = Grid();
        var view = Measured(table);

        foreach (var text in (string[])["a1", "b1", "a2", "b2"])
        {
            var start = StartOf(table, text);

            for (var offset = 0; offset <= 2; offset++)
            {
                var position = start + offset;
                var rect = view.GetCaretRect(position);
                var probe = new Point(rect.X + 0.5, rect.Y + (rect.Height / 2));

                Assert.Equal(position, view.HitTest(probe));
            }
        }
    }

    [AvaloniaFact]
    public void AClickInTheSecondColumnLandsInTheSecondColumn()
    {
        var table = Grid();
        var view = Measured(table);

        var hit = view.HitTest(new Point(300, 10));

        Assert.InRange(hit, StartOf(table, "b1"), StartOf(table, "b1") + 2);
    }

    /// <summary>
    /// A click outside the grid — in the gap beside a short last row, or past the right edge —
    /// lands in the cell it is nearest rather than in whichever cell happens to be first.
    /// </summary>
    [AvaloniaFact]
    public void AClickOutsideTheTableLandsInTheNearestCell()
    {
        var table = Grid();
        var view = Measured(table);

        var topLeft = view.HitTest(new Point(-50, -50));
        var bottomRight = view.HitTest(new Point(9000, 9000));

        Assert.InRange(topLeft, StartOf(table, "a1"), StartOf(table, "a1") + 2);
        Assert.InRange(bottomRight, StartOf(table, "b2"), StartOf(table, "b2") + 2);
    }

    [AvaloniaFact]
    public void ASelectionAcrossTwoCellsHighlightsBoth()
    {
        var table = Grid();
        var view = Measured(table);

        var rects = view.GetSelectionRects(StartOf(table, "a1"), StartOf(table, "b1") + 2);

        Assert.True(rects.Count >= 2, $"only {rects.Count} rectangles");
        Assert.Contains(rects, r => r.X < 200);
        Assert.Contains(rects, r => r.X >= 200);
    }

    [AvaloniaFact]
    public void AnEmptySelectionHighlightsNothing()
    {
        var table = Grid();
        var view = Measured(table);
        var at = StartOf(table, "a1");

        Assert.Empty(view.GetSelectionRects(at, at));
        Assert.Empty(view.GetSelectionRects(at + 2, at));
    }

    [AvaloniaFact]
    public void ACellSpanningTwoColumnsIsAsWideAsBothOfThem()
    {
        var table = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2),
            ]),
            new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
        ]);

        var wordy = new TableNode([
            new TableRowNode([
                new TableCellNode(
                    [ParagraphNode.FromText(string.Join(" ", Enumerable.Repeat("word", 30)))],
                    columnSpan: 2),
            ]),
            new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
        ]);

        var narrow = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText(string.Join(" ", Enumerable.Repeat("word", 30)))]),
                TableCellNode.FromText("b1"),
            ]),
            new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
        ]);

        Assert.Equal(2, Measured(table).Columns);

        // The same text in a cell twice as wide wraps to fewer lines, so the table is shorter.
        // Height is the only thing that can tell the two apart from outside.
        Assert.True(
            Measured(wordy).Size.Height < Measured(narrow).Size.Height,
            $"spanned {Measured(wordy).Size.Height}, unspanned {Measured(narrow).Size.Height}");
    }

    [AvaloniaFact]
    public void ARowSpanningCellDoesNotOverlapTheRowBelowIt()
    {
        var table = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                TableCellNode.FromText("b1"),
            ]),
            new TableRowNode([TableCellNode.FromText("b2")]),
        ]);

        var view = Measured(table);

        var first = view.GetCaretRect(StartOf(table, "b1"));
        var second = view.GetCaretRect(StartOf(table, "b2"));

        Assert.True(second.Bottom <= view.Size.Height, "the second row fell out of the table");
        Assert.True(second.Y >= first.Bottom, "the two rows overlapped");
    }

    /// <summary>
    /// A cell taller than the row it spans into pushes the <em>last</em> row it covers, not the
    /// first. Charging its whole height to the row it starts in is the classic way to get one
    /// enormous row, which is why the spanning cells are settled after the single-row ones.
    /// </summary>
    [AvaloniaFact]
    public void ATallSpanningCellDoesNotStretchTheRowItStartsIn()
    {
        var tall = string.Join(" ", Enumerable.Repeat("word", 60));

        var spanned = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText(tall)], rowSpan: 2),
                TableCellNode.FromText("b1"),
            ]),
            new TableRowNode([TableCellNode.FromText("b2")]),
        ]);

        var view = Measured(spanned);

        // Where the second row starts is where the first row ended. The first row holds one
        // short line, so it must be a small fraction of the table however tall the spanning
        // cell is - if its height were charged to the first row, this would be most of the way
        // down instead.
        var second = view.GetCaretRect(StartOf(spanned, "b2"));

        Assert.True(
            second.Y < view.Size.Height / 2,
            $"the second row started at {second.Y} of {view.Size.Height}");
    }

    [AvaloniaFact]
    public void ATableWithMoreTextIsTallerThanOneWithLess()
    {
        var wordy = string.Join(" ", Enumerable.Repeat("word", 80));

        var small = Measured(Grid());
        var large = Measured(new TableNode([
            new TableRowNode([TableCellNode.FromText(wordy), TableCellNode.FromText("b1")]),
        ]));

        Assert.True(
            large.Size.Height > small.Size.Height,
            $"a wrapped cell measured {large.Size.Height}, two short rows {small.Size.Height}");
    }

    [AvaloniaFact]
    public void ATableActuallyPaints()
    {
        var view = new RichTextView
        {
            State = EditorState.Create(new DocumentNode([Grid()])),
            TableBorderBrush = Brushes.Red,
        };

        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing rendered");

        var blank = new Window { Width = 400, Height = 300 };

        blank.Show();
        blank.Measure(new Size(400, 300));
        blank.Arrange(new Rect(0, 0, 400, 300));

        using var empty = blank.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing rendered");

        Assert.NotEqual(Bytes(empty), Bytes(frame));
    }

    /// <summary>
    /// A header cell is tinted so a header row looks like one, and a background the document
    /// states beats the tint — the document was explicit, the tint is only a default.
    /// </summary>
    [AvaloniaFact]
    public void AHeaderCellIsTintedUnlessItStatesItsOwnBackground()
    {
        var plain = new TableNode([new TableRowNode([
            new TableCellNode([ParagraphNode.FromText("h")], isHeader: true),
        ])]);

        var stated = new TableNode([new TableRowNode([
            new TableCellNode(
                [ParagraphNode.FromText("h")],
                background: new Rgba(0x00, 0x80, 0x00, 0xFF),
                isHeader: true),
        ])]);

        Assert.NotEqual(Painted(plain, null), Painted(plain, Brushes.Red));
        Assert.Equal(Painted(stated, null), Painted(stated, Brushes.Red));
    }

    private static byte[] Painted(TableNode table, IBrush? header)
    {
        var view = new RichTextView
        {
            State = EditorState.Create(new DocumentNode([table])),
            TableHeaderBrush = header,
        };

        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing rendered");

        return Bytes(frame);
    }

    private static byte[] Bytes(global::Avalonia.Media.Imaging.Bitmap bitmap)
    {
        using var stream = new MemoryStream();

        bitmap.Save(stream, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    /// <summary>
    /// The seam the editing layer leans on. If this answered the wrong paragraph, typing would
    /// go into the wrong cell while the caret looked right.
    /// </summary>
    [AvaloniaFact]
    public void TheParagraphAtAPositionIsTheOneThatPositionIsIn()
    {
        var table = Grid();
        var view = Measured(table);

        foreach (var text in (string[])["a1", "b1", "a2", "b2"])
        {
            var at = view.ParagraphAt(StartOf(table, text) + 1);

            Assert.NotNull(at);
            Assert.Equal(text, at!.Value.View.Paragraph.Content.Text);
            Assert.Equal(StartOf(table, text), at.Value.Start);
        }
    }

    [AvaloniaFact]
    public void TheParagraphOriginIsWhereThatParagraphIsDrawn()
    {
        var table = Grid();
        var view = Measured(table);

        var at = view.ParagraphAt(StartOf(table, "b2"))!.Value;
        var caret = view.GetCaretRect(StartOf(table, "b2"));

        // The caret rectangle is the paragraph's own, shifted by where the paragraph sits, so
        // the two have to agree about where that is.
        Assert.Equal(caret.X, at.Origin.X + at.View.GetCaretRect(0).X, 3);
        Assert.Equal(caret.Y, at.Origin.Y + at.View.GetCaretRect(0).Y, 3);
    }
    /// <summary>
    /// An empty cell is not schema-valid, but the schema reports rather than throws and nothing
    /// stops a caller constructing one, so it must not take the view down with it.
    /// </summary>
    [AvaloniaFact]
    public void AnEmptyCellDoesNotCrashTheView()
    {
        var table = new TableNode([
            new TableRowNode([new TableCellNode([]), TableCellNode.FromText("b1")]),
        ]);

        var view = Measured(table);

        // The row opens at 0 so its content starts at 1; the empty cell opens there, so its own
        // content starts at 2. With nothing inside it that is the only position it can offer.
        Assert.Equal(2, view.HitTest(new Point(20, 10)));
        Assert.Null(view.ParagraphAt(2));
    }
}