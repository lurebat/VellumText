using System.Runtime.InteropServices;

namespace VellumText.Avalonia;

/// <summary>How much the control animates.</summary>
/// <remarks>
/// WCAG 2.3.3 asks that motion started by an interaction can be turned off, and every desktop
/// platform has a setting for it. <see cref="Auto"/> is that setting.
/// </remarks>
public enum MotionPreference
{
    /// <summary>
    /// Follow the operating system: Windows' "Show animations in Windows" and macOS's
    /// "Reduce motion". Linux has no setting the control can read without a D-Bus client, so
    /// there it means <see cref="Full"/>; a host that reads the desktop's setting can set
    /// <see cref="Reduced"/> itself.
    /// </summary>
    Auto,

    /// <summary>Move instantly: scrolls jump and the caret snaps, as with no animation at all.</summary>
    Reduced,

    /// <summary>Animate regardless of the operating system's setting.</summary>
    Full,
}

/// <summary>What the operating system says about motion and the caret.</summary>
/// <remarks>
/// <para>
/// Avalonia 12 exposes neither. Its <c>IPlatformSettings</c> carries tap size, double-tap time,
/// hold duration and colours, and nothing else, on every platform it supports.
/// </para>
/// <para>
/// Read when asked rather than once. Each is a single cheap system call, and caching them would
/// keep a setting the user has since changed.
/// </para>
/// </remarks>
internal static class SystemMotion
{
    /// <summary>The blink interval used where the platform has none to offer.</summary>
    internal static readonly TimeSpan FallbackBlinkInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Whether the user has asked the operating system for less motion.</summary>
    internal static bool PrefersReducedMotion()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsPrefersReducedMotion();
            }

            if (OperatingSystem.IsMacOS())
            {
                return Mac.ReduceMotion();
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // A platform without the library is a platform without the setting.
        }

        return false;
    }

    /// <summary>"Show animations in Windows", off. Remote Desktop sessions usually turn it off.</summary>
    /// <remarks>Not guarded: on Windows a failure here is a wrong declaration, and a test has to see it.</remarks>
    internal static bool WindowsPrefersReducedMotion()
    {
        var enabled = 1;

        return Windows.SystemParametersInfo(Windows.SpiGetClientAreaAnimation, 0, ref enabled, 0)
            && enabled == 0;
    }

    /// <summary>
    /// How long the caret stays in each state, or null when the user has turned blinking off.
    /// </summary>
    /// <remarks>
    /// Windows only. The other platforms keep this in settings a control cannot read cheaply, so
    /// they get <see cref="FallbackBlinkInterval"/>, which is what they default to anyway.
    /// </remarks>
    internal static TimeSpan? CaretBlinkInterval()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return FromWindowsBlinkTime(Windows.GetCaretBlinkTime());
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return FallbackBlinkInterval;
    }

    /// <summary>Reads what <c>GetCaretBlinkTime</c> returned.</summary>
    internal static TimeSpan? FromWindowsBlinkTime(uint ms) => ms switch
    {
        // INFINITE: the accessibility setting that stops the caret blinking.
        uint.MaxValue => null,

        // Zero is the call failing, not a caret that blinks infinitely fast.
        0 => FallbackBlinkInterval,
        _ => TimeSpan.FromMilliseconds(ms),
    };

    private static class Windows
    {
        internal const uint SpiGetClientAreaAnimation = 0x1042;

        // user32 exports only the A and W forms; with exact spelling the bare name does not resolve.
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint flags);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern uint GetCaretBlinkTime();
    }

    private static class Mac
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        /// <summary><c>[[NSWorkspace sharedWorkspace] accessibilityDisplayShouldReduceMotion]</c>.</summary>
        /// <remarks>
        /// Zero when AppKit is not loaded, which is the case in a headless process: there is no
        /// NSWorkspace class to ask, so there is no setting.
        /// </remarks>
        internal static bool ReduceMotion()
        {
            var workspaceClass = objc_getClass("NSWorkspace");

            if (workspaceClass == 0)
            {
                return false;
            }

            var workspace = objc_msgSend(workspaceClass, sel_registerName("sharedWorkspace"));

            return workspace != 0
                && objc_msgSend_bool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion"));
        }

        [DllImport(ObjC, ExactSpelling = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern nint objc_getClass(string name);

        [DllImport(ObjC, ExactSpelling = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern nint sel_registerName(string name);

        [DllImport(ObjC, ExactSpelling = true)]
        private static extern nint objc_msgSend(nint receiver, nint selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool objc_msgSend_bool(nint receiver, nint selector);
    }
}
