using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Rows of a table in a cell that scrolling has left far behind give up their views and keep their
/// heights (rubber-duck review finding 10.4, scope (b), owner decision): what scrolling through a
/// nested table keeps is a few screenfuls, the caret's row and a composition are kept wherever they
/// are, and every question about a row let go is answered as a fully laid-out copy answers it.
/// </summary>
/// <remarks>
/// What a whole traversal keeps on the heap is asserted in a child process
/// (<see cref="NestedTableLayoutProbeTests"/>); here, the rows a table keeps are counted, and the
/// geometry is held against a fully laid-out copy. Each test scrolls through a long document, so
/// each runs in a child process with a memory cap and a time limit (<see cref="Isolated"/>).
/// </remarks>
[Collection(ChildProcessCollection.Name)]
public class NestedTableEvictionTests
{
    private const double Width = 500;
    private const double Height = 400;

    // ---- heights kept, screen covered ---------------------------------------------------------

    /// <summary>
    /// Scrolling far down a table in a cell, one to three deep, lets go of the rows left behind and
    /// keeps the height of every one of them: each is the height a fully laid-out copy gives it.
    /// Scrolling back moves the text by exactly the scroll, with every row on screen laid out and
    /// drawn where the copy draws it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RowsLetGoKeepTheirHeightsAndComeBackWhereTheyWere(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(RowsLetGoKeepTheirHeightsAndComeBackWhereTheyWereHere), depth);

    /// <summary>The body of <see cref="RowsLetGoKeepTheirHeightsAndComeBackWhereTheyWere"/>, run in a child process.</summary>
    internal static void RowsLetGoKeepTheirHeightsAndComeBackWhereTheyWereHere(int depth)
    {
        var doc = NestedTableVirtualizationTests.Nested(depth, NestedTableVirtualizationTests.Irregular(3_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);
        var twin = GeometryTransactionTests.FullyLaidOut(doc);

        ToNested(window, view);

        var inner = Innermost(view, depth);
        var first = int.MaxValue;
        var last = -1;

        for (var page = 0; page < 150; page++)
        {
            view.Offset += new Vector(0, Height);
            Layout(window);
            NestedTableVirtualizationTests.AssertScreenLaidOut(view);

            foreach (var (row, _, _, _) in inner.LaidOutRows())
            {
                first = Math.Min(first, row);
                last = Math.Max(last, row);
            }
        }

        Assert.True(last - first > 300, $"the scroll passed rows {first} to {last} only");
        Assert.True(inner.RealizedRows <= 150, $"the table keeps {inner.RealizedRows} rows after scrolling past {last - first}");

        var copy = Innermost(twin, depth);

        for (var r = first; r <= last; r++)
        {
            Assert.True(inner.IsRowMeasured(r), $"row {r}, laid out on the way down, has no height");

            var height = inner.RowTopOf(r + 1) - inner.RowTopOf(r);
            var truth = copy.RowTopOf(r + 1) - copy.RowTopOf(r);

            Assert.True(Math.Abs(height - truth) <= 0.01, $"row {r} is {height:F2} tall, {truth:F2} in a fully laid-out copy");
        }

        for (var step = 0; step < 300; step++)
        {
            var probe = view.PositionAt(new Point(20, 100));
            var y = ScreenTop(view, probe);

            view.Offset -= new Vector(0, Height / 2);
            Layout(window);

            Assert.True(Math.Abs(ScreenTop(view, probe) - (y + (Height / 2))) <= 1, $"step {step}: the line at {probe} moved from {y:F1} to {ScreenTop(view, probe):F1}");
            NestedTableVirtualizationTests.AssertScreenLaidOut(view);

            if (step % 50 == 0)
            {
                NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, twin);
                Draw(window, view);
            }
        }

        AssertNoRepairs();
    }

    /// <summary>
    /// A table at the top of the document keeps every row it has laid out, as it always did: only
    /// tables in cells let rows go (owner decision).
    /// </summary>
    [Fact]
    public void ATableAtTheTopOfTheDocumentKeepsEveryRowItLaidOut() =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(ATableAtTheTopOfTheDocumentKeepsEveryRowItLaidOutHere));

    /// <summary>The body of <see cref="ATableAtTheTopOfTheDocumentKeepsEveryRowItLaidOut"/>, run in a child process.</summary>
    internal static void ATableAtTheTopOfTheDocumentKeepsEveryRowItLaidOutHere()
    {
        var (window, view) = Shown(NestedTableVirtualizationTests.Nested(0, NestedTableVirtualizationTests.Uniform(3_000)));
        var table = (TableBlockView)view.Blocks[0];

        for (var page = 0; page < 100; page++)
        {
            view.Offset += new Vector(0, Height);
            Layout(window);
        }

        var passed = table.LaidOutRows().Max(row => row.Row);

        Assert.True(passed > 800, $"the scroll reached row {passed} only");
        Assert.True(table.RealizedRows > passed, $"the table at the top keeps {table.RealizedRows} of the {passed} rows it laid out");
    }

    // ---- pins ---------------------------------------------------------------------------------

    /// <summary>
    /// The caret's row is kept while the view scrolls far below it — the paragraph it is in is the
    /// very view it was — and typing there afterwards lands in that paragraph, with the caret shown
    /// where a fully laid-out copy shows it.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void TheCaretsRowIsKeptWhileScrollingFarAwayAndTypingThereLandsInPlace(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(TheCaretsRowIsKeptWhileScrollingFarAwayAndTypingThereLandsInPlaceHere), depth);

    /// <summary>The body of <see cref="TheCaretsRowIsKeptWhileScrollingFarAwayAndTypingThereLandsInPlace"/>, run in a child process.</summary>
    internal static void TheCaretsRowIsKeptWhileScrollingFarAwayAndTypingThereLandsInPlaceHere(int depth)
    {
        var doc = NestedTableVirtualizationTests.Nested(depth, NestedTableVirtualizationTests.Uniform(3_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);

        ToNested(window, view);

        var caret = view.PositionAt(new Point(20, Height / 2));

        view.MoveTo(caret, extend: false);
        Layout(window);

        var paragraph = ParagraphViewAt(view, caret);
        var text = Paragraph(view.State.Doc, caret);
        var into = caret - NestedTableVirtualizationTests.TextAt(view.State.Doc, text);

        ScrollDown(window, view, 150);

        Assert.True(Innermost(view, depth).RealizedRows <= 200, $"the table keeps {Innermost(view, depth).RealizedRows} rows");
        Assert.Same(paragraph, ParagraphViewAt(view, caret));

        window.KeyTextInput("typed");
        Layout(window);

        Assert.Equal(text.Insert(into, "typed"), Paragraph(view.State.Doc, caret));
        Assert.InRange(view.CaretRect().Top, 0, Height);
        NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc));
        Draw(window, view);
        AssertNoRepairs();
    }

    /// <summary>
    /// A composition at the caret in a cell of a table nested two and three deep survives
    /// scrolling far away and back: still composing, drawn in the paragraph it was composed in, the
    /// caret where it was, the line at the top where it was, and nothing on screen unlaid.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ACompositionScrolledFarAwayAndBackIsKeptAndTheScreenStill(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(ACompositionScrolledFarAwayAndBackIsKeptAndTheScreenStillHere), depth);

    /// <summary>The body of <see cref="ACompositionScrolledFarAwayAndBackIsKeptAndTheScreenStill"/>, run in a child process.</summary>
    internal static void ACompositionScrolledFarAwayAndBackIsKeptAndTheScreenStillHere(int depth)
    {
        var doc = NestedTableVirtualizationTests.Nested(depth, NestedTableVirtualizationTests.Uniform(3_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);

        ToNested(window, view);

        var at = view.PositionAt(new Point(20, 100));

        view.MoveTo(at, extend: false);
        Layout(window);

        Assert.True(view.SetPreedit("WWWWWW"));
        Layout(window);

        var composing = ParagraphViewAt(view, at);
        var offset = view.Offset;
        var caret = view.CaretRect();
        var top = view.PositionAt(new Point(20, 2));
        var y = ScreenTop(view, top);

        Assert.NotNull(composing.Preedit);

        ScrollDown(window, view, 150);

        Assert.True(Innermost(view, depth).RealizedRows <= 200, $"the table keeps {Innermost(view, depth).RealizedRows} rows");

        view.Offset = offset;
        Layout(window);

        Assert.True(view.IsComposing, "the composition was dropped");
        Assert.Same(composing, ParagraphViewAt(view, at));
        Assert.NotNull(composing.Preedit);
        Assert.True(Math.Abs(view.CaretRect().Top - caret.Top) <= 1, $"the caret moved from {caret.Top:F1} to {view.CaretRect().Top:F1}");
        Assert.True(Math.Abs(ScreenTop(view, top) - y) <= 1, $"the line at the top moved from {y:F1} to {ScreenTop(view, top):F1}");
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);
        Draw(window, view);
        AssertNoRepairs();
    }

    /// <summary>
    /// A composition over a selection that starts in a row of a table in a cell and ends after the
    /// table, in the cell holding it: the composition is drawn at the start, rows away from the
    /// caret at the end, and is kept while the view is far from both — scrolled back to, it is
    /// still there, drawn in the paragraph it was composed in, with nothing on screen unlaid.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ACompositionAwayFromTheCaretIsKeptWhileTheViewIsElsewhere(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(ACompositionAwayFromTheCaretIsKeptWhileTheViewIsElsewhereHere), depth);

    /// <summary>The body of <see cref="ACompositionAwayFromTheCaretIsKeptWhileTheViewIsElsewhere"/>, run in a child process.</summary>
    internal static void ACompositionAwayFromTheCaretIsKeptWhileTheViewIsElsewhereHere(int depth)
    {
        var doc = NestedBeforeText(depth, NestedTableVirtualizationTests.Uniform(600));
        var (window, view) = Shown(doc);

        ToNested(window, view);

        var at = view.PositionAt(new Point(20, 100));
        var shown = view.Offset;

        view.MoveTo(at, extend: false);
        Layout(window);
        view.MoveTo(NestedTableVirtualizationTests.TextAt(view.State.Doc, "below the inner table") + 3, extend: true);
        Layout(window);

        Assert.IsType<TextSelection>(view.State.Selection);
        Assert.Equal(at, view.State.Selection.From);
        Assert.True(view.SetPreedit("WWWWWW"));
        Layout(window);

        var composing = ParagraphViewAt(view, at);

        Assert.NotNull(composing.Preedit);

        // Far from both ends: down past the end, then up to the middle of the inner table.
        ScrollDown(window, view, 20);
        view.Offset = shown + new Vector(0, 9_000);
        Layout(window);
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);

        Assert.True(Innermost(view, depth).RealizedRows <= 200, $"the table keeps {Innermost(view, depth).RealizedRows} rows");

        view.Offset = shown;
        Layout(window);

        Assert.True(view.IsComposing, "the composition was dropped");
        Assert.Same(composing, ParagraphViewAt(view, at));
        Assert.NotNull(composing.Preedit);
        Assert.InRange(view.CaretRect().Top, 0, Height);
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);
        Draw(window, view);
        AssertNoRepairs();
    }

    /// <summary>
    /// Scrolled far down a nested table, with rows let go behind and the caret far above,
    /// <c>ScrollToEnd</c> ends the view at the document's end — laid out from the end up, with
    /// everything on screen laid out — and the caret's paragraph is still the view it was.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ScrollToEndAfterRowsWereLetGoShowsTheEndAndKeepsTheCaret(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(ScrollToEndAfterRowsWereLetGoShowsTheEndAndKeepsTheCaretHere), depth);

    /// <summary>The body of <see cref="ScrollToEndAfterRowsWereLetGoShowsTheEndAndKeepsTheCaret"/>, run in a child process.</summary>
    internal static void ScrollToEndAfterRowsWereLetGoShowsTheEndAndKeepsTheCaretHere(int depth)
    {
        var doc = NestedTableVirtualizationTests.Nested(depth, NestedTableVirtualizationTests.Irregular(3_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);

        ToNested(window, view);

        var caret = view.PositionAt(new Point(20, Height / 2));

        view.MoveTo(caret, extend: false);
        Layout(window);

        var paragraph = ParagraphViewAt(view, caret);

        ScrollDown(window, view, 100);
        view.ScrollToEnd();
        Layout(window);

        var last = view.Blocks.Count - 1;
        var end = view.BlockOrigin(last).Y + view.Blocks[last].Size.Height;

        Assert.True(Math.Abs(end - Height) <= 1, $"the document ends at {end:F1} on screen, not at the bottom of the viewport");
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);
        Assert.Same(paragraph, ParagraphViewAt(view, caret));
        Draw(window, view);

        // Held by the end, the rows above the held content that the window cuts through are made
        // exact, which three deep lays the long table out whole (owner decision Q1); the next pass,
        // with a window again, lets its far rows go.
        view.Offset -= new Vector(0, 1);
        Layout(window);

        Assert.True(Innermost(view, depth).RealizedRows <= 200, $"the table keeps {Innermost(view, depth).RealizedRows} rows");
        AssertNoRepairs();
    }

    // ---- what is known ------------------------------------------------------------------------

    /// <summary>
    /// A table in a cell whose rows each hold a table of six rows, scrolled through to its end and
    /// back: at every level, what each table knows of its height (<c>ExactHeight</c>) is never more
    /// than its height in a fully laid-out copy, and a table that says it is wholly laid out is that
    /// height — after its rows were let go, after the rows it let go were laid out again, each with
    /// its table whole, so the table of tables is still wholly laid out, and after an edit shrank a
    /// row it had let go, undone and redone.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void WhatATableKnowsOfItsHeightStaysTrueAsRowsAreLetGoAndLaidOutAgain(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(WhatATableKnowsOfItsHeightStaysTrueAsRowsAreLetGoAndLaidOutAgainHere), depth);

    /// <summary>The body of <see cref="WhatATableKnowsOfItsHeightStaysTrueAsRowsAreLetGoAndLaidOutAgain"/>, run in a child process.</summary>
    internal static void WhatATableKnowsOfItsHeightStaysTrueAsRowsAreLetGoAndLaidOutAgainHere(int depth)
    {
        var doc = NestedTableVirtualizationTests.Nested(depth, Hosts(80));
        var (window, view) = Shown(doc);
        var steps = 0;

        while (view.Offset.Y < view.Extent.Height - Height - 1 && steps++ < 2_000)
        {
            view.Offset += new Vector(0, 600);
            Layout(window);
        }

        var hosts = Innermost(view, depth);

        Assert.True(hosts.RealizedRows < 60, $"the table of tables keeps {hosts.RealizedRows} of its 80 rows");
        Assert.True(hosts.IsComplete(), "scrolled through to the end, the table of tables is not wholly known");
        var truth = GeometryTransactionTests.FullyLaidOut(view.State.Doc);

        AssertKnownTruly(view, truth, "at the end");

        // Back up into rows let go: each is laid out again with its table whole, as it was.
        for (var page = 0; page < 30; page++)
        {
            view.Offset -= new Vector(0, 600);
            Layout(window);
            AssertKnownTruly(view, truth, $"{page + 1} pages back up");
        }

        Assert.True(hosts.IsComplete(), "laid out again, the table of tables is not wholly known");

        // A row let go far above, shrunk by seven lines.
        var shrunk = view.State.Doc;
        var from = NestedTableVirtualizationTests.TextAt(shrunk, "h5-r1 line 1") - 1;
        var to = NestedTableVirtualizationTests.TextAt(shrunk, "h5-r1 line 7") + "h5-r1 line 7".Length + 1;

        view.Apply(view.State.Transaction().Delete(from, to));
        Layout(window);
        AssertKnownTruly(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc), "after the shrink");

        Assert.True(view.Undo());
        Layout(window);
        AssertKnownTruly(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc), "after the undo");

        Assert.True(view.Redo());
        Layout(window);
        AssertKnownTruly(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc), "after the redo");
        AssertNoRepairs();
    }

    /// <summary>
    /// A table in a cell whose rows each hold a table too tall to lay out whole again on the way
    /// back — twelve rows, about three times the window — and too short to let its own rows go,
    /// scrolled through to its end: the rows of those tables far behind are let go all the same, so
    /// what the traversal keeps does not grow with the number of rows passed. Nothing moved for it:
    /// every row of the table of tables is where it was, it is as tall, and coming back up, every
    /// row on screen is laid out where a fully laid-out copy has it.
    /// </summary>
    /// <remarks>
    /// Rubber-duck review of scope (b), finding 1: such a band was never let go, being too tall, and
    /// the tables in it never let their rows go, being short and no longer measured, so every one of
    /// them stayed laid out.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TallRowsLeftBehindLetTheirTablesRowsGo(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(TallRowsLeftBehindLetTheirTablesRowsGoHere), depth);

    /// <summary>The body of <see cref="TallRowsLeftBehindLetTheirTablesRowsGo"/>, run in a child process.</summary>
    internal static void TallRowsLeftBehindLetTheirTablesRowsGoHere(int depth)
    {
        var (window, view) = Shown(NestedTableVirtualizationTests.Nested(depth, Hosts(100, inner: 12)));
        var hosts = Innermost(view, depth);
        var steps = 0;

        while (view.Offset.Y < view.Extent.Height - Height - 1 && steps++ < 2_000)
        {
            view.Offset += new Vector(0, 600);
            Layout(window);
        }

        Assert.True(hosts.IsComplete(), "scrolled through to the end, the table of tables is not wholly known");

        var tall = hosts.RealizedNested().Count(nested => nested.Table.Size.Height > 2 * Height);
        var kept = hosts.RealizedNested().Sum(nested => nested.Table.RealizedRows);

        Assert.True(tall >= 50, $"only {tall} of the tables in the table of tables are taller than twice the window");
        Assert.True(kept <= 100, $"the tables in the table of tables keep {kept} rows after a traversal of {hosts.RowCount * 12}");

        var tops = Enumerable.Range(0, hosts.RowCount + 1).Select(hosts.RowTopOf).ToArray();
        var height = hosts.Size.Height;
        var truth = GeometryTransactionTests.FullyLaidOut(view.State.Doc);

        AssertKnownTruly(view, truth, "at the end");

        for (var page = 0; page < 40; page++)
        {
            view.Offset -= new Vector(0, 600);
            Layout(window);
            NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, truth);
        }

        for (var r = 0; r <= hosts.RowCount; r++)
        {
            Assert.True(hosts.RowTopOf(r).Equals(tops[r]), $"row {r} of the table of tables moved from {tops[r]:F1} to {hosts.RowTopOf(r):F1}");
        }

        Assert.Equal(height, hosts.Size.Height);
        AssertKnownTruly(view, truth, "back up");
        AssertNoRepairs();
    }

    /// <summary>
    /// The same three tables deep: rows each holding a table of two rows, each of those holding a
    /// table of twenty, every level taller than twice the window. After a traversal the deepest
    /// tables keep few rows; coming back, nothing has moved, and a deepest table on screen lays out
    /// only what the window reaches of it — let go row by row, not laid out whole again on the way
    /// back.
    /// </summary>
    [Fact]
    public void TallRowsOfTallRowsLetTheDeepestRowsGo() =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(TallRowsOfTallRowsLetTheDeepestRowsGoHere));

    /// <summary>The body of <see cref="TallRowsOfTallRowsLetTheDeepestRowsGo"/>, run in a child process.</summary>
    internal static void TallRowsOfTallRowsLetTheDeepestRowsGoHere()
    {
        var doc = NestedTableVirtualizationTests.Nested(1, new TableNode(Enumerable.Range(0, 40).Select(r => new TableRowNode([
            new TableCellNode([new TableNode(Enumerable.Range(0, 2).Select(m => new TableRowNode([
                new TableCellNode([NestedTableVirtualizationTests.Irregular(20, $"h{r}-{m}-")]),
                TableCellNode.FromText($"middle {r}-{m}"),
            ])))]),
            TableCellNode.FromText($"host {r}"),
        ]))));
        var (window, view) = Shown(doc);
        var hosts = Innermost(view, 1);
        var steps = 0;

        while (view.Offset.Y < view.Extent.Height - Height - 1 && steps++ < 2_000)
        {
            view.Offset += new Vector(0, 600);
            Layout(window);
        }

        IEnumerable<TableBlockView> Deepest() =>
            hosts.RealizedNested().SelectMany(middle => middle.Table.RealizedNested()).Select(deep => deep.Table);

        var kept = Deepest().Sum(deep => deep.RealizedRows);

        Assert.True(Deepest().Count() >= 40, $"only {Deepest().Count()} of the deepest tables are still there");
        Assert.True(kept <= 150, $"the deepest tables keep {kept} rows after a traversal of {40 * 2 * 20}");

        var tops = Enumerable.Range(0, hosts.RowCount + 1).Select(hosts.RowTopOf).ToArray();
        var truth = GeometryTransactionTests.FullyLaidOut(view.State.Doc);

        for (var page = 0; page < 30; page++)
        {
            view.Offset -= new Vector(0, 600);
            Layout(window);
            NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, truth);
        }

        for (var r = 0; r <= hosts.RowCount; r++)
        {
            Assert.True(hosts.RowTopOf(r).Equals(tops[r]), $"row {r} of the table of tables moved from {tops[r]:F1} to {hosts.RowTopOf(r):F1}");
        }

        Assert.Contains(Deepest(), deep => deep.RealizedRows > 0 && deep.RealizedRows < deep.RowCount);
        AssertKnownTruly(view, truth, "back up");
        AssertNoRepairs();
    }

    /// <summary>
    /// A table in a cell of sixty rows, a few screens tall, scrolled through: none of its rows is
    /// let go, so a short table is not laid out again and again as its window moves over it.
    /// </summary>
    [Fact]
    public void AShortTableInACellLetsNoRowGo() =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(AShortTableInACellLetsNoRowGoHere));

    /// <summary>The body of <see cref="AShortTableInACellLetsNoRowGo"/>, run in a child process.</summary>
    internal static void AShortTableInACellLetsNoRowGoHere()
    {
        var (window, view) = Shown(NestedTableVirtualizationTests.Nested(1, NestedTableVirtualizationTests.Irregular(60)));
        var inner = Innermost(view, 1);

        WorkCounters.TableRowsLetGo = 0;

        for (var page = 0; page < 40 && view.Offset.Y < view.Extent.Height - Height - 1; page++)
        {
            view.Offset += new Vector(0, Height);
            Layout(window);
        }

        Assert.True(inner.Size.Height > 6 * Height, $"the table is only {inner.Size.Height:F0} tall");
        Assert.Equal(60, inner.RealizedRows);
        Assert.Equal(0, WorkCounters.TableRowsLetGo);
    }

    /// <summary>
    /// A question about a position in a row let go far above — a picture's rectangle, which lays
    /// the row out to answer — moves nothing: the row comes back with its table laid out whole,
    /// exactly the height it kept, so every row of the table of tables is where it was, the table is
    /// as tall, and the screen is still.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AQuestionAboutARowLetGoMovesNothing(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(AQuestionAboutARowLetGoMovesNothingHere), depth);

    /// <summary>The body of <see cref="AQuestionAboutARowLetGoMovesNothing"/>, run in a child process.</summary>
    internal static void AQuestionAboutARowLetGoMovesNothingHere(int depth)
    {
        var (window, view) = Shown(NestedTableVirtualizationTests.Nested(depth, Hosts(80)));
        var steps = 0;

        while (view.Offset.Y < view.Extent.Height - Height - 1 && steps++ < 2_000)
        {
            view.Offset += new Vector(0, 600);
            Layout(window);
        }

        // Back up a little, off the end, so the screen is held by its top line.
        view.Offset -= new Vector(0, 1_000);
        Layout(window);

        var hosts = Innermost(view, depth);

        Assert.True(hosts.RealizedRows < 60, $"the table of tables keeps {hosts.RealizedRows} of its 80 rows");

        var tops = Enumerable.Range(0, hosts.RowCount + 1).Select(hosts.RowTopOf).ToArray();
        var height = hosts.Size.Height;
        var probe = view.PositionAt(new Point(20, Height / 2));
        var y = ScreenTop(view, probe);

        _ = view.ImageRect(NestedTableVirtualizationTests.TextAt(view.State.Doc, "h5-r2c0"));
        Layout(window);

        for (var r = 0; r <= hosts.RowCount; r++)
        {
            Assert.True(hosts.RowTopOf(r).Equals(tops[r]), $"row {r} of the table of tables moved from {tops[r]:F1} to {hosts.RowTopOf(r):F1}");
        }

        Assert.Equal(height, hosts.Size.Height);
        Assert.True(Math.Abs(ScreenTop(view, probe) - y) <= 1, $"the line at {probe} moved from {y:F1} to {ScreenTop(view, probe):F1}");
        AssertNoRepairs();
    }

    /// <summary>
    /// A row holding a table laid out only in part — the screen showed its top — is not let go
    /// while the view is far away: coming back, the table in it is the very table it was, and the
    /// screen shows it laid out. Let go, it came back with its table laid out afresh around the
    /// window, another height, and a window held by a share of that row chased the share from pass
    /// to pass until it was laid out whole as a fallback.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ARowHoldingATablePartlyLaidOutKeepsItWhileTheViewIsAway(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(ARowHoldingATablePartlyLaidOutKeepsItWhileTheViewIsAwayHere), depth);

    /// <summary>The body of <see cref="ARowHoldingATablePartlyLaidOutKeepsItWhileTheViewIsAway"/>, run in a child process.</summary>
    internal static void ARowHoldingATablePartlyLaidOutKeepsItWhileTheViewIsAwayHere(int depth)
    {
        var doc = NestedTableVirtualizationTests.Nested(depth, Hosts(150, inner: 10));
        var (window, view) = Shown(doc);
        var at = NestedTableVirtualizationTests.TextAt(doc, "h40-r0c0");

        view.Offset += new Vector(0, ScreenTop(view, at) - 10);
        Layout(window);

        var shown = view.Offset;
        var hosts = Innermost(view, depth);
        var local = at - hosts.DocumentStart;
        var partly = hosts.RealizedNested().Single(nested => nested.Start <= local && local <= nested.Start + nested.Table.ContentSize);

        Assert.False(partly.Table.IsComplete(), "the table at the top of the screen is wholly laid out already");

        // Far down — a jump, so the table at the top is left partly laid out — and on page by page,
        // past many more rows than a table keeps.
        view.Offset += new Vector(0, 20_000);
        Layout(window);

        for (var page = 0; page < 180; page++)
        {
            view.Offset += new Vector(0, 600);
            Layout(window);
        }

        Assert.True(hosts.RealizedRows < 100, $"the table of tables keeps {hosts.RealizedRows} rows");

        view.Offset = shown;
        Layout(window);

        Assert.Same(partly.Table, hosts.RealizedNested().Single(nested => nested.Start == partly.Start).Table);
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);
        Draw(window, view);
        AssertNoRepairs();
    }

    /// <summary>
    /// A row of a table in a cell, edited while off screen and let go before it was laid out again,
    /// is known by no more than it is: the table, wholly laid out but for that row, neither knows
    /// itself taller (<c>ExactHeight</c>) nor counts itself at least taller (its bands' lower bound)
    /// than a fully laid-out copy.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ARowEditedAndLetGoBeforeItWasLaidOutIsKnownByNoMoreThanItIs(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(ARowEditedAndLetGoBeforeItWasLaidOutIsKnownByNoMoreThanItIsHere), depth);

    /// <summary>The body of <see cref="ARowEditedAndLetGoBeforeItWasLaidOutIsKnownByNoMoreThanItIs"/>, run in a child process.</summary>
    internal static void ARowEditedAndLetGoBeforeItWasLaidOutIsKnownByNoMoreThanItIsHere(int depth)
    {
        var (window, view) = Shown(NestedTableVirtualizationTests.Nested(depth, NestedTableVirtualizationTests.Irregular(300, "k")));
        var steps = 0;

        while (view.Offset.Y < view.Extent.Height - Height - 1 && steps++ < 2_000)
        {
            view.Offset += new Vector(0, 600);
            Layout(window);
        }

        // The caret on screen, so the edit scrolls nothing into view.
        view.MoveTo(view.PositionAt(new Point(20, Height / 2)), extend: false);
        Layout(window);

        // A row of four lines or more above the window, which it is not laid out again for, but
        // near enough to it not to have been let go yet.
        var row = Enumerable.Range(0, 300).Reverse()
            .First(r => 1 + (r * 7 % 11) >= 4 && ScreenTop(view, NestedTableVirtualizationTests.TextAt(view.State.Doc, $"kr{r}c0")) is < -450 and > -700);
        var lines = 1 + (row * 7 % 11);
        var from = NestedTableVirtualizationTests.TextAt(view.State.Doc, $"kr{row} line 1") - 1;
        var to = NestedTableVirtualizationTests.TextAt(view.State.Doc, $"kr{row} line {lines - 1}") + $"kr{row} line {lines - 1}".Length + 1;

        view.Apply(view.State.Transaction().Delete(from, to));
        Layout(window);

        var inner = Innermost(view, depth);

        Assert.True(inner.IsRowRealized(row) && !inner.IsRowMeasured(row), $"row {row} was laid out again, or let go, before the view went away");

        // Away to the top, and down through enough rows for the table to let rows go: the edited row
        // is let go before anything laid it out again.
        view.Offset = default;
        Layout(window);

        for (var page = 0; page < 40; page++)
        {
            view.Offset += new Vector(0, 600);
            Layout(window);
        }

        Assert.False(inner.IsRowRealized(row), $"row {row} was not let go");
        AssertKnownTruly(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc), $"row {row} shrunk by {lines - 1} lines and let go");
        AssertNoRepairs();
    }

    // ---- questions about rows let go ----------------------------------------------------------

    /// <summary>
    /// After scrolling far down a nested table: a selection from a row let go far above to the
    /// screen paints, and its rectangles on screen are a fully laid-out copy's; a point far above
    /// lands where the copy has it; replacing all with text that wraps, then undoing and redoing,
    /// keeps the line at the top still and the screen laid out as the copy has it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RowsLetGoAnswerSelectionPointsAndReplaceAllAsAFullyLaidOutCopy(int depth) =>
        Isolated.Run(typeof(NestedTableEvictionTests), nameof(RowsLetGoAnswerSelectionPointsAndReplaceAllAsAFullyLaidOutCopyHere), depth);

    /// <summary>The body of <see cref="RowsLetGoAnswerSelectionPointsAndReplaceAllAsAFullyLaidOutCopy"/>, run in a child process.</summary>
    internal static void RowsLetGoAnswerSelectionPointsAndReplaceAllAsAFullyLaidOutCopyHere(int depth)
    {
        var doc = NestedTableVirtualizationTests.Nested(depth, NestedTableVirtualizationTests.Uniform(2_000), outerRows: 20, hostRow: 10);
        var (window, view) = Shown(doc);
        var twin = GeometryTransactionTests.FullyLaidOut(doc);

        ToNested(window, view);
        ScrollDown(window, view, 120);

        Assert.True(Innermost(view, depth).RealizedRows <= 200, $"the table keeps {Innermost(view, depth).RealizedRows} rows");

        // A selection from far above to the middle of the screen.
        var anchor = NestedTableVirtualizationTests.TextAt(doc, "r5c1") + 1;
        var head = view.PositionAt(new Point(20, Height / 2));

        view.Apply(view.State.Transaction().SetSelection(new TextSelection(anchor, head)));
        Layout(window);
        Draw(window, view);

        var shift = ScreenTop(twin, head) - ScreenTop(view, head);
        var shown = SelectionOnScreen(view, anchor, head, 0);
        var copied = SelectionOnScreen(twin, anchor, head, shift);

        Assert.True(shown.Count > 0, "nothing of the selection is on screen");
        Assert.Equal(copied.Count, shown.Count);

        for (var i = 0; i < shown.Count; i++)
        {
            Assert.True(
                Math.Abs(shown[i].Top - copied[i].Top) <= 1 && Math.Abs(shown[i].Left - copied[i].Left) <= 1 && Math.Abs(shown[i].Height - copied[i].Height) <= 1,
                $"selection rectangle {i} is {shown[i]}, {copied[i]} in a fully laid-out copy");
        }

        // A point far above, in rows let go.
        var far = view.PositionAt(new Point(200, -5_000));

        shift = ScreenTop(twin, head) - ScreenTop(view, head);
        Assert.Equal(twin.PositionAt(new Point(200, -5_000 + shift)), far);

        // Replace all, undone and redone, with the caret near the top so nothing scrolls into view.
        view.MoveTo(view.PositionAt(new Point(20, 40)), extend: false);
        Layout(window);

        var top = view.PositionAt(new Point(20, 2));
        var text = Paragraph(view.State.Doc, top);
        var into = top - NestedTableVirtualizationTests.TextAt(view.State.Doc, text);
        var y = ScreenTop(view, top);

        Assert.True(view.Find("c1") >= 2_000);
        Assert.True(view.ReplaceAll("C1 " + string.Join(" ", Enumerable.Repeat("replaced with words that wrap", 4))) >= 2_000);
        Layout(window);
        AssertLineAt(view, text, into, y);
        NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc));

        Assert.True(view.Undo());
        Layout(window);
        AssertLineAt(view, text, into, y);
        NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc));

        Assert.True(view.Redo());
        Layout(window);
        AssertLineAt(view, text, into, y);
        NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc));
        Draw(window, view);
        AssertNoRepairs();
    }

    // ---- fixtures and checks ------------------------------------------------------------------

    /// <summary>
    /// <paramref name="rows"/> rows, each holding in its first cell a table of six rows one to
    /// eleven lines tall (<c>h{row}-</c>), about seven hundred pixels, and text in its second.
    /// </summary>
    private static TableNode Hosts(int rows, int inner = 6) =>
        new(Enumerable.Range(0, rows).Select(r => new TableRowNode([
            new TableCellNode([NestedTableVirtualizationTests.Irregular(inner, $"h{r}-")]),
            TableCellNode.FromText($"host {r}"),
        ])));

    /// <summary>
    /// At every level, every table laid out in the view against the same table in a fully laid-out
    /// copy: it knows no more of its height than the copy's height, and if it says it is wholly
    /// laid out, it is that height and knows it.
    /// </summary>
    private static void AssertKnownTruly(RichTextView view, RichTextView twin, string when)
    {
        for (var i = view.RealizedBlocks.Start.Value; i < view.RealizedBlocks.End.Value; i++)
        {
            if (view.Blocks[i] is TableBlockView table && twin.Blocks[i] is TableBlockView copy)
            {
                Compare(table, copy, $"block {i}");
            }
        }

        void Compare(TableBlockView table, TableBlockView copy, string path)
        {
            var truth = copy.Size.Height;

            Assert.True(table.ExactHeight <= truth + 0.01, $"{when}: {path} knows it is {table.ExactHeight:F1} tall, more than the {truth:F1} it is");
            Assert.True(table.LowerHeight() <= truth + 0.01, $"{when}: {path} is at least {table.LowerHeight():F1} tall by its bands, more than the {truth:F1} it is");

            if (table.IsComplete())
            {
                Assert.True(Math.Abs(table.Size.Height - truth) <= 0.01, $"{when}: {path} says it is wholly laid out at {table.Size.Height:F1}, not the {truth:F1} it is");
                Assert.True(Math.Abs(table.ExactHeight - truth) <= 0.01, $"{when}: {path} is wholly laid out and knows only {table.ExactHeight:F1} of its {truth:F1}");
            }

            var copies = copy.RealizedNested().ToDictionary(nested => nested.Start, nested => nested.Table);

            foreach (var (start, nested) in table.RealizedNested())
            {
                Compare(nested, copies[start], $"{path} > table at {start}");
            }
        }
    }

    /// <summary>The selection's rectangles in the first block on screen, aligned by <paramref name="shift"/>.</summary>
    private static List<Rect> SelectionOnScreen(RichTextView view, int from, int to, double shift)
    {
        var table = (TableBlockView)view.Blocks[0];
        var origin = view.BlockOrigin(0);
        var start = view.Slots[0].Start;
        var visible = new Rect(-origin.X, -origin.Y + shift, Width, Height);

        return [.. table.GetSelectionRects(Math.Min(from, to) - start, Math.Max(from, to) - start, visible)
            .Select(rect => rect.Translate(new Vector(origin.X, origin.Y - shift)))
            .Where(rect => rect.Bottom > 0 && rect.Top < Height)
            .OrderBy(rect => rect.Top)
            .ThenBy(rect => rect.Left)];
    }

    /// <summary>
    /// <paramref name="inner"/> in the first cell of a one-row table with a paragraph after it,
    /// <c>below the inner table</c>, nested <paramref name="depth"/> deep in all; the outermost
    /// table has twenty rows with the rest in row ten, then a paragraph.
    /// </summary>
    private static DocumentNode NestedBeforeText(int depth, TableNode inner)
    {
        BlockNode content = new TableNode([new TableRowNode([new TableCellNode([inner, ParagraphNode.FromText("below the inner table")])])]);

        for (var d = 2; d <= depth; d++)
        {
            content = d < depth
                ? new TableNode([new TableRowNode([new TableCellNode([content])])])
                : new TableNode(Enumerable.Range(0, 20).Select(r => new TableRowNode(r == 10
                    ? [new TableCellNode([content]), TableCellNode.FromText($"outer {r} side")]
                    : [TableCellNode.FromText($"outer {r}"), TableCellNode.FromText($"outer {r} side")])));
        }

        return new DocumentNode([content, ParagraphNode.FromText("After the tables.")]);
    }

    /// <summary>Scrolls to a little way into the nested table, past the outer rows above it.</summary>
    private static void ToNested(Window window, RichTextView view)
    {
        var host = view.State.Doc.Blocks[0] is TableNode outer
            && outer.Rows.FirstOrDefault(r => r.Cells.Length > 1 && r.Cells[0].Blocks[0] is TableNode && r.Cells[1].Blocks[0] is ParagraphNode) is { } row
            ? ((ParagraphNode)row.Cells[1].Blocks[0]).Content.Text
            : null;

        if (host is not null)
        {
            view.MoveTo(NestedTableVirtualizationTests.TextAt(view.State.Doc, host), extend: false);
            Layout(window);
        }

        var at = NestedTableVirtualizationTests.TextAt(view.State.Doc, view.State.Doc.Blocks[0] is TableNode ? FirstInnermost(view.State.Doc) : "After the tables.");

        view.Offset += new Vector(0, ScreenTop(view, at) + 300);
        Layout(window);
    }

    private static string FirstInnermost(DocumentNode doc)
    {
        Node node = doc.Blocks[0];

        while (node is TableNode table && table.Rows.FirstOrDefault(r => r.Cells.Any(c => c.Blocks[0] is TableNode)) is { } row)
        {
            node = row.Cells.First(c => c.Blocks[0] is TableNode).Blocks[0];
        }

        return ((ParagraphNode)((TableNode)node).Rows[0].Cells[0].Blocks[0]).Content.Text;
    }

    private static TableBlockView Innermost(RichTextView view, int depth)
    {
        var table = (TableBlockView)view.Blocks[0];

        for (var d = 0; d < depth; d++)
        {
            table = table.NestedTables.First();
        }

        return table;
    }

    private static ParagraphView ParagraphViewAt(RichTextView view, int position)
    {
        var index = view.BlockIndexOf(position);

        return view.Blocks[index].ParagraphAt(position - view.Slots[index].Start)?.View
            ?? throw new InvalidOperationException($"no paragraph at {position}");
    }

    private static void ScrollDown(Window window, RichTextView view, int pages)
    {
        for (var page = 0; page < pages; page++)
        {
            view.Offset += new Vector(0, Height);
            Layout(window);
            NestedTableVirtualizationTests.AssertScreenLaidOut(view);
        }
    }

    /// <remarks>The counters are thread-static and shared with every other test on the thread.</remarks>
    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        WorkCounters.CoverageRepairs = 0;
        WorkCounters.FullLayoutBackstops = 0;
        WorkCounters.NestedWindowFallbacks = 0;

        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        Layout(window);
        view.Focus();

        return (window, view);
    }

    private static void AssertNoRepairs()
    {
        Assert.Equal(0, WorkCounters.CoverageRepairs);
        Assert.Equal(0, WorkCounters.FullLayoutBackstops);
        Assert.Equal(0, WorkCounters.NestedWindowFallbacks);
    }

    private static void AssertLineAt(RichTextView view, string paragraph, int into, double y)
    {
        var at = ScreenTop(view, NestedTableVirtualizationTests.TextAt(view.State.Doc, paragraph) + into);

        Assert.True(Math.Abs(at - y) <= 1, $"the line at the top is drawn at {at:F1}, not {y:F1}");
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);
    }

    private static void Draw(Window window, RichTextView view)
    {
        _ = view.CaretRect();

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }

    private static void Layout(Window window) => GeometryTransactionTests.Layout(window);

    private static double ScreenTop(RichTextView view, int position) => GeometryTransactionTests.ScreenTop(view, position);

    private static string Paragraph(DocumentNode doc, int position) =>
        Assert.IsType<ParagraphNode>(doc.Resolve(position).Parent).Content.Text;
}
