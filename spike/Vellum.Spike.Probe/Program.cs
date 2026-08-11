using BenchmarkDotNet.Running;
using Vellum.Spike.Probe;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "probe";

var known = new[] { "probe", "a2", "c1", "bidi", "a1", "a1diag", "a1width", "review", "bench" };
if (!known.Contains(mode))
{
    Console.Error.WriteLine($"unknown mode '{mode}'. known modes: {string.Join(", ", known)}");
    return 2;
}

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

if (mode is "probe" or "review")
{
    var rev = ReviewCheck.Run();
    rev.Save(Path.Combine(Host.ResultsDir, "review-verification.md"));
    failures += rev.Failures;
}

if (mode is "probe" or "a1" or "a1width")
{
    var width = A1Width.Run();
    width.Save(Path.Combine(Host.ResultsDir, "a1-width-only-relayout.md"));
    failures += width.Failures;
}

if (mode is "probe" or "a1")
{
    var a1 = A1Manual.Run();
    a1.Save(Path.Combine(Host.ResultsDir, "a1-textruncache-stopwatch.md"));
    failures += a1.Failures;
}

Console.WriteLine($"\n=== total failed checks: {failures} ===");
return failures == 0 ? 0 : 1;
