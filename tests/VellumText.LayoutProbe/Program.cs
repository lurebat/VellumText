using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.LayoutProbe;

/// <summary>Lays out a long table — at the top level or nested in cells — and reports what it cost.</summary>
/// <remarks>
/// <para>
/// Usage: <c>VellumText.LayoutProbe &lt;depth&gt; &lt;rows&gt; [scenario] [side|hosts]</c>. Depth 0 is
/// a table of <c>rows</c> × 3 at the top of the document; depth <c>d</c> puts that table in the
/// only cell of a one-row table, <c>d</c> times over — or, with <c>side</c>, in the first of two
/// cells, the second holding a line of text. With <c>hosts</c>, the first cell of every row of the
/// long table holds a table of two rows of its own instead of text. Output is <c>key=value</c>
/// lines; the exit code is 0 when everything returned.
/// </para>
/// <para>
/// Scenarios: <c>profile</c> (the default) times and weighs a first layout, a resize, a keystroke
/// at the top of the long table, a frame and a jump to its middle row; <c>traverse</c> scrolls
/// through it a page at a time and reports the heap left after.
/// </para>
/// </remarks>
public static class Program
{
    private const double Width = 800;
    private const double Height = 600;

    private static bool side;

    private static bool hosts;

    /// <summary>Entry point.</summary>
    /// <param name="args">Depth, rows, and optionally the scenario.</param>
    /// <returns>0 when the scenario returned; 2 for bad arguments.</returns>
    public static int Main(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out var depth) || !int.TryParse(args[1], out var rows))
        {
            Console.Error.WriteLine("usage: VellumText.LayoutProbe <depth> <rows> [profile|traverse] [side|hosts]");

            return 2;
        }

        var scenario = args.Length > 2 ? args[2] : "profile";

        side = args.Length > 3 && args[3] == "side";
        hosts = args.Length > 3 && args[3] == "hosts";

        AppBuilder
            .Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        // Loading and compiling the layout code costs most of a second the first time, which would
        // swamp the first figure; a small table of the same shape pays it.
        Profile(depth, 50, report: false);

        switch (scenario)
        {
            case "profile":
                Profile(depth, rows, report: true);
                break;
            case "traverse":
                Traverse(depth, rows);
                break;
            case "retention-diagnostics":
                Traverse(depth, rows, diagnostics: true);
                break;
            default:
                Console.Error.WriteLine($"unknown scenario '{scenario}'");

                return 2;
        }

        using (var self = Process.GetCurrentProcess())
        {
            Console.Out.WriteLine($"peakWorkingSetBytes={self.PeakWorkingSet64}");
        }

        return 0;
    }

    private static void Profile(int depth, int rows, bool report)
    {
        var doc = Document(depth, rows);

        Settle();

        var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var (bytes, ms) = Cost(out var shown, () => Show(doc));
        var (window, view) = shown;
        var retained = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;

        Report(report, "firstLayoutBytes", bytes);
        Report(report, "firstLayoutMs", ms);
        Report(report, "retainedBytes", retained);

        (bytes, ms) = Cost(out _, () =>
        {
            window.Width = Width - 120;
            Layout(window);

            return 0;
        });

        Report(report, "resizeBytes", bytes);
        Report(report, "resizeMs", ms);

        var first = Find(doc, "Row 0 column 1 value 0");

        view.State = view.State.Apply(view.State.Transaction().SetSelection(TextSelection.Cursor(first + 3)));
        Layout(window);

        (bytes, ms) = Cost(out _, () =>
        {
            var state = view.State;
            var tr = state.Transaction();

            tr.InsertText(first + 3, "x");
            view.State = state.Apply(tr.SetSelection(TextSelection.Cursor(first + 4)));
            Layout(window);

            return 0;
        });

        Report(report, "keystrokeBytes", bytes);
        Report(report, "keystrokeMs", ms);

        (bytes, ms) = Cost(out _, () =>
        {
            using var frame = window.CaptureRenderedFrame();

            return frame is null ? 0 : 1;
        });

        Report(report, "frameBytes", bytes);
        Report(report, "frameMs", ms);

        var middle = Find(view.State.Doc, $"Row {rows / 2} column 1 value {rows / 2}");

        (bytes, ms) = Cost(out _, () =>
        {
            view.State = view.State.Apply(view.State.Transaction().SetSelection(TextSelection.Cursor(middle + 3)));
            Layout(window);

            return 0;
        });

        Report(report, "caretToMiddleBytes", bytes);
        Report(report, "caretToMiddleMs", ms);
        Report(report, "caretToMiddleOnScreen", view.CaretRect().Top is >= 0 and < Height ? 1 : 0);

        GC.KeepAlive(window);
        window.Close();
    }

    private static void Traverse(int depth, int rows, bool diagnostics = false)
    {
        var doc = Document(depth, rows);

        Settle();

        var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var (window, view) = Show(doc);
        Settle();
        var afterFirst = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;
        var watch = Stopwatch.StartNew();
        var pages = 0;
        var observed = new List<WeakReference>();

        for (var offset = 0.0; offset < view.Extent.Height - Height; offset += Height)
        {
            view.Offset = new Vector(0, offset);
            Layout(window);
            if (diagnostics && pages % 100 == 0)
            {
                observed.AddRange(RealizedViews(view).Select(item => new WeakReference(item)));
            }
            pages++;
        }

        watch.Stop();

        var beforeFinalizers = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;

        // Use the same finalizer-drained boundary as the baseline; also report the
        // raw collection so disposal backlog can be distinguished from live retention.
        Settle();

        var afterTraverse = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;

        Console.Out.WriteLine($"retainedBeforeFinalizersBytes={beforeFinalizers}");
        Console.Out.WriteLine($"retainedAfterFirstLayoutBytes={afterFirst}");
        Console.Out.WriteLine($"retainedAfterTraverseBytes={afterTraverse}");
        Console.Out.WriteLine($"pages={pages}");
        Console.Out.WriteLine($"extentAfterTraverse={(long)view.Extent.Height}");
        Console.Out.WriteLine($"traverseMs={watch.ElapsedMilliseconds}");
        if (diagnostics)
        {
            var reachable = RealizedViews(view).ToHashSet(ReferenceEqualityComparer.Instance);
            var alive = observed.Select(reference => reference.Target).OfType<object>()
                .ToHashSet(ReferenceEqualityComparer.Instance);
            Console.Out.WriteLine($"reachableBlockViews={reachable.Count}");
            Console.Out.WriteLine($"observedBlockViewsStillAlive={alive.Count}");
            Console.Out.WriteLine($"evictedBlockViewsStillAlive={alive.Count(item => !reachable.Contains(item))}");
            Console.Out.WriteLine($"realizedNestedRows={reachable.Sum(RealizedRows)}");
        }

        GC.KeepAlive(window);
        window.Close();
    }

    private static int RealizedRows(object view) =>
        view.GetType().GetProperty("RealizedRows", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)?.GetValue(view) is int count ? count : 0;

    private static IEnumerable<object> RealizedViews(object parent)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var blocks = parent.GetType().GetProperty("Blocks", flags)?.GetValue(parent) as System.Collections.IEnumerable;
        if (blocks is not null)
        {
            foreach (var block in blocks)
            {
                yield return block;
                foreach (var nested in RealizedViews(block))
                {
                    yield return nested;
                }
            }
        }
        var rows = parent.GetType().GetField("_rows", flags)?.GetValue(parent) as System.Collections.IEnumerable;
        if (rows is null)
        {
            yield break;
        }
        foreach (var row in rows)
        {
            if (row.GetType().GetProperty("Cells", flags)?.GetValue(row) is not System.Collections.IEnumerable cells)
            {
                continue;
            }
            foreach (var cell in cells)
            {
                var children = (System.Collections.IEnumerable)cell.GetType().GetProperty("Blocks", flags)!.GetValue(cell)!;
                foreach (var child in children)
                {
                    var block = child.GetType().GetProperty("View", flags)!.GetValue(child)!;
                    yield return block;
                    foreach (var nested in RealizedViews(block))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    /// <summary>A rows × 3 table, nested in a one-row table <paramref name="depth"/> times.</summary>
    private static DocumentNode Document(int depth, int rows)
    {
        BlockNode table = new TableNode(Enumerable.Range(0, rows).Select(r => new TableRowNode(
            Enumerable.Range(0, 3).Select(c => hosts && c == 0
                ? new TableCellNode([new TableNode(Enumerable.Range(0, 2).Select(i => new TableRowNode([
                    TableCellNode.FromText($"Row {r} inner {i} left"),
                    TableCellNode.FromText($"Row {r} inner {i} right"),
                ])))])
                : TableCellNode.FromText($"Row {r} column {c} value {r * c}")))));

        for (var d = 0; d < depth; d++)
        {
            table = new TableNode([new TableRowNode(side
                ? [new TableCellNode([table]), TableCellNode.FromText($"Beside the table at depth {d}.")]
                : [new TableCellNode([table])])]);
        }

        return new DocumentNode([table, ParagraphNode.FromText("After the table.")]);
    }

    private static (Window Window, RichTextView View) Show(DocumentNode doc)
    {
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

    private static (long Bytes, long Ms) Cost<T>(out T result, Func<T> run)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();

        result = run();

        watch.Stop();

        return (GC.GetAllocatedBytesForCurrentThread() - before, watch.ElapsedMilliseconds);
    }

    private static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Report(bool report, string key, long value)
    {
        if (report)
        {
            Console.Out.WriteLine($"{key}={value}");
        }
    }

    /// <summary>The first position inside the paragraph reading <paramref name="text"/>.</summary>
    private static int Find(DocumentNode doc, string text)
    {
        return Walk(doc.Blocks, 0) ?? throw new InvalidOperationException($"No paragraph reads {text}.");

        int? Walk(IReadOnlyList<Node> nodes, int start)
        {
            var position = start;

            foreach (var node in nodes)
            {
                if (node is ParagraphNode paragraph && paragraph.Content.Text == text)
                {
                    return position + 1;
                }

                if (node.Children.Length > 0 && Walk(node.Children, position + 1) is { } found)
                {
                    return found;
                }

                position += node.NodeSize;
            }

            return null;
        }
    }
}
