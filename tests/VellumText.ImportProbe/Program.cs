using System.Diagnostics;
using System.Text;

using VellumText;
using VellumText.Interop.Html;
using VellumText.Interop.Json;
using VellumText.Interop.Markdown;
using VellumText.Interop.Rtf;

namespace VellumText.ImportProbe;

/// <summary>Imports one file in this process and reports what came out.</summary>
/// <remarks>
/// <para>
/// Usage: <c>VellumText.ImportProbe &lt;format&gt; &lt;input-file&gt;</c>, where format is one of
/// <c>html</c>, <c>html-unbounded</c>, <c>html-clipboard</c>, <c>markdown</c>,
/// <c>markdown-current-stack</c>, <c>rtf</c>, <c>rtf-no-data-images</c>, <c>rtf-small-data-images</c>
/// or <c>json</c>.
/// </para>
/// <para>
/// The import is called from a thread with a 1 MiB stack — what a Windows main thread or a
/// thread-pool thread gets — rather than from this process's main thread, whose size differs by
/// platform (8 MiB on Linux). The point is to stand in for a host calling the importer from an
/// ordinary thread, so the protection has to come from the importer and not from the caller.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>The caller's stack, in bytes.</summary>
    public const int CallerStackBytes = 1024 * 1024;

    /// <summary>Entry point.</summary>
    /// <param name="args">Format and input path.</param>
    /// <returns>0 when the import returned; 2 for bad arguments.</returns>
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: VellumText.ImportProbe <format> <input-file>");
            return 2;
        }

        var format = args[0];
        var input = File.ReadAllText(args[1], Encoding.UTF8);
        ImportResult? result = null;
        Exception? failure = null;

        // Loading and compiling the importer costs most of a second the first time, which would
        // swamp the timing of a small payload.
        Import(format, string.Empty);

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();

        var caller = new Thread(
            () =>
            {
                try
                {
                    result = Import(format, input);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            CallerStackBytes);

        caller.Start();
        caller.Join();
        watch.Stop();

        // Process-wide, because the import runs on its own thread.
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        if (failure is not null)
        {
            Console.Out.WriteLine($"threw={failure.GetType().Name}: {failure.Message}");
            return 3;
        }

        var doc = result!.Doc;

        Console.Out.WriteLine($"ms={watch.ElapsedMilliseconds}");
        Console.Out.WriteLine($"allocatedMb={allocated / (1024 * 1024)}");
        Console.Out.WriteLine($"blocks={doc.Children.Length}");
        Console.Out.WriteLine($"text={DocumentText.Of(doc).Replace("\n", "\\n", StringComparison.Ordinal)}");

        foreach (var diagnostic in result.Diagnostics)
        {
            Console.Out.WriteLine($"diagnostic={diagnostic.Severity}|{diagnostic.Context}|{diagnostic.Message}");
        }

        return 0;
    }

    private static readonly HtmlImportOptions Unbounded = HtmlImportOptions.Default with
    {
        MaxElementDepth = int.MaxValue,
        MaxStyleNesting = int.MaxValue,
        MaxAttributesPerElement = int.MaxValue,
    };

    /// <remarks>
    /// Blocks on the task, which a console process with no synchronization context can do safely,
    /// so that the import is started from the small caller stack the probe exists to give it.
    /// </remarks>
    private static ImportResult Import(string format, string input) => format switch
    {
        "html" => HtmlImporter.ImportAsync(input).GetAwaiter().GetResult(),

        // The pre-parse limits lifted, so that the large stack is the only protection left.
        "html-unbounded" => HtmlImporter.ImportAsync(input, Unbounded).GetAwaiter().GetResult(),
        "html-clipboard" => HtmlImporter.ImportClipboardAsync(input).GetAwaiter().GetResult(),
        "markdown" => MarkdownImporter.ImportAsync(input).GetAwaiter().GetResult(),

        // The Markdown stage without the large stack in front of it; see ImportOnCurrentStack.
        "markdown-current-stack" => MarkdownImporter.ImportOnCurrentStack(input, MarkdownImportOptions.Default),
        "rtf" => RtfImporter.ImportAsync(input).GetAwaiter().GetResult(),
        "rtf-no-data-images" => RtfImporter.ImportAsync(input, new RtfImportOptions { AllowDataImages = false }).GetAwaiter().GetResult(),
        "rtf-small-data-images" => RtfImporter.ImportAsync(input, new RtfImportOptions { MaxDataImageBytes = 1024 }).GetAwaiter().GetResult(),
        "json" => JsonFormat.Instance.ImportAsync(input).GetAwaiter().GetResult(),
        _ => throw new ArgumentException($"unknown format '{format}'"),
    };
}
