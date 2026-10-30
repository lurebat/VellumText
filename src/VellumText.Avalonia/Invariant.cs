using System.Diagnostics;

namespace VellumText.Avalonia;

/// <summary>Layout invariants checked in debug builds.</summary>
/// <remarks>
/// Thrown rather than asserted. <see cref="Debug.Assert(bool)"/> fails by tearing the process
/// down on .NET when no debugger is attached, and a test host that dies can still report the
/// tests it finished as passed; an exception fails the test that broke the invariant.
/// </remarks>
internal static class Invariant
{
    /// <summary>Throws if <paramref name="condition"/> is false, in debug builds only.</summary>
    /// <param name="condition">What must hold.</param>
    /// <param name="what">What it says, for the message.</param>
    /// <exception cref="InvalidOperationException">It does not hold.</exception>
    [Conditional("DEBUG")]
    public static void That(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Layout invariant broken: " + what);
        }
    }
}
