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

    /// <summary>
    /// The importer's own walk of Markdig's inline tree must not recurse per level of nesting.
    /// </summary>
    /// <remarks>
    /// Run on the caller's ordinary stack, deliberately bypassing the large-stack thread: that
    /// thread would hide a recursive walk at every depth Markdig lets through, and this walk is
    /// code this library owns. Markdig caps its own nesting at some depths and not others — 8,000
    /// <c>[</c> got past it and overflowed in <c>RemoveRawHtml</c>, 7,000 and 12,000 did not — so
    /// the theory sweeps the range rather than trusting one number.
    /// </remarks>
    [Theory]
    [InlineData('[', 4_000)]
    [InlineData('[', 8_000)]
    [InlineData('[', 10_000)]
    [InlineData('[', 16_000)]
    [InlineData('>', 4_000)]
    [InlineData('>', 8_000)]
    [InlineData('>', 16_000)]
    [InlineData('*', 4_000)]
    [InlineData('*', 8_000)]
    [InlineData('*', 16_000)]
    public void Deep_markdown_nesting_does_not_overflow_the_importers_own_walk(char opener, int count)
    {
        var markdown = new string(opener, count) + " x";

        var result = ProbeRunner.Run("markdown-current-stack", markdown);

        Assert.True(result.ExitCode == 0, result.ToString());
    }
}
