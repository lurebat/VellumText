using Avalonia;

namespace VellumText.Avalonia;

/// <summary>Whether the control animates, and the clock its animations run on.</summary>
public abstract partial class DocumentPresenter
{
    /// <summary>Defines the <see cref="Motion"/> property.</summary>
    public static readonly StyledProperty<MotionPreference> MotionProperty =
        AvaloniaProperty.Register<DocumentPresenter, MotionPreference>(nameof(Motion));

    /// <summary>How much the control animates.</summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see cref="MotionPreference.Auto"/>, which follows the operating system. Under
    /// reduced motion every animated movement happens at once — a scroll jumps, a gliding caret
    /// snaps, a smooth blink becomes an ordinary one — and nothing else changes.
    /// </para>
    /// <para>
    /// Remote Desktop sessions usually report animations off, so a control evaluated over one
    /// shows no animation at all under <see cref="MotionPreference.Auto"/>. That is the setting
    /// working, not the animation missing; <see cref="MotionPreference.Full"/> overrides it.
    /// </para>
    /// </remarks>
    public MotionPreference Motion
    {
        get => GetValue(MotionProperty);
        set => SetValue(MotionProperty, value);
    }

    /// <summary>Asks the operating system whether it wants less motion. Replaced by tests.</summary>
    internal Func<bool> SystemPrefersReducedMotion { get; set; } = SystemMotion.PrefersReducedMotion;

    /// <summary>The clock animations are timed against. Replaced by tests.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Whether movement should happen at once, now.</summary>
    /// <remarks>
    /// Asked when an animation would start rather than remembered, so a user who changes the
    /// setting does not have to restart the application for it to take.
    /// </remarks>
    internal bool ReducesMotion => Motion switch
    {
        MotionPreference.Reduced => true,
        MotionPreference.Full => false,
        _ => SystemPrefersReducedMotion(),
    };

    /// <summary>Milliseconds elapsed since <paramref name="start"/>, on <see cref="Time"/>.</summary>
    private protected double ElapsedMilliseconds(long start) => Time.GetElapsedTime(start).TotalMilliseconds;
}
