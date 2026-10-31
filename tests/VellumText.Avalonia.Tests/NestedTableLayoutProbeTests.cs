using VellumText.LayoutProbe;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A 10,000 × 3 table nested one, two and three deep, laid out in a child process against the
/// same table at the top of the document (rubber-duck review finding 10.4): the process exits,
/// in bounded time, having allocated, kept and peaked at about what the top-level table does.
/// </summary>
/// <remarks>
/// <para>
/// In a child process because a layout that runs away — every row of every depth laid out, a
/// hang, a stack overflow — takes the process that runs it with it, and a test host that dies can
/// still report the tests it finished as passed. Here the exit code, the time and the memory are
/// asserted from outside.
/// </para>
/// <para>
/// Before nested tables were windowed, the depth-one figures were 135 MB allocated and 115 MB
/// kept by the first layout, and 26 MB by a resize, against 5.2 MB, 1.6 MB and 54 KB at the top.
/// </para>
/// </remarks>
[Collection(ChildProcessCollection.Name)]
public class NestedTableLayoutProbeTests
{
    private const int Rows = 10_000;

    // A hang guard only, generous for a slow shared runner: the cost these tests bound is bytes,
    // relative to the same table at the top, which a busy machine does not change.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private static readonly Lazy<LayoutProbeResult> TopLevel = new(() => Profile(0));

    private static readonly Lazy<LayoutProbeResult> TopLevelTraversal = new(() => Traverse(0));

    /// <summary>
    /// First layout, resize, keystroke and a jump to the middle row cost a nested table about what
    /// they cost the table at the top.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ANestedTenThousandRowTableCostsAboutWhatATopLevelOneDoes(int depth)
    {
        var top = TopLevel.Value;
        var nested = Profile(depth);

        Assert.True(nested.ExitCode == 0, nested.ToString());
        Assert.True(top.ExitCode == 0, top.ToString());

        AssertClose(nested, top, "firstLayoutBytes", slack: 256_000);
        AssertClose(nested, top, "retainedBytes", slack: 512_000);
        AssertClose(nested, top, "keystrokeBytes", slack: 512_000);
        Assert.True(nested.Value("resizeBytes") <= 2_000_000, $"a resize allocated {nested.Value("resizeBytes"):N0} bytes\n{nested}");

        // No wall-clock bound: on a slow shared runner a correct probe took 105 s (macOS CI), and the
        // bytes above already catch every row being laid out (135 MB for the first layout before).

        Assert.Equal(1, nested.Value("caretToMiddleOnScreen"));
        Assert.True(
            nested.Value("peakWorkingSetBytes") <= top.Value("peakWorkingSetBytes") + 64_000_000,
            $"peaked at {nested.Value("peakWorkingSetBytes"):N0} bytes against {top.Value("peakWorkingSetBytes"):N0} at the top\n{nested}");
    }

    /// <summary>
    /// Scrolling through the whole of a nested table a page at a time ends, and keeps about what
    /// its first layout kept — a few screenfuls of rows, not the table: rows of a table in a cell
    /// that the window has left give up their views and keep their heights. The same table at the
    /// top of the document keeps every row it has laid out, as it always did.
    /// </summary>
    /// <remarks>
    /// Before nested rows were let go, every depth kept what the top level does, 116 MB, and three
    /// deep beside a line of text 562 MB.
    /// </remarks>
    [Theory]
    [InlineData(1, "")]
    [InlineData(3, "")]
    [InlineData(3, "side")]
    [InlineData(1, "hosts")]
    [InlineData(2, "hosts")]
    public void ScrollingThroughANestedTenThousandRowTableKeepsAboutWhatItsFirstLayoutDid(int depth, string variant)
    {
        var top = TopLevelTraversal.Value;
        var nested = Traverse(depth, Rows, variant);

        Assert.True(nested.ExitCode == 0, nested.ToString());
        Assert.True(top.ExitCode == 0, top.ToString());

        var first = nested.Value("retainedAfterFirstLayoutBytes");
        var after = nested.Value("retainedAfterTraverseBytes");

        Assert.True(
            after <= (first * 4) + 2_000_000,
            $"scrolling through kept {after:N0} bytes, against {first:N0} after the first layout\n{nested}");
        Assert.True(top.Value("retainedAfterTraverseBytes") > 50_000_000, $"the top level let rows go\n{top}");
        Assert.True(nested.Value("pages") >= top.Value("pages") / 2, nested.ToString());
    }

    /// <summary>
    /// What scrolling through a nested table keeps does not grow with the table: 2,500, 5,000 and
    /// 10,000 rows keep the same few screenfuls, beyond what a row costs before it is ever laid out.
    /// </summary>
    [Theory]
    [InlineData(1, "")]
    [InlineData(3, "side")]
    [InlineData(2, "hosts")]
    public void ScrollingThroughLongerNestedTablesKeepsNoMore(int depth, string variant)
    {
        var runs = new[] { 2_500, 5_000, 10_000 }.Select(rows => Traverse(depth, rows, variant)).ToArray();

        foreach (var run in runs)
        {
            Assert.True(run.ExitCode == 0, run.ToString());
        }

        // A row nobody has laid out still costs its slots in the table's arrays; that part grows
        // with the rows from the first layout on, and is taken out.
        var grown = runs.Select(run => run.Value("retainedAfterTraverseBytes") - run.Value("retainedAfterFirstLayoutBytes")).ToArray();

        Assert.True(
            grown[2] <= grown[0] + 2_000_000,
            $"scrolling through kept {string.Join(", ", grown.Select(g => g.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)))} bytes beyond the first layout at 2,500, 5,000 and 10,000 rows\n{runs[2]}");
    }

    private static LayoutProbeResult Profile(int depth) =>
        LayoutProbeRunner.Run(Timeout, depth.ToString(System.Globalization.CultureInfo.InvariantCulture), Rows.ToString(System.Globalization.CultureInfo.InvariantCulture), "profile");

    private static LayoutProbeResult Traverse(int depth, int rows = Rows, string variant = "") =>
        LayoutProbeRunner.Run(
            Timeout,
            depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            rows.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "traverse",
            variant);

    private static void AssertClose(LayoutProbeResult nested, LayoutProbeResult top, string key, long slack)
    {
        var value = nested.Value(key);
        var bound = (top.Value(key) * 5 / 4) + slack;

        Assert.True(value <= bound, $"{key} = {value:N0}, more than {bound:N0} (the top level's {top.Value(key):N0} and a quarter, and {slack:N0})\n{nested}");
    }
}
