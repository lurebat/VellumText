using VellumText.ImportProbe;

namespace VellumText.Interop.Markdown.Tests;

/// <summary>
/// Markdown whose nesting exhausts a thread's stack, imported in a child process so that a stack
/// overflow fails the test by exit code instead of killing the test host.
/// </summary>
/// <remarks>
/// The child calls the importer from a thread with an ordinary 1 MiB stack, standing in for a host
/// that imports from a thread-pool thread.
/// </remarks>
public sealed class HostileNestingTests
{
    /// <summary>
    /// Raw <c>template</c> tags are passed on to the HTML stage on purpose (their bodies are never
    /// content), so a paste that reads as plain text reaches AngleSharp's recursive tree builder
    /// with the default options.
    /// </summary>
    [Fact]
    public void Deeply_nested_raw_template_does_not_crash_the_host()
    {
        var markdown = "a " + string.Concat(Enumerable.Repeat("<template>", 8_000)) + " b";

        var result = ProbeRunner.Run("markdown", markdown);

        Assert.True(result.ExitCode == 0, result.ToString());
    }
}
