using Avalonia;
using Avalonia.Headless;
using Vellum.Tests.Avalonia;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Vellum.Tests.Avalonia;

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
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
