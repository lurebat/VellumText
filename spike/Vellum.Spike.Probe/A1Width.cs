using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.Probe;

/// <summary>
/// A1, second question: the edit case is settled (the cache goes stale), but an editor also
/// relayouts constantly *without* an edit — every pane resize rewraps every paragraph at a new
/// width. Shaping is width-independent, so a run cache could legitimately pay off there.
///
/// The trap (same one that made the edit arm look fast) is that a cache which returns the
/// PREVIOUS width's layout is both wrong and fast. So correctness here is not "matches itself",
/// it is "matches an uncached layout built at the same new width".
/// </summary>
public static class A1Width
{
    private const int Length = 800;

    private static TextLayout Build(ITextSource source, double width, TextRunCache? cache)
        => cache is null
            ? new TextLayout(source, A1Common.Props, TextTrimming.None, width, double.PositiveInfinity, int.MaxValue)
            : new TextLayout(source, A1Common.Props, TextTrimming.None, width, double.PositiveInfinity, int.MaxValue, cache);

    public static Report Run()
    {
        var r = new Report("A1 (width-only relayout) — does a run cache help when the text does not change?");

        r.Line();
        r.Line("Scenario: an 800-char paragraph whose text is **never modified**, relaid out at a "
               + "sequence of different wrap widths — i.e. the user dragging the editor pane wider "
               + "and narrower. No `InvalidateFrom` is called, because nothing was edited.");

        var text = Scenarios.Paragraph(Length);
        var widths = new double[] { 320, 360, 400, 440, 480, 520, 560, 600 };

        // ------------------------------------------------------------- 1. correctness first
        r.Section("1. Correctness — is the cached layout right for the NEW width?");
        r.Line();
        r.Line("| width | cached fingerprint | matches uncached? |");
        r.Line("|---|---|---|");

        var correct = true;
        {
            var source = new BenchSource(text);
            using var cache = new TextRunCache();
            foreach (var w in widths)
            {
                using var cached = Build(source, w, cache);
                using var plain = Build(new BenchSource(text), w, null);
                var fc = A1Common.Fingerprint(cached);
                var fp = A1Common.Fingerprint(plain);
                var ok = fc == fp;
                correct &= ok;
                var shown = fc.Length > 44 ? fc[..44] + "…" : fc;
                r.Line($"| {w} | `{shown}` | {(ok ? "yes" : "**NO**")} |");
            }
        }

        r.Line();
        r.Check(correct,
            "cached width-only relayout matches an uncached layout at every width",
            "cached relayout returned a layout for the wrong width — the cache is stale here too");

        // --------------------------------------------------- 2. does it even re-read the source?
        r.Section("2. Does the cached build re-query the text source?");

        int coldCalls, warmCalls;
        {
            var counting = new CountingSource(text);
            using var cache = new TextRunCache();
            using (Build(counting, widths[0], cache)) { }
            coldCalls = counting.Calls;
            counting.Reset();
            using (Build(counting, widths[^1], cache)) { }
            warmCalls = counting.Calls;
        }

        r.Line();
        r.Line($"- GetTextRun calls, first build (cold cache): **{coldCalls}**");
        r.Line($"- GetTextRun calls, later build at a new width (warm cache): **{warmCalls}**");
        r.Line();
        r.Line(warmCalls == 0
            ? "The warm build asks the source for nothing — the shaped runs are reused wholesale "
              + "and only line breaking is redone. That is exactly the intended behaviour here, "
              + "and unlike the edit case it is **also correct**, because the text really has not changed."
            : "The warm build still re-queries the source, so the cache is not reusing shaped runs.");

        // ------------------------------------------------------------------ 3. timing
        r.Section("3. Timing");
        r.Line();
        r.Line("Interleaved arms, repeated, reporting the median of the per-repetition ratios — "
               + "this VM's timing noise is roughly ±30%, so a single ratio means little.");

        const int Reps = 7;
        const int Iterations = 200;
        var ratios = new List<double>();
        var noCacheMs = new List<double>();
        var cacheMs = new List<double>();

        for (var rep = 0; rep < Reps + 1; rep++)
        {
            // arm A: no cache
            var swA = Stopwatch.StartNew();
            {
                var source = new BenchSource(text);
                for (var i = 0; i < Iterations; i++)
                    using (Build(source, widths[i % widths.Length], null)) { }
            }
            swA.Stop();

            // arm B: one persistent cache, text never edited
            var swB = Stopwatch.StartNew();
            {
                var source = new BenchSource(text);
                using var cache = new TextRunCache();
                for (var i = 0; i < Iterations; i++)
                    using (Build(source, widths[i % widths.Length], cache)) { }
            }
            swB.Stop();

            if (rep == 0) continue; // warmup discarded

            var a = swA.Elapsed.TotalMilliseconds / Iterations * 1000;
            var b = swB.Elapsed.TotalMilliseconds / Iterations * 1000;
            noCacheMs.Add(a);
            cacheMs.Add(b);
            ratios.Add(a / b);
        }

        static double Median(List<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        r.Line();
        r.Line("| rep | no cache (µs/op) | persistent cache (µs/op) | ratio |");
        r.Line("|---|---|---|---|");
        for (var i = 0; i < ratios.Count; i++)
            r.Line($"| {i + 1} | {noCacheMs[i]:0.0} | {cacheMs[i]:0.0} | {ratios[i]:0.00}× |");

        var med = Median(ratios);
        r.Line();
        r.Line($"- median no-cache : **{Median(noCacheMs):0.0} µs/op**");
        r.Line($"- median cached   : **{Median(cacheMs):0.0} µs/op**");
        r.Line($"- **median speedup: {med:0.00}×**  (range {ratios.Min():0.00}×–{ratios.Max():0.00}×)");

        r.Line();
        r.Check(correct && med > 1.5,
            $"width-only relayout is both correct and materially faster with a persistent cache ({med:0.00}×)",
            $"width-only relayout shows no material speedup ({med:0.00}×) — treat the cache as not worth it");

        return r;
    }
}
