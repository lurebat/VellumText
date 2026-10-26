using VellumText.ImportProbe;

namespace VellumText.Interop.Html.Tests;

/// <summary>
/// Nesting deep enough to exhaust a thread's stack inside a dependency, imported in a child
/// process.
/// </summary>
/// <remarks>
/// A stack overflow ends the process without running a single <c>catch</c> or <c>finally</c>. Run
/// in the test host, a failure here would take the host down and could still leave a "Passed!"
/// summary for the tests that had already finished, so each case runs in
/// <see cref="ProbeRunner"/> and asserts the child's exit code. The child calls the importer from
/// a thread with an ordinary 1 MiB stack: the protection must come from the importer, because a
/// host calling it from a thread-pool thread has no other.
/// </remarks>
public sealed class HostileNestingTests
{
    /// <summary>
    /// AngleSharp.Css parses <c>calc(</c> by recursive descent, one frame set per bracket; 3,300
    /// was enough to overflow a 1 MiB stack when this was found.
    /// </summary>
    [Fact]
    public void Deeply_nested_css_calc_does_not_crash_the_host()
    {
        var html = "<p style=\"font-size:calc(" + new string('(', 8_000) + "\">x</p><p>after</p>";

        var result = ProbeRunner.Run("html", html);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("after", result.Value("text"), StringComparison.Ordinal);
    }

    /// <summary>
    /// AngleSharp's tree builder recurses per open <c>template</c> when it finishes loading; about
    /// 5,000 overflowed a 1 MiB stack. Nine other tags survive 20,000 levels, so a test built on
    /// <c>div</c> would never have found it.
    /// </summary>
    [Fact]
    public void Deeply_nested_template_does_not_crash_the_host()
    {
        var html = "<p>before</p>" + string.Concat(Enumerable.Repeat("<template>", 8_000));

        var result = ProbeRunner.Run("html", html);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("before", result.Value("text"), StringComparison.Ordinal);
    }

    /// <summary>The clipboard entry point reaches the same parser and needs the same protection.</summary>
    [Fact]
    public void Deeply_nested_template_on_the_clipboard_path_does_not_crash_the_host()
    {
        var html = "<p>before</p>" + string.Concat(Enumerable.Repeat("<template>", 8_000));

        var result = ProbeRunner.Run("html-clipboard", html);

        Assert.True(result.ExitCode == 0, result.ToString());
    }
}
