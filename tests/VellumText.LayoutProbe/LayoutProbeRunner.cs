using System.Diagnostics;
using System.Text;

namespace VellumText.LayoutProbe;

/// <summary>What a layout probe run produced.</summary>
/// <param name="ExitCode">The child's exit code; 0 means the scenario returned.</param>
/// <param name="Output">Standard output: <c>key=value</c> lines.</param>
/// <param name="Error">Standard error, which carries the runtime's crash report.</param>
/// <param name="Elapsed">How long the child ran, start to exit.</param>
public sealed record LayoutProbeResult(int ExitCode, string Output, string Error, TimeSpan Elapsed)
{
    /// <summary>The value of the first <c>key=…</c> line as a number.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The child printed no such line.</exception>
    public long Value(string key) =>
        Output.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .FirstOrDefault(line => line.StartsWith(key + "=", StringComparison.Ordinal)) is { } line
            ? long.Parse(line[(key.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"the probe printed no {key}:\n{this}");

    /// <inheritdoc/>
    public override string ToString()
    {
        var error = Error.Length > 600 ? Error[..600] + "…" : Error;

        return $"exit={ExitCode} (0x{ExitCode:X8}) after {Elapsed.TotalSeconds:F1} s\nstdout:\n{Output}\nstderr:\n{error}";
    }
}

/// <summary>Runs a layout scenario in a child process.</summary>
public static class LayoutProbeRunner
{
    /// <summary>Runs <see cref="Program"/> with <paramref name="arguments"/> in a fresh process.</summary>
    /// <param name="timeout">How long to wait before killing the child.</param>
    /// <param name="arguments">Depth, rows and scenario.</param>
    /// <returns>The child's exit code, output and running time.</returns>
    public static LayoutProbeResult Run(TimeSpan timeout, params string[] arguments)
    {
        var probe = typeof(Program).Assembly.Location;
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } named
            && File.Exists(named)
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
        info.ArgumentList.Add(probe);

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // The runtime's own crash dialog and dump would turn a failing test into a hang.
        info.Environment["DOTNET_DbgEnableMiniDump"] = "0";

        var watch = Stopwatch.StartNew();

        using var child = Process.Start(info)
            ?? throw new InvalidOperationException("could not start the probe");

        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();

        if (!child.WaitForExit(timeout))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();

            return new LayoutProbeResult(-1, output.Result, "timed out\n" + error.Result, watch.Elapsed);
        }

        return new LayoutProbeResult(child.ExitCode, output.Result, error.Result, watch.Elapsed);
    }
}