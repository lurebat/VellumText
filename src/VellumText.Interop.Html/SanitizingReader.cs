using System.Collections.Immutable;

using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace VellumText.Interop.Html;

/// <summary>
/// Configures <see cref="HtmlSanitizer"/> for what the VellumText document model can actually
/// represent, and reports what it removed.
/// </summary>
/// <remarks>
/// <para>
/// The sanitizing itself is deliberately not ours. An XSS filter is a block-list of everything
/// anyone has thought of so far, maintained against a research community that thinks of new ones;
/// a hand-written one is a list of the attacks its author happened to know about on the day. So the
/// policy here is configuration of a maintained implementation, not a reimplementation of it.
/// </para>
/// <para>
/// What is ours is the allow-list's shape, because that is a question about this editor rather than
/// about security in general: there is no point keeping an element the document model has nowhere
/// to put.
/// </para>
/// </remarks>
internal sealed class SanitizingReader
{
    /// <summary>
    /// Elements worth keeping. Anything else has its tags dropped and its children kept, so the
    /// text inside an unrecognised wrapper survives.
    /// </summary>
    private static readonly string[] Tags =
    [
        // The document's own scaffolding. These are not content and never become blocks, but
        // removing them takes the body with them and leaves nothing to walk.
        "html", "head", "body",

        // Blocks the model has a node for.
        "p", "div", "br", "h1", "h2", "h3", "h4", "h5", "h6",
        "blockquote", "pre", "ul", "ol", "li", "hr",

        // Tables.
        "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "colgroup", "col",

        // Inline formatting.
        "span", "a", "img", "strong", "b", "em", "i", "u", "ins", "s", "strike", "del",
        "sup", "sub", "mark", "small", "big", "code", "tt", "kbd", "samp", "var", "font",
        "abbr", "cite", "q", "time", "dfn", "wbr",

        // Containers that carry no formatting but do carry block structure.
        "section", "article", "main", "header", "footer", "aside", "nav",
        "figure", "figcaption", "dl", "dt", "dd", "center", "address",
    ];

    /// <summary>
    /// Elements whose text is never rendered, and so is never content.
    /// </summary>
    /// <remarks>
    /// These are removed with their subtrees before the sanitizer runs, because
    /// <see cref="HtmlSanitizer.KeepChildNodes"/> is deliberately on and would otherwise promote
    /// their bodies to visible text. The setting is right for an unrecognised wrapper — a fragment
    /// inside a <c>&lt;div&gt;</c> is still a fragment — and wrong here, where the children are
    /// program text rather than something the user copied. A browser renders none of these.
    /// <para>
    /// Visible to <c>VellumText.Interop.Markdown</c> rather than copied into it. That importer does
    /// not remove these elements itself — it cannot, because its parser has no notion of raw text
    /// and so cannot find their bodies — but it does have to recognise their tags, in order to leave
    /// them in the tree for this sanitizer instead of deleting them and freeing what they enclosed.
    /// One implementation of "whose text is never content" and one list to extend, rather than two
    /// of each. It is exposed as an <see cref="ImmutableArray{T}"/> so sharing it cannot mean
    /// sharing the ability to edit it.
    /// </para>
    /// </remarks>
    internal static readonly ImmutableArray<string> NonContentElements =
    [
        "script", "style", "template", "noscript", "noframes", "noembed", "title",
    ];

    private static readonly string[] Attributes =
    [
        "href", "src", "alt", "title", "style", "dir", "lang",
        "colspan", "rowspan", "start", "type", "reversed", "value",
        "width", "height", "align", "valign", "color", "face", "size", "cite",
    ];

    /// <summary>
    /// CSS properties the model can express. Everything else is removed, which matters more than
    /// it looks: <c>position</c>, <c>behavior</c> and <c>-moz-binding</c> have all been script
    /// vectors, and none of them mean anything in a document model with no layout of its own.
    /// </summary>
    private static readonly string[] CssProperties =
    [
        "font-weight", "font-style", "font-family", "font-size",
        "text-decoration", "text-decoration-line", "text-decoration-style",
        "color", "background-color", "background",
        "text-align", "vertical-align", "direction",
        "margin-left", "margin-inline-start", "padding-left", "padding-inline-start",
        "text-indent", "list-style-type", "white-space",
    ];

    private static readonly string[] Schemes = ["http", "https", "mailto", "tel"];

    private static readonly HashSet<string> SchemeSet =
        new(Schemes, StringComparer.OrdinalIgnoreCase);

    /// <summary>What HTML strips from either end of a URL before parsing it: C0 controls and space.</summary>
    private static readonly char[] UrlEdgeWhitespace =
        [.. Enumerable.Range(0, 0x21).Select(c => (char)c)];

    /// <summary>
    /// Media types an inline <c>data:</c> image may declare.
    /// </summary>
    /// <remarks>
    /// <c>image/svg+xml</c> is deliberately absent, and its absence is the whole reason this list
    /// is written out rather than expressed as "anything beginning image/". SVG is a document
    /// format that can carry script and fetch external references, so an SVG data URL is markup
    /// injection wearing an image's name — and it arrives having already passed every check that
    /// looks at the word before the slash.
    /// </remarks>
    private static readonly HashSet<string> DataImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/png", "image/jpeg", "image/jpg", "image/gif",
            "image/bmp", "image/webp", "image/tiff",
            "image/x-icon", "image/vnd.microsoft.icon",
        };

    private readonly HtmlImportOptions _options;
    private readonly List<ImportDiagnostic> _diagnostics;
    private readonly HashSet<string> _alreadyReported = new(StringComparer.OrdinalIgnoreCase);

    internal SanitizingReader(HtmlImportOptions options, List<ImportDiagnostic> diagnostics)
    {
        _options = options;
        _diagnostics = diagnostics;
    }

    /// <summary>Sanitizes a parsed document in place.</summary>
    /// <param name="document">The document to clean.</param>
    internal void Sanitize(IDocument document)
    {
        if (_options.RemoveBeforeSanitizing is { } selector)
        {
            foreach (var element in document.QuerySelectorAll(selector))
            {
                element.Remove();
            }
        }

        if (_options.BorderedEmptyParagraphIsRule)
        {
            foreach (var paragraph in document.QuerySelectorAll("p[style*='border-bottom']"))
            {
                if (!paragraph.HasChildNodes)
                {
                    paragraph.Replace(document.CreateElement("hr"));
                }
            }
        }

        var sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(Attributes, StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(CssProperties, StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(Schemes, StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
            UriAttributes = new HashSet<string>(["href", "src", "cite"], StringComparer.OrdinalIgnoreCase),
        })
        {
            // An unknown element's tags go but its content stays. A fragment wrapped in something
            // we do not recognise is still a fragment, and throwing away the text inside it would
            // lose the thing the user actually copied.
            KeepChildNodes = true,

            // "data-*" attributes are an application's private state. They mean nothing here.
            AllowDataAttributes = false,
        };

        sanitizer.RemovingTag += (_, e) => ReportOnce(
            DiagnosticSeverity.Dropped,
            "Removed an element that cannot appear in a document.",
            e.Tag.NodeName.ToLowerInvariant());

        sanitizer.RemovingAttribute += (_, e) => ReportOnce(
            DiagnosticSeverity.Dropped,
            "Removed an attribute that is not safe to keep.",
            e.Attribute.Name.ToLowerInvariant());

        sanitizer.RemovingStyle += (_, e) => ReportOnce(
            DiagnosticSeverity.Downgraded,
            "Removed a style the editor cannot represent.",
            e.Style.Name.ToLowerInvariant());

        sanitizer.FilterUrl += OnFilterUrl;

        // Before the sanitizer, not through it. The sanitizer would remove these tags and - because
        // KeepChildNodes is on - hand their bodies back as text, so a stylesheet becomes the first
        // paragraph of a Word paste and a script becomes prose. Removing the subtree first is the
        // only order that keeps KeepChildNodes doing the job it is there for.
        foreach (var element in document.QuerySelectorAll(string.Join(',', NonContentElements)).ToArray())
        {
            ReportOnce(
                DiagnosticSeverity.Dropped,
                "Removed an element whose contents are code rather than text.",
                element.NodeName.ToLowerInvariant());

            element.Remove();
        }

        // The base address has to reach the sanitizer, not just the walker: it resolves relative
        // URLs before it checks their scheme, so without it a relative address is judged on its
        // own and there is nothing left for the walker to resolve afterwards.
        var baseUrl = _options.BaseUri?.AbsoluteUri ?? string.Empty;

        // Style attributes go through the sanitizer one distinct value at a time rather than once
        // per element (F-16). The document is parsed without CSS support, because with it every
        // element's style is parsed while the tree is built and parsed again here: RtfPipe writes
        // the same three style strings on every element of a Word document, and 10,000 paragraphs
        // allocated 850 MB doing it, against 35 MB for the same markup without styles. Without CSS
        // support the sanitizer would simply strip every style, so they are set aside first, each
        // distinct value is sanitized once by this same sanitizer in a one-element document that
        // does have CSS support, and the result is put back on the elements that survived.
        var styles = new List<(IElement Element, string Style)>();

        foreach (var element in document.QuerySelectorAll("[style]"))
        {
            styles.Add((element, element.GetAttribute("style") ?? string.Empty));
            element.RemoveAttribute("style");
        }

        sanitizer.SanitizeDom((IHtmlDocument)document, context: null, baseUrl: baseUrl);

        if (styles.Count == 0)
        {
            return;
        }

        var survivors = document.QuerySelectorAll("*").ToHashSet();
        var sanitized = new Dictionary<string, string?>(StringComparer.Ordinal);
        var probe = new HtmlParser(new HtmlParserOptions(), BrowsingContext.New(Configuration.Default.WithCss()));

        foreach (var (element, style) in styles)
        {
            if (!survivors.Contains(element))
            {
                continue;
            }

            if (!sanitized.TryGetValue(style, out var clean))
            {
                clean = SanitizeStyle(sanitizer, probe, style, baseUrl);
                sanitized[style] = clean;
            }

            if (!string.IsNullOrWhiteSpace(clean))
            {
                element.SetAttribute("style", clean);
            }
        }
    }

    /// <summary>
    /// What <paramref name="sanitizer"/> leaves of one style attribute, decided on an element of
    /// its own in a document that can read CSS.
    /// </summary>
    private static string? SanitizeStyle(HtmlSanitizer sanitizer, HtmlParser probe, string style, string baseUrl)
    {
        var document = probe.ParseDocument("<span></span>");
        var span = document.QuerySelector("span")!;

        span.SetAttribute("style", style);
        sanitizer.SanitizeDom(document, context: null, baseUrl: baseUrl);

        return document.QuerySelector("span")?.GetAttribute("style");
    }

    /// <summary>
    /// Applies the two URL decisions the sanitizer cannot make for us.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first is that a remote image is not merely a picture. Fetching one tells whoever sent
    /// the document that it was opened, by which address and when, so it stays out unless the host
    /// has said otherwise — and the sanitizer will happily have approved it already, because
    /// <c>https</c> is a perfectly good scheme. Refusing it therefore means taking an approval
    /// back, not declining to grant one.
    /// </para>
    /// <para>
    /// The second is the reverse: <c>data:</c> is not an allowed scheme, which is right for a
    /// hyperlink — <c>data:text/html</c> in an <c>href</c> is a navigable document — and wrong for
    /// the <c>&lt;img&gt;</c> that a pasted picture actually arrives as. So the exception is
    /// granted, as narrowly as the event allows: an image element, the value that is actually its
    /// source rather than some other URL attribute that happens to sit on it, a declared media type
    /// on the list above, and a length agreed in advance.
    /// </para>
    /// </remarks>
    private void OnFilterUrl(object? sender, FilterUrlEventArgs e)
    {
        var original = e.OriginalUrl;

        if (original is null)
        {
            return;
        }

        // The event says which element the URL came from but not which attribute, and an element
        // can carry more than one. Matching the value against the source attribute is what keeps
        // the exception below from leaking onto an "href" that happens to sit on an image.
        var isImageSource =
            e.Tag is IHtmlImageElement
            && string.Equals(e.Tag.GetAttribute("src"), original, StringComparison.Ordinal);

        if (e.SanitizedUrl is not null)
        {
            // The sanitizer resolves relative addresses against the base the caller gave it. If
            // one is still relative afterwards there was no base, and keeping it would leave the
            // address to be resolved against whatever the host happens to be showing later.
            //
            // The test is the scheme, not UriKind.Absolute. "Absolute" is not the same question:
            // System.Uri reads a scheme-less address as a *file path* whenever the platform has
            // one that fits, so "//host/path" and "\\host\share" parse absolute with scheme file
            // on Windows, and "/etc/passwd" does the same on Unix. All three are relative
            // addresses that would have been kept. Requiring an allowed scheme asks the question
            // that was meant, and asks it the same way on every platform.
            if (!Uri.TryCreate(e.SanitizedUrl, UriKind.Absolute, out var resolved)
                || !SchemeSet.Contains(resolved.Scheme))
            {
                e.SanitizedUrl = null;

                ReportOnce(
                    DiagnosticSeverity.Dropped,
                    "Removed an address that is relative to a document this one did not come with.",
                    original);
                return;
            }

            if (isImageSource
                && !_options.AllowRemoteImages
                && !original.AsSpan().TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                e.SanitizedUrl = null;

                ReportOnce(
                    DiagnosticSeverity.Dropped,
                    "Removed an image loaded from the network, which would have told its host that "
                    + "this document was opened.",
                    "img");
            }

            return;
        }

        // Stripped as HTML strips any URL in an attribute, and stored stripped, so that what the
        // document holds is a URL the renderer and the exporters read the same way.
        var url = original.Trim(UrlEdgeWhitespace);

        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            ReportOnce(
                DiagnosticSeverity.Dropped,
                "Removed a link whose address is not http, https, mailto or tel.",
                SchemeOf(original));
            return;
        }

        if (!isImageSource)
        {
            ReportOnce(
                DiagnosticSeverity.Dropped,
                "Removed inline data used somewhere other than an image source.",
                "data:");
            return;
        }

        if (!_options.AllowDataImages)
        {
            ReportOnce(DiagnosticSeverity.Dropped, "Removed an inline image.", "data:");
            return;
        }

        if (!DataUri.TryParse(url, out var mediaType, out var base64))
        {
            // A percent-encoded payload is a legal data URL, but nothing in this library can draw
            // one, so admitting it would put an image in the document that only ever draws as a
            // missing-image box.
            ReportOnce(
                DiagnosticSeverity.Dropped,
                "Removed an inline image that is not a base64 data URL.",
                "data:");
            return;
        }

        if (!DataImageTypes.Contains(mediaType))
        {
            ReportOnce(
                DiagnosticSeverity.Dropped,
                "Removed inline data that does not declare a supported image type.",
                mediaType);
            return;
        }

        switch (MeasureDataImage(base64.Span, _options.MaxDataImageBytes))
        {
            case DataImageVerdict.TooLarge:
                ReportOnce(
                    DiagnosticSeverity.Dropped,
                    $"Removed an inline image larger than {_options.MaxDataImageBytes} bytes.",
                    "data:");
                return;

            case DataImageVerdict.Undecodable:
                ReportOnce(
                    DiagnosticSeverity.Dropped,
                    "Removed an inline image whose data is not valid base64.",
                    "data:");
                return;
        }

        e.SanitizedUrl = url;
    }

    /// <summary>What inspecting the payload of a <c>data:</c> image concluded.</summary>
    private enum DataImageVerdict
    {
        /// <summary>The payload decodes and is within the size limit.</summary>
        Usable,

        /// <summary>The payload decodes to more bytes than are allowed.</summary>
        TooLarge,

        /// <summary>The payload does not decode at all.</summary>
        Undecodable,
    }

    /// <summary>Checks that a <c>data:</c> image decodes, and how large it really is.</summary>
    /// <param name="payload">The data URL's base64 payload.</param>
    /// <param name="maxBytes">The largest decoded size to allow.</param>
    /// <returns>What the payload turned out to be.</returns>
    /// <remarks>
    /// The limit is named in bytes and has to be measured in bytes. Base64 spends four characters
    /// on every three bytes, so judging the URL's length instead silently applies a limit a quarter
    /// tighter than the one that was asked for.
    /// <para>
    /// Decoding is also the only way to find out whether the payload is an image at all rather than
    /// a broken paste. A run of characters that cannot decode would otherwise reach the document as
    /// an embed and fail later, at draw time, far away from anything that could explain it.
    /// </para>
    /// </remarks>
    private static DataImageVerdict MeasureDataImage(ReadOnlySpan<char> payload, int maxBytes)
    {
        // Four characters become three bytes, so the decoded size is at least this, less padding.
        // Checking it first means an enormous payload is rejected without allocating room for it.
        if ((long)(payload.Length / 4) * 3 - 2 > maxBytes)
        {
            return DataImageVerdict.TooLarge;
        }

        var buffer = new byte[(payload.Length / 4 * 3) + 3];

        if (!Convert.TryFromBase64Chars(payload, buffer, out var written))
        {
            return DataImageVerdict.Undecodable;
        }

        if (written == 0)
        {
            return DataImageVerdict.Undecodable;
        }

        return written > maxBytes ? DataImageVerdict.TooLarge : DataImageVerdict.Usable;
    }

    private static string SchemeOf(string url)
    {
        var colon = url.IndexOf(':');

        return colon <= 0 ? "relative" : url[..colon].Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Records a diagnostic the first time each distinct thing is removed.
    /// </summary>
    /// <remarks>
    /// A page's worth of HTML can contain thousands of removals of the same kind. Reporting each
    /// one turns a useful list into a wall the caller cannot read, and gives the source control
    /// over how much memory the diagnostics take.
    /// </remarks>
    private void ReportOnce(DiagnosticSeverity severity, string message, string context)
    {
        if (_alreadyReported.Add($"{severity}|{context}"))
        {
            _diagnostics.Add(new ImportDiagnostic(severity, message, context));
        }
    }
}
