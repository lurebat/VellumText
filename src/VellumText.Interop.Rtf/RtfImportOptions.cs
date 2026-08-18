using VellumText.Interop.Html;

namespace VellumText.Interop.Rtf;

/// <summary>
/// How much of the incoming RTF to trust.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are the paranoid ones. RTF arriving on the clipboard or from a file is hostile
/// input in the ordinary case, never mind the malicious one, so anything that could reach the
/// network is off unless the host turns it on deliberately.
/// </para>
/// <para>
/// This is a separate type from <c>HtmlImportOptions</c> rather than a reuse of it. RTF import runs
/// through an HTML stage internally — see §5.4 — but that is an implementation choice, and naming
/// HTML's options type in this package's public API would make every consumer of RTF acquire a
/// dependency on the HTML package to configure it, and would freeze an internal decision into the
/// public surface where a later rewrite of the RTF reader could not change it.
/// </para>
/// <para>
/// It is deliberately smaller. <c>MaxDepth</c> and <c>PreserveIdentifiers</c> are absent because
/// the HTML they would apply to is generated here rather than authored: there are no <c>class</c>
/// or <c>id</c> attributes to preserve, and the nesting bound is a stack-overflow guard that a host
/// has no reason to loosen on markup it did not write.
/// </para>
/// </remarks>
public sealed record RtfImportOptions
{
    /// <summary>The defaults: trust nothing that could phone home.</summary>
    public static RtfImportOptions Default { get; } = new();

    /// <summary>
    /// Whether to keep images whose source is a remote URL. Off by default.
    /// </summary>
    /// <remarks>
    /// A remote image is a tracking pixel with extra steps: rendering one tells whoever sent the
    /// document that it was opened, from which address, and when. RTF reaches these through an
    /// <c>INCLUDEPICTURE</c> field, which is a URL chosen by the document rather than by the user.
    /// </remarks>
    public bool AllowRemoteImages { get; init; }

    /// <summary>
    /// Whether to keep images carried inside the document. On by default.
    /// </summary>
    /// <remarks>
    /// This is how an image in RTF actually arrives — <c>\pict</c> is bytes in the payload, which
    /// reach the model as a <c>data:</c> URL — so turning it off means images do not survive a
    /// paste from a word processor at all. It reaches no network, and only payloads whose media
    /// type is an image are accepted regardless.
    /// </remarks>
    public bool AllowDataImages { get; init; } = true;

    /// <summary>
    /// The largest number of bytes an embedded image may take, before decoding. Defaults to 8 MiB.
    /// </summary>
    /// <remarks>
    /// A bound is needed because the source controls the length: a clipboard payload can name a
    /// hundred-megabyte image as easily as a small one, and a paste is not a good moment to find
    /// that out.
    /// </remarks>
    public int MaxDataImageBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// The address relative URLs are resolved against, or null to drop relative URLs.
    /// </summary>
    /// <remarks>
    /// Null is the safe default: a relative link in a pasted fragment has no meaning in the
    /// document it is being pasted into, and guessing one is how a link ends up pointing somewhere
    /// nobody intended.
    /// </remarks>
    public Uri? BaseUri { get; init; }

    /// <summary>The equivalent HTML options for the internal HTML stage.</summary>
    internal HtmlImportOptions ToHtmlOptions() => new()
    {
        AllowRemoteImages = AllowRemoteImages,
        AllowDataImages = AllowDataImages,
        MaxDataImageBytes = MaxDataImageBytes,
        BaseUri = BaseUri,
    };
}
