using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>Which way <see cref="DocumentPresenter.Motion"/> resolves, and what the system probes read.</summary>
public class MotionPreferenceTests
{
    [AvaloniaFact]
    public void TheDefaultFollowsTheOperatingSystem()
    {
        var view = new RichTextView();

        Assert.Equal(MotionPreference.Auto, view.Motion);

        view.SystemPrefersReducedMotion = () => true;
        Assert.True(view.ReducesMotion);

        view.SystemPrefersReducedMotion = () => false;
        Assert.False(view.ReducesMotion);
    }

    [AvaloniaTheory]
    [InlineData(MotionPreference.Reduced, false, true)]
    [InlineData(MotionPreference.Reduced, true, true)]
    [InlineData(MotionPreference.Full, true, false)]
    [InlineData(MotionPreference.Full, false, false)]
    public void AnExplicitPreferenceOverridesTheOperatingSystem(MotionPreference motion, bool system, bool reduced)
    {
        var viewer = new RichTextViewer { Motion = motion, SystemPrefersReducedMotion = () => system };

        Assert.Equal(reduced, viewer.ReducesMotion);
    }

    [AvaloniaFact]
    public void TheOperatingSystemIsAskedEachTimeNotOnce()
    {
        // A user who turns animations off should not have to restart the application.
        var asked = 0;
        var view = new RichTextView { SystemPrefersReducedMotion = () => ++asked % 2 == 0 };

        Assert.False(view.ReducesMotion);
        Assert.True(view.ReducesMotion);
        Assert.Equal(2, asked);
    }

    [AvaloniaFact]
    public void TheEditorHandsItsPreferencesToItsView()
    {
        var editor = new RichTextEditor
        {
            Motion = MotionPreference.Reduced,
            SmoothCaret = true,
            CaretBlink = CaretBlinkStyle.Solid,
        };

        var view = editor.EnsureView();

        Assert.Equal(MotionPreference.Reduced, view.Motion);
        Assert.True(view.SmoothCaret);
        Assert.Equal(CaretBlinkStyle.Solid, view.CaretBlink);

        editor.Motion = MotionPreference.Full;
        editor.SmoothCaret = false;
        editor.CaretBlink = CaretBlinkStyle.Smooth;

        Assert.Equal(MotionPreference.Full, view.Motion);
        Assert.False(view.SmoothCaret);
        Assert.Equal(CaretBlinkStyle.Smooth, view.CaretBlink);
    }

    [AvaloniaFact]
    public void TheProbesAnswerOnThisPlatformWithoutThrowing()
    {
        // On Linux and in a headless macOS process there is nothing to ask, and that has to read
        // as "no preference" rather than as a missing library taking the control down.
        _ = SystemMotion.PrefersReducedMotion();

        var blink = SystemMotion.CaretBlinkInterval();

        Assert.True(blink is null || blink > TimeSpan.Zero);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(RichTextView.BlinkInterval, blink);
        }
    }

    [AvaloniaTheory]
    [InlineData(530u, 530.0)]
    [InlineData(1200u, 1200.0)]
    [InlineData(0u, 500.0)]
    public void WindowsBlinkTimesAreReadAsIntervals(uint reported, double expected)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expected), SystemMotion.FromWindowsBlinkTime(reported));
    }

    [AvaloniaFact]
    public void WindowsInfiniteMeansTheUserTurnedBlinkingOff()
    {
        Assert.Null(SystemMotion.FromWindowsBlinkTime(uint.MaxValue));
    }
}
