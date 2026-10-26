namespace VellumText.Core.Tests;

/// <summary>The thread every built-in importer parses on.</summary>
public sealed class LargeStackTests
{
    [Fact]
    public void Work_runs_on_another_thread_and_its_result_comes_back()
    {
        var caller = Environment.CurrentManagedThreadId;

        var (worker, large) = LargeStack.Run(() => (Environment.CurrentManagedThreadId, LargeStack.IsCurrentThreadLarge));

        Assert.NotEqual(caller, worker);
        Assert.True(large);
        Assert.False(LargeStack.IsCurrentThreadLarge);
    }

    [Fact]
    public void A_nested_call_runs_inline_rather_than_starting_another_thread()
    {
        // Markdown and RTF finish in the HTML importer; each would otherwise start a second thread
        // and park the first on it.
        var (outer, inner) = LargeStack.Run(() =>
            (Environment.CurrentManagedThreadId, LargeStack.Run(() => Environment.CurrentManagedThreadId)));

        Assert.Equal(outer, inner);
    }

    [Fact]
    public void An_exception_from_the_work_reaches_the_caller_with_its_type()
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => LargeStack.Run<int>(() => throw new InvalidOperationException("boom")));

        Assert.Equal("boom", thrown.Message);
    }

}
