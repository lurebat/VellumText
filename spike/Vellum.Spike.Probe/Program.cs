using BenchmarkDotNet.Running;
using Vellum.Spike.Probe;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "probe";

if (mode == "bench")
{
    // BenchmarkDotNet spawns its own process; Avalonia is initialized there in [GlobalSetup].
    BenchmarkRunner.Run<A1Bench>();
    return 0;
}

Host.EnsureInitialized();
Console.WriteLine($"Results directory: {Host.ResultsDir}\n");

var failures = 0;

if (mode is "probe" or "a2")
{
    var a2 = A2Probe.Run();
    a2.Save(Path.Combine(Host.ResultsDir, "a2-drawabletextrun.md"));
    failures += a2.Failures;
}

if (mode is "probe" or "c1")
{
    var c1 = C1Probe.Run();
    c1.Save(Path.Combine(Host.ResultsDir, "c1-textstyleoverrides.md"));
    failures += c1.Failures;
}

if (mode is "probe" or "bidi")
{
    var bidi = BidiProbe.Run();
    bidi.Save(Path.Combine(Host.ResultsDir, "bidi-graphemes.md"));
    failures += bidi.Failures;
}

if (mode is "probe" or "a1" or "a1diag")
{
    var diag = A1Diag.Run();
    diag.Save(Path.Combine(Host.ResultsDir, "a1-diagnostic.md"));
    failures += diag.Failures;
}

if (mode is "probe" or "a1")
{
    var a1 = A1Manual.Run();
    a1.Save(Path.Combine(Host.ResultsDir, "a1-textruncache-stopwatch.md"));
    failures += a1.Failures;
}

Console.WriteLine($"\n=== total failed checks: {failures} ===");
return 0;
