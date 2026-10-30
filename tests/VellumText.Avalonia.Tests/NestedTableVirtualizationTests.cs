using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Row virtualization for tables inside table cells, one, two and three deep (rubber-duck review
/// finding 10.4, owner decision Option A): bounded layout, nothing blank, nothing sliding, and
/// every question about a far row answered.
/// </summary>
/// <remarks>
/// Laid-out geometry is what is asserted — rows on screen laid out, text where a fully laid-out
/// copy has it, lines that do not move — not the windows the tables passed each other.
/// </remarks>
public class NestedTableVirtualizationTests
{
    private const double Width = 500;
    private const double Height = 400;

    // ---- bounded work -------------------------------------------------------------------------

    /// <summary>Laying out the top of a long table in a cell measures what is on screen, however long it is.</summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LayingOutANestedLongTableRealizesOnlyTheRowsOnScreen(int depth)
    {
        var built = new List<long>();
        var measured = Growth.Measure(
            [2_500, 5_000, 10_000],
            n =>
            {
                var doc = Nested(depth, Uniform(n));

                WorkCounters.TableCellsMeasured = 0;
                WorkCounters.TableCellsBuilt = 0;
                WorkCounters.NestedWindowFallbacks = 0;

                Shown(doc);

                built.Add(WorkCounters.TableCellsBuilt);
                Assert.Equal(0, WorkCounters.NestedWindowFallbacks);

                return WorkCounters.TableCellsMeasured;
            });

        Growth.AssertFlat(measured, $"cells measured laying out the top of a long table {depth} deep");
        Growth.AssertFlat([.. built], $"cells built laying out the top of a long table {depth} deep");
    }

    /// <summary>
    /// Laying out a 10,000-row table in a cell allocates about what the same table does at the top
    /// of the document; it allocated 26 times as much when every row of it was laid out.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void LayingOutANestedTenThousandRowTableAllocatesLikeATopLevelOne(int depth)
    {
        var top = FirstLayoutBytes(Nested(0, Uniform(10_000)));
        var nested = FirstLayoutBytes(Nested(depth, Uniform(10_000)));

        Assert.True(
            nested <= (top * 1.25) + 256_000,
            $"laying out a table {depth} deep allocated {nested:N0} bytes against {top:N0} at the top");

        static long FirstLayoutBytes(DocumentNode doc)
        {
            Shown(Nested(0, Uniform(50)));

            var before = GC.GetAllocatedBytesForCurrentThread();

            Shown(doc);

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    /// <summary>A narrower window lays out again only the rows on screen, at any length.</summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void ResizingANestedLongTableMeasuresOnlyTheWindow(int depth)
    {
        var bytes = 0L;
        var measured = Growth.Measure(
            [2_500, 5_000, 10_000],
            n =>
            {
                var (window, _) = Shown(Nested(depth, Uniform(n)));

                WorkCounters.TableCellsMeasured = 0;

                var before = GC.GetAllocatedBytesForCurrentThread();

                window.Width = Width - 120;
                Layout(window);

                bytes = Math.Max(bytes, GC.GetAllocatedBytesForCurrentThread() - before);

                return WorkCounters.TableCellsMeasured;
            });

        Growth.AssertFlat(measured, $"cells measured by a resize of a long table {depth} deep");
        Assert.True(bytes <= 2_000_000, $"a resize allocated {bytes:N0} bytes");
    }

    // What the first layout keeps alive is asserted in a child process, by
    // NestedTableLayoutProbeTests (retainedBytes, depths 1-3, same bound against the top level):
    // GC.GetTotalMemory is process-wide, and measured here it counted whatever tests in other
    // classes allocated meanwhile, which failed a correct layout under the full suite.

    /// <summary>
    /// Text beside a long table in a cell, a few lines of it, does not make the table be laid out
    /// down to the viewport: it provably ends far above.
    /// </summary>
    [AvaloniaFact]
    public void ShortCellsBesideANestedTableAreBounded()
    {
        var measured = Growth.Measure(
            [2_500, 5_000, 10_000],
            n =>
            {
                var doc = new DocumentNode([
                    new TableNode([new TableRowNode([
                        new TableCellNode([Uniform(n)]),
                        TableCellNode.FromText("A short note beside the table."),
                    ])]),
                    ParagraphNode.FromText("After."),
                ]);
                var (window, view) = Shown(doc);

                WorkCounters.TableCellsMeasured = 0;
                view.Offset = new Vector(0, view.Blocks[0].Size.Height / 2);
                Layout(window);

                return WorkCounters.TableCellsMeasured;
            });

        Growth.AssertFlat(measured, "cells measured scrolling to the middle of a table beside a short cell");
    }

    /// <summary>
    /// Scrolling page by page through a long table in a cell with text beside it: what is
    /// examined to keep the text beside it still is a screenful per page, not the table's length
    /// nor the length of what has been laid out above. Walking either on every pass made
    /// scrolling through the table quadratic.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void ScrollingThroughALongNestedTableBesideTextExaminesAScreenfulPerPage(int depth)
    {
        var measured = Growth.Measure(
            [2_500, 5_000, 10_000],
            n =>
            {
                var (window, view) = Shown(Nested(depth, Uniform(n), outerRows: 3, hostRow: 1));

                // A fifth of it read page by page from the top first, so everything above is laid out.
                ToShare(window, view, 0);

                while (view.Offset.Y < view.Blocks[0].Size.Height / 5)
                {
                    view.Offset += new Vector(0, Height);
                    Layout(window);
                }

                WorkCounters.NestedBoundBands = 0;

                for (var page = 0; page < 20; page++)
                {
                    view.Offset += new Vector(0, Height);
                    Layout(window);
                }

                AssertScreenLaidOut(view);

                return WorkCounters.NestedBoundBands;
            });

        Growth.AssertFlat(measured, $"bands examined scrolling 20 pages through a long table {depth} deep beside text");
    }

    /// <summary>
    /// A long table laid out whole beside the table the viewport is held in: scrolling twenty
    /// pages through the held table examines a screenful per page however long the finished table
    /// beside it is, and the text in both moves by exactly the scroll and stays where a fully
    /// laid-out copy has it. Asking whether the finished table was finished walked every row of it
    /// on every pass.
    /// </summary>
    [AvaloniaFact]
    public void ScrollingBesideALongTableLaidOutWholeExaminesAScreenfulPerPage()
    {
        var measured = Growth.Measure(
            [2_500, 5_000, 10_000],
            n =>
            {
                var doc = new DocumentNode([
                    new TableNode([new TableRowNode([
                        new TableCellNode([Uniform(1_000, tag: "a")]),
                        new TableCellNode([Uniform(n, tag: "b")]),
                    ])]),
                    ParagraphNode.FromText("After."),
                ]);
                var (window, view) = Shown(doc);
                var beside = ((TableBlockView)view.Blocks[0]).NestedTables.Last();
                var end = TextAt(doc, "a r999c0");

                // The end of the held table on screen: the row's bottom could be there, so the
                // table beside it is laid out whole (decision D2).
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, end) - (Height / 2));
                    Layout(window);
                }

                Assert.Equal(n, beside.RealizedRows);

                view.Offset = new Vector(0, Height);
                Layout(window);

                ResetCounters();
                WorkCounters.NestedBoundBands = 0;

                for (var page = 0; page < 20; page++)
                {
                    var a = view.PositionAt(new Point(Width * 0.25, Height / 2));
                    var b = view.PositionAt(new Point(Width * 0.75, Height / 2));
                    var (ya, yb) = (ScreenTop(view, a), ScreenTop(view, b));

                    view.Offset += new Vector(0, Height);
                    Layout(window);

                    Assert.True(Math.Abs(ScreenTop(view, a) - (ya - Height)) <= 1, $"page {page}: the held table's line moved from {ya:F1} to {ScreenTop(view, a):F1}");
                    Assert.True(Math.Abs(ScreenTop(view, b) - (yb - Height)) <= 1, $"page {page}: the line beside moved from {yb:F1} to {ScreenTop(view, b):F1}");
                    AssertScreenLaidOut(view);
                }

                var examined = WorkCounters.NestedBoundBands;

                AssertNoRepairs();

                var twin = GeometryTransactionTests.FullyLaidOut(doc);
                var (left, right) = (view.PositionAt(new Point(Width * 0.25, Height / 2)), view.PositionAt(new Point(Width * 0.75, Height / 2)));

                Assert.Equal(
                    ScreenTop(twin, right) - ScreenTop(twin, left),
                    ScreenTop(view, right) - ScreenTop(view, left),
                    1.0);

                return examined;
            });

        Growth.AssertFlat(measured, "bands examined scrolling 20 pages beside a long table laid out whole");
    }

    /// <summary>
    /// A short table beside the held one, laid out whole and ending above the viewport, is made
    /// much taller by an edit far up it: it now reaches into the viewport, and is drawn there where
    /// a fully laid-out copy has it — not left at the height it had, as a table that had been found
    /// complete once and was trusted to still be would be.
    /// </summary>
    [AvaloniaFact]
    public void AnEditFarUpAFinishedTableBesideTheHeldOneShowsItWhereItReallyIs()
    {
        var (window, view) = BesideAFinishedTable();

        Place(window, view);

        var top = view.PositionAt(new Point(20, 2));
        var text = Paragraph(view.State.Doc, top);
        var into = top - TextAt(view.State.Doc, text);
        var y = ScreenTop(view, top);

        ResetCounters();

        view.Apply(view.State.Transaction().InsertText(TextAt(view.State.Doc, "b r2c0") + 6, string.Concat(Enumerable.Repeat(" more words in the cell", 60))));
        Layout(window);

        AssertLineAt(view, text, into, y);
        AssertPlacedAsInTwin(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc), TextAt(view.State.Doc, text) + into);
        Assert.True(ScreenTop(view, TextAt(view.State.Doc, "b r39c0")) > 0, "the edited table does not reach into the viewport");
        AssertNoRepairs();
    }

    /// <summary>
    /// The same finished table made taller by a composition far up it rather than an edit: drawn
    /// where a fully laid-out copy composing the same text has it.
    /// </summary>
    [AvaloniaFact]
    public void ComposingFarUpAFinishedTableBesideTheHeldOneShowsItWhereItReallyIs()
    {
        var (window, view) = BesideAFinishedTable();
        var offset = view.Offset;
        var caret = TextAt(view.State.Doc, "b r2c0") + 6;
        var composed = string.Concat(Enumerable.Repeat(" more words in the cell", 60));

        view.MoveTo(caret, extend: false);
        Layout(window);
        view.Offset = offset;
        Layout(window);

        var top = view.PositionAt(new Point(20, 2));
        var y = ScreenTop(view, top);

        ResetCounters();

        Assert.True(view.SetPreedit(composed));
        Layout(window);

        Assert.True(Math.Abs(ScreenTop(view, top) - y) <= 1, $"the line at the top is drawn at {ScreenTop(view, top):F1}, not {y:F1}");

        var twin = GeometryTransactionTests.FullyLaidOut(view.State.Doc);

        twin.MoveTo(caret, extend: false);
        Assert.True(twin.SetPreedit(composed));
        twin.Measure(new Size(Width, double.PositiveInfinity));
        twin.Arrange(new Rect(0, 0, Width, twin.DesiredSize.Height));

        AssertPlacedAsInTwin(view, twin, top);
        Assert.True(ScreenTop(view, TextAt(view.State.Doc, "b r39c0")) > 0, "the composing table does not reach into the viewport");

        // The caret leaves for the held table on screen, which ends the composition: the finished
        // table is as short as it was, and drawn so.
        view.MoveTo(top, extend: false);
        Layout(window);

        Assert.False(view.IsComposing);
        Assert.True(Math.Abs(ScreenTop(view, top) - y) <= 1, $"the line at the top is drawn at {ScreenTop(view, top):F1}, not {y:F1}");

        var cleared = GeometryTransactionTests.FullyLaidOut(view.State.Doc);
        var last = TextAt(view.State.Doc, "b r39c0");

        AssertPlacedAsInTwin(view, cleared, top);
        Assert.Equal(ScreenTop(cleared, last) - ScreenTop(cleared, top), ScreenTop(view, last) - ScreenTop(view, top), 1.0);
        AssertNoRepairs();
    }

    /// <summary>
    /// A long table held at the top of the viewport and, beside it, a short one laid out whole whose
    /// last row is two hundred pixels above the viewport.
    /// </summary>
    private static (Window Window, RichTextView View) BesideAFinishedTable()
    {
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([Uniform(600, tag: "a")]),
                new TableCellNode([Uniform(40, tag: "b")]),
            ])]),
            ParagraphNode.FromText("After."),
        ]);
        var (window, view) = Shown(doc);
        var last = TextAt(doc, "b r39c0");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, last) + 200);
            Layout(window);
        }

        return (window, view);
    }

    // ---- nothing blank ------------------------------------------------------------------------

    /// <summary>
    /// Scrolling into the middle of a long table in a cell whose first rows teach an estimate far
    /// too short: every row on screen, at every depth, is laid out before it is drawn, and drawing
    /// moves no height. The Phase 9 bug of the top-level table, pinned for nested ones.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ScrollingAmongNestedRowsTallerThanTheEstimateMeasuresAndDrawsTheRowsOnScreen(int depth)
    {
        var (window, view) = Shown(Nested(depth, GeometryTransactionTests.TallAfterShort(2_000), outerRows: 200, hostRow: 120));

        ResetCounters();

        foreach (var share in new[] { 0.3, 0.5, 0.7 })
        {
            ToShare(window, view, share);

            AssertScreenLaidOut(view);
        }

        var height = view.Blocks[0].Size.Height;

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        Assert.Equal(height, view.Blocks[0].Size.Height);
        AssertNoRepairs();
    }

    /// <summary>
    /// A long table below six hundred pixels of text in its cell: its window starts where it does,
    /// not at the top of the cell, so the rows on screen are the rows laid out.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void ANestedTableBelowTallCellContentIsCoveredOnScreen(int depth)
    {
        var lead = Enumerable.Range(0, 30).Select(i => (BlockNode)ParagraphNode.FromText($"Lead paragraph {i} above the table in the cell."));
        var cell = new TableCellNode([.. lead, Uniform(3_000)]);
        BlockNode content = new TableNode([new TableRowNode([cell])]);

        for (var d = 1; d < depth; d++)
        {
            content = new TableNode([new TableRowNode([new TableCellNode([content])])]);
        }

        var (window, view) = Shown(new DocumentNode([content, ParagraphNode.FromText("After.")]));

        ResetCounters();

        foreach (var y in new[] { 300.0, 650, 2_000, 9_000 })
        {
            view.Offset = new Vector(0, y);
            Layout(window);

            AssertScreenLaidOut(view);
        }

        AssertNoRepairs();
    }

    /// <summary>
    /// An outer row inserted above a row holding a long table carries that row and its table
    /// across the rebuild; scrolling into the table afterwards still lays out only the rows on
    /// screen.
    /// </summary>
    [AvaloniaFact]
    public void AnOuterStructuralEditCarriesANestedTableThatStaysVirtualized()
    {
        var doc = Nested(1, Uniform(3_000), outerRows: 40, hostRow: 30);
        var (window, view) = Shown(doc);
        var outer = (TableBlockView)view.Blocks[0];

        ToShare(window, view, 0.5);

        var table = (TableNode)view.State.Doc.Blocks[0];

        // A row inserted at the top, which changes the grid's shape and so rebuilds the rows,
        // carrying the band that holds the long table.
        view.MoveTo(TextAt(doc, "outer 0") + 1, extend: false);
        Assert.True(view.InsertRowAbove());
        Layout(window);

        ResetCounters();

        foreach (var share in new[] { 0.4, 0.6, 0.8 })
        {
            ToShare(window, view, share);

            AssertScreenLaidOut(view);
        }

        Assert.InRange(outer.NestedTables.Single().RealizedRows, 1, 400);
        AssertNoRepairs();
        Assert.Equal(table.Rows.Length + 1, ((TableNode)view.State.Doc.Blocks[0]).Rows.Length);
    }

    /// <summary>
    /// Two long tables side by side in one row: both are laid out where the viewport shows them —
    /// the one the viewport is not held by down from its top (owner decision Q1) — and questions
    /// about far rows of either move nothing on screen in either.
    /// </summary>
    [AvaloniaFact]
    public void SideBySideLongCellsStayStillAndCovered()
    {
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([Uniform(1_500)]),
                new TableCellNode([Uniform(1_500, tag: "b")]),
            ])]),
            ParagraphNode.FromText("After."),
        ]);
        var (window, view) = Shown(doc);

        ToShare(window, view, 0.5);
        ResetCounters();

        var left = view.PositionAt(new Point(Width * 0.25, Height / 2));
        var right = view.PositionAt(new Point(Width * 0.75, Height / 2));
        var (l, r) = (ScreenTop(view, left), ScreenTop(view, right));

        AssertScreenLaidOut(view);

        foreach (var far in new[] { "r1400c0", "b r1400c0", "r3c1", "b r3c1" })
        {
            _ = view.ImageRect(TextAt(view.State.Doc, far));
            Layout(window);

            Assert.Equal(l, ScreenTop(view, left), 1);
            Assert.Equal(r, ScreenTop(view, right), 1);
            AssertScreenLaidOut(view);
        }

        AssertNoRepairs();
    }

    /// <summary>
    /// A cell's table, short by its estimate, holds a row two thousand pixels tall that no layout
    /// has reached, and so really reaches the viewport beside the text the viewport is held on:
    /// it is laid out there by the layout pass, where a fully laid-out copy has it, rather than
    /// appearing later when something lays the row out (decision D1: potential reach).
    /// </summary>
    [AvaloniaFact]
    public void AShortLookingSiblingThatReachesTheViewportIsLaidOutWhereItReallyIs()
    {
        var doc = ReachingSibling();
        var (window, view) = Shown(doc);
        var twin = GeometryTransactionTests.FullyLaidOut(doc);

        // Below where the table's estimate says it ends, by more than the overscan, and beside the
        // tall row in truth.
        view.Offset = new Vector(0, 2_500);
        Layout(window);

        AssertTruthOnScreenIsLaidOut(view, twin);
        AssertScreenLaidOut(view);
        Assert.Equal(0, WorkCounters.NestedWindowFallbacks);
    }

    /// <summary>
    /// The anchored cell is the tallest by every estimate; beside it, a table whose rows the first
    /// layout measured only partly holds one enormous row further down. With the row's bottom on
    /// screen, that table is laid out whole so the bottom is where it really is (decision D2), and
    /// a question about the enormous row moves nothing on screen afterwards.
    /// </summary>
    [AvaloniaFact]
    public void AnInvisibleSiblingThatOutgrowsTheAnchoredCellDoesNotMoveFollowingRows()
    {
        var paragraphs = Enumerable.Range(0, 100).Select(i => (BlockNode)ParagraphNode.FromText($"Cell A paragraph {i}."));
        var sibling = new TableNode(Enumerable.Range(0, 44).Select(r => new TableRowNode([
            r == 40
                ? new TableCellNode(Enumerable.Range(0, 100).Select(i => (BlockNode)ParagraphNode.FromText($"tall line {i}")))
                : TableCellNode.FromText($"s{r}"),
        ])));
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([new TableCellNode([.. paragraphs]), new TableCellNode([sibling])]),
                new TableRowNode([TableCellNode.FromText("Following row text"), TableCellNode.FromText("x")]),
                new TableRowNode([TableCellNode.FromText("Another following row"), TableCellNode.FromText("y")]),
            ]),
            ParagraphNode.FromText("After."),
        ]);
        var (window, view) = Shown(doc);
        var twin = GeometryTransactionTests.FullyLaidOut(doc);
        var last = TextAt(doc, "Cell A paragraph 99.");

        // The end of cell A at the middle of the viewport: the row's bottom could be on screen.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, last) - (Height / 2));
            Layout(window);
        }

        AssertTruthOnScreenIsLaidOut(view, twin);

        var following = TextAt(doc, "Following row text");
        var y = ScreenTop(view, following);

        ResetCounters();

        _ = view.ImageRect(TextAt(doc, "tall line 50"));
        Layout(window);

        Assert.Equal(y, ScreenTop(view, following), 1);
        AssertScreenLaidOut(view);
        AssertNoRepairs();
    }

    /// <summary>
    /// A jump far into a long table in a cell, beside a long column of text: the text beside is
    /// drawn where a fully laid-out copy draws it, though the rows above the point jumped to were
    /// priced by an estimate the short rows at the table's top make wrong — the anchor's depth in
    /// the row is made exact, because the text beside hangs from the row's top. One table deep and,
    /// inside one-row tables, three.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void AJumpBesideALongColumnOfTextDrawsTheTextWhereItReallyIs(int depth)
    {
        BlockNode content = new TableNode([new TableRowNode([
            new TableCellNode([Irregular(400)]),
            new TableCellNode([.. Enumerable.Range(0, 2_000).Select(i => (BlockNode)ParagraphNode.FromText($"Beside {i}."))]),
        ])]);

        for (var d = 1; d < depth; d++)
        {
            content = new TableNode([new TableRowNode([new TableCellNode([content])])]);
        }

        var doc = new DocumentNode([content, ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var twin = GeometryTransactionTests.FullyLaidOut(doc);

        ResetCounters();

        view.Offset = new Vector(0, 20_000);
        Layout(window);

        AssertTruthOnScreenIsLaidOut(view, twin);
        AssertNoRepairs();
    }

    /// <summary>
    /// The viewport is held inside a table in a cell, near its end, and the cell beside is a
    /// little taller in truth: the row's bottom, and the row after it on screen, hang from that
    /// taller cell, so the depth of the anchor in the row is made exact and the row after is laid
    /// out where a fully laid-out copy has it (decision D2) — not where the estimate of the rows
    /// above the anchor would put it, far below the viewport.
    /// </summary>
    [AvaloniaFact]
    public void HeldInATableBesideATallerCellTheRowAfterIsLaidOutWhereItReallyIs()
    {
        static DocumentNode Doc(int paragraphs) => new([
            new TableNode([
                new TableRowNode([
                    new TableCellNode([GeometryTransactionTests.TallAfterShort(60)]),
                    new TableCellNode([.. Enumerable.Range(0, paragraphs).Select(i => (BlockNode)ParagraphNode.FromText($"Cell B paragraph {i}."))]),
                ]),
                new TableRowNode([TableCellNode.FromText("Following row text"), TableCellNode.FromText("x")]),
                new TableRowNode([TableCellNode.FromText("Another following row"), TableCellNode.FromText("y")]),
            ]),
            ParagraphNode.FromText("After."),
        ]);

        // Cell B a hundred and fifty pixels taller than the table beside it, in truth.
        var alone = GeometryTransactionTests.FullyLaidOut(Doc(0));
        var tableHeight = ((TableBlockView)alone.Blocks[0]).NestedTables.Single().Size.Height;
        var sample = GeometryTransactionTests.FullyLaidOut(Doc(100));
        var paragraph = (ScreenTop(sample, TextAt(Doc(100), "Cell B paragraph 99.")) - ScreenTop(sample, TextAt(Doc(100), "Cell B paragraph 0."))) / 99;
        var doc = Doc((int)Math.Ceiling((tableHeight + 150) / paragraph));
        var twin = GeometryTransactionTests.FullyLaidOut(doc);
        var following = TextAt(doc, "Following row text");

        // In truth, the row after is three hundred pixels down the viewport; the content at the top
        // is in the table's last rows.
        var top = ScreenTop(twin, following) - 300;
        var held = twin.PositionAt(new Point(20, top + 2));

        Assert.StartsWith("row 5", Paragraph(doc, held), StringComparison.Ordinal);

        var (window, view) = Shown(doc);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, held) - (ScreenTop(twin, held) - top));
            Layout(window);
        }

        AssertTruthOnScreenIsLaidOut(view, twin);
        Assert.Equal(300, ScreenTop(view, following), 1);
        Assert.Equal(0, WorkCounters.NestedWindowFallbacks);
    }

    /// <summary>
    /// The overscan above the viewport reaches the bottom of a long table above the text at the
    /// top — in the same cell, below thirty paragraphs, or in the row above — and its last rows
    /// there are laid out, measured from the table's real bottom edge.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheOverscanAboveTheViewportCoversTheBottomOfANestedTableAboveIt(bool sameCell)
    {
        var lead = Enumerable.Range(0, 30).Select(i => (BlockNode)ParagraphNode.FromText($"Lead paragraph {i} above the table."));
        var after = Enumerable.Range(0, 60).Select(i => (BlockNode)ParagraphNode.FromText($"after {i}"));
        var outer = sameCell
            ? new TableNode([new TableRowNode([new TableCellNode([.. lead, Uniform(300), .. after])])])
            : new TableNode([
                new TableRowNode([new TableCellNode([.. lead, Uniform(300)])]),
                new TableRowNode([new TableCellNode([.. after])]),
            ]);
        var doc = new DocumentNode([outer, ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var text = TextAt(doc, "after 2");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, text) - 2);
            Layout(window);
        }

        var table = (TableBlockView)view.Blocks[0];
        var top = -view.BlockOrigin(0).Y;
        var bottom = ScreenTop(view, TextAt(doc, "r299c0")) - view.BlockOrigin(0).Y;

        // The table's last row ends within the overscan above the viewport.
        Assert.InRange(top - bottom, 0, 150);
        Assert.True(table.Covers(top - 150, top + Height), "rows of the table in the overscan above the viewport are not laid out");
    }

    /// <summary>
    /// Jumping to the middle of an outer row nobody has laid out, in a table of twenty rows each
    /// holding a long table: the table in it is measured outward from the point jumped to, not
    /// from its top down to there.
    /// </summary>
    [AvaloniaFact]
    public void JumpingIntoTheMiddleOfARowNobodyLaidOutMeasuresAScreenfulOfItsTable()
    {
        var doc = new DocumentNode([
            new TableNode(Enumerable.Range(0, 20).Select(r => new TableRowNode([
                new TableCellNode([Uniform(2_000, tag: $"t{r}")]),
                TableCellNode.FromText($"side {r}"),
            ]))),
            ParagraphNode.FromText("After."),
        ]);
        var (window, view) = Shown(doc);

        ResetCounters();

        view.Offset = new Vector(0, view.Blocks[0].Size.Height * 10.5 / 20);
        Layout(window);

        AssertScreenLaidOut(view);
        Assert.True(WorkCounters.TableCellsMeasured < 400, $"the jump measured {WorkCounters.TableCellsMeasured} cells");
        Assert.Equal(0, WorkCounters.NestedWindowFallbacks);
    }

    // ---- nothing sliding ----------------------------------------------------------------------

    /// <summary>
    /// Scrolling down step by step through nested rows taller than the estimate the first rows
    /// taught: the text in the middle of the screen moves by exactly the scroll, however much
    /// the estimates below it change as the rows coming on screen are measured.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ScrollingThroughNestedRowsMovesTheScreenByTheScroll(int depth)
    {
        var (window, view) = Shown(Nested(depth, GeometryTransactionTests.TallAfterShort(2_000), outerRows: 200, hostRow: 120));

        ToShare(window, view, 0.4);
        ResetCounters();

        for (var step = 0; step < 25; step++)
        {
            var probe = view.PositionAt(new Point(20, Height / 2));
            var y = ScreenTop(view, probe);

            view.Offset += new Vector(0, 60);
            Layout(window);

            Assert.True(Math.Abs(ScreenTop(view, probe) - (y - 60)) <= 1, $"step {step}: the middle line moved from {y:F1} to {ScreenTop(view, probe):F1}, not {y - 60:F1}");
            AssertScreenLaidOut(view);
        }

        AssertNoRepairs();
    }

    /// <summary>
    /// A narrower and then a wider window keep the line at the top of a nested table's viewport
    /// where it was (owner decision Q3), and lay out only what is on screen.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ResizingKeepsTheLineAtTheTopOfANestedTableStill(int depth)
    {
        var (window, view) = Shown(Nested(depth, GeometryTransactionTests.TallAfterShort(3_000), outerRows: 50, hostRow: 25));

        ToShare(window, view, 0.4);

        var top = view.PositionAt(new Point(20, 2));
        var text = Paragraph(view.State.Doc, top);
        var into = top - TextAt(view.State.Doc, text);
        var y = ScreenTop(view, top);

        ResetCounters();

        foreach (var width in new[] { Width - 120, Width + 200, Width })
        {
            window.Width = width;
            Layout(window);

            AssertLineAt(view, text, into, y);
        }

        Assert.True(WorkCounters.TableCellsMeasured < 3_000, $"resizing measured {WorkCounters.TableCellsMeasured} cells");
        AssertNoRepairs();
    }

    /// <summary>
    /// Text typed into far rows of a nested table, above the viewport and below it, makes those
    /// rows taller: the line at the top of the viewport stays where it is.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EditingFarNestedRowsKeepsTheScreenStill(int depth)
    {
        var (window, view) = Shown(Nested(depth, Uniform(3_000), outerRows: 20, hostRow: 10));

        ToShare(window, view, 0.5);
        Place(window, view);

        var top = view.PositionAt(new Point(20, 2));
        var text = Paragraph(view.State.Doc, top);
        var into = top - TextAt(view.State.Doc, text);
        var y = ScreenTop(view, top);
        var words = string.Concat(Enumerable.Repeat(" more words in the cell", 30));

        ResetCounters();

        foreach (var far in new[] { "r2900c1", "r40c1", "r2950c0", "r10c2" })
        {
            view.Apply(view.State.Transaction().InsertText(TextAt(view.State.Doc, far) + far.Length, words));
            Layout(window);

            AssertLineAt(view, text, into, y);
        }

        AssertNoRepairs();
    }

    // ---- caret, selection, hit-test -----------------------------------------------------------

    /// <summary>The caret goes to far rows of a table nested three deep and is drawn on screen there.</summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void TheCaretGoesToFarNestedRowsAndIsShownThere(int depth)
    {
        var doc = Nested(depth, GeometryTransactionTests.TallAfterShort(3_000), outerRows: 50, hostRow: 25);
        var (window, view) = Shown(doc);

        ResetCounters();

        foreach (var row in new[] { 900, 2_500, 40, 2_999 })
        {
            view.MoveTo(TextAt(doc, $"r{row}c1") + 1, extend: false);
            Layout(window);

            Assert.InRange(view.CaretRect().Top, 0, Height);
            AssertScreenLaidOut(view);
        }

        AssertNoRepairs();
    }

    /// <summary>A click on a cell far down a nested table lands in that cell.</summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void AClickOnAFarNestedRowLandsInItsCell(int depth)
    {
        var doc = Nested(depth, GeometryTransactionTests.TallAfterShort(3_000), outerRows: 50, hostRow: 25);
        var (window, view) = Shown(doc);

        view.MoveTo(TextAt(doc, "r2400c1") + 1, extend: false);
        Layout(window);

        var caret = view.CaretRect();
        var clicked = view.PositionAt(new Point(caret.X + 2, caret.Y + (caret.Height / 2)));

        Assert.Equal("r2400c1", Assert.IsType<ParagraphNode>(view.State.Doc.Resolve(clicked).Parent).Content.Text);
    }

    /// <summary>
    /// A selection across the whole of a long nested table is painted from the rows on screen; it
    /// does not lay out every row it covers in the middle of a frame.
    /// </summary>
    [AvaloniaFact]
    public void PaintingASelectionAcrossANestedTableRealizesOnlyTheRowsOnScreen()
    {
        var doc = Nested(1, Uniform(5_000));
        var (window, view) = Shown(doc);
        var outer = (TableBlockView)view.Blocks[0];

        view.MoveTo(TextAt(doc, "r0c0"), extend: false);
        view.MoveTo(TextAt(doc, "r4999c2") + 2, extend: true);
        view.Offset = default;
        Layout(window);

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        Assert.InRange(outer.NestedTables.Single().RealizedRows, 1, 200);
    }

    /// <summary>
    /// A question about a far row of a nested table, asked with no layout pass after it, leaves
    /// the screen as it was and the height every table above it and the presenter record matching
    /// what it now is.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void AFarNestedGeometryQueryRecordsTheNewHeightAllTheWayUpAndLeavesTheScreenStill(int depth)
    {
        var doc = Nested(depth, GeometryTransactionTests.TallAfterShort(2_000), outerRows: 200, hostRow: 120);
        var (window, view) = Shown(doc);
        var table = view.Blocks[0];

        ToShare(window, view, 0.4);

        var top = view.PositionAt(new Point(20, 2));
        var y = ScreenTop(view, top);
        var height = table.Size.Height;

        ResetCounters();

        Assert.Null(view.ImageRect(TextAt(doc, "r1900c1")));

        Assert.NotEqual(height, table.Size.Height);
        Assert.Equal(table.Size.Height, view.BlockOrigin(1).Y - view.BlockOrigin(0).Y, 6);
        Assert.Equal(y, ScreenTop(view, top), 1);

        // The extent is already what the next layout pass makes it.
        var extent = view.Extent.Height;

        view.InvalidateMeasure();
        Layout(window);

        Assert.Equal(extent, view.Extent.Height, 6);
    }

    // ---- find, replace, undo ------------------------------------------------------------------

    /// <summary>Find Next into a far row of a nested table shows the match laid out and highlighted.</summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void FindNextIntoAFarNestedRowShowsTheMatch(int depth)
    {
        var doc = Nested(depth, Uniform(10_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);
        var outer = (TableBlockView)view.Blocks[0];

        Assert.Equal(1, view.Find("r7500c2"));
        Assert.True(view.FindNext());
        Layout(window);

        Assert.InRange(view.CaretRect().Top, 0, Height);
        Assert.Equal("r7500c2", view.SelectedText());
        AssertScreenLaidOut(view);

        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }

        Assert.True(Innermost(outer, depth).RealizedRows < 600, "the far match realized most of the table");
    }

    /// <summary>
    /// Replacing every match in a long nested table lays out only what is on screen, wherever the
    /// replacement leaves the caret, and a far row scrolled to afterwards reads the replacement.
    /// </summary>
    [AvaloniaFact]
    public void ReplaceAllInANestedTableLaysOutOnlyWhatIsShown()
    {
        var doc = Nested(1, Uniform(3_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);

        ToShare(window, view, 0.5);

        ResetCounters();

        Assert.True(view.Find("c1") > 2_000);
        Assert.True(view.ReplaceAll("C1") > 2_000);
        Layout(window);

        Assert.True(WorkCounters.TableCellsMeasured < 2_000, $"replacing measured {WorkCounters.TableCellsMeasured} cells");
        AssertScreenLaidOut(view);

        ToShare(window, view, 0.25);

        // Across the middle row of the screen, the cells read what they were replaced with.
        var shown = Enumerable.Range(1, 9)
            .Select(i => Paragraph(view.State.Doc, view.PositionAt(new Point(Width * i / 10, Height / 2))))
            .ToList();

        Assert.Contains(shown, text => text.EndsWith("C1", StringComparison.Ordinal));
        Assert.DoesNotContain(shown, text => text.EndsWith("c1", StringComparison.Ordinal));
        AssertScreenLaidOut(view);
    }

    /// <summary>
    /// Rows inserted and deleted far above the viewport in a nested table, then undone and redone:
    /// the line at the top stays, and nothing on screen is left unlaid.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void StructuralEditsFarAboveInANestedTableUndoneAndRedoneLeaveTheScreenStill(int depth)
    {
        var doc = Nested(depth, Uniform(3_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);

        ToShare(window, view, 0.5);
        Place(window, view);

        var top = view.PositionAt(new Point(20, 2));
        var y = ScreenTop(view, top);
        var text = Paragraph(view.State.Doc, top);
        var into = top - TextAt(view.State.Doc, text);

        // Ten rows inserted near the top of the nested table, then five deleted.
        var at = TextAt(view.State.Doc, "r100c0") - 3;
        var tr = view.State.Transaction();

        tr.Replace(at, at, new Slice(Enumerable.Range(0, 10).Select(i => new TableRowNode([
            TableCellNode.FromText($"new {i} a"),
            TableCellNode.FromText($"new {i} b"),
            TableCellNode.FromText($"new {i} c"),
        ])), 0, 0));
        view.Apply(tr);
        Layout(window);
        AssertLineAt(view, text, into, y);

        var gone = TextAt(view.State.Doc, "r200c0") - 3;
        var rows = ((TableNode)Innermost(view.State.Doc, depth)).Rows;
        var span = Enumerable.Range(210, 5).Sum(i => rows[i].NodeSize);

        view.Apply(view.State.Transaction().Delete(gone, gone + span));
        Layout(window);
        AssertLineAt(view, text, into, y);

        Assert.True(view.Undo());
        Layout(window);
        AssertLineAt(view, text, into, y);

        Assert.True(view.Undo());
        Layout(window);
        AssertLineAt(view, text, into, y);

        Assert.True(view.Redo());
        Layout(window);
        AssertLineAt(view, text, into, y);

        AssertNoRepairs();
    }

    // ---- composition --------------------------------------------------------------------------

    /// <summary>
    /// Composing in a cell far down a table nested two and three deep, moving out of it and
    /// clearing the composition after the document moved it: every step answers where the caret
    /// is and draws, with nothing unlaid (the 10.1 lesson, nested and windowed).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(3)]
    public void ComposingInAFarNestedCellKeepsTheCellLaidOut(int depth)
    {
        var doc = Nested(depth, Uniform(3_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);
        var outer = (TableBlockView)view.Blocks[0];

        view.MoveTo(TextAt(doc, "r1500c1") + 2, extend: false);
        Layout(window);
        Draw(window, view);

        Assert.True(view.SetPreedit(new string('W', 80)));
        Draw(window, view);

        Assert.True(view.SetPreedit(new string('W', 160)));
        Draw(window, view);

        view.MoveTo(TextAt(view.State.Doc, "r1501c0") + 1, extend: false);
        Assert.False(view.IsComposing);
        Draw(window, view);

        Assert.True(Innermost(outer, depth).RealizedRows < 400);
    }

    // ---- fallback -----------------------------------------------------------------------------

    /// <summary>
    /// Windows that do not settle within the limit lay the table in the cell out whole — slow,
    /// counted and traced, and nothing on screen blank.
    /// </summary>
    [AvaloniaFact]
    public void ANestedTableWhoseWindowDoesNotSettleIsLaidOutInFull()
    {
        var table = new TableNode([new TableRowNode([new TableCellNode([GeometryTransactionTests.TallAfterShort(1_000)])])]);
        var view = new TableBlockView(table, CreateNoRefresh, Brushes.Gray) { RefreshLimit = 1 };
        var listener = new Captured();

        Trace.Listeners.Add(listener);

        try
        {
            WorkCounters.NestedWindowFallbacks = 0;

            view.SetWindow(3_000, 200, 600);
            view.Measure(Width);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        var nested = view.NestedTables.Single();

        Assert.Equal(1, WorkCounters.NestedWindowFallbacks);
        Assert.Contains("laid out in full (1000 rows)", listener.Text, StringComparison.Ordinal);
        Assert.Equal(1_000, nested.RealizedRows);
        Assert.True(view.Covers(2_800, 3_600));
    }

    private sealed class Captured : TraceListener
    {
        private readonly System.Text.StringBuilder _text = new();

        public string Text => _text.ToString();

        public override void Write(string? message) => _text.Append(message);

        public override void WriteLine(string? message) => _text.AppendLine(message);
    }

    private static readonly TextStyleResolver Styles = new(new Typeface("Segoe UI"), 14, Brushes.Black);

    private static BlockView CreateNoRefresh(BlockNode block) => block switch
    {
        ParagraphNode paragraph => new ParagraphView(paragraph, Styles),
        TableNode table => new TableBlockView(table, CreateNoRefresh, Brushes.Gray) { RefreshLimit = 1 },
        _ => throw new NotSupportedException(),
    };

    // ---- fixtures and checks ------------------------------------------------------------------

    /// <summary>A rows × 3 table of one-line cells reading <c>r{row}c{column}</c>.</summary>
    internal static TableNode Uniform(int rows, string? tag = null) =>
        new(Enumerable.Range(0, rows).Select(r => new TableRowNode(
            Enumerable.Range(0, 3).Select(c => TableCellNode.FromText(tag is null ? $"r{r}c{c}" : $"{tag} r{r}c{c}")))));

    /// <summary>
    /// A rows × 2 table whose rows are one to eleven lines tall in no simple order, so no estimate
    /// prices the rows nobody has measured at what they are.
    /// </summary>
    internal static TableNode Irregular(int rows, string? tag = null) =>
        new(Enumerable.Range(0, rows).Select(r => new TableRowNode([
            new TableCellNode([.. Enumerable.Range(0, 1 + ((r * 7) % 11)).Select(i => (BlockNode)ParagraphNode.FromText(i == 0 ? $"{tag}r{r}c0" : $"{tag}r{r} line {i}"))]),
            TableCellNode.FromText($"{tag}r{r}c1"),
        ])));

    /// <summary>
    /// <paramref name="inner"/> nested <paramref name="depth"/> deep: in the only cell of one-row
    /// tables, the outermost of which has <paramref name="outerRows"/> rows with the table in row
    /// <paramref name="hostRow"/>; then a paragraph.
    /// </summary>
    internal static DocumentNode Nested(int depth, TableNode inner, int outerRows = 1, int hostRow = 0)
    {
        BlockNode content = inner;

        for (var d = 1; d <= depth; d++)
        {
            content = d < depth || outerRows == 1
                ? new TableNode([new TableRowNode([new TableCellNode([content])])])
                : new TableNode(Enumerable.Range(0, outerRows).Select(r => new TableRowNode(r == hostRow
                    ? [new TableCellNode([content]), TableCellNode.FromText($"outer {r} side")]
                    : [TableCellNode.FromText($"outer {r}"), TableCellNode.FromText($"outer {r} side")])));
        }

        return new DocumentNode([content, ParagraphNode.FromText("After the tables.")]);
    }

    /// <summary>
    /// A row holding a long column of text, and beside it a table short by its estimate whose
    /// row 40 — past what the first layout reaches — is two thousand pixels tall.
    /// </summary>
    internal static DocumentNode ReachingSibling()
    {
        var paragraphs = Enumerable.Range(0, 200).Select(i => (BlockNode)ParagraphNode.FromText($"Cell A paragraph {i}."));
        var sibling = new TableNode(Enumerable.Range(0, 45).Select(r => new TableRowNode([
            r == 40
                ? new TableCellNode(Enumerable.Range(0, 100).Select(i => (BlockNode)ParagraphNode.FromText($"tall line {i}")))
                : TableCellNode.FromText($"s{r}"),
        ])));

        return new DocumentNode([
            new TableNode([new TableRowNode([new TableCellNode([.. paragraphs]), new TableCellNode([sibling])])]),
            ParagraphNode.FromText("After."),
        ]);
    }

    /// <summary>
    /// Everything a fully laid-out copy shows in the viewport is laid out in the view and drawn
    /// where the copy draws it (<see cref="AssertPlacedAsInTwin"/>), aligned on the content held
    /// at the top of the viewport — read from the view's last layout, and asked whether it is laid
    /// out before anything reads its geometry, so the check lays nothing out first.
    /// </summary>
    internal static void AssertTruthOnScreenIsLaidOut(RichTextView view, RichTextView twin)
    {
        var block = Enumerable.Range(view.RealizedBlocks.Start.Value, view.RealizedBlocks.End.Value - view.RealizedBlocks.Start.Value)
            .First(i => view.BlockOrigin(i).Y + view.Blocks[i].Size.Height > 0);
        var held = view.Blocks[block].CaptureAnchor(-view.BlockOrigin(block).Y);

        AssertPlacedAsInTwin(view, twin, view.Slots[block].Start + held.Position);
    }

    /// <summary>
    /// Everything a fully laid-out copy shows in the viewport is laid out in the view and drawn
    /// where the copy draws it: the copy is aligned on <paramref name="held"/>, a position on
    /// screen in the view, and every point of a grid over the viewport is looked up in the copy;
    /// the view must have that position laid out — asked before any geometry is read, so nothing
    /// is laid out by the check — and its caret there within a pixel of the copy's.
    /// </summary>
    internal static void AssertPlacedAsInTwin(RichTextView view, RichTextView twin, int held)
    {
        AssertPrepared(view, held, "the held position");

        var shift = Caret(twin, held).Top - Caret(view, held).Top;

        for (var y = 1.0; y < Height; y += Height / 16)
        {
            for (var x = 4.0; x < Width; x += Width / 8)
            {
                var position = twin.PositionAt(new Point(x, y + shift));

                AssertPrepared(view, position, $"position {position}, on screen at ({x:F0}, {y:F0}) in a fully laid-out copy,");

                var expected = Caret(twin, position).Translate(new Vector(0, -shift));

                // A point in the empty part of a cell beside a taller one hits the end of that
                // cell's text, which can be far above: only what the copy draws on screen is held
                // to where it draws it.
                if (expected.Bottom < 0 || expected.Top > Height)
                {
                    continue;
                }

                var actual = Caret(view, position);

                Assert.True(
                    Math.Abs(actual.Top - expected.Top) <= 1 && Math.Abs(actual.Left - expected.Left) <= 1,
                    $"position {position} is drawn at ({actual.Left:F1}, {actual.Top:F1}), not ({expected.Left:F1}, {expected.Top:F1}) as in a fully laid-out copy");
            }
        }

        static void AssertPrepared(RichTextView view, int position, string what)
        {
            var index = view.BlockIndexOf(position);

            Assert.True(view.Blocks[index].IsPreparedAt(position - view.Slots[index].Start), $"{what} is not laid out");
        }

        static Rect Caret(RichTextView view, int position)
        {
            var index = view.BlockIndexOf(position);

            return view.Blocks[index].GetCaretRect(position - view.Slots[index].Start).Translate((Vector)view.BlockOrigin(index));
        }
    }

    private static TableBlockView Innermost(TableBlockView outer, int depth)
    {
        var table = outer;

        for (var d = 0; d < depth; d++)
        {
            table = table.NestedTables.First();
        }

        return table;
    }

    private static Node Innermost(DocumentNode doc, int depth)
    {
        Node node = doc.Blocks[0];

        for (var d = 0; d < depth; d++)
        {
            node = ((TableNode)node).Rows.First(r => r.Cells.Any(c => c.Blocks[0] is TableNode)).Cells.First(c => c.Blocks[0] is TableNode).Blocks[0];
        }

        return node;
    }

    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        Layout(window);
        view.Focus();

        return (window, view);
    }

    private static void Layout(Window window) => GeometryTransactionTests.Layout(window);

    private static void Draw(Window window, RichTextView view)
    {
        _ = view.CaretRect();

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }

    /// <summary>Puts the caret in the middle of the viewport, so edits that follow scroll nothing into view.</summary>
    private static void Place(Window window, RichTextView view)
    {
        var offset = view.Offset;

        view.MoveTo(view.PositionAt(new Point(20, Height / 2)), extend: false);
        Layout(window);

        Assert.Equal(offset, view.Offset);
    }

    /// <summary>
    /// Scrolls to a share of the first block's height. A row holding the nested table among outer
    /// rows of text is laid out first, by putting the caret in it: until then it is priced like
    /// the rows of text, and the share would land in those.
    /// </summary>
    private static void ToShare(Window window, RichTextView view, double share)
    {
        if (view.State.Doc.Blocks[0] is TableNode outer
            && outer.Rows.FirstOrDefault(r => r.Cells.Length > 1 && r.Cells[0].Blocks[0] is TableNode && r.Cells[1].Blocks[0] is ParagraphNode) is { } host)
        {
            view.MoveTo(TextAt(view.State.Doc, ((ParagraphNode)host.Cells[1].Blocks[0]).Content.Text), extend: false);
            Layout(window);
        }

        view.Offset = new Vector(0, view.Blocks[0].Size.Height * share);
        Layout(window);

        var middle = Paragraph(view.State.Doc, view.PositionAt(new Point(20, Height / 2)));

        Assert.False(
            middle.StartsWith("outer", StringComparison.Ordinal) || middle.StartsWith("After", StringComparison.Ordinal),
            $"the middle of the screen is outside the nested table, at \"{middle}\"");
    }

    private static void ResetCounters()
    {
        WorkCounters.CoverageRepairs = 0;
        WorkCounters.FullLayoutBackstops = 0;
        WorkCounters.NestedWindowFallbacks = 0;
        WorkCounters.TableCellsMeasured = 0;
    }

    private static void AssertNoRepairs()
    {
        Assert.Equal(0, WorkCounters.CoverageRepairs);
        Assert.Equal(0, WorkCounters.FullLayoutBackstops);
        Assert.Equal(0, WorkCounters.NestedWindowFallbacks);
    }

    /// <summary>Every row the viewport meets, in every table at every depth, is laid out.</summary>
    internal static void AssertScreenLaidOut(RichTextView view)
    {
        for (var i = view.RealizedBlocks.Start.Value; i < view.RealizedBlocks.End.Value; i++)
        {
            if (view.Blocks[i] is TableBlockView table)
            {
                var top = -view.BlockOrigin(i).Y;

                Assert.True(table.Covers(top, top + view.Viewport.Height), $"block {i} has rows on screen that are not laid out");
            }
        }
    }

    private static double ScreenTop(RichTextView view, int position) => GeometryTransactionTests.ScreenTop(view, position);

    private static void AssertLineAt(RichTextView view, string paragraph, int into, double y)
    {
        var at = ScreenTop(view, TextAt(view.State.Doc, paragraph) + into);

        Assert.True(Math.Abs(at - y) <= 1, $"the line at the top is drawn at {at:F1}, not {y:F1}");
        AssertScreenLaidOut(view);
    }

    private static string Paragraph(DocumentNode doc, int position) =>
        Assert.IsType<ParagraphNode>(doc.Resolve(position).Parent).Content.Text;

    /// <summary>The first position inside the paragraph reading <paramref name="text"/>.</summary>
    internal static int TextAt(DocumentNode doc, string text) => GeometryTransactionTests.PositionOf(doc, text);
}
