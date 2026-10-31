using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Windows a test leaves open are closed when the next test opens one, so the documents laid out
/// in them are not kept alive for the rest of the run (see <see cref="TestWindows"/>).
/// </summary>
public class TestWindowCleanupTests
{
    /// <summary>
    /// The next test's first window closes what earlier tests left open, and not the windows of
    /// the test that opens it.
    /// </summary>
    [AvaloniaFact]
    public void TheNextTestsFirstWindowClosesWhatEarlierTestsLeft()
    {
        var leftBehind = Shown();

        // As if an earlier test had opened it and left it open.
        TestWindows.Record(leftBehind, generationOpened: -1);

        var mine = Shown();
        var alsoMine = Shown();

        Assert.False(leftBehind.IsVisible);
        Assert.True(mine.IsVisible);
        Assert.True(alsoMine.IsVisible);
        Assert.Equal(2, TestWindows.OpenCount);
    }

    /// <summary>A test starts with no window from an earlier test still open.</summary>
    [AvaloniaFact]
    public void ATestStartsWithNothingEarlierTestsLeftOpen()
    {
        Shown();

        Assert.Equal(1, TestWindows.OpenCount);
    }

    /// <summary>
    /// A test starting meanwhile on another thread — one that only starts a child process, run
    /// beside this one — does not make this test's windows look left behind: the next window this
    /// test opens leaves the first open.
    /// </summary>
    /// <remarks>Rubber-duck review of scope (b), finding 2.</remarks>
    [AvaloniaFact]
    public void ATestStartingOnAnotherThreadClosesNothingOfThisOnes()
    {
        var first = Shown();

        Task.Run(() => new MarkEachTestForWindowCleanupAttribute().Before(null!, null!)).Wait(TestContext.Current.CancellationToken);

        var second = Shown();

        Assert.True(first.IsVisible);
        Assert.True(second.IsVisible);
    }

    private static Window Shown()
    {
        var window = new Window { Width = 200, Height = 100 };

        window.Show();

        return window;
    }
}
