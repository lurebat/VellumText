using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.Probe;

/// <summary>
/// A1 — does <see cref="TextRunCache.InvalidateFrom"/> beat rebuilding the layout outright?
///
/// Note: Avalonia exposes no "re-layout in place" API. The only reuse mechanism is passing the
/// same TextRunCache into a NEW TextLayout, so this measures shaping/run reuse, not layout reuse.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 5, iterationCount: 12, invocationCount: 256)]
public class A1Bench
{
    private BenchSource _source = null!;
    private TextRunCache _persistentCache = null!;
    private string _baseText = null!;
    private int _offset;
    private int _tick;

    [Params(100, 400, 800)]
    public int Length { get; set; }

    [Params(10, 50, 90)]
    public int EditPercent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Host.EnsureInitialized();
        _baseText = Scenarios.Paragraph(Length);
        _offset = Length * EditPercent / 100;
        _source = new BenchSource(_baseText);
        _persistentCache = new TextRunCache();

        // Warm the persistent cache with a first full layout, as a live editor would have.
        using (A1Common.Build(_source, _persistentCache)) { }
    }

    [GlobalCleanup]
    public void Cleanup() => _persistentCache.Dispose();

    /// <summary>Simulates one keystroke: text mutates by one character at the edit offset.</summary>
    private void Edit()
    {
        _tick++;
        _source.Text = A1Common.Insert(_baseText, _offset, (char)('a' + (_tick % 26)));
    }

    [Benchmark(Baseline = true, Description = "Fresh TextLayout, no cache")]
    public double NoCache()
    {
        Edit();
        using var layout = A1Common.Build(_source, null);
        return layout.Height;
    }

    [Benchmark(Description = "Persistent cache + InvalidateFrom(offset)")]
    public double CacheInvalidateFrom()
    {
        Edit();
        _persistentCache.InvalidateFrom(_offset);
        using var layout = A1Common.Build(_source, _persistentCache);
        return layout.Height;
    }

    [Benchmark(Description = "Persistent cache + Invalidate() (all)")]
    public double CacheInvalidateAll()
    {
        Edit();
        _persistentCache.Invalidate();
        using var layout = A1Common.Build(_source, _persistentCache);
        return layout.Height;
    }

    [Benchmark(Description = "Fresh cache each edit (control)")]
    public double FreshCacheEachTime()
    {
        Edit();
        using var cache = new TextRunCache();
        using var layout = A1Common.Build(_source, cache);
        return layout.Height;
    }
}
