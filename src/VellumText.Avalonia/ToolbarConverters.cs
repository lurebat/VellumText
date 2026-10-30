using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>
/// The conversions a <see cref="RichTextToolbar"/> template needs and markup cannot do.
/// </summary>
/// <remarks>
/// A toolbar reports what the selection <em>is</em> — Heading2, Center, Ordered — and a button
/// asks whether that is the one thing it stands for. Avalonia has no comparison binding, so the
/// alternative to these is a boolean property per button on the control, which would put the
/// template's arrangement of buttons back into the control the template exists to keep it out of.
/// </remarks>
public static class ToolbarConverters
{
    /// <summary>Whether a value equals the converter parameter.</summary>
    /// <remarks>
    /// The parameter arrives from markup as a string, so the comparison is by name. That also
    /// makes null answer false rather than throwing, which is what a mixed selection is.
    /// </remarks>
    public static readonly IValueConverter Is = new IsConverter();

    /// <summary>A VellumText colour as a brush, or transparent where there is no colour.</summary>
    /// <remarks>
    /// No colour is not black. It means the text takes the colour it inherits, and a swatch that
    /// showed black for it would say the opposite of what is true.
    /// </remarks>
    public static readonly IValueConverter Swatch = new SwatchConverter();

    /// <summary>A font size in points, or the empty string where there is no one size.</summary>
    public static readonly IValueConverter Points = new PointsConverter();

    /// <summary>A stored family as the font box shows it: a quoted name without its quotes.</summary>
    /// <remarks>
    /// A family name holding a comma is stored quoted (<c>"ACME, Inc."</c>) so that an export
    /// reads it as one name rather than a stack of two. The quotes are notation, not part of the
    /// name the user knows, so the box shows <c>ACME, Inc.</c>; <see cref="FamilyToStore"/> puts
    /// them back when the user commits.
    /// </remarks>
    internal static readonly IValueConverter FamilyName = new FamilyNameConverter();

    /// <summary>The form a family the user picked or typed is stored in.</summary>
    internal static readonly IValueConverter FamilyToStore = new FamilyToStoreConverter();

    /// <summary>The name a stored family stands for, without the quotes that keep it one name.</summary>
    /// <remarks>
    /// The inverse of <see cref="StoredFamily"/>, and the same rule the interop exporters use: only
    /// a quoted name holding a comma, with no quote of its own kind and no trailing backslash
    /// inside, is unwrapped. Anything else is shown as stored.
    /// </remarks>
    internal static string DisplayedFamily(string family)
    {
        if (family.Length < 2 || family[0] is not ('"' or '\'') || family[^1] != family[0])
        {
            return family;
        }

        var name = family[1..^1];

        return name.Contains(',', StringComparison.Ordinal)
            && !name.Contains(family[0], StringComparison.Ordinal)
            && !name.EndsWith('\\')
                ? name
                : family;
    }

    /// <summary>The stored form of a family name the user picked or typed.</summary>
    /// <remarks>
    /// A name holding a comma is wrapped in a quote it does not contain, the form the importers
    /// store such a name in, so what the box commits is exactly what a paste of the same font
    /// would have produced. A name the user already quoted, a name with no comma, and one no quote
    /// can hold — it holds both, or ends in the backslash that would escape the closing quote —
    /// are stored as typed.
    /// </remarks>
    internal static string? StoredFamily(string? typed)
    {
        if (typed is null)
        {
            return null;
        }

        var name = typed.Trim();

        if (!name.Contains(',', StringComparison.Ordinal)
            || name.EndsWith('\\')
            || DisplayedFamily(name) != name)
        {
            return name;
        }

        if (!name.Contains('"', StringComparison.Ordinal))
        {
            return $"\"{name}\"";
        }

        return name.Contains('\'', StringComparison.Ordinal) ? name : $"'{name}'";
    }

    private sealed class IsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not null
            && parameter is not null
            && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            null;
    }

    private sealed class SwatchConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Rgba colour
                ? new SolidColorBrush(colour.ToAvalonia())
                : Brushes.Transparent;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            null;
    }

    private sealed class PointsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is double size ? size.ToString("0.#", culture) : string.Empty;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value?.ToString();
    }

    private sealed class FamilyNameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string family ? DisplayedFamily(family) : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            StoredFamily(value as string);
    }

    private sealed class FamilyToStoreConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string typed ? StoredFamily(typed) : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            null;
    }
}
