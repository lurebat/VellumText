using System.Diagnostics;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.Probe;

/// <summary>
/// Stopwatch harness for A1. Runs alongside BenchmarkDotNet so there is a second, independent
/// number for the same question, and so a result exists even if BDN misbehaves under a
/// headless Avalonia host.
/// </summary>
public static class A1Manual
{
    private const int Warmup = 2_000;
    private const int Measure = 20_000;

    private enum Arm { NoCache, InvalidateFrom, InvalidateFromZero, InvalidateAll, FreshCache }

    public static Report Run()
    {
        var r = new Report("A1 — `TextRunCache.InvalidateFrom` (stopwatch harness)");

        r.Line();
        r.Line("Avalonia exposes no re-layout-in-place API: the only reuse mechanism is passing the "
               + "same `TextRunCache` into a **new** `TextLayout`. So this measures shaping/run reuse, "
               + "not layout-object reuse.");
        r.Line();
        r.Line($"Warmup {Warmup:N0} iterations, measured {Measure:N0} iterations per arm, "
               + $"wrap width {A1Common.MaxWidth}px, style-run boundary every 25 chars.");

        // ------------------------------------------------ correctness cross-check first
        r.Section("Correctness cross-check — do the arms produce identical geometry?");

        var mismatch = 0;
        var checkedCount = 0;
        foreach (var length in new[] { 100, 400, 800 })
        foreach (var percent in new[] { 10, 50, 90 })
        {
            var baseText = Scenarios.Paragraph(length);
            var offset = length * percent / 100;

            var plainSource = new BenchSource(baseText);
            var cachedSource = new BenchSource(baseText);
            using var cache = new TextRunCache();
            using (A1Common.Build(cachedSource, cache)) { }

            for (var edit = 0; edit < 40; edit++)
            {
                var text = A1Common.Insert(baseText, offset, (char)('a' + edit % 26));
                plainSource.Text = text;
                cachedSource.Text = text;

                using var fresh = A1Common.Build(plainSource, null);
                cache.InvalidateFrom(offset);
                using var cached = A1Common.Build(cachedSource, cache);

                checkedCount++;
                if (A1Common.Fingerprint(fresh) != A1Common.Fingerprint(cached))
                {
                    if (mismatch < 5)
                    {
                        r.Line($"- MISMATCH len={length} pct={percent} edit={edit}");
                        r.Line($"  fresh : {A1Common.Fingerprint(fresh)}");
                        r.Line($"  cached: {A1Common.Fingerprint(cached)}");
                    }
                    mismatch++;
                }
            }
        }

        r.Line();
        r.Line($"Mismatches: {mismatch}/{checkedCount}. Every arm here used `InvalidateFrom(offset)` "
               + "with `offset > 0`. The A1 diagnostic section establishes that this returns the "
               + "**pre-edit** layout, so a mismatch is the expected — and disqualifying — outcome, "
               + "not a harness fault.");
        r.Line();
        r.Check(mismatch == checkedCount,
            "`InvalidateFrom(offset > 0)` reproducibly yields a stale layout on **every** edit "
            + "(a consistent, reproducible defect rather than a flaky one)",
            mismatch == checkedCount ? "" : $"only {mismatch} of {checkedCount} were stale");

        // ------------------------------------------------ timing
        r.Section("Timings");

        var rows = new List<string>();
        foreach (var length in new[] { 100, 400, 800 })
        foreach (var percent in new[] { 10, 50, 90 })
        {
            var results = new Dictionary<Arm, (double meanUs, double p50, double p95)>();
            foreach (Arm arm in Enum.GetValues<Arm>())
                results[arm] = Time(arm, length, percent);

            var baseline = results[Arm.NoCache].meanUs;
            rows.Add(
                $"{length}|{percent}%|{baseline:0.###}|"
                + $"{results[Arm.InvalidateFrom].meanUs:0.###} ({results[Arm.InvalidateFrom].meanUs / baseline:0.00}x)|"
                + $"{results[Arm.InvalidateFromZero].meanUs:0.###} ({results[Arm.InvalidateFromZero].meanUs / baseline:0.00}x)|"
                + $"{results[Arm.InvalidateAll].meanUs:0.###} ({results[Arm.InvalidateAll].meanUs / baseline:0.00}x)|"
                + $"{results[Arm.FreshCache].meanUs:0.###} ({results[Arm.FreshCache].meanUs / baseline:0.00}x)");

            Console.WriteLine($"  ...done len={length} pct={percent}");
        }

        r.Table("chars|edit at|NoCache µs|InvalidateFrom(edit) µs **[STALE]**|InvalidateFrom(0) µs|Invalidate() µs|FreshCache µs", rows.ToArray());

        r.Line();
        r.Line("**The `InvalidateFrom(edit)` column is not a real result.** The cross-check above and "
               + "the diagnostic report show that arm returns the pre-edit layout; it is timing the "
               + "cost of returning a stale answer. The honest comparison is NoCache versus "
               + "`InvalidateFrom(0)` / `Invalidate()`, which are the only cached usages that produce "
               + "a correct layout.");

        r.Line();
        r.Line("Percentiles for the two arms that matter — the uncached baseline versus the fastest "
               + "*correct* cached usage — at 800 chars / 50%:");
        var a = Time(Arm.NoCache, 800, 50);
        var b = Time(Arm.InvalidateFromZero, 800, 50);
        r.Table("arm|mean µs|p50 µs|p95 µs",
            $"NoCache|{a.meanUs:0.###}|{a.p50:0.###}|{a.p95:0.###}",
            $"InvalidateFrom(0)|{b.meanUs:0.###}|{b.p50:0.###}|{b.p95:0.###}");
        r.Line();
        r.Line($"Speedup from the correct cached path: **{a.meanUs / b.meanUs:0.00}x**.");

        r.Section("A1 (stopwatch) result");
        r.Line($"Checks failed: {r.Failures}");
        return r;
    }

    private static (double meanUs, double p50, double p95) Time(Arm arm, int length, int percent)
    {
        var baseText = Scenarios.Paragraph(length);
        var offset = length * percent / 100;
        var source = new BenchSource(baseText);
        var cache = new TextRunCache();
        using (A1Common.Build(source, cache)) { }

        void One(int tick)
        {
            source.Text = A1Common.Insert(baseText, offset, (char)('a' + tick % 26));
            switch (arm)
            {
                case Arm.NoCache:
                {
                    using var l = A1Common.Build(source, null);
                    _ = l.Height;
                    break;
                }
                case Arm.InvalidateFrom:
                {
                    cache.InvalidateFrom(offset);
                    using var l = A1Common.Build(source, cache);
                    _ = l.Height;
                    break;
                }
                case Arm.InvalidateFromZero:
                {
                    cache.InvalidateFrom(0);
                    using var l = A1Common.Build(source, cache);
                    _ = l.Height;
                    break;
                }
                case Arm.InvalidateAll:
                {
                    cache.Invalidate();
                    using var l = A1Common.Build(source, cache);
                    _ = l.Height;
                    break;
                }
                default:
                {
                    using var c = new TextRunCache();
                    using var l = A1Common.Build(source, c);
                    _ = l.Height;
                    break;
                }
            }
        }

        for (var i = 0; i < Warmup; i++) One(i);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var samples = new double[Measure];
        var sw = new Stopwatch();
        for (var i = 0; i < Measure; i++)
        {
            sw.Restart();
            One(i);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMicroseconds;
        }

        cache.Dispose();

        var mean = samples.Average();
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        return (mean, sorted[Measure / 2], sorted[(int)(Measure * 0.95)]);
    }
}
