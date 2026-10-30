namespace VellumText.Interop.Html;

/// <summary>
/// Pictures handed to the HTML stage as their bytes, each named in the markup by a short token
/// instead of a data URL.
/// </summary>
/// <remarks>
/// <para>
/// For the RTF importer, whose converter would otherwise write each picture into the HTML as a
/// data URL, for the input guard to scan, the parser to tokenize and the DOM to resolve: about 17
/// times the RTF as a string for a 4 MiB picture. The converter asks for each picture's source
/// (<c>ImageSourceResolver</c>), and is given a token; the sanitizer approves a registered token
/// on the checks it gives a data image, and the walker puts the data URL in the document, built
/// once.
/// </para>
/// <para>
/// A token holds a value chosen at random for each import, so nothing in the document can name a
/// picture it was not given: a source that is not registered is judged as the address it is, and
/// refused.
/// </para>
/// </remarks>
internal sealed class InlinePictures
{
    private readonly string _prefix = "#vellum-picture-" + Guid.NewGuid().ToString("N") + "-";
    private readonly Dictionary<string, (string MediaType, byte[] Bytes)> _pictures = new(StringComparer.Ordinal);

    /// <summary>Registers a picture, and returns the token that names it.</summary>
    /// <param name="mediaType">Its media type, as a data URL would declare it.</param>
    /// <param name="bytes">Its bytes. Kept, not copied.</param>
    internal string Add(string mediaType, byte[] bytes)
    {
        var token = _prefix + _pictures.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

        _pictures.Add(token, (mediaType, bytes));

        return token;
    }

    /// <summary>The picture a source names, if it is one of these tokens.</summary>
    internal bool TryGet(string? source, out string mediaType, out byte[] bytes)
    {
        if (source is not null && _pictures.TryGetValue(source, out var picture))
        {
            (mediaType, bytes) = picture;

            return true;
        }

        (mediaType, bytes) = (string.Empty, []);

        return false;
    }

    /// <summary>The data URL for a picture, written once at its final length.</summary>
    internal static string DataUrl(string mediaType, byte[] bytes)
    {
        var prefix = "data:" + mediaType + ";base64,";
        var length = prefix.Length + checked(((bytes.Length + 2) / 3) * 4);

        return string.Create(length, (prefix, bytes), static (span, state) =>
        {
            state.prefix.AsSpan().CopyTo(span);

            if (!Convert.TryToBase64Chars(state.bytes, span[state.prefix.Length..], out var written)
                || written != span.Length - state.prefix.Length)
            {
                throw new InvalidOperationException("A picture's data URL came out the wrong length.");
            }
        });
    }
}
