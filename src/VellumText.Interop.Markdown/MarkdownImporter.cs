using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using VellumText.Interop.Html;

using MarkdigMarkdown = Markdig.Markdown;

namespace VellumText.Interop.Markdown;

/// <summary>
/// Reads CommonMark with GitHub-flavoured pipe tables and strikethrough.
/// </summary>
/// <remarks>
/// <para>
/// Markdown is parsed by Markdig, rendered to HTML, and then imported by
/// <see cref="HtmlImporter"/>, rather than being mapped onto the document model directly. This is
/// the same route <c>RtfImporter</c> takes, for the same reason: it means there is exactly one
/// place that decides what an import is allowed to contain. A second importer with its own idea of
/// which URLs are safe is a second importer to get that wrong in — and this library has already
/// paid for that lesson twice, both times in the HTML reader's URL handling.
/// </para>
/// <para>
/// The consequence worth knowing is that the security guards are the HTML ones, unmodified:
/// <c>[x](javascript:alert(1))</c> loses its link because <c>javascript</c> is not in the scheme
/// allow-list, and <c>[x](//host/p)</c> and <c>[x](\\host\share)</c> lose theirs because a URL with
/// no scheme is a relative address and relative addresses are dropped unless a
/// <see cref="MarkdownImportOptions.BaseUri"/> was supplied. The text of such a link survives; only
/// the destination goes.
/// </para>
/// <para>
/// Raw HTML is removed before rendering unless
/// <see cref="MarkdownImportOptions.AllowRawHtml"/> is set — CommonMark says to pass it through, and
/// passing it through would make every Markdown document a carrier for every HTML hazard. See that
/// property for what the two settings do.
/// </para>
/// <para>
/// There is no <c>IsMarkdown</c> counterpart to <c>RtfImporter.IsRtf</c>, and there cannot be: every
/// string is valid Markdown. A caller deciding between flavours has to decide on the media type or
/// the file extension, not on the bytes.
/// </para>
/// </remarks>
public static class MarkdownImporter
{
    /// <summary>
    /// The parser configuration, which must stay equal to the one
    /// <see cref="MarkdownExporter"/> writes for or the two halves of the round trip disagree.
    /// </summary>
    /// <remarks>
    /// Pipe tables because tables are a first-class part of this document model and a Markdown
    /// reader that cannot see one would make export the only direction that worked. Strikethrough
    /// because <see cref="TextStyle.Strikethrough"/> exists in the model and <c>~~</c> is the only
    /// notation anyone writes it in. Nothing else: every further extension is a dialect, and a
    /// dialect read here is a dialect this library then has to keep reading.
    /// </remarks>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    /// <summary>Reads a Markdown document.</summary>
    /// <param name="markdown">The Markdown source.</param>
    /// <param name="options">How much to trust it, or null for the paranoid defaults.</param>
    /// <returns>The document, and everything that could not be brought across.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is null.</exception>
    /// <remarks>
    /// Never throws for bad input. Markdown has no invalid form — every string parses — but the
    /// parser can still run out of stack on pathological nesting, and an import is not a good place
    /// to learn that.
    /// </remarks>
    public static ImportResult Import(string markdown, MarkdownImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        options ??= MarkdownImportOptions.Default;

        var log = new DiagnosticLog();
        string html;

        try
        {
            var parsed = MarkdigMarkdown.Parse(markdown, Pipeline);

            Prepare(parsed, options, log);

            html = MarkdigMarkdown.ToHtml(parsed, Pipeline);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ImportResult(
                DocumentNode.Empty,
                [
                    new ImportDiagnostic(
                        DiagnosticSeverity.Malformed,
                        $"The Markdown could not be read: {ex.Message}",
                        ex.GetType().Name),
                ]);
        }

        var result = HtmlImporter.Import(html, options.ToHtmlOptions());

        return log.IsEmpty
            ? result
            : new ImportResult(result.Doc, [.. log.Items, .. result.Diagnostics]);
    }

    /// <summary>
    /// Edits the parsed tree into the shape the HTML stage should see.
    /// </summary>
    /// <remarks>
    /// Done on the syntax tree rather than on the rendered HTML because a string edit of generated
    /// markup is a parser written by accident, and because the tree still knows which text came
    /// from the author and which from a tag.
    /// </remarks>
    private static void Prepare(
        MarkdownDocument document,
        MarkdownImportOptions options,
        DiagnosticLog log)
    {
        if (!options.AllowRawHtml)
        {
            RemoveRawHtml(document, log);
        }

        foreach (var code in document.Descendants<FencedCodeBlock>())
        {
            RemoveLanguageClass(code, log);
        }

        foreach (var table in document.Descendants<Table>())
        {
            ReportColumnAlignment(table, log);
        }
    }

    /// <summary>Removes every raw HTML block and inline tag from the tree.</summary>
    /// <remarks>
    /// <para>
    /// The cases are not equally bad and are not reported as if they were. Removing an inline
    /// formatting tag leaves the text it wrapped, because in Markdig a tag and the text after it are
    /// separate nodes — <c>a &lt;b&gt;word&lt;/b&gt; b</c> keeps "word". Removing an HTML block takes
    /// its entire contents, because the block <em>is</em> the text. So the first is a downgrade and
    /// the second is a loss.
    /// </para>
    /// <para>
    /// Keeping the words is right for a tag that only styles them and catastrophic for one whose
    /// body is program text: applied to <c>a &lt;script&gt;steal()&lt;/script&gt; b</c> it hands the
    /// script to the document as prose. So an inline tag from
    /// <see cref="SanitizingReader.NonContentElements"/> takes everything through its matching
    /// closing tag, which is the same distinction the HTML reader makes and the same list it makes
    /// it from. An unclosed one takes the rest of its paragraph, and a <c>&lt;script/&gt;</c> counts
    /// as unclosed, because that is what a browser does with it too.
    /// </para>
    /// </remarks>
    private static void RemoveRawHtml(MarkdownDocument document, DiagnosticLog log)
    {
        // Materialised before mutating: Descendants is lazy, and editing a tree mid-walk is how a
        // removal loop silently skips every second node.
        foreach (var block in document.Descendants<HtmlBlock>().ToList())
        {
            block.Parent?.Remove(block);

            log.ReportOnce(
                DiagnosticSeverity.Dropped,
                "Removed a block of raw HTML embedded in the Markdown, along with its contents.",
                "html-block");
        }

        foreach (var leaf in document.Descendants<LeafBlock>().ToList())
        {
            if (leaf.Inline is { } inlines)
            {
                RemoveRawHtml(inlines, log);
            }
        }
    }

    /// <summary>Removes the raw HTML among one container's inlines, and inside its children.</summary>
    /// <remarks>
    /// Walked by sibling rather than over a materialised <c>Descendants</c> list because removing a
    /// non-content element removes the nodes that follow it, and a pre-built list would then hold
    /// nodes that are no longer anywhere — including, in
    /// <c>&lt;script&gt;x **&lt;/script&gt;** y</c>, a closing tag that has already been consumed
    /// and would otherwise be processed a second time.
    /// </remarks>
    private static void RemoveRawHtml(ContainerInline container, DiagnosticLog log)
    {
        var node = container.FirstChild;

        while (node is not null)
        {
            var next = node.NextSibling;

            switch (node)
            {
                case HtmlInline tag
                    when IsOpeningTag(tag.Tag)
                        && SanitizingReader.NonContentElements.Contains(TagName(tag.Tag)):
                    next = RemoveThroughClosingTag(tag, log);
                    break;

                case HtmlInline tag:
                    tag.Remove();

                    log.ReportOnce(
                        DiagnosticSeverity.Downgraded,
                        "Removed a raw HTML tag embedded in the Markdown; the text around it was kept.",
                        TagName(tag.Tag));
                    break;

                case ContainerInline child:
                    RemoveRawHtml(child, log);
                    break;
            }

            node = next;
        }
    }

    /// <summary>
    /// Removes a non-content element's opening tag, its contents, and its closing tag.
    /// </summary>
    /// <returns>The first sibling that survives, so the caller's walk can resume there.</returns>
    /// <remarks>
    /// Nesting is counted rather than assumed absent: an inner <c>&lt;script&gt;</c> inside the text
    /// of an outer one would otherwise let the inner closing tag end the removal early and release
    /// the rest of the body as prose.
    /// </remarks>
    private static Inline? RemoveThroughClosingTag(HtmlInline open, DiagnosticLog log)
    {
        var name = TagName(open.Tag);
        var node = (Inline?)open;
        var depth = 0;

        while (node is not null)
        {
            var next = node.NextSibling;

            if (node is HtmlInline tag && TagName(tag.Tag) == name)
            {
                depth += IsOpeningTag(tag.Tag) ? 1 : -1;
            }

            node.Remove();
            node = next;

            if (depth == 0)
            {
                break;
            }
        }

        log.ReportOnce(
            DiagnosticSeverity.Dropped,
            "Removed a raw HTML element whose contents a browser would never show as text, "
                + "along with those contents.",
            name);

        return node;
    }

    /// <summary>Whether a raw tag opens an element rather than closing one.</summary>
    private static bool IsOpeningTag(string? tag) =>
        tag is { Length: > 1 } && tag[0] == '<' && tag[1] != '/';

    /// <summary>
    /// Strips the <c>language-…</c> class Markdig puts on a fenced block's <c>&lt;code&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Left in place the sanitizer removes it and reports "an attribute that is not safe to keep",
    /// which is true of the attribute and misleading about the document: nothing unsafe happened,
    /// the model simply has no place to record a language. Removing it here means the one
    /// diagnostic the caller sees is the one that describes what was actually lost.
    /// </remarks>
    private static void RemoveLanguageClass(FencedCodeBlock code, DiagnosticLog log)
    {
        if (code.Info is not { Length: > 0 } language)
        {
            return;
        }

        code.GetAttributes().Classes?.RemoveAll(
            c => c.StartsWith("language-", StringComparison.Ordinal));

        log.ReportOnce(
            DiagnosticSeverity.Downgraded,
            "A code block named a language, which the document model does not record.",
            language);
    }

    /// <summary>Reports a pipe table's column alignment, which does not survive the import.</summary>
    /// <remarks>
    /// <see cref="MarkdownExporter"/> <em>writes</em> column alignment, so this is deliberately
    /// asymmetric: the marker is worth emitting because every other Markdown reader honours it, and
    /// it is not read back because the HTML stage does not carry a cell's <c>text-align</c> onto the
    /// paragraphs inside it. Making it survive would mean changing the HTML importer's table
    /// handling, which is a change to a different package's behaviour and to its tests. Until then
    /// the honest thing is to say so rather than to pretend the round trip is exact.
    /// </remarks>
    private static void ReportColumnAlignment(Table table, DiagnosticLog log)
    {
        foreach (var column in table.ColumnDefinitions)
        {
            if (column.Alignment is not { } alignment)
            {
                continue;
            }

            log.ReportOnce(
                DiagnosticSeverity.Downgraded,
                "A table column's alignment was not kept.",
                alignment.ToString().ToLowerInvariant());
        }
    }

    /// <summary>The element name inside a raw tag, for use as a diagnostic's context.</summary>
    /// <remarks>
    /// The whole tag would be useless as a deduplication key — every <c>&lt;span style="…"&gt;</c>
    /// in a document is a different string — and would put attacker-chosen text into a message a
    /// host may display.
    /// </remarks>
    private static string TagName(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return "html";
        }

        var span = tag.AsSpan().TrimStart('<').TrimStart('/');
        var end = 0;

        while (end < span.Length && (char.IsAsciiLetterOrDigit(span[end]) || span[end] == '-'))
        {
            end++;
        }

        return end == 0 ? "html" : span[..end].ToString().ToLowerInvariant();
    }
}
