using System.Text;
using VellumText.ImportProbe;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// RTF whose nesting could exhaust a thread's stack, imported in a child process so that a stack
/// overflow fails the test by exit code instead of killing the test host.
/// </summary>
/// <remarks>
/// The reader walks groups and the HTML stage after it walks elements, and either could recurse per
/// level; the import runs on a large-stack thread for that reason (see <c>LargeStack</c>). The rows
/// hold that for whichever reader sits behind <see cref="RtfImporter"/>, so they sweep sizes either
/// side of what a 1 MiB stack survives rather than trusting one number.
/// </remarks>
public sealed class HostileNestingTests
{
    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void Deeply_nested_groups_do_not_crash_the_host(int depth)
    {
        var rtf = @"{\rtf1\ansi " + new string('{', depth) + "x" + new string('}', depth) + @" after\par}";

        var result = ProbeRunner.Run("rtf", rtf);

        Assert.True(result.ExitCode == 0, result.ToString());
    }

    /// <summary>
    /// Formatting opened in every group, so the reader has a reason to carry each level through to
    /// the HTML it produces rather than collapsing empty groups.
    /// </summary>
    [Theory]
    [InlineData(5_000)]
    [InlineData(50_000)]
    public void Deeply_nested_formatting_does_not_crash_the_host(int depth)
    {
        var opening = string.Concat(Enumerable.Range(0, depth).Select(i => i % 2 == 0 ? @"{\b x" : @"{\i y"));
        var rtf = @"{\rtf1\ansi " + opening + new string('}', depth) + @" after\par}";

        var result = ProbeRunner.Run("rtf", rtf);

        Assert.True(result.ExitCode == 0, result.ToString());
    }

    /// <summary>
    /// Tables nested inside table cells (<c>\itap</c>), which the reader turns into tables nested in
    /// HTML cells and the HTML stage then walks level by level.
    /// </summary>
    /// <remarks>
    /// This is the row that shows the large stack is needed today: imported on the caller's 1 MiB
    /// stack instead of <c>LargeStack</c>, 500 levels already overflow it.
    /// </remarks>
    [Theory]
    [InlineData(500)]
    [InlineData(2_000)]
    public void Deeply_nested_tables_do_not_crash_the_host(int depth)
    {
        var rtf = new StringBuilder(@"{\rtf1\ansi ");

        for (var level = 1; level <= depth; level++)
        {
            rtf.Append(@"\pard\intbl\itap").Append(level).Append(' ');
        }

        rtf.Append('x');

        for (var level = depth; level >= 1; level--)
        {
            rtf.Append(@"\nestcell{\*\nesttableprops\trowd\cellx1000\nestrow}\itap").Append(level - 1).Append(' ');
        }

        rtf.Append(@"\pard after\par}");

        var result = ProbeRunner.Run("rtf", rtf.ToString());

        Assert.True(result.ExitCode == 0, result.ToString());
    }
}