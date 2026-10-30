using System.Diagnostics;

namespace VellumText.Avalonia;

/// <summary>Catches layout being changed while a frame is painted, in debug builds.</summary>
/// <remarks>
/// <para>
/// Painting lays nothing out. A row or a paragraph laid out in the middle of a frame changes a
/// height the presenter recorded, and with it the extent and the position of everything below,
/// behind the presenter's back and after part of the frame was drawn from the old values. Every
/// position painting asks about is laid out beforehand instead (<c>PreparePaintDependencies</c>),
/// and painting asks only questions that can be answered from what is laid out.
/// </para>
/// <para>
/// This is the check that it does. It throws in debug builds, where the tests run, rather than
/// asserting, so a test that paints a frame which lays something out fails at the line that did.
/// </para>
/// </remarks>
internal static class LayoutGuard
{
    [ThreadStatic]
    private static int s_painting;

    /// <summary>Whether a frame is being painted on this thread.</summary>
    public static bool IsPainting => s_painting > 0;

    /// <summary>Marks a frame as being painted until the scope is disposed.</summary>
    public static Scope Painting()
    {
        s_painting++;

        return default;
    }

    /// <summary>Throws, in debug builds, if a frame is being painted.</summary>
    /// <param name="what">What was about to change, for the message.</param>
    /// <exception cref="InvalidOperationException">A frame is being painted.</exception>
    [Conditional("DEBUG")]
    public static void Changing(string what)
    {
        if (s_painting > 0)
        {
            throw new InvalidOperationException($"Layout changed while painting: {what}.");
        }
    }

    /// <summary>The extent of a <see cref="Painting"/> scope.</summary>
    public readonly struct Scope : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() => s_painting--;
    }
}
