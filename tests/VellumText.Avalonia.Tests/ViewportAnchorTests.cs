using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The line of text at the top of the viewport stays there while the content around it changes:
/// an edit above it, an edit around it, a composition above it, a width change, an undo (owner
/// decision Q3, rubber-duck review finding 10.4).
/// </summary>
/// <remarks>
/// <para>
/// Every test finds the text it expects at the top from the document, by what each edit did to
/// it, and compares where that text is drawn before and after. Nothing here asks the view where it
/// thinks its anchor is, because a view that held the wrong text perfectly still would pass a test
/// that did.
/// </para>
/// <para>
/// The content is long paragraphs inside table cells, because inside a table is where the
/// viewport was held by a share of a row's height — a row that is one long paragraph is a row
/// whose share names different text as soon as anything in it reflows.
/// </para>
/// </remarks>
public class ViewportAnchorTests
{
    private const double Width = 600;
    private const double Height = 400;

    [AvaloniaFact]
    public void InsertingTextAboveTheTopLineOfAParagraphInACellKeepsThatLineStill()
    {
        var doc = Doc(1, Long("P"));
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "P");
        var inserted = new string('x', 100) + " ";

        Place(window, view);
        view.Apply(Insert(view.State, Start(view.State.Doc, "P"), inserted));
        Layout(window);

        AssertAt(view, top + inserted.Length, y);
    }

    [AvaloniaFact]
    public void DeletingTextAboveTheTopLineOfAParagraphInACellKeepsThatLineStill()
    {
        var doc = Doc(1, Long("P"));
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "P");
        var start = Start(view.State.Doc, "P");

        Place(window, view);
        view.Apply(view.State.Transaction().Delete(start, start + 50));
        Layout(window);

        AssertAt(view, top - 50, y);
    }

    /// <summary>
    /// One transaction adds a paragraph before the paragraphs before the top line and one after
    /// those after it, so the paragraph at the top is unchanged but is neither the first nor the
    /// last of what the edit left unchanged.
    /// </summary>
    [AvaloniaFact]
    public void DisjointEditsAroundAnUnchangedParagraphKeepItsLineStill()
    {
        var doc = Doc(1, Long("A"), Long("P"), Long("B"));
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "P");
        var x = ParagraphNode.FromText("Inserted before everything.");
        var z = ParagraphNode.FromText("Inserted after everything.");
        var a = Start(view.State.Doc, "A") - 1;
        var b = Start(view.State.Doc, "B") + Text("B").Length + 1;

        Place(window, view);

        var tr = view.State.Transaction();

        tr.Replace(b, b, new Slice([z], 0, 0));
        tr.Replace(a, a, new Slice([x], 0, 0));
        view.Apply(tr);
        Layout(window);

        AssertAt(view, top + x.NodeSize, y);
    }

    /// <summary>
    /// A cell holding a long nested table and then a long paragraph, scrolled into the paragraph;
    /// an edit far up the nested table makes it taller. The paragraph's line stays put — at the top
    /// level, and with the whole cell nested two and three tables deep.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AParagraphAfterANestedTableStaysStillWhenAFarEditGrowsTheTable(int depth)
    {
        var nested = new TableNode(Enumerable.Range(0, 60).Select(r => new TableRowNode([
            TableCellNode.FromText($"n{r}a"),
            TableCellNode.FromText($"n{r}b"),
        ])));
        var doc = Doc(depth, nested, Long("P"));
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "P");
        var grown = " " + string.Join(" ", Enumerable.Repeat("much longer cell text", 30));

        Place(window, view);
        view.Apply(Insert(view.State, PositionOf(view.State.Doc, "n3a") + 3, grown));
        Layout(window);

        AssertAt(view, top + grown.Length, y);
    }

    /// <summary>Deleting the whole paragraph at the top puts the next one's first line there.</summary>
    [AvaloniaFact]
    public void DeletingTheWholeTopParagraphPutsTheNextOneAtTheTop()
    {
        var doc = Doc(1, Long("A"), Long("P"), Long("B"));
        var (window, view) = Shown(doc);
        var (top, _) = ScrollIntoMiddleOf(window, view, "P");
        var p = Start(view.State.Doc, "P");

        Place(window, view);
        view.Apply(view.State.Transaction().Delete(p - 1, p + Text("P").Length + 1));
        Layout(window);

        AssertAt(view, Start(view.State.Doc, "B"), 0);
    }

    /// <summary>Deleting the whole row at the top puts the next row's first line there.</summary>
    [AvaloniaFact]
    public void DeletingTheWholeTopRowPutsTheNextRowAtTheTop()
    {
        var table = new TableNode(Enumerable.Range(0, 30).Select(r => new TableRowNode([
            new TableCellNode([Long($"R{r}", sentences: 40)]),
        ])));
        var doc = new DocumentNode([table, ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var (top, _) = ScrollIntoMiddleOf(window, view, "R12");
        var row = Start(view.State.Doc, "R12") - 3;

        Place(window, view);
        view.Apply(view.State.Transaction().Delete(row, row + table.Rows[12].NodeSize));
        Layout(window);

        AssertAt(view, Start(view.State.Doc, "R13"), 0);
    }

    [AvaloniaFact]
    public void EditingAListItemAboveKeepsTheLineInALaterItemStill()
    {
        var list = new ListNode([
            new ListItemNode([Long("I1")]),
            new ListItemNode([Long("I2")]),
        ]);
        var doc = Doc(1, list, Long("P"));
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "I2");
        var inserted = new string('x', 120) + " ";

        Place(window, view);
        view.Apply(Insert(view.State, Start(view.State.Doc, "I1"), inserted));
        Layout(window);

        AssertAt(view, top + inserted.Length, y);
    }

    [AvaloniaFact]
    public void UndoingAndRedoingAnEditAboveTheTopLineKeepsThatLineStill()
    {
        var doc = Doc(1, Long("A"), Long("P"));
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "P");
        var inserted = new string('x', 100) + " ";

        Place(window, view);
        view.Apply(Insert(view.State, Start(view.State.Doc, "A"), inserted));
        Layout(window);
        AssertAt(view, top + inserted.Length, y);

        Assert.True(view.Undo());
        Layout(window);
        AssertAt(view, top, y);

        Assert.True(view.Redo());
        Layout(window);
        AssertAt(view, top + inserted.Length, y);
    }

    /// <summary>
    /// Composing far above the top line in the same paragraph reflows everything between, and
    /// the line at the top stays — while the composition grows and when it is cleared.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ComposingAboveTheTopLineInTheSameParagraphKeepsThatLineStill(int depth)
    {
        var doc = Doc(depth, Long("P"));
        var (window, view) = Shown(doc);

        view.MoveTo(Start(doc, "P") + 10, extend: false);
        Layout(window);

        var (top, y) = ScrollIntoMiddleOf(window, view, "P");

        Assert.True(view.SetPreedit(string.Join(" ", Enumerable.Repeat("composed", 40))));
        Layout(window);
        AssertAt(view, top, y);

        Assert.True(view.SetPreedit(string.Join(" ", Enumerable.Repeat("composed", 90))));
        Layout(window);
        AssertAt(view, top, y);

        Assert.True(view.SetPreedit(null));
        Layout(window);
        AssertAt(view, top, y);
    }

    /// <summary>
    /// A composition of whole lines before a paragraph's text, ending at a line break, and the
    /// paragraph's own first line at the top of the viewport: that line is held, not the line the
    /// composition starts on — when it was scrolled to, while the composition grows, across a
    /// resize, and when the composition is cleared.
    /// </summary>
    /// <remarks>
    /// The line after the composition starts at the position the composition is at, which is also
    /// where the composition's own first line starts; read back as a document position it named
    /// the composition's first line, and the viewport snapped up to it.
    /// </remarks>
    [AvaloniaFact]
    public void TheLineRightAfterACompositionOfWholeLinesIsHeldAtTheTop()
    {
        var doc = Doc(1, Long("A", 30), Long("P", 30));
        var (window, view) = Shown(doc);
        var p = Start(doc, "P");

        // The second character of the paragraph's own text: on its first line, and a position the
        // composition before it does not share.
        var marker = p + 1;

        view.MoveTo(p, extend: false);
        Layout(window);

        Assert.True(view.SetPreedit("composed line one\ncomposed line two\n"));
        Layout(window);

        Assert.True(ScreenTop(view, marker) > ScreenTop(view, p), "the composition does not end at a line break");

        // The paragraph's first line just across the top of the viewport.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, marker) + 2);
            Layout(window);
        }

        AssertAt(view, marker, -2);

        Assert.True(view.SetPreedit("composed line one\ncomposed line two\ncomposed line three\n"));
        Layout(window);
        AssertAt(view, marker, -2);

        window.Width = Width - 150;
        Layout(window);
        AssertAt(view, marker, -2);

        Assert.True(view.SetPreedit(null));
        Layout(window);
        AssertAt(view, marker, -2);
    }

    /// <summary>
    /// Composing straight after a scroll, before any layout pass has run: the line at the new top
    /// is read from the layout before the composition drops the paragraph's, so the composition
    /// growing above it moves nothing.
    /// </summary>
    [AvaloniaFact]
    public void ComposingStraightAfterAScrollKeepsTheNewTopLineStill()
    {
        var doc = Doc(1, Long("P"));
        var (window, view) = Shown(doc);

        view.MoveTo(Start(doc, "P") + 10, extend: false);
        Layout(window);
        ScrollIntoMiddleOf(window, view, "P");

        view.Offset = new Vector(0, view.Offset.Y + 50);

        var top = view.PositionAt(new Point(1, 0.5));
        var y = ScreenTop(view, top);

        Assert.True(view.SetPreedit(string.Join(" ", Enumerable.Repeat("composed", 60))));
        Layout(window);

        AssertAt(view, top, y);
    }

    /// <summary>
    /// Near the end of the document an edit below the viewport shortens it, the offset is clamped
    /// up by more than the overscan, and what that brings on screen is laid out by the pass that
    /// clamped it — not by a repair after.
    /// </summary>
    [AvaloniaFact]
    public void AnOffsetClampedUpByMoreThanTheOverscanLaysOutWhatItBringsOnScreen()
    {
        var table = new TableNode(Enumerable.Range(0, 400).Select(r => new TableRowNode([
            TableCellNode.FromText($"row {r} first"),
            TableCellNode.FromText($"row {r} second"),
        ])));
        var doc = new DocumentNode([table, Long("TAIL", sentences: 60)]);
        var (window, view) = Shown(doc);
        var block = (TableBlockView)view.Blocks[0];

        view.Offset = new Vector(0, block.Size.Height - (Height / 2));
        Layout(window);
        Place(window, view);

        var tail = Start(view.State.Doc, "TAIL");

        WorkCounters.CoverageRepairs = 0;
        view.Apply(view.State.Transaction().Delete(tail, tail + Length(view.State.Doc, "TAIL")));
        Layout(window);

        Assert.Equal(0, WorkCounters.CoverageRepairs);
        AssertRowsOnScreenAreLaidOut(view, block);
    }

    /// <summary>
    /// A pass that leaves a row on screen not laid out is followed by another, in every build, so
    /// nothing is drawn blank.
    /// </summary>
    [AvaloniaFact]
    public void AWindowThatDoesNotCoverTheViewportIsLaidOutAgain()
    {
        var table = new TableNode(Enumerable.Range(0, 400).Select(r => new TableRowNode([
            TableCellNode.FromText($"row {r} first"),
            TableCellNode.FromText($"row {r} second"),
        ])));
        var doc = new DocumentNode([table, ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var block = (TableBlockView)view.Blocks[0];
        var broken = 0;

        view.AfterSettle = () =>
        {
            if (broken++ == 0)
            {
                BreakRowOnScreen(view, block);
            }
        };

        WorkCounters.CoverageRepairs = 0;
        view.Offset = new Vector(0, block.Size.Height / 2);
        Layout(window);

        Assert.Equal(1, WorkCounters.CoverageRepairs);
        Assert.Equal(0, WorkCounters.FullLayoutBackstops);
        AssertRowsOnScreenAreLaidOut(view, block);
    }

    /// <summary>
    /// And when laying the window out again does not help either, every block is laid out whole:
    /// slow, and nothing left blank.
    /// </summary>
    [AvaloniaFact]
    public void AWindowThatStaysUncoveredEndsWithEveryBlockLaidOut()
    {
        var table = new TableNode(Enumerable.Range(0, 400).Select(r => new TableRowNode([
            TableCellNode.FromText($"row {r} first"),
            TableCellNode.FromText($"row {r} second"),
        ])));
        var doc = new DocumentNode([table, ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var block = (TableBlockView)view.Blocks[0];

        view.AfterSettle = () => BreakRowOnScreen(view, block);

        WorkCounters.CoverageRepairs = 0;
        WorkCounters.FullLayoutBackstops = 0;
        view.Offset = new Vector(0, block.Size.Height / 2);
        Layout(window);

        Assert.Equal(1, WorkCounters.FullLayoutBackstops);
        AssertRowsOnScreenAreLaidOut(view, block);

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }

    /// <summary>Marks the row at the middle of the viewport as edited since it was laid out.</summary>
    private static void BreakRowOnScreen(RichTextView view, TableBlockView block)
    {
        var position = view.PositionAt(new Point(Width / 4, Height / 2));
        var local = position - view.Slots[0].Start;
        var paragraph = block.ParagraphAt(local)!.Value.View;

        block.ParagraphChanged(paragraph, local);
    }

    private static void AssertRowsOnScreenAreLaidOut(RichTextView view, TableBlockView block)
    {
        var top = view.Offset.Y - (view.BlockOrigin(0).Y + view.Offset.Y);
        var first = block.RowAnchor(Math.Max(0, top)).Row;
        var last = block.RowAnchor(Math.Max(0, top + Height)).Row;

        for (var r = first; r <= last; r++)
        {
            Assert.True(block.IsRowMeasured(r), $"row {r} is on screen but not laid out ({first} to {last})");
        }
    }

    /// <summary>
    /// A narrower view rewraps everything; the line at the top is the one that was, wherever the
    /// rest of the paragraph's lines now break.
    /// </summary>
    [AvaloniaFact]
    public void ResizingKeepsTheTopLineOfTextAtTheTop()
    {
        var doc = Doc(1, Long("A"), Long("P"));
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "P");

        window.Width = Width - 150;
        Layout(window);

        AssertAt(view, top, y);
    }

    /// <summary>
    /// The same inside a cell merged across a hundred rows, whose rows below the first hold no
    /// cells of their own: the line at the top of the viewport is in the merged cell, not in the
    /// empty row the viewport's top happens to fall in.
    /// </summary>
    [AvaloniaFact]
    public void ResizingKeepsTheTopLineOfTextInACellMergedAcrossRows()
    {
        var table = new TableNode(Enumerable.Range(0, 100).Select(r => new TableRowNode(r == 0
            ? [new TableCellNode([Long("M")], rowSpan: 100), TableCellNode.FromText("side")]
            : [])));
        var doc = new DocumentNode([table, ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "M");

        window.Width = Width - 150;
        Layout(window);

        AssertAt(view, top, y);
    }

    /// <summary>
    /// Narrower, then a question about a far row above the viewport that moves everything under
    /// it, then as wide as before: the line held at the top is the one the reader had at the top
    /// all along, not the line it was folded into while narrow.
    /// </summary>
    /// <remarks>
    /// Narrow, the held line's first position sits inside a line that starts earlier. The question
    /// put the viewport back from a fresh reading of the top, which named that earlier start, and
    /// the next resize held it: wide again, the reader's line came back a line lower.
    /// </remarks>
    [AvaloniaFact]
    public void AFarQuestionBetweenTwoResizesKeepsTheLineHeldAtTheTop()
    {
        var table = new TableNode(Enumerable.Range(0, 300).Select(r => new TableRowNode([
            r == 50
                ? new TableCellNode([.. Enumerable.Range(0, 60).Select(i => (BlockNode)ParagraphNode.FromText($"tall {i}"))])
                : new TableCellNode([Long($"R{r}", 8)]),
        ])));
        var doc = new DocumentNode([table, ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);
        var (top, y) = ScrollIntoMiddleOf(window, view, "R200");

        window.Width = Width - 150;
        Layout(window);

        AssertAt(view, top, y);
        Assert.NotEqual(top, view.PositionAt(new Point(1, 0.5)));

        _ = view.ImageRect(PositionOf(view.State.Doc, "tall 30"));

        AssertAt(view, top, y);

        window.Width = Width;
        Layout(window);

        AssertAt(view, top, y);
    }

    /// <summary>
    /// The top of the viewport in the spacing between two paragraphs of a cell: the text the
    /// reader sees first is the second paragraph's, and a narrower view that rewraps the first
    /// leaves the second's first line where it was.
    /// </summary>
    [AvaloniaFact]
    public void ResizingWithTheTopInTheSpacingBetweenParagraphsKeepsTheParagraphBelowStill()
    {
        var doc = Doc(1, Long("A", 30), Long("B", 30), Long("C", 30));
        var (window, view) = Shown(doc);
        var b = Start(view.State.Doc, "B");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, b) - 2);
            Layout(window);
        }

        Assert.Equal(2, ScreenTop(view, b), 1);

        window.Width = Width - 150;
        Layout(window);

        AssertAt(view, b, 2);
    }

    /// <summary>
    /// The top of the viewport in the spacing just before a paragraph, and text that wraps onto
    /// lines of its own inserted at the paragraph's very start: the text that was first stays
    /// where it was, the new lines going above it out of sight, as an insertion above the top
    /// line does anywhere else; through undo and redo too.
    /// </summary>
    [AvaloniaFact]
    public void InsertingAtTheStartOfTheParagraphBelowTheSpacingAtTheTopKeepsItsTextStill()
    {
        var doc = Doc(1, Long("A", 30), Long("B", 30), Long("C", 30));
        var (window, view) = Shown(doc);
        var b = Start(view.State.Doc, "B");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, b) - 2);
            Layout(window);
        }

        Assert.Equal(2, ScreenTop(view, b), 1);
        Place(window, view);

        var prefix = Text("NEW", 6) + " ";

        view.Apply(Insert(view.State, b, prefix));
        Layout(window);

        AssertAt(view, b + prefix.Length, 2);

        Assert.True(view.Undo());
        Layout(window);

        AssertAt(view, b, 2);

        Assert.True(view.Redo());
        Layout(window);

        AssertAt(view, b + prefix.Length, 2);
    }

    /// <summary>
    /// The top of the viewport in the room a heading leaves above its line: the heading is the
    /// text held, and stays where it was when the paragraph above it rewraps.
    /// </summary>
    [AvaloniaFact]
    public void ResizingWithTheTopAboveAHeadingsLineKeepsTheHeadingStill()
    {
        var heading = new ParagraphNode(InlineContent.FromText("H: a heading in the cell"), ParagraphKind.Heading1);
        var doc = Doc(1, Long("A", 30), heading, Long("C", 30));
        var (window, view) = Shown(doc);
        var h = Start(view.State.Doc, "H");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, h) - 3);
            Layout(window);
        }

        Assert.Equal(3, ScreenTop(view, h), 1);

        window.Width = Width - 150;
        Layout(window);

        AssertAt(view, h, 3);
    }

    /// <summary>
    /// Scrolling step by step through a long table whose rows are taller than the first rows
    /// taught the estimate: the text on screen moves by exactly the scroll, wherever the top of
    /// the viewport falls — in a line, or in the spacing around one.
    /// </summary>
    [AvaloniaFact]
    public void ScrollingThroughALongTableMovesTheTextByExactlyTheScroll()
    {
        var doc = new DocumentNode([GeometryTransactionTests.TallAfterShort(2_000), ParagraphNode.FromText("After.")]);
        var (window, view) = Shown(doc);

        view.Offset = new Vector(0, view.Blocks[0].Size.Height * 0.4);
        Layout(window);

        for (var step = 0; step < 25; step++)
        {
            var probe = view.PositionAt(new Point(20, Height / 2));
            var y = ScreenTop(view, probe);

            view.Offset += new Vector(0, 60);
            Layout(window);

            Assert.True(Math.Abs(ScreenTop(view, probe) - (y - 60)) <= 1, $"step {step}: the text moved from {y:F1} to {ScreenTop(view, probe):F1}, not {y - 60:F1}");
        }
    }

    /// <summary>
    /// Scrolled straight to rows nobody had laid out, the viewport is held by a share of a row
    /// until they are; once they are drawn, a resize keeps the line the reader sees at the top
    /// still, not the share of a row that rewrapped (owner decision Q3). The row's height is set
    /// by the cell beside, whose short lines do not rewrap, so the share and the line part ways.
    /// </summary>
    [AvaloniaFact]
    public void ResizingAfterAJumpToRowsNobodyLaidOutKeepsTheTopLineStill()
    {
        var table = new TableNode(Enumerable.Range(0, 400).Select(r => new TableRowNode([
            new TableCellNode([Long($"R{r}", 12)]),
            new TableCellNode([.. Enumerable.Range(0, 30).Select(i => (BlockNode)ParagraphNode.FromText($"side {r} {i}"))]),
        ])));
        var (window, view) = Shown(new DocumentNode([table, ParagraphNode.FromText("After.")]));

        view.Offset = new Vector(0, view.Blocks[0].Size.Height * (160.2 / 400));
        Layout(window);

        // The line at the top is in whichever cell has text there; the other cell's nearest text
        // can have ended far above.
        var top = new[] { 1, Width * 0.75 }
            .Select(x => view.PositionAt(new Point(x, 0.5)))
            .MaxBy(p => ScreenTop(view, p));
        var y = ScreenTop(view, top);

        Assert.InRange(y, -40, 1);

        window.Width = Width - 150;
        Layout(window);

        AssertAt(view, top, y);
    }

    /// <summary>
    /// Two edits applied before the view sees either leave it no mapping to follow; it keeps the
    /// distance it always kept, and draws no row it has not laid out.
    /// </summary>
    [AvaloniaFact]
    public void ApplyingTwoEditsBeforeTheViewSeesEitherIsSafe()
    {
        var doc = Doc(1, Long("A"), Long("P"));
        var (window, view) = Shown(doc);
        var (top, _) = ScrollIntoMiddleOf(window, view, "P");

        Place(window, view);

        var once = view.State.Apply(Insert(view.State, Start(view.State.Doc, "A") + 3, "first "));
        var twice = once.Apply(Insert(once, Start(once.Doc, "A") + 3, "second "));

        view.State = twice;
        Layout(window);

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);

        var table = (TableBlockView)view.Blocks[0];

        for (var r = 0; r < table.RowCount; r++)
        {
            Assert.True(table.IsRowMeasured(r));
        }
    }

    private static readonly string[] Words =
        ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta", "iota", "kappa"];

    /// <summary>A paragraph of many sentences, tagged so it can be found again.</summary>
    private static ParagraphNode Long(string tag, int sentences = 120) =>
        ParagraphNode.FromText(Text(tag, sentences));

    private static string Text(string tag, int sentences = 120) =>
        tag + ": " + string.Join(" ", Enumerable.Range(0, sentences).Select(i =>
            $"Sentence {i} of {tag} says {Words[i % Words.Length]} and {Words[(i * 7) % Words.Length]} again."));

    /// <summary>A document whose blocks sit in a cell of a table nested <paramref name="depth"/> deep.</summary>
    private static DocumentNode Doc(int depth, params BlockNode[] blocks)
    {
        BlockNode[] content = blocks;

        for (var d = 0; d < depth; d++)
        {
            content = [new TableNode([new TableRowNode([new TableCellNode(content)])])];
        }

        return new DocumentNode([.. content, ParagraphNode.FromText("After the table."), Long("TAIL")]);
    }

    /// <summary>The first position inside the paragraph tagged <paramref name="tag"/>.</summary>
    private static int Start(DocumentNode doc, string tag) =>
        Find(doc.Blocks, 0, p => p.Content.Text.StartsWith(tag + ": ", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No paragraph is tagged {tag}.");

    /// <summary>The length of the paragraph tagged <paramref name="tag"/>.</summary>
    private static int Length(DocumentNode doc, string tag)
    {
        var length = -1;

        Find(doc.Blocks, 0, p =>
        {
            if (!p.Content.Text.StartsWith(tag + ": ", StringComparison.Ordinal))
            {
                return false;
            }

            length = p.ContentSize;

            return true;
        });

        return length;
    }

    /// <summary>The first position inside the paragraph reading <paramref name="text"/>.</summary>
    private static int PositionOf(DocumentNode doc, string text) =>
        Find(doc.Blocks, 0, p => p.Content.Text == text)
            ?? throw new InvalidOperationException($"No paragraph reads {text}.");

    private static int? Find(IReadOnlyList<Node> nodes, int start, Func<ParagraphNode, bool> match)
    {
        var position = start;

        foreach (var node in nodes)
        {
            if (node is ParagraphNode paragraph && match(paragraph))
            {
                return position + 1;
            }

            if (node.Children.Length > 0 && Find(node.Children, position + 1, match) is { } found)
            {
                return found;
            }

            position += node.NodeSize;
        }

        return null;
    }

    private static Transaction Insert(EditorState state, int at, string text)
    {
        var tr = state.Transaction();

        tr.InsertText(at, text);

        return tr;
    }

    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        WorkCounters.CoverageRepairs = 0;
        WorkCounters.FullLayoutBackstops = 0;

        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

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

    /// <summary>
    /// Scrolls so the top of the viewport is two thirds of the way into a paragraph, and reports
    /// the position at the start of the line at the top and where that line's top is drawn.
    /// </summary>
    private static (int Top, double Y) ScrollIntoMiddleOf(Window window, RichTextView view, string tag)
    {
        var start = Start(view.State.Doc, tag);
        var length = Length(view.State.Doc, tag);
        var middle = start + ((length * 2) / 3);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + ScreenTop(view, middle) - 1);
            Layout(window);
        }

        var top = view.PositionAt(new Point(1, 0.5));

        Assert.InRange(top, start, start + length);

        return (top, ScreenTop(view, top));
    }


    /// <summary>Puts the caret in the middle of the viewport, so the edits that follow scroll nothing into view.</summary>
    private static void Place(Window window, RichTextView view)
    {
        var offset = view.Offset;

        view.MoveTo(view.PositionAt(new Point(Width / 2, Height / 2)), extend: false);
        Layout(window);

        Assert.Equal(offset, view.Offset);
    }

    /// <summary>Where the top of the line holding a position is drawn, in the view's coordinates.</summary>
    private static double ScreenTop(RichTextView view, int position)
    {
        var block = view.BlockIndexOf(position);

        return view.BlockOrigin(block).Y
            + view.Blocks[block].GetCaretRect(position - view.Slots[block].Start).Top;
    }

    private static void AssertAt(RichTextView view, int position, double y)
    {
        var at = ScreenTop(view, position);

        Assert.True(Math.Abs(at - y) <= 1, $"the line holding position {position} is drawn at {at:F1}, not {y:F1}");

        // Held still by laying out the right window the first time, not by repairing a wrong one.
        Assert.Equal(0, WorkCounters.CoverageRepairs);
    }
}
