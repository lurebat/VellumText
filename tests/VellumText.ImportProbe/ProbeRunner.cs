using System.Diagnostics;
using System.Text;

namespace VellumText.ImportProbe;

/// <summary>What a probe run produced.</summary>
/// <param name="ExitCode">The child's exit code; 0 means the import returned.</param>
/// <param name="Output">Standard output: <c>key=value</c> lines.</param>
/// <param name="Error">Standard error, which carries the runtime's stack-overflow report.</param>
public sealed record ProbeResult(int ExitCode, string Output, string Error)
{
    /// <summary>The value of the first <c>key=…</c> line, or null.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>
    public string? Value(string key) =>
        Output.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .FirstOrDefault(line => line.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..];

    /// <summary>Every diagnostic line, as <c>severity|context|message</c>.</summary>
    public IEnumerable<string> Diagnostics =>
        Output.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("diagnostic=", StringComparison.Ordinal))
            .Select(line => line["diagnostic=".Length..]);

    /// <inheritdoc/>
    public override string ToString()
    {
        var error = Error.Length > 600 ? Error[..600] + "…" : Error;
        var output = Output.Length > 600 ? Output[..600] + "…" : Output;

        return $"exit={ExitCode} (0x{ExitCode:X8})\nstdout:\n{output}\nstderr:\n{error}";
    }
}

/// <summary>Runs an import in a child process.</summary>
public static class ProbeRunner
{
    /// <summary>Imports <paramref name="input"/> as <paramref name="format"/> in a fresh process.</summary>
    /// <param name="format">See <see cref="Program"/>.</param>
    /// <param name="input">The payload.</param>
    /// <param name="timeout">How long to wait before killing the child.</param>
    /// <returns>The child's exit code and output.</returns>
    public static ProbeResult Run(string format, string input, TimeSpan? timeout = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vt-probe-{Guid.NewGuid():N}.txt");

        File.WriteAllText(path, input, new UTF8Encoding(false));

        try
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
            info.ArgumentList.Add(format);
            info.ArgumentList.Add(path);

            // The runtime's own crash dialog and dump would turn a failing test into a hang.
            info.Environment["DOTNET_DbgEnableMiniDump"] = "0";

            using var child = Process.Start(info)
                ?? throw new InvalidOperationException("could not start the probe");

            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();

            if (!child.WaitForExit(timeout ?? TimeSpan.FromMinutes(2)))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();

                return new ProbeResult(-1, output.Result, "timed out\n" + error.Result);
            }

            return new ProbeResult(child.ExitCode, output.Result, error.Result);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
