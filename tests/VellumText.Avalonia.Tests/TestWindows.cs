using System.Reflection;
using Avalonia.Controls;
using VellumText.Tests.Avalonia;
using Xunit.v3;

[assembly: MarkEachTestForWindowCleanup]

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Closes the windows earlier tests left open, so the documents laid out in them are not kept
/// alive for the rest of the run.
/// </summary>
/// <remarks>
/// <para>
/// A shown window stays alive until it is closed, and with it the view, its document and
/// everything laid out in it. Tests show windows and rarely close them, which cost little while
/// documents were small. The nested-table tests lay out 10,000-row tables, and fully laid-out
/// copies of them to compare against, so the windows they left open piled up: the test process
/// reached 8.9 GB, and macOS CI took 33 minutes where it had taken a minute and a half. Measured:
/// ten windows over a 3,000-row table kept 29 MB alive left open and 3 MB closed.
/// </para>
/// <para>
/// The tests share one application and dispatcher (<see cref="TestAppBuilder"/>), so a window an
/// earlier test left can be closed from a later one. Each window remembers the test it was opened
/// in — <see cref="MarkEachTestForWindowCleanupAttribute"/> counts tests — and when a window opens,
/// every window an earlier test opened and left open is closed. With an application per test, the
/// headless runner's default, the earlier windows belonged to dispatchers that were gone: they
/// could not be closed and stayed alive regardless. Closing them in an after-test hook instead
/// made the runner dispatch again after each test, which failed tests in cleanup now and then.
/// </para>
/// </remarks>
public static class TestWindows
{
    private static readonly Dictionary<Window, int> Open = [];
    private static int generation;
    private static int tracking;

    /// <summary>How many tracked windows are open now.</summary>
    public static int OpenCount => Open.Count;

    /// <summary>Starts tracking windows. Safe to call more than once.</summary>
    /// <remarks>Window events are hooked by static class handlers, so they are added once only.</remarks>
    public static void Track()
    {
        if (Interlocked.Exchange(ref tracking, 1) == 1)
        {
            return;
        }

        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            CloseLeftOpen();
            Open[window] = Volatile.Read(ref generation);
        });
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Open.Remove(window));
    }

    /// <summary>Marks the start of the next test. Touches nothing of Avalonia's, so it is safe on any thread.</summary>
    public static void NextTest() => Interlocked.Increment(ref generation);
    /// <summary>Records a window as opened by the given test (for the tests of this class).</summary>
    internal static void Record(Window window, int generationOpened) => Open[window] = generationOpened;

    /// <summary>Closes the windows that other tests opened and left open.</summary>
    public static void CloseLeftOpen()
    {
        var current = Volatile.Read(ref generation);

        foreach (var (window, opened) in Open.ToArray())
        {
            if (opened >= current)
            {
                continue;
            }

            // A window this thread cannot reach cannot be closed from here; keeping it in this
            // list would be what keeps it alive.
            if (window.CheckAccess())
            {
                window.Close();
            }

            Open.Remove(window);
        }
    }
}

/// <summary>Tells <see cref="TestWindows"/> where each test begins.</summary>
/// <remarks>
/// Only <see cref="Before"/>, and it touches nothing of Avalonia's: an <c>After</c> that closed the
/// windows on the UI thread made the headless runner dispatch again after the test, which failed
/// tests in cleanup now and then. The windows are closed when the next test opens one instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false)]
public sealed class MarkEachTestForWindowCleanupAttribute : BeforeAfterTestAttribute
{
    /// <inheritdoc/>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => TestWindows.NextTest();
}
