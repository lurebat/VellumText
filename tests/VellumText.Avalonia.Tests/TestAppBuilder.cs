using Avalonia;
using Avalonia.Headless;
using VellumText.Avalonia;
using VellumText.Tests.Avalonia;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// One application and dispatcher for the whole assembly, so windows a test leaves open can be closed
// by a later one (TestWindows); with one per test they could not be, and the process reached 8.9 GB.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The Avalonia application the headless tests run inside.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseHeadlessDrawing = false</c> is the load-bearing setting. The headless platform's own
/// drawing stack does not shape text: it reports made-up metrics, so every assertion about a
/// caret rectangle, a hit test, or a selection rectangle would be measuring the stub rather
/// than Avalonia. Turning it off and adding Skia makes the tests exercise the real text stack.
/// </para>
/// </remarks>
public static class TestAppBuilder
{
    /// <summary>Builds the headless application used by every <c>[AvaloniaFact]</c>.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<Application>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .AfterSetup(_ =>
        {
            TestWindows.Track();

            // Motion.Auto reads the machine's animation setting, which differs between a CI runner
            // and a developer over Remote Desktop. Pinned to reduced, so a scroll or a caret move is
            // immediate unless a test asks for animation by setting Motion.Full.
            DocumentPresenter.DefaultSystemPrefersReducedMotion = () => true;
        });
}
