using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Random documents with tables nested in cells one to three deep — merged cells, lists, long and
/// tall cells — put through random scrolls, jumps, resizes, edits far from the viewport, structural
/// edits with undo and redo, compositions and questions about far points (rubber-duck review
/// finding 10.4, owner requirement: property and fuzz tests).
/// </summary>
/// <remarks>
/// <para>
/// After every operation: nothing threw (and in DEBUG no layout invariant did); the extent is the
/// sum of every block's height, measured or estimated, and every block laid out is the height
/// recorded for it; every row on screen is laid out at every depth; nothing was laid out whole as a
/// fallback or a backstop. After a scroll by a step, every line on screen moved by exactly the
/// step; after an edit or a question somewhere not laid out, nothing on screen moved; after a
/// resize, the line held at the top is where it was — each within 1 DIP. At the end, everything a
/// fully laid-out copy shows in the viewport is laid out.
/// </para>
/// <para>
/// Each seed is replayable: a failure names the seed and the step. Seeds that found defects are
/// kept in <see cref="RandomOperationsKeepTheInvariants"/>; <c>VELLUM_FUZZ_RUNS</c> runs that many
/// more from a fixed base, for an exploratory run.
/// </para>
/// </remarks>
[Collection(ChildProcessCollection.Name)]
public class NestedTableFuzzTests
{
    private const double Width = 500;
    private const double Height = 400;

    /// <summary>How long one seed may run in its child process.</summary>
    private static readonly TimeSpan SeedTimeout = TimeSpan.FromSeconds(180);

    /// <summary>
    /// The working set one seed is killed at. The fully laid-out copy each step is compared with is
    /// a whole document's layout: seeds 5, 6, 10 and 10,061 peak at about 2.5 GB with the view beside
    /// it, the same with letting rows go turned off, and seed 10 runs out of a 2.25 GB heap. Children
    /// run one at a time (<see cref="ChildProcessCollection"/>).
    /// </summary>
    private const long SeedMemory = 4L << 30;

    /// <summary>The recorded seeds, replayed on every run, each in a child process (<see cref="Isolated"/>).</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(10_056)]
    [InlineData(10_061)]
    [InlineData(10_063)]
    [InlineData(10_064)]
    [InlineData(10_026)]
    [InlineData(10_036)]
    [InlineData(10_043)]
    [InlineData(10_087)]
    [InlineData(10_088)]
    [InlineData(10_004)]
    [InlineData(10_015)]
    [InlineData(10_045)]
    [InlineData(10_046)]
    [InlineData(10_042)]
    public void RandomOperationsKeepTheInvariants(int seed) =>
        Isolated.Run(SeedTimeout, SeedMemory, typeof(NestedTableFuzzTests), nameof(Seed), seed, 30, false);

    /// <summary>
    /// The recorded seeds of runs that also scroll many pages away and back, through documents
    /// whose tables in cells can be several hundred rows long, so rows are let go and laid out again
    /// (rubber-duck review finding 10.4, scope (b)).
    /// </summary>
    [Theory]
    [InlineData(20_001)]
    [InlineData(20_002)]
    [InlineData(20_003)]
    [InlineData(20_004)]
    [InlineData(20_005)]
    [InlineData(20_006)]
    [InlineData(20_007)]
    [InlineData(20_008)]
    [InlineData(20_009)]
    [InlineData(20_010)]
    public void FarScrollsThatLetRowsGoKeepTheInvariants(int seed) =>
        Isolated.Run(SeedTimeout, SeedMemory, typeof(NestedTableFuzzTests), nameof(Seed), seed, 30, true);

    /// <summary>
    /// More seeds, as many as <c>VELLUM_FUZZ_RUNS</c> asks for (none by default), with and without
    /// far scrolls, each in a child process.
    /// </summary>
    [Fact]
    public void ExploratoryRun()
    {
        var runs = int.TryParse(Environment.GetEnvironmentVariable("VELLUM_FUZZ_RUNS"), out var n) ? n : 0;
        var failures = new List<string>();

        for (var seed = 10_000; seed < 10_000 + runs; seed++)
        {
            foreach (var far in new[] { false, true })
            {
                var replayed = far ? seed + 10_000 : seed;

                try
                {
                    Isolated.Run(SeedTimeout, SeedMemory, typeof(NestedTableFuzzTests), nameof(Seed), replayed, 40, far);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    failures.Add($"seed {replayed}{(far ? " (far scrolls)" : "")}: {e.Message.Split('\n')[0]}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>One seed, as a child process runs it.</summary>
    internal static void Seed(int seed, int steps, bool farScrolls) => Run(seed, steps, farScrolls: farScrolls);

    internal static void Run(int seed, int steps, Action<int, RichTextView>? before = null, bool farScrolls = false)
    {
        var random = new Random(seed);
        var doc = new Generator(random) { LongTables = farScrolls }.Document();

        // Before the document is shown, so what its first layout repairs is counted, and nothing an
        // earlier test on this thread counted is.
        Reset();

        var (window, view) = Shown(doc);
        var twin = new Twin(fresh: farScrolls);

        Check(view, $"seed {seed}, shown");

        for (var step = 0; step < steps; step++)
        {
            before?.Invoke(step, view);

            var operation = random.Next(farScrolls ? 10 : 9);
            var what = $"seed {seed}, step {step}";

            switch (operation)
            {
                case 0:
                    what += Jump(window, view, random);
                    break;
                case 1:
                case 2:
                    what += Step(window, view, random, what);
                    break;
                case 3:
                    what += Resize(window, view, random, what);
                    break;
                case 4:
                    what += EditFar(window, view, random, what);
                    break;
                case 5:
                    what += Structural(window, view, random, what);
                    break;
                case 6:
                    what += Compose(window, view, random);
                    break;
                case 9:
                    what += Sweep(window, view, random, what, twin);
                    break;
                default:
                    what += AskFar(window, view, random, what, twin);
                    break;
            }

            Check(view, what);
            CheckLedger(view, what);
            NestedTableExtentTests.AssertNearTheTruth(view, twin.Of(view.State.Doc, window.Width), NestedTableExtentTests.Over, what);
            CheckSettled(window, view, what);

            // A frame of every step, asking nothing first: drawing lays nothing out (in DEBUG it
            // throws if it does) and asks only about what the step prepared.
            using (var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))
            {
                Assert.True(frame is not null, $"{what}: no frame was drawn");
            }
        }

        // As wide as the copy, and then the truth of what is on screen.
        window.Width = Width;
        Layout(window);
        Check(view, $"seed {seed}, end");

        NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, twin.Of(view.State.Doc, Width));
        window.Close();
    }

    private static string Jump(Window window, RichTextView view, Random random)
    {
        var y = random.NextDouble() * Math.Max(0, view.Extent.Height - Height);

        view.Offset = new Vector(0, y);
        Layout(window);

        return $": jump to {y:F0}";
    }

    private static string Step(Window window, RichTextView view, Random random, string what)
    {
        var delta = (random.Next(2) == 0 ? -1 : 1) * random.Next(20, 160);
        var target = view.Offset.Y + delta;

        if (target < 0 || target > view.Extent.Height - Height)
        {
            return $": step {delta} skipped at the edge";
        }

        var probes = Probes(view);

        view.Offset = new Vector(0, target);
        Layout(window);

        // The offset can be corrected for estimates replaced above the viewport; the text moves
        // by exactly what the reader scrolled, which is the scroll asked for less any correction.
        var moved = view.Offset.Y - target;

        foreach (var (position, y) in probes)
        {
            var now = ScreenTop(view, position);

            Assert.True(
                Math.Abs(now - (y - delta)) <= 1,
                $"{what}: step {delta}: the line at {position} moved from {y:F1} to {now:F1}, not {y - delta:F1}");
        }

        return $": step {delta}";
    }

    /// <summary>
    /// Scrolls a page at a time many pages one way, letting the rows left behind go, and back the
    /// same way, every row on screen laid out at each page; then what is on screen is a fully
    /// laid-out copy's. Where the text is after coming back is not held: a row holding a table is
    /// laid out afresh, and can come back another height (owner decision Q2).
    /// </summary>
    private static string Sweep(Window window, RichTextView view, Random random, string what, Twin twin)
    {
        var pages = random.Next(3, 30);
        var sign = random.Next(2) == 0 ? -1 : 1;
        var moved = 0;

        foreach (var direction in new[] { sign, -sign })
        {
            for (var page = 0; page < pages; page++)
            {
                var target = Math.Clamp(view.Offset.Y + (direction * Height), 0, Math.Max(0, view.Extent.Height - Height));

                if (target.Equals(view.Offset.Y))
                {
                    break;
                }

                view.Offset = new Vector(0, target);
                Layout(window);
                Check(view, $"{what}: sweep, page {page} {(direction > 0 ? "down" : "up")}");
                moved++;
            }
        }

        NestedTableVirtualizationTests.AssertTruthOnScreenIsLaidOut(view, twin.Of(view.State.Doc, window.Width));

        return $": sweep {pages} pages {(sign > 0 ? "down" : "up")} and back ({moved} moves)";
    }

    private static string Resize(Window window, RichTextView view, Random random, string what)
    {
        var width = new[] { 360.0, 440, 500, 620 }[random.Next(4)];
        var held = view.HeldLine;
        var y = held is { } line ? ScreenTop(view, line) : double.NaN;

        window.Width = width;
        Layout(window);

        if (held is { } kept && view.Offset.Y > 0 && view.Offset.Y < view.Extent.Height - Height)
        {
            var now = ScreenTop(view, kept);

            Assert.True(Math.Abs(now - y) <= 1, $"{what}: resize to {width}: the line held at the top moved from {y:F1} to {now:F1}");
        }

        return $": resize to {width}";
    }

    private static string EditFar(Window window, RichTextView view, Random random, string what)
    {
        if (FarParagraph(view, random) is not { } at)
        {
            return ": no far paragraph to edit";
        }

        // The caret on the line held at the top, as for a structural edit: an edit scrolls a caret
        // off screen into view, and a caret in another cell of the row can be moved off it. What
        // stays still is the held line and the lines in its cell; a line in another cell of the
        // same row moves when the edit grows what is above it in that cell, as it must — both
        // cannot be held.
        var held = PlaceOnHeldLine(window, view) ? view.HeldLine : null;
        var cell = held is { } line ? CellOf(view.State.Doc, line) : (-1, -1);
        var probes = Probes(view).Where(probe => held is null || CellOf(view.State.Doc, probe.Position) == cell).ToList();

        if (held is { } kept)
        {
            probes.Add((kept, ScreenTop(view, kept)));
        }

        var text = random.Next(2) == 0 ? " x" : string.Concat(Enumerable.Repeat(" more words in a far cell", random.Next(1, 30)));
        var tr = view.State.Transaction().InsertText(at, text);

        view.Apply(tr);
        Layout(window);

        AssertStill(view, probes, tr.Mapping, $"{what}: insert {text.Length} at {at}");

        return $": insert {text.Length} at {at}";
    }

    private static string Structural(Window window, RichTextView view, Random random, string what)
    {
        // A row inserted or deleted in a table somewhere, then often taken back and redone.
        if (Tables(view.State.Doc).ToList() is not { Count: > 0 } tables)
        {
            return ": no table";
        }

        var (table, start) = tables[random.Next(tables.Count)];

        // The line held at the top, followed through the edit by its mapping, and back through
        // the undo: it stays where it is unless the edit deleted it. With the caret on that line:
        // an edit scrolls a caret off screen into view, which moves everything, and a caret
        // anywhere else on screen can be moved off it by the edit — beside the held cell, text
        // hangs from the row's top, which moves when the held cell grows above the held line.
        var held = PlaceOnHeldLine(window, view) ? view.HeldLine : null;
        var y = held is { } line ? ScreenTop(view, line) : double.NaN;
        Transaction tr;
        string done;

        if (random.Next(2) == 0 && table.Rows.Length > 2 && table.Rows.All(r => r.Cells.All(c => c.RowSpan == 1)))
        {
            var row = random.Next(table.Rows.Length);
            var from = start + 1 + table.Rows.Take(row).Sum(r => r.NodeSize);

            tr = view.State.Transaction().Delete(from, from + table.Rows[row].NodeSize);
            done = $": delete row {row} of the table at {start}";
        }
        else
        {
            var row = random.Next(table.Rows.Length + 1);
            var at = start + 1 + table.Rows.Take(row).Sum(r => r.NodeSize);
            var columns = table.Rows[0].Cells.Sum(c => c.ColumnSpan);
            var spanned = table.Rows.Take(row).Select((r, i) => (r, i)).Any(x => x.r.Cells.Any(c => x.i + c.RowSpan > row));

            if (spanned)
            {
                return ": insert skipped under a merged cell";
            }

            var inserted = new TableRowNode(Enumerable.Range(0, columns).Select(c => TableCellNode.FromText($"inserted {c}")));

            tr = view.State.Transaction().Replace(at, at, new Slice([inserted], 0, 0));
            done = $": insert a row at {row} of the table at {start}";
        }

        view.Apply(tr);
        Layout(window);

        var mapped = held is { } h ? tr.Mapping.MapWithResult(h) : default;
        var kept = held is not null && !mapped.Deleted;

        if (kept)
        {
            AssertHeld(view, mapped.Pos, y, what + done);
        }

        if (random.Next(2) == 0)
        {
            Assert.True(view.Undo());
            Layout(window);

            if (kept)
            {
                AssertHeld(view, held!.Value, y, what + done + ", undone");
            }

            Assert.True(view.Redo());
            Layout(window);

            if (kept)
            {
                AssertHeld(view, mapped.Pos, y, what + done + ", undone, redone");
            }

            done += ", undone, redone";
        }

        return done;
    }

    /// <summary>The line held at the top is where it was, unless the offset is at either end.</summary>
    private static void AssertHeld(RichTextView view, int position, double y, string what)
    {
        if (view.Offset.Y <= 0 || view.Offset.Y >= view.Extent.Height - Height - 0.5)
        {
            return;
        }

        var now = ScreenTop(view, position);

        Assert.True(Math.Abs(now - y) <= 1, $"{what}: the line held at the top moved from {y:F1} to {now:F1}");
    }

    private static string Compose(Window window, RichTextView view, Random random)
    {
        var position = view.PositionAt(new Point(random.Next(10, (int)Width - 10), random.Next(20, (int)Height - 20)));

        view.MoveTo(position, extend: false);
        Layout(window);

        if (!view.SetPreedit(new string('W', random.Next(1, 200))))
        {
            return ": nothing to compose in";
        }

        Layout(window);
        Draw(window, view);
        Assert.True(view.SetPreedit(new string('M', random.Next(1, 300))));
        Layout(window);
        Draw(window, view);
        Assert.True(view.SetPreedit(null));
        Layout(window);

        return $": compose at {position}";
    }

    private static string AskFar(Window window, RichTextView view, Random random, string what, Twin twin)
    {
        var probes = Probes(view);
        string asked;
        Action<RichTextView, double>? answered = null;

        switch (random.Next(3))
        {
            case 0 when FarParagraph(view, random) is { } at:
                _ = view.ImageRect(at);
                asked = $": image rect at {at}";
                break;
            case 1:
                var y = random.Next(2) == 0 ? -random.Next(2_000, 20_000) : Height + random.Next(2_000, 20_000);
                var x = random.Next(10, (int)Width);
                var position = view.PositionAt(new Point(x, y));

                asked = $": position at y {y}";
                answered = (copy, shift) =>
                {
                    var expected = copy.PositionAt(new Point(x, y + shift));

                    Assert.True(position == expected, $"{what}{asked}: answered {position}, a fully laid-out copy {expected}");
                };
                break;
            default:
                var below = Height + random.Next(1_000, 10_000);
                var bx = random.Next(10, (int)Width);
                var boundary = view.ColumnBoundaryAt(new Point(bx, below), 4);

                asked = $": column boundary at y {below}";
                answered = (copy, shift) =>
                {
                    var expected = copy.ColumnBoundaryAt(new Point(bx, below + shift), 4);

                    Assert.True(
                        boundary?.TablePos == expected?.TablePos && boundary?.Boundary == expected?.Boundary,
                        $"{what}{asked}: answered {boundary?.TablePos}/{boundary?.Boundary}, a fully laid-out copy {expected?.TablePos}/{expected?.Boundary}");
                };
                break;
        }

        // Without a layout pass, and after one.
        AssertStill(view, probes, null, what + asked);
        Layout(window);
        AssertStill(view, probes, null, what + asked + ", laid out");

        // The answer is the one a fully laid-out copy gives, aligned on a line on screen.
        if (answered is not null && probes is [var (held, _), ..])
        {
            var fully = twin.Of(view.State.Doc, window.Width);

            answered(fully, ScreenTop(fully, held) - ScreenTop(view, held));
        }

        return asked;
    }

    /// <summary>A paragraph not laid out, so not on screen; its first text position.</summary>
    private static int? FarParagraph(RichTextView view, Random random)
    {
        var candidates = new List<int>();

        Paragraphs(view.State.Doc.Blocks, 0, candidates);

        for (var attempt = 0; attempt < 20 && candidates.Count > 0; attempt++)
        {
            var at = candidates[random.Next(candidates.Count)];
            var block = view.BlockIndexOf(at);

            if (!view.Blocks[block].IsPreparedAt(at - view.Slots[block].Start))
            {
                return at;
            }
        }

        return null;
    }

    private static void Paragraphs(IReadOnlyList<Node> nodes, int start, List<int> into)
    {
        var position = start;

        foreach (var node in nodes)
        {
            if (node is ParagraphNode)
            {
                into.Add(position + 1);
            }
            else if (node.Children.Length > 0)
            {
                Paragraphs(node.Children, position + 1, into);
            }

            position += node.NodeSize;
        }
    }

    private static IEnumerable<(TableNode Table, int Start)> Tables(DocumentNode doc)
    {
        var found = new List<(TableNode, int)>();

        Walk(doc.Blocks, 0);

        return found;

        void Walk(IReadOnlyList<Node> nodes, int start)
        {
            var position = start;

            foreach (var node in nodes)
            {
                if (node is TableNode table)
                {
                    found.Add((table, position));
                }

                if (node.Children.Length > 0)
                {
                    Walk(node.Children, position + 1);
                }

                position += node.NodeSize;
            }
        }
    }

    /// <summary>
    /// Where the innermost table cell holding a position starts and ends, or (-1, -1) for a
    /// position in no table.
    /// </summary>
    private static (int Start, int End) CellOf(DocumentNode doc, int position)
    {
        var found = (-1, -1);

        Walk(doc.Blocks, 0);

        return found;

        void Walk(IReadOnlyList<Node> nodes, int start)
        {
            var at = start;

            foreach (var node in nodes)
            {
                if (position > at && position < at + node.NodeSize)
                {
                    if (node is TableCellNode)
                    {
                        found = (at, at + node.NodeSize);
                    }

                    Walk(node.Children, at + 1);

                    return;
                }

                at += node.NodeSize;
            }
        }
    }

    /// <summary>Lines on screen, by a position on each and where its top is.</summary>
    private static List<(int Position, double Y)> Probes(RichTextView view)
    {
        var probes = new List<(int, double)>();

        for (var y = 20.0; y < Height - 20; y += 90)
        {
            for (var x = 10.0; x < Width; x += 120)
            {
                var position = view.PositionAt(new Point(x, y));
                var top = ScreenTop(view, position);

                if (top >= 0 && top <= Height - 20)
                {
                    probes.Add((position, top));
                }
            }
        }

        return probes;
    }

    private static void AssertStill(RichTextView view, List<(int Position, double Y)> probes, Mapping? mapping, string what)
    {
        // An offset at either end cannot hold anything still: the edge is where it is.
        if (view.Offset.Y <= 0 || view.Offset.Y >= view.Extent.Height - Height - 0.5)
        {
            return;
        }

        foreach (var (position, y) in probes)
        {
            var now = ScreenTop(view, mapping?.Map(position) ?? position);

            Assert.True(Math.Abs(now - y) <= 1, $"{what}: the line at {position} moved from {y:F1} to {now:F1}");
        }
    }

    private static void Check(RichTextView view, string what)
    {
        var (sum, agree) = view.HeightLedger();

        Assert.True(Math.Abs(view.Extent.Height - sum) <= 0.5, $"{what}: the extent is {view.Extent.Height:F1}, the heights add up to {sum:F1}");
        Assert.True(agree, $"{what}: a block laid out is not the height recorded for it");
        NestedTableVirtualizationTests.AssertScreenLaidOut(view);
        Assert.True(WorkCounters.CoverageRepairs == 0, $"{what}: the window left part of the viewport unlaid and was repaired");
        Assert.True(WorkCounters.FullLayoutBackstops == 0, $"{what}: every block was laid out as a backstop");
        Assert.True(WorkCounters.NestedWindowFallbacks == 0, $"{what}: a table in a cell was laid out whole as a fallback");
    }

    /// <summary>
    /// Every table laid out, at every depth: each block in a laid-out row is the height the row
    /// recorded for it — a table in a cell included, which is how a nested table's height reaches
    /// the presenter — and a row alone in its band is as tall as its tallest cell's content and
    /// padding, worked out here from the recorded heights.
    /// </summary>
    private static void CheckLedger(RichTextView view, string what)
    {
        for (var i = view.RealizedBlocks.Start.Value; i < view.RealizedBlocks.End.Value; i++)
        {
            if (view.Blocks[i] is TableBlockView table)
            {
                Ledger(table, $"block {i}");
            }
        }

        void Ledger(TableBlockView table, string path)
        {
            foreach (var (row, alone, height, cells) in table.LaidOutRows())
            {
                var tallest = 0.0;

                foreach (var (span, blocks) in cells)
                {
                    var content = 0.0;

                    foreach (var (block, recorded) in blocks)
                    {
                        Assert.True(
                            Math.Abs(block.Size.Height - recorded) <= 0.01,
                            $"{what}: {path}, row {row}: a {block.GetType().Name} {block.Size.Height:F1} tall is recorded as {recorded:F1}");

                        content += recorded;

                        if (block is TableBlockView nested)
                        {
                            Ledger(nested, $"{path}, row {row}");
                        }
                    }

                    if (span == 1)
                    {
                        tallest = Math.Max(tallest, content + (2 * TableBlockView.CellPadding));
                    }
                }

                Assert.True(
                    !alone || Math.Abs(height - tallest) <= 0.01,
                    $"{what}: {path}, row {row} is {height:F1} tall, its tallest cell {tallest:F1}");
            }
        }
    }

    /// <summary>
    /// Laying out again with nothing changed moves nothing on screen: what the last pass drew was
    /// placed by real heights, not by estimates a further pass replaces.
    /// </summary>
    private static void CheckSettled(Window window, RichTextView view, string what)
    {
        var probes = Probes(view);

        view.InvalidateMeasure();
        Layout(window);

        AssertStill(view, probes, null, what + ", laid out again");
    }

    private static void Reset()
    {
        WorkCounters.CoverageRepairs = 0;
        WorkCounters.FullLayoutBackstops = 0;
        WorkCounters.NestedWindowFallbacks = 0;
    }

    /// <summary>
    /// Puts the caret on a line on screen, so an edit scrolls nothing into view to show it. The
    /// position nearest the middle of the screen can be a cell's text far above it, beside a
    /// taller cell, so the line is one whose top is seen to be on screen.
    /// </summary>
    /// <returns>Whether there was such a line: otherwise the caret is where it was.</returns>
    private static bool Place(Window window, RichTextView view)
    {
        if (Probes(view) is not [var (position, _), ..])
        {
            return false;
        }

        view.MoveTo(position, extend: false);
        Layout(window);

        return true;
    }

    /// <summary>
    /// Puts the caret on the line held at the top of the viewport, or, with none held, on a line
    /// on screen (<see cref="Place"/>).
    /// </summary>
    /// <remarks>
    /// A held line partly above the viewport is scrolled wholly onto it first: an edit brings a
    /// caret that is partly off screen into view, which moved it to the top by the part that was
    /// off — fuzz seeds 10,015 and 10,042, where the held line was 10 pixels above the top.
    /// </remarks>
    private static bool PlaceOnHeldLine(Window window, RichTextView view)
    {
        if (view.HeldLine is not { } line)
        {
            return Place(window, view);
        }

        view.MoveTo(line, extend: false);
        Layout(window);

        if (ScreenTop(view, line) is var top and < 0)
        {
            view.Offset = new Vector(0, Math.Max(0, view.Offset.Y + top));
            Layout(window);
        }

        return true;
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

    private static void Draw(Window window, RichTextView view)
    {
        _ = view.CaretRect();

        using var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);

        Assert.NotNull(frame);
    }

    private static void Layout(Window window) => GeometryTransactionTests.Layout(window);

    private static double ScreenTop(RichTextView view, int position) => GeometryTransactionTests.ScreenTop(view, position);

    /// <summary>
    /// A fully laid-out copy of the document, kept across the steps of a run and brought up to the
    /// document and width asked for, so a question is compared with it without laying the whole
    /// document out from nothing each time.
    /// </summary>
    /// <param name="fresh">
    /// Whether a changed document is laid out afresh, the old copy collected first, rather than
    /// updated: for documents of long tables, whose copy is hundreds of megabytes and two at once
    /// doubled what a run peaked at. Not for the others: laying out afresh at every step made the
    /// recorded seeds three times slower (466 s against 157 s).
    /// </param>
    private sealed class Twin(bool fresh)
    {
        private RichTextView? _view;

        public RichTextView Of(DocumentNode doc, double width)
        {
            if (_view is null)
            {
                return _view = GeometryTransactionTests.FullyLaidOut(doc, width);
            }

            if (!ReferenceEquals(_view.State.Doc, doc) && !fresh)
            {
                _view.State = EditorState.Create(doc);
            }
            else if (!ReferenceEquals(_view.State.Doc, doc))
            {
                _view = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();

                return _view = GeometryTransactionTests.FullyLaidOut(doc, width);
            }

            _view.Measure(new Size(width, double.PositiveInfinity));
            _view.Arrange(new Rect(0, 0, width, _view.DesiredSize.Height));

            return _view;
        }
    }

    /// <summary>Random documents with tables in cells.</summary>
    internal sealed class Generator(Random random)
    {
        private int _text;

        /// <summary>Whether a table in a cell is now and then a thousand rows long.</summary>
        public bool LongTables { get; init; }

        public DocumentNode Document()
        {
            var blocks = new List<BlockNode>();

            blocks.AddRange(Paragraphs(random.Next(0, 3)));
            blocks.Add(Outer());

            if (random.Next(2) == 0)
            {
                blocks.Add(List());
            }

            blocks.AddRange(Paragraphs(random.Next(1, 3)));

            return new DocumentNode(blocks);
        }

        private TableNode Outer()
        {
            var rows = random.Next(2, 7);
            var columns = random.Next(1, 4);

            // Now and then a cell in the first column spanning two rows.
            var merged = columns > 1 && random.Next(3) == 0 ? random.Next(rows - 1) : -1;

            return new TableNode(Enumerable.Range(0, rows).Select(r => new TableRowNode(r == merged + 1 && merged >= 0
                ? Enumerable.Range(1, columns - 1).Select(_ => Cell(depth: 1)).ToList()
                : Enumerable.Range(0, columns).Select(c => r == merged && c == 0
                    ? new TableCellNode(Content(depth: 1), rowSpan: 2)
                    : Cell(depth: 1)).ToList())));
        }

        private TableCellNode Cell(int depth) => new(Content(depth));

        private List<BlockNode> Content(int depth)
        {
            var blocks = new List<BlockNode>();
            var count = random.Next(1, 4);

            for (var i = 0; i < count; i++)
            {
                var pick = random.Next(10);

                blocks.Add(pick switch
                {
                    < 4 when depth <= 3 => Nested(depth),
                    < 6 => Paragraph(wraps: true),
                    6 => List(),
                    _ => Paragraph(wraps: false),
                });
            }

            return blocks;
        }

        private TableNode Nested(int depth)
        {
            // Long tables are of short cells: the fully laid-out copy every step is compared with
            // lays the whole document out, and long tables of the usual cells made that copy
            // gigabytes.
            var isLong = depth == 1 && LongTables && random.Next(3) == 0;
            var rows = depth switch
            {
                _ when isLong => random.Next(300, 900),
                1 => random.Next(30, 300),
                2 => random.Next(10, 120),
                _ => random.Next(5, 60),
            };
            var columns = random.Next(1, 4);

            return new TableNode(Enumerable.Range(0, rows).Select(_ => new TableRowNode(Enumerable.Range(0, columns).Select(_ =>
            {
                var pick = random.Next(100);

                return pick switch
                {
                    < 4 when isLong => new TableCellNode(Paragraphs(random.Next(2, 5))),
                    _ when isLong => new TableCellNode([Paragraph(wraps: false)]),
                    < 2 when depth < 3 => Cell(depth + 1),
                    < 10 => new TableCellNode(Paragraphs(random.Next(3, 9))),
                    _ => new TableCellNode([Paragraph(wraps: false)]),
                };
            }))));
        }

        private ListNode List() => new(
            Enumerable.Range(0, random.Next(2, 5)).Select(_ => new ListItemNode([Paragraph(wraps: random.Next(2) == 0)])),
            random.Next(2) == 0 ? ListKind.Ordered : ListKind.Unordered);

        private List<BlockNode> Paragraphs(int count) =>
            [.. Enumerable.Range(0, count).Select(_ => (BlockNode)Paragraph(wraps: random.Next(3) == 0))];

        private ParagraphNode Paragraph(bool wraps)
        {
            var tag = $"t{_text++}";

            return ParagraphNode.FromText(wraps
                ? tag + " " + string.Join(" ", Enumerable.Repeat("words that wrap across the cell", random.Next(2, 12)))
                : tag);
        }
    }
}
