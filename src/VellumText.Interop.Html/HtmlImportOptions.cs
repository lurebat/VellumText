namespace VellumText.Interop.Html;

/// <summary>
/// How much of the incoming HTML to trust.
/// </summary>
/// <remarks>
/// The defaults are the paranoid ones. HTML arriving on the clipboard or from a file is hostile
/// input in the ordinary case, never mind the malicious one, so anything that could reach the
/// network or execute is off unless the host turns it on deliberately.
/// </remarks>
public sealed record HtmlImportOptions
{
    /// <summary>The defaults: trust nothing that could phone home.</summary>
    public static HtmlImportOptions Default { get; } = new();

    /// <summary>
    /// Whether to keep images whose source is a remote URL. Off by default.
    /// </summary>
    /// <remarks>
    /// A remote image is a tracking pixel with extra steps: rendering one tells whoever sent the
    /// document that it was opened, from which address, and when. Hosts that already accept that
    /// tradeoff — a mail client showing a message the user asked to see — can turn it on.
    /// </remarks>
    public bool AllowRemoteImages { get; init; }

    /// <summary>
    /// Whether to keep images encoded inline as a <c>data:</c> URL. On by default.
    /// </summary>
    /// <remarks>
    /// This is how a pasted image actually arrives — from RTF, from a browser, from Word — so
    /// turning it off means images do not survive a paste at all. It is materially safer than a
    /// remote image because it reaches no network, and only URLs whose media type is an image are
    /// accepted regardless.
    /// </remarks>
    public bool AllowDataImages { get; init; } = true;

    /// <summary>
    /// The largest number of bytes an inline <c>data:</c> image may take, before decoding.
    /// Defaults to 8 MiB.
    /// </summary>
    /// <remarks>
    /// A bound is needed because the source controls the length: a clipboard fragment can name a
    /// hundred-megabyte image as easily as a small one, and a paste is not a good moment to find
    /// that out.
    /// </remarks>
    public int MaxDataImageBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// The deepest the element tree may nest before the importer stops descending. Defaults to 256.
    /// </summary>
    /// <remarks>
    /// The parser handles arbitrarily deep documents, but a recursive walk over one does not — a
    /// fragment of ten thousand nested <c>&lt;div&gt;</c> is a stack overflow, and a stack overflow
    /// cannot be caught. Content below the limit is kept as text.
    /// </remarks>
    public int MaxDepth { get; init; } = 256;

    /// <summary>
    /// The deepest the markup may nest before the parser sees it. Defaults to 512 open elements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate, earlier bound than <see cref="MaxDepth"/>, for a different cost: the HTML
    /// parser's tree builder scans its stack of open elements for each block it opens, so its time
    /// grows with the square of the nesting — 200 KB of nested <c>&lt;div&gt;</c> took seven
    /// seconds. Tags nested deeper than this are dropped before parsing and their text is kept,
    /// with a <see cref="DiagnosticSeverity.Downgraded"/> diagnostic. It is above
    /// <see cref="MaxDepth"/> so that it never changes a document that walk would have kept.
    /// </para>
    /// </remarks>
    public int MaxElementDepth { get; init; } = 512;

    /// <summary>
    /// The most attributes one element may carry. Defaults to 256.
    /// </summary>
    /// <remarks>
    /// The parser checks each attribute against all the earlier ones on its element, so one
    /// element with 40,000 attributes took twelve seconds. Attributes past the limit are ignored,
    /// with a <see cref="DiagnosticSeverity.Downgraded"/> diagnostic.
    /// </remarks>
    public int MaxAttributesPerElement { get; init; } = 256;

    /// <summary>
    /// The deepest a <c>style</c> attribute may nest brackets. Defaults to 32.
    /// </summary>
    /// <remarks>
    /// The CSS parser reads <c>calc(</c> and its relatives by recursion, one level per bracket, and
    /// it does so while the document is being parsed, before anything here can look at the result.
    /// A style attribute nested deeper than this is removed before parsing, with a
    /// <see cref="DiagnosticSeverity.Downgraded"/> diagnostic. Real stylesheets nest two or three
    /// levels. Brackets inside comments do not count; from the first string, escape or
    /// <c>url(</c> in a value on, every opening bracket counts and no closing one does, because
    /// where the parser ends those is not modelled here.
    /// </remarks>
    public int MaxStyleNesting { get; init; } = 32;

    /// <summary>
    /// The most grid slots the tables of one import may cover between them. Defaults to 100,000.
    /// </summary>
    /// <remarks>
    /// A cell's <c>rowspan</c> and <c>colspan</c> are each clamped to 1,000, but their product
    /// is still a million slots for a 31-byte cell, and a few kilobytes of such cells cost seconds
    /// and gigabytes to lay out. A table is charged the rectangle it spans — its rows by its
    /// widest laid-out row, holes included, because that is the grid validating it walks; one
    /// that would take the import over budget becomes plain paragraphs, with a
    /// <see cref="DiagnosticSeverity.Downgraded"/> diagnostic. 100,000 is a 1,000-row,
    /// 100-column paste.
    /// </remarks>
    public int MaxTableSlots { get; init; } = 100_000;

    /// <summary>
    /// Whether a run of spaces keeps its length (as one space followed by non-breaking spaces) and
    /// a tab is dropped, instead of both collapsing to one space as HTML says.
    /// </summary>
    /// <remarks>
    /// Internal, for the RTF importer: in RTF every space is content, and the reader writes them
    /// into HTML as plain spaces. A tab is dropped by decision (see <c>RtfImporter</c>'s remarks).
    /// </remarks>
    internal bool KeepSpaceRuns { get; init; }

    /// <summary>
    /// A CSS selector for elements removed before sanitizing, with their content, or null.
    /// </summary>
    /// <remarks>
    /// Internal, for the RTF importer, whose reader writes a list item's marker as text inside the
    /// item; the list itself carries the marker here.
    /// </remarks>
    internal string? RemoveBeforeSanitizing { get; init; }

    /// <summary>
    /// A prefix for style properties removed without a diagnostic, or null to report every one.
    /// </summary>
    /// <remarks>
    /// Internal, for the RTF importer, whose reader writes private <c>--officeimo-rtf-*</c>
    /// properties for its own round trip. They are bookkeeping, not formatting the user had, so
    /// reporting them as lost is noise; they are still removed.
    /// </remarks>
    internal string? QuietStylePrefix { get; init; }

    /// <summary>
    /// Whether an empty paragraph whose only drawing is a bottom border is read as a rule.
    /// </summary>
    /// <remarks>
    /// Internal, for the RTF importer: RTF has no rule, and a paragraph with a bottom border is the
    /// form Word and this library's exporter write one in, which the reader passes on as exactly
    /// that paragraph.
    /// </remarks>
    internal bool BorderedEmptyParagraphIsRule { get; init; }

    /// <summary>
    /// The address relative URLs are resolved against, or null to drop relative URLs.
    /// </summary>
    /// <remarks>
    /// Null is the safe default: a relative link in a pasted fragment has no meaning in the
    /// document it is being pasted into, and guessing one is how a link ends up pointing somewhere
    /// nobody intended.
    /// </remarks>
    public Uri? BaseUri { get; init; }

}
