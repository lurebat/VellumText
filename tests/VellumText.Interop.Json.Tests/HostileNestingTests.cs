using VellumText.ImportProbe;

namespace VellumText.Tests.Interop.Json;

/// <summary>
/// JSON nested past any depth the format writes, imported in a child process so that a stack
/// overflow fails the test by exit code instead of killing the test host.
/// </summary>
/// <remarks>
/// The reader's depth limit is what stops these (see <c>JsonDepthTests</c>): with it lifted, the
/// block reader recurses once per level and these rows crash the child. The large-stack thread is a
/// second line behind it. A child process makes sure a regression is a red test rather than a
/// crashed test run that can still report "Passed!".
/// </remarks>
public sealed class HostileNestingTests
{
    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void Deeply_nested_arrays_do_not_crash_the_host(int depth)
    {
        var json = new string('[', depth) + new string(']', depth);

        var result = ProbeRunner.Run("json", json);

        Assert.True(result.ExitCode == 0, result.ToString());
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void Deeply_nested_objects_do_not_crash_the_host(int depth)
    {
        var json = string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);

        var result = ProbeRunner.Run("json", json);

        Assert.True(result.ExitCode == 0, result.ToString());
    }
}