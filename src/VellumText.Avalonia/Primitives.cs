using AvColor = Avalonia.Media.Color;

namespace VellumText.Avalonia;

/// <summary>
/// Converts the two primitives <c>VellumText.Core</c> owns because it takes no dependency on
/// Avalonia, per architecture 4.1.
/// </summary>
/// <remarks>
/// The layouts are deliberately identical on both sides, so these are field-for-field copies
/// rather than translations. This is the only place in the product that knows both shapes.
/// </remarks>
internal static class Primitives
{
    /// <summary>Converts a VellumText colour to an Avalonia one.</summary>
    /// <param name="colour">The colour to convert.</param>
    public static AvColor ToAvalonia(this Rgba colour) =>
        AvColor.FromArgb(colour.A, colour.R, colour.G, colour.B);

    /// <summary>Converts an Avalonia colour to a VellumText one.</summary>
    /// <param name="colour">The colour to convert.</param>
    public static Rgba ToVellumText(this AvColor colour) =>
        new(colour.R, colour.G, colour.B, colour.A);

    /// <summary>Converts a VellumText span to an Avalonia one.</summary>
    /// <remarks>
    /// The two shapes agree on <c>Start</c>, <c>Length</c> and the value, which is all the
    /// conversion needs. They are not identical: Avalonia's span has no <c>End</c>, so callers
    /// that want one keep working in VellumText's span until the last moment.
    /// </remarks>
    /// <typeparam name="T">The attached value.</typeparam>
    /// <param name="span">The span to convert.</param>
    public static global::Avalonia.Utilities.ValueSpan<T> ToAvalonia<T>(this ValueSpan<T> span) =>
        new(span.Start, span.Length, span.Value);

    /// <summary>Converts an Avalonia span to a VellumText one.</summary>
    /// <typeparam name="T">The attached value.</typeparam>
    /// <param name="span">The span to convert.</param>
    public static ValueSpan<T> ToVellumText<T>(this global::Avalonia.Utilities.ValueSpan<T> span) =>
        new(span.Start, span.Length, span.Value);
}
