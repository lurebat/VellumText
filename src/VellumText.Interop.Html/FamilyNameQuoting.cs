namespace VellumText.Interop.Html;

/// <summary>
/// The quoted form a single family name is stored in when it holds a comma.
/// </summary>
/// <remarks>
/// <see cref="MarkSet.FontFamily"/> is read by the HTML exporter as a font stack, in which a comma
/// separates names unless it is inside a name the host quoted. A name that holds a comma, such as
/// <c>ACME, Inc.</c>, is therefore stored as <c>"ACME, Inc."</c>, or it would be exported as the
/// two families <c>ACME</c> and <c>Inc.</c>. The exporter strips the quotes and does not resolve
/// escapes inside them, so the name is wrapped in a quote it does not contain, and a name that
/// ends in a backslash, which would escape the closing quote, or holds both quotes is left bare.
/// A name without a comma is stored bare, as it always was.
/// </remarks>
internal static class FamilyNameQuoting
{
    /// <summary>The form <paramref name="name"/> is stored in.</summary>
    /// <param name="name">A single family name.</param>
    /// <returns>The name, quoted if it holds a comma and a quote can hold it.</returns>
    internal static string Quote(string name)
    {
        name = name.Trim();

        if (!name.Contains(',', StringComparison.Ordinal) || name.EndsWith('\\'))
        {
            return name;
        }

        if (!name.Contains('"', StringComparison.Ordinal))
        {
            return $"\"{name}\"";
        }

        return name.Contains('\'', StringComparison.Ordinal) ? name : $"'{name}'";
    }

    /// <summary>The name a value <see cref="Quote"/> produced stands for.</summary>
    /// <param name="family">A stored family.</param>
    /// <returns>
    /// The name inside the quotes if <paramref name="family"/> is a quoted name, otherwise
    /// <paramref name="family"/> unchanged.
    /// </returns>
    /// <remarks>
    /// For a format with no notion of a font stack, which takes the stored family as one name.
    /// </remarks>
    internal static string Unquote(string family)
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
}