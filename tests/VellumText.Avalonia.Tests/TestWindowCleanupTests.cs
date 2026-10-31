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

    private static Window Shown()
    {
        var window = new Window { Width = 200, Height = 100 };

        window.Show();

        return window;
    }
}
