using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Avalonia.Headless.XUnit;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Runs a test body in a child process of this test assembly, killed and failed when it outgrows a
/// memory cap or a time limit.
/// </summary>
/// <remarks>
/// <para>
/// For every test that walks or edits a document long enough to grow without bound if the layout
/// misbehaves — traversals, letting rows go and laying them out again, the nested-table fuzz. Run in
/// the test process, one such defect grew it to a 40 GB working set on a shared machine before
/// anyone noticed; a test host that runs away takes every test with it, and one that dies can still
/// report the tests it finished as passed. Here the child's exit code, peak working set and running
/// time are asserted from outside, as <c>LayoutProbeRunner</c> does for the layout probe.
/// </para>
/// <para>
/// The child is this assembly run as its own xUnit runner with one test selected,
/// <see cref="IsolatedChild.Run"/>, which calls the static method named in
/// <see cref="Variable"/>. The managed heap is also capped in the child
/// (<c>DOTNET_GCHeapHardLimit</c>), so a runaway fails with an out-of-memory error at the cap rather
/// than only when the working set is next sampled.
/// </para>
/// </remarks>
internal static class Isolated
{
    /// <summary>The environment variable naming the body a child runs: type, method and arguments.</summary>
    internal const string Variable = "VELLUM_ISOLATED";

    /// <summary>The working set a child is killed at, unless the caller says otherwise.</summary>
    internal const long MemoryCap = 2L << 30;

    /// <summary>How long a child may run before it is killed.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>Runs a static method of this assembly in a child process and fails if it does.</summary>
    /// <param name="type">The type declaring the method.</param>
    /// <param name="method">The method's name; it takes only arguments convertible from strings.</param>
    /// <param name="args">Its arguments.</param>
    internal static void Run(Type type, string method, params object[] args) => Run(Timeout, MemoryCap, type, method, args);

    /// <summary>Runs a static method in a child process with limits of its own.</summary>
    /// <param name="timeout">How long the child may run.</param>
    /// <param name="memoryCap">The working set it is killed at; its managed heap is capped at three quarters of it.</param>
    /// <param name="type">The type declaring the method.</param>
    /// <param name="method">The method's name.</param>
    /// <param name="args">Its arguments.</param>
    internal static void Run(TimeSpan timeout, long memoryCap, Type type, string method, params object[] args)
    {
        var call = string.Join('|', new[] { type.FullName!, method }.Concat(args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)!)));
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } named && File.Exists(named)
            ? named
            : "dotnet";
        var info = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };

        info.ArgumentList.Add("exec");
        info.ArgumentList.Add(typeof(Isolated).Assembly.Location);
        info.ArgumentList.Add("-method");
        info.ArgumentList.Add(typeof(IsolatedChild).FullName + "." + nameof(IsolatedChild.Run));
        info.ArgumentList.Add("-noColor");
        info.Environment[Variable] = call;
        info.Environment["DOTNET_GCHeapHardLimit"] = "0x" + (memoryCap / 4 * 3).ToString("X", CultureInfo.InvariantCulture);

        // The runtime's own crash dialog and dump would turn a failing test into a hang.
        info.Environment["DOTNET_DbgEnableMiniDump"] = "0";

        var watch = Stopwatch.StartNew();
        using var child = Process.Start(info) ?? throw new InvalidOperationException("could not start the child");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        var peak = 0L;
        string? killed = null;

        while (!child.WaitForExit(100))
        {
            try
            {
                child.Refresh();
                peak = Math.Max(peak, child.WorkingSet64);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            killed = peak > memoryCap ? $"its working set reached {peak:N0} bytes, past the cap of {memoryCap:N0}"
                : watch.Elapsed > timeout ? $"it ran past {timeout.TotalSeconds:F0} s"
                : null;

            if (killed is not null)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();

                break;
            }
        }

        child.WaitForExit();

        var text = output.Result + error.Result;
        var failed = text.IndexOf("[FAIL]", StringComparison.Ordinal);
        var tail = failed >= 0
            ? text[failed..Math.Min(text.Length, failed + 3_000)]
            : text.Length > 4_000 ? "…" + text[^4_000..] : text;
        var summary = $"{call} in a child process: exit {child.ExitCode}, {watch.Elapsed.TotalSeconds:F1} s, peak working set {peak:N0} bytes";

        TestContext.Current.TestOutputHelper?.WriteLine(summary);

        if (Environment.GetEnvironmentVariable("VELLUM_ISOLATED_LOG") is { Length: > 0 } log)
        {
            File.AppendAllText(log, summary + Environment.NewLine);
        }

        Assert.True(killed is null, $"{summary}; killed because {killed}\n{tail}");
        Assert.True(child.ExitCode == 0 && text.Contains("Total: 1,", StringComparison.Ordinal), $"{summary}\n{tail}");
    }
}

/// <summary>
/// The tests that start child processes, run one at a time and alone, so no two children of a few
/// gigabytes each run at once on a shared machine.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ChildProcessCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Child processes";
}

/// <summary>The one test a child process of <see cref="Isolated"/> runs.</summary>
public class IsolatedChild
{
    /// <summary>
    /// Calls the static method <see cref="Isolated.Variable"/> names, on the UI thread; in any other
    /// process, does nothing.
    /// </summary>
    [AvaloniaFact]
    public void Run()
    {
        if (Environment.GetEnvironmentVariable(Isolated.Variable) is not { Length: > 0 } call)
        {
            return;
        }

        var parts = call.Split('|');
        var type = typeof(IsolatedChild).Assembly.GetType(parts[0], throwOnError: true)!;
        var method = type.GetMethod(parts[1], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(parts[0], parts[1]);
        var parameters = method.GetParameters();
        var args = parameters.Select((p, i) => Convert.ChangeType(parts[i + 2], p.ParameterType, CultureInfo.InvariantCulture)).ToArray();

        try
        {
            method.Invoke(null, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
        }
    }
}
