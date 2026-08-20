using VellumText.Interop.Html;

namespace VellumText.Interop.Markdown;

/// <summary>
/// How much of the incoming Markdown to trust.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are the paranoid ones. Markdown arriving from a file, a feed or a model's output is
/// hostile input in the ordinary case, never mind the malicious one, so anything that could reach
/// the network is off unless the host turns it on deliberately.
/// </para>
/// <para>
/// This is a separate type from <c>HtmlImportOptions</c> rather than a reuse of it. Markdown import
/// runs through an HTML stage internally — see <see cref="MarkdownImporter"/> — but that is an
/// implementation choice, and naming HTML's options type in this package's public API would make
/// every consumer of Markdown acquire a dependency on the HTML package to configure it, and would
/// freeze an internal decision into the public surface where a later rewrite could not change it.
/// This is the same reasoning that produced <c>RtfImportOptions</c>; the two are siblings.
/// </para>
/// <para>
/// It is deliberately smaller than HTML's. <c>MaxDepth</c> is absent because the HTML it would
/// apply to is generated here by Markdig rather than authored, and the nesting bound is a
/// stack-overflow guard that a host has no reason to loosen on markup it did not write.
/// </para>
/// </remarks>
public sealed record MarkdownImportOptions
{
    /// <summary>The defaults: trust nothing that could phone home, and no raw HTML.</summary>
    public static MarkdownImportOptions Default { get; } = new();

    /// <summary>
    /// Whether to keep images whose source is a remote URL. Off by default.
    /// </summary>
    /// <remarks>
    /// A remote image is a tracking pixel with extra steps: rendering one tells whoever wrote the
    /// document that it was opened, from which address, and when. In Markdown every image is a URL
    /// the document chose — <c>![](https://…)</c> — so this is the common case, not the rare one.
    /// </remarks>
    public bool AllowRemoteImages { get; init; }

    /// <summary>
    /// Whether to keep images carried inside the document as <c>data:</c> URLs. On by default.
    /// </summary>
    /// <remarks>
    /// A <c>data:</c> URL reaches no network, and only payloads whose media type is an image are
    /// accepted regardless. Turning it off means a self-contained Markdown document loses its
    /// pictures.
    /// </remarks>
    public bool AllowDataImages { get; init; } = true;

    /// <summary>
    /// The largest number of bytes an embedded image may take, before decoding. Defaults to 8 MiB.
    /// </summary>
    /// <remarks>
    /// A bound is needed because the source controls the length: a document can name a
    /// hundred-megabyte image as easily as a small one, and an import is not a good moment to find
    /// that out.
    /// </remarks>
    public int MaxDataImageBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// The address relative URLs are resolved against, or null to drop relative URLs.
    /// </summary>
    /// <remarks>
    /// Null is the safe default: a relative link in a document has no meaning in the document it is
    /// being pasted into, and guessing one is how a link ends up pointing somewhere nobody intended.
    /// </remarks>
    public Uri? BaseUri { get; init; }

    /// <summary>
    /// Whether raw HTML embedded in the Markdown is read at all. Off by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CommonMark permits arbitrary HTML, as blocks and inline, and passes it through untouched.
    /// That makes every Markdown document a potential carrier for every HTML hazard — script,
    /// event handlers, <c>iframe</c>, <c>object</c>, styled overlays — in a format whose whole
    /// reputation is for being plain text. A reader that renders it because CommonMark says to has
    /// imported an attack surface nobody asked for.
    /// </para>
    /// <para>
    /// Off, every <c>HtmlBlock</c> is removed from the parsed tree before anything is rendered, and
    /// so is every <c>HtmlInline</c> whose element can hold ordinary text. An inline tag's
    /// surrounding text survives — the tags are separate nodes from the literals they wrap — but a
    /// raw HTML block's text goes with it, which is why that case is reported as
    /// <see cref="DiagnosticSeverity.Dropped"/> rather than as a downgrade.
    /// </para>
    /// <para>
    /// A tag for an element whose body is never content, such as <c>script</c> or <c>style</c>, is
    /// the exception: it is left in the tree so that the sanitizer below removes the element with
    /// its subtree. Those elements are <em>raw text</em> in HTML — everything to the matching close
    /// is text and not markup — and the Markdown parser has no such concept, so the body between
    /// two such tags can be scattered across containers and blocks that no walk of the tree can
    /// relate. Deleting the tags here would release that body as prose; handing them on deletes it.
    /// Nothing reaches the document either way, and both settings agree on every case.
    /// </para>
    /// <para>
    /// On, the raw HTML is rendered and then passes through the same sanitizer every HTML import
    /// goes through: one scheme allow-list, one set of permitted elements, one place to get it
    /// wrong. It is still not <em>trusted</em>; it is merely checked rather than discarded.
    /// </para>
    /// </remarks>
    public bool AllowRawHtml { get; init; }

    /// <summary>The equivalent HTML options for the internal HTML stage.</summary>
    internal HtmlImportOptions ToHtmlOptions() => new()
    {
        AllowRemoteImages = AllowRemoteImages,
        AllowDataImages = AllowDataImages,
        MaxDataImageBytes = MaxDataImageBytes,
        BaseUri = BaseUri,
    };
}
