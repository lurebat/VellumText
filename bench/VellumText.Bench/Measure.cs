using System.Diagnostics;

namespace VellumText.Bench;

/// <summary>One size point of a scenario: what it cost, and how much it allocated.</summary>
internal readonly record struct Point(int Size, double Milliseconds, long Bytes);

/// <summary>
/// Runs a scenario across doubling sizes and reports the growth per doubling.
/// </summary>
/// <remarks>
/// <para>
/// The growth ratio is the output that matters, not the absolute time. A single number cannot
/// tell you the shape, and the shape is where this project's performance bugs have lived: an
/// operation costing 49 ms looks tolerable until you learn it cost 3.7 ms at half the size.
/// Every scenario here therefore runs at several sizes and prints the ratio between neighbours.
/// </para>
/// <para>
/// <b>Both curves are reported, and the gap between them is itself a diagnosis.</b> Bytes
/// allocated is deterministic — it does not move when another process wakes up or when a CI
/// runner is slower than a laptop — so it establishes the shape without needing a quiet machine.
/// But allocation is blind to work that allocates nothing, and a linear scan over an array is
/// exactly that: it burns CPU and allocates not one byte.
/// </para>
/// <para>
/// So: <b>allocation growing faster than expected means the operation is touching the whole
/// document. Time growing faster than allocation means it is scanning something.</b> The second
/// is this project's recurring bug, and reading only the allocation column would hide it — which
/// it did, on the first run of the mark-span scenario, until the two columns were compared.
/// </para>
/// </remarks>
internal static class Measure
{
    /// <summary>Runs <paramref name="action"/> and reports the median of several timed rounds.</summary>
    /// <param name="setup">Builds the state for one size. Not measured.</param>
    /// <param name="action">The operation under test. Measured.</param>
    /// <param name="size">The size being measured, reported alongside the result.</param>
    /// <param name="rounds">Timed rounds. The median is reported, so one hiccup cannot dominate.</param>
    /// <param name="warmup">Rounds run and discarded first, to pay for JIT and first-touch costs.</param>
    internal static Point Run<T>(
        Func<T> setup,
        Action<T> action,
        int size,
        int rounds = 9,
        int warmup = 3)
    {
        for (var i = 0; i < warmup; i++)
        {
            action(setup());
        }

        var times = new double[rounds];
        var bytes = new long[rounds];

        for (var i = 0; i < rounds; i++)
        {
            var state = setup();

            // Settle before each round rather than once at the start: the setup for a large
            // document allocates heavily, and a collection landing inside the measured region
            // would be charged to the operation that happened to be running.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();

            action(state);

            clock.Stop();
            bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            times[i] = clock.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        Array.Sort(bytes);

        return new Point(size, times[rounds / 2], bytes[rounds / 2]);
    }

    /// <summary>Prints one scenario's points and the growth between them.</summary>
    /// <param name="name">The scenario name.</param>
    /// <param name="unit">What one unit of <c>Size</c> means, for the header.</param>
    /// <param name="points">The measured points, in increasing size order.</param>
    /// <param name="note">What the reader should conclude, if anything.</param>
    internal static void Report(string name, string unit, IReadOnlyList<Point> points, string? note = null)
    {
        Console.WriteLine();
        Console.WriteLine($"## {name}");
        Console.WriteLine();
        Console.WriteLine($"| {unit,10} | {"ms",10} | {"bytes",14} | {"x time",8} | {"x bytes",8} |");
        Console.WriteLine($"|{new string('-', 12)}|{new string('-', 12)}|{new string('-', 16)}|{new string('-', 10)}|{new string('-', 10)}|");

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var timeRatio = i == 0 ? "" : Ratio(points[i - 1].Milliseconds, p.Milliseconds);
            var byteRatio = i == 0 ? "" : Ratio(points[i - 1].Bytes, p.Bytes);

            Console.WriteLine(
                $"| {p.Size,10:N0} | {p.Milliseconds,10:F3} | {p.Bytes,14:N0} | {timeRatio,8} | {byteRatio,8} |");
        }

        if (points.Count > 1)
        {
            Console.WriteLine();

            var bytes = MeanRatio(points, p => p.Bytes);
            var time = MeanRatio(points, p => p.Milliseconds);

            Console.WriteLine($"   bytes: {bytes:F2}x per doubling -> {Verdict(bytes)}");
            Console.WriteLine($"   time:  {time:F2}x per doubling -> {Verdict(time)}");

            // The gap is the diagnosis. A scan allocates nothing, so it can only show up here.
            if (time > bytes * 1.25)
            {
                Console.WriteLine(
                    "   ^^ time is growing faster than allocation: something is SCANNING rather "
                    + "than allocating. This is the shape of a linear lookup over sorted data.");
            }
        }

        if (note is not null)
        {
            Console.WriteLine($"   note:  {note}");
        }
    }

    private static string Ratio(double from, double to) =>
        from <= 0 ? "-" : $"{to / from:F2}x";

    private static double MeanRatio(IReadOnlyList<Point> points, Func<Point, double> of)
    {
        var ratios = new List<double>();

        for (var i = 1; i < points.Count; i++)
        {
            var previous = of(points[i - 1]);

            if (previous > 0)
            {
                ratios.Add(of(points[i]) / previous);
            }
        }

        return ratios.Count == 0 ? 0 : ratios.Average();
    }

    /// <summary>
    /// Names a growth ratio measured across doublings of the input.
    /// </summary>
    /// <remarks>
    /// Roughly 1 means the cost does not depend on the size — which is what a virtualized editor
    /// claims about a keystroke. Roughly 2 means linear. Above about 3 means superlinear, and
    /// every quadratic this project has found looked like that before it was fixed.
    /// </remarks>
    private static string Verdict(double mean) => mean switch
    {
        <= 0 => "nothing to measure",
        < 1.35 => "FLAT - independent of size",
        < 1.7 => "sublinear",
        < 2.4 => "LINEAR in size",
        < 3.2 => "worse than linear",
        _ => "QUADRATIC or worse",
    };
}
