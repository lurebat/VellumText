namespace VellumText;

/// <summary>
/// The one reading of a <c>data:</c> URL in this library.
/// </summary>
/// <remarks>
/// The importer's sanitizer, the image renderer, the RTF exporter and the data-URL image store
/// each used to parse these by their own rules: one trimmed leading whitespace and the next did
/// not, one looked for <c>;base64</c> anywhere in the header and the next only at its end. So the
/// importer admitted images the renderer could only draw as a missing-image box. They all ask
/// here now.
/// </remarks>
internal static class DataUri
{
    private const string Scheme = "data:";
    private const string Base64Marker = ";base64";

    /// <summary>Splits a base64 <c>data:</c> URL into its media type and payload.</summary>
    /// <param name="source">The URL, exactly as stored.</param>
    /// <param name="mediaType">The declared media type; empty if none was declared.</param>
    /// <param name="base64">The payload, still encoded.</param>
    /// <returns>Whether <paramref name="source"/> is a base64 <c>data:</c> URL.</returns>
    /// <remarks>
    /// Strict, because what it reads is a stored value: no surrounding whitespace (stripping that
    /// is HTML's rule for attribute values, applied before a URL is stored), and <c>;base64</c>
    /// must end the header, as RFC 2397 has it. A percent-encoded payload is a legal data URL that
    /// nothing here can draw, so it is refused too. The payload is not decoded; each caller bounds
    /// its own allocation first.
    /// </remarks>
    internal static bool TryParse(string? source, out string mediaType, out ReadOnlyMemory<char> base64)
    {
        mediaType = string.Empty;
        base64 = default;

        if (source is null || !source.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comma = source.IndexOf(',', Scheme.Length);

        if (comma < 0)
        {
            return false;
        }

        var header = source.AsSpan(Scheme.Length, comma - Scheme.Length);

        if (!header.EndsWith(Base64Marker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The marker ends the header, so there is always a semicolon to stop at.
        mediaType = header[..header.IndexOf(';')].ToString();
        base64 = source.AsMemory(comma + 1);

        return true;
    }

    /// <summary>Writes bytes as a base64 <c>data:</c> URL that <see cref="TryParse"/> reads back.</summary>
    /// <param name="mediaType">The media type to declare.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The URL.</returns>
    internal static string Format(string mediaType, ReadOnlySpan<byte> bytes) =>
        $"{Scheme}{mediaType}{Base64Marker},{Convert.ToBase64String(bytes)}";
}
