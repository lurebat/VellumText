using Avalonia;
using Avalonia.Headless;

namespace VellumText.Bench;

/// <summary>
/// Runs the performance scenarios that the review findings are claims about.
/// </summary>
/// <remarks>
/// <para>
/// A console tool rather than a test. The numbers here are noisy by nature and depend on the
/// machine, so making them assertions in the unit suite would buy a flaky build and a habit of
/// ignoring it. What this is for is answering "did that change help, and by how much" with
/// something better than an opinion — and for making <c>architecture.md</c>'s AC12 budget a
/// thing that gets looked at rather than a sentence.
/// </para>
/// <para>
/// Usage: <c>dotnet run -c Release --project bench/VellumText.Bench -- [scenario...]</c>, or with
/// no arguments to run all of them.
/// </para>
/// </remarks>
internal static class Program
{
    private static readonly (string Name, Action Run)[] All =
    [
        ("keystroke", Scenarios.Keystroke),
        ("caret", Scenarios.CaretMove),
        ("render", Scenarios.FirstRender),
        ("marks", Scenarios.MarkSpans),
        ("rtf", Scenarios.RtfColours),
        ("heights", Scenarios.HeightIndex),
        ("resolve", Scenarios.ResolvePosition),
        ("find", Scenarios.FindQuery),
        ("ac12", Scenarios.Ac12),
        ("model", Scenarios.ModelEdit),

        // The codebase audit's reproductions; see AuditScenarios.
        ("a-undo", AuditScenarios.UndoTypingRun),
        ("a-undofmt", AuditScenarios.UndoFormat),
        ("a-longpara", AuditScenarios.LongParagraph),
        ("a-table", AuditScenarios.TableCell),
        ("a-findrender", AuditScenarios.FindRender),
        ("a-selectall", AuditScenarios.SelectAll),
        ("a-paste", AuditScenarios.Paste),
        ("a-rtf", AuditScenarios.RtfImport),
        ("a-exports", AuditScenarios.Exports),
        ("a-images", AuditScenarios.Images),
        ("a-histmem", AuditScenarios.HistoryMemory),
        ("a-structure", AuditScenarios.StructureKeys),
        ("a-stages", AuditScenarios.Stages),
        ("a-sustained", AuditScenarios.Sustained),
        ("a-mdspaces", AuditScenarios.MarkdownSpaces),
        ("a-htmlstages", AuditScenarios.HtmlStages),
        ("a-preview", AuditScenarios.Preview),

        // The table-edit scaling research (docs/plans/2026-10-28-table-edit-scaling-research.md).
        ("r-table", ResearchScenarios.Table),
        ("r-tablestruct", ResearchScenarios.TableStructure),
        ("r-paths", ResearchScenarios.Paths),
        ("r-heavy", ResearchScenarios.Heavy),
        ("r-paste", ResearchScenarios.PasteAndLong),
    ];

    private static int Main(string[] args)
    {
        // The same headless configuration as the Avalonia tests. UseHeadlessDrawing=false is
        // load-bearing: the headless drawing stack does not shape text and reports invented
        // metrics, so layout would be measured against a stub rather than against Skia.
        AppBuilder
            .Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var wanted = args.Length == 0
            ? All
            : All.Where(s => args.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (wanted.Length == 0)
        {
            Console.Error.WriteLine($"No such scenario. Known: {string.Join(", ", All.Select(s => s.Name))}");

            return 1;
        }

        Console.WriteLine($"# VellumText benchmarks — {DateTime.Now:yyyy-MM-dd HH:mm}");
        Console.WriteLine();
        Console.WriteLine("Read both ratio columns. Allocation is deterministic, so it establishes the");
        Console.WriteLine("shape without needing a quiet machine — but it is blind to work that allocates");
        Console.WriteLine("nothing. Allocation growing means the whole document is being touched; time");
        Console.WriteLine("growing faster than allocation means something is scanning.");

        foreach (var (name, run) in wanted)
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                // A scenario that throws must say so loudly. A benchmark that silently measures
                // a failed operation reports a wonderful number for doing nothing, which is
                // exactly how a reviewer here was recently fooled into benchmarking a no-op.
                Console.WriteLine();
                Console.WriteLine($"## {name} FAILED");
                Console.WriteLine(ex);

                return 2;
            }
        }

        return 0;
    }
}
