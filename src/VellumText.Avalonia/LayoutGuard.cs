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

    [ThreadStatic]
    private static int s_answering;

    /// <summary>Whether a frame is being painted on this thread.</summary>
    public static bool IsPainting => s_painting > 0;

    /// <summary>Marks a frame as being painted until the scope is disposed.</summary>
    public static Scope Painting()
    {
        s_painting++;

        return new Scope(Scope.Kind.Painting);
    }

    /// <summary>
    /// Marks a geometry question as being answered from what was laid out for it, until the scope
    /// is disposed; or nothing, when its preparation could not be completed.
    /// </summary>
    /// <param name="prepared">Whether everything the answer reads was laid out beforehand.</param>
    public static Scope Answering(bool prepared)
    {
        if (!prepared)
        {
            return default;
        }

        s_answering++;

        return new Scope(Scope.Kind.Answering);
    }

    /// <summary>Throws, in debug builds, if a frame is being painted.</summary>
    /// <param name="what">What was about to change, for the message.</param>
    /// <exception cref="InvalidOperationException">A frame is being painted, or a prepared question answered.</exception>
    [Conditional("DEBUG")]
    public static void Changing(string what)
    {
        if (s_painting > 0)
        {
            throw new InvalidOperationException($"Layout changed while painting: {what}.");
        }

        if (s_answering > 0)
        {
            throw new InvalidOperationException($"Layout changed while answering a prepared question: {what}.");
        }
    }

    /// <summary>The extent of a <see cref="Painting"/> or <see cref="Answering"/> scope.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly Kind _kind;

        internal Scope(Kind kind) => _kind = kind;

        internal enum Kind
        {
            None,
            Painting,
            Answering,
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            switch (_kind)
            {
                case Kind.Painting:
                    s_painting--;
                    break;
                case Kind.Answering:
                    s_answering--;
                    break;
            }
        }
    }
}
