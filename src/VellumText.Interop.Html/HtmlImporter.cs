using System.Globalization;
using System.Text;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens.Struct;

namespace VellumText.Interop.Html;

/// <summary>
/// Reads HTML into a VellumText document.
/// </summary>
/// <remarks>
/// <para>
/// The input is assumed hostile. It is parsed by AngleSharp, sanitized by a configured
/// <c>HtmlSanitizer</c>, and only then read — so what this class walks is already free of script,
/// event handlers, embedded objects and URLs pointing anywhere dangerous.
/// </para>
/// <para>
/// It does not throw. A document that cannot be read at all comes back empty with a
/// <see cref="DiagnosticSeverity.Malformed"/> diagnostic, because the caller is usually a paste and
/// a paste that throws is worse than a paste that pastes nothing.
/// </para>
/// </remarks>
public static class HtmlImporter
{
    /// <summary>Reads HTML into a document.</summary>
    /// <param name="html">The markup. A fragment is fine; it does not need to be a whole document.</param>
    /// <param name="options">How much to trust it, or null for the paranoid defaults.</param>
    /// <returns>The document, and everything that could not be brought across.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    public static ImportResult Import(string html, HtmlImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);

        options ??= HtmlImportOptions.Default;

        return Run(html, options, fragment: null);
    }

    // The parser and the CSS parser recurse per level of nesting in the input, so they get a
    // stack the input cannot exhaust; see LargeStack. A nested call from the Markdown or RTF
    // importer is already on one and runs inline.
    private static ImportResult Run(string html, HtmlImportOptions options, string? fragment) =>
        LargeStack.Run(() => ImportOnLargeStack(html, options, fragment));

    private static ImportResult ImportOnLargeStack(string html, HtmlImportOptions options, string? fragment)
    {
        var diagnostics = new List<ImportDiagnostic>();

        try
        {
            var doc = Build(html, options, fragment, diagnostics);

            // Every construction site below guards its own shape, but a document that breaks a
            // schema rule would be refused when the paste is applied, and a silent no-op is a
            // worse outcome than an empty one that says why.
            if (!DocumentSchema.IsValid(doc))
            {
                diagnostics.Add(new ImportDiagnostic(
                    DiagnosticSeverity.Malformed,
                    "The imported document did not satisfy the model's rules and was discarded: "
                    + string.Join("; ", DocumentSchema.Validate(doc).Take(3)),
                    "schema"));

                return new ImportResult(DocumentNode.Empty, diagnostics);
            }

            return new ImportResult(doc, diagnostics);
        }
        catch (Exception ex)
        {
            // The parser and the sanitizer are both meant to cope with anything. If one of them
            // does not, that is a bug worth knowing about, but it is not worth taking the host
            // application down over a paste.
            diagnostics.Add(new ImportDiagnostic(
                DiagnosticSeverity.Malformed,
                $"The HTML could not be read: {ex.Message}",
                ex.GetType().Name));

            return new ImportResult(DocumentNode.Empty, diagnostics);
        }
    }

    /// <summary>
    /// Reads a clipboard payload, unwrapping the <c>CF_HTML</c> envelope if it has one.
    /// </summary>
    /// <param name="payload">What came off the clipboard.</param>
    /// <param name="options">How much to trust it, or null for the paranoid defaults.</param>
    /// <returns>The document, and everything that could not be brought across.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// The envelope's <c>SourceURL</c> becomes the base address for relative links, but only when
    /// the caller has not named one: an explicit choice outranks a copied document's opinion about
    /// where it came from.
    /// </para>
    /// <para>
    /// A fragment that opens inside a table, as Excel's does, is read in its document instead,
    /// keeping only what lies between the fragment markers and the elements that enclose them.
    /// On its own it is rows with no table, and a pasted range would become one paragraph. Markers
    /// the parsed document does not have as comments, because a script or a style took them for
    /// its text, cannot be clipped at, and the fragment is then read on its own after all.
    /// </para>
    /// </remarks>
    public static ImportResult ImportClipboard(string payload, HtmlImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        options ??= HtmlImportOptions.Default;

        var fragment = ClipboardHtml.ExtractFragment(payload, out var sourceUri);

        if (sourceUri is not null && options.BaseUri is null)
        {
            options = options with { BaseUri = sourceUri };
        }

        return ClipboardHtml.TryGetTableContext(payload, fragment, out var document)
            ? Run(document, options, fragment)
            : Run(fragment, options, fragment: null);
    }

    /// <summary>Reads <paramref name="html"/> into a document.</summary>
    /// <param name="html">The markup.</param>
    /// <param name="options">How much to trust it.</param>
    /// <param name="fragment">
    /// Null to read <paramref name="html"/> as it is. Otherwise <paramref name="html"/> is the
    /// clipboard document, to be clipped to its fragment markers, and this is the fragment the
    /// markers were found around in its text.
    /// </param>
    /// <param name="diagnostics">Where to report what could not be brought across.</param>
    private static DocumentNode Build(
        string html,
        HtmlImportOptions options,
        string? fragment,
        List<ImportDiagnostic> diagnostics)
    {
        // CSS support has to be configured on the context, not added later: without it an element
        // has no style declaration, and the sanitizer would then find nothing to sanitize in a
        // style attribute rather than finding it clean.
        var context = BrowsingContext.New(Configuration.Default.WithCss());
        var document = Parse(context, html, options, diagnostics);

        // Before sanitizing, so that nothing outside the fragment is reported as removed.
        if (fragment is not null
            && !ClipToFragment(document, html.Contains(ClipboardHtml.EndFragmentComment, StringComparison.OrdinalIgnoreCase)))
        {
            // The text has the markers but the parsed page does not: a script or a style read them
            // as its own text. The page cannot be clipped, and unclipped it is everything the
            // fragment was cut from (rubber-duck review 4.1). Read the fragment alone instead, as
            // one that does not open inside a table is. What parsing the page reported was about
            // the page, so it goes too.
            diagnostics.Clear();
            document = Parse(context, fragment, options, diagnostics);
        }

        // Also before sanitizing, which removes the styles that say a paragraph is a list item.
        WordLists.Rewrite(document);

        new SanitizingReader(options, diagnostics).Sanitize(document);

        var walker = new Walker(options, diagnostics);
        var blocks = walker.Run(document.Body ?? (INode)document);

        // A document must have somewhere to put the caret.
        return blocks.Count == 0
            ? DocumentNode.Empty
            : new DocumentNode(blocks);
    }

    private static IDocument Parse(
        IBrowsingContext context,
        string html,
        HtmlImportOptions options,
        List<ImportDiagnostic> diagnostics)
    {
        var guarded = HtmlInputGuard.Bound(html, options);

        ReportGuard(guarded, options, diagnostics);

        var maxAttributes = options.MaxAttributesPerElement;
        var parser = new HtmlParser(
            new HtmlParserOptions
            {
                // The guard has already reported these; capping here as well is what keeps the
                // parser's own duplicate check linear.
                ShouldEmitAttribute = (ref StructHtmlToken token, ReadOnlyMemory<char> name) =>
                    token.Attributes.Count < maxAttributes,
            },
            context);

        return parser.ParseDocument(guarded.Html);
    }

    /// <summary>
    /// Removes everything before the <c>StartFragment</c> marker and after the <c>EndFragment</c>
    /// one, keeping the elements that enclose them.
    /// </summary>
    /// <param name="document">The parsed clipboard document.</param>
    /// <param name="endExpected">Whether the text has an <c>EndFragment</c> marker.</param>
    /// <returns>
    /// Whether the markers were found in the document. If not, it is left as it was.
    /// </returns>
    /// <remarks>
    /// The markers are found where the parser put them rather than where they sat in the text, so
    /// a marker between <c>&lt;table&gt;</c> and its first row clips inside the table and the
    /// table itself survives.
    /// </remarks>
    private static bool ClipToFragment(IDocument document, bool endExpected)
    {
        IComment? start = null;
        IComment? end = null;

        foreach (var comment in document.Descendants<IComment>())
        {
            if (start is null)
            {
                if (IsMarker(comment, "StartFragment"))
                {
                    start = comment;
                }
            }
            else if (IsMarker(comment, "EndFragment"))
            {
                end = comment;
                break;
            }
        }

        if (start is null || (end is null && endExpected))
        {
            return false;
        }

        RemoveOutside(start, before: true);

        if (end is not null)
        {
            RemoveOutside(end, before: false);
        }

        // The page's own html and body stay as containers, but their attributes describe the page
        // the fragment was cut from, not anything that was copied.
        foreach (var page in new[] { document.DocumentElement, document.Body })
        {
            foreach (var name in page?.Attributes.Select(attribute => attribute.Name).ToArray() ?? [])
            {
                page!.RemoveAttribute(name);
            }
        }

        return true;
    }

    private static bool IsMarker(IComment comment, string name) =>
        string.Equals(comment.Data.Trim(), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes every node on one side of <paramref name="marker"/>, at every level up to the root.</summary>
    private static void RemoveOutside(INode marker, bool before)
    {
        for (var node = marker; node.Parent is { } parent; node = parent)
        {
            var sibling = before ? node.PreviousSibling : node.NextSibling;

            while (sibling is not null)
            {
                var next = before ? sibling.PreviousSibling : sibling.NextSibling;

                parent.RemoveChild(sibling);
                sibling = next;
            }
        }
    }

    private static void ReportGuard(
        HtmlInputGuard.Outcome guarded,
        HtmlImportOptions options,
        List<ImportDiagnostic> diagnostics)
    {
        if (guarded.DroppedDeepTags)
        {
            diagnostics.Add(new ImportDiagnostic(
                DiagnosticSeverity.Downgraded,
                $"The markup nested deeper than {options.MaxElementDepth} elements; the tags below "
                + "that were dropped and their text was kept.",
                "element-depth"));
        }

        if (guarded.DroppedAttributes)
        {
            diagnostics.Add(new ImportDiagnostic(
                DiagnosticSeverity.Downgraded,
                $"An element carried more than {options.MaxAttributesPerElement} attributes; the rest "
                + "were ignored.",
                "attributes"));
        }

        if (guarded.DroppedStyles)
        {
            diagnostics.Add(new ImportDiagnostic(
                DiagnosticSeverity.Downgraded,
                $"A style attribute nested brackets deeper than {options.MaxStyleNesting} levels and "
                + "was removed.",
                "style-nesting"));
        }
    }

    private sealed class Walker(HtmlImportOptions options, List<ImportDiagnostic> diagnostics)
    {
        private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The grid slots the tables still to come may cover; see <see cref="HtmlImportOptions.MaxTableSlots"/>.</summary>
        private long _tableSlotsLeft = options.MaxTableSlots;

        internal List<BlockNode> Run(INode root)
        {
            var sink = new BlockSink(Downgrade);

            WalkChildren(root, MarkSet.Empty, BlockStyle.Default, sink, 0);
            sink.Flush(BlockStyle.Default);

            return [.. sink.Blocks];
        }

        private void WalkChildren(INode node, MarkSet mark, BlockStyle block, BlockSink sink, int depth)
        {
            if (depth > options.MaxDepth)
            {
                // Keep the text, lose the nesting. The alternative is a stack overflow, which
                // cannot be caught and takes the host process with it.
                Report(
                    DiagnosticSeverity.Downgraded,
                    $"Stopped descending below {options.MaxDepth} levels of nesting; the text was "
                    + "kept but its formatting was not.",
                    "nesting");

                // Deliberately not INode.TextContent, which AngleSharp gathers recursively: on the
                // subtree that tripped this guard that call is itself the stack overflow the guard
                // exists to prevent.
                sink.Add(FlattenText(node), mark, block.Preformatted);
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                switch (child)
                {
                    case IText text:
                        sink.Add(text.Data, mark, block.Preformatted);
                        break;

                    case IElement element:
                        WalkElement(element, mark, block, sink, depth + 1);
                        break;

                    default:
                        // Comments, processing instructions and doctypes carry nothing a document
                        // can hold.
                        break;
                }
            }
        }

        /// <summary>Collects the text under a node without recursing.</summary>
        /// <param name="root">The node whose descendants' text is wanted.</param>
        /// <returns>The text, in document order.</returns>
        /// <remarks>
        /// This is only reached for a subtree too deep to walk, so it must not use the stack in
        /// proportion to that depth. An explicit stack is the whole point of it.
        /// </remarks>
        private static string FlattenText(INode root)
        {
            var text = new StringBuilder();
            var pending = new Stack<INode>();

            pending.Push(root);

            while (pending.Count > 0)
            {
                var node = pending.Pop();

                if (node is IText leaf)
                {
                    text.Append(leaf.Data);
                    continue;
                }

                // Pushed in reverse so that popping them yields document order.
                for (var i = node.ChildNodes.Length - 1; i >= 0; i--)
                {
                    pending.Push(node.ChildNodes[i]);
                }
            }

            return text.ToString();
        }

        private void WalkElement(IElement element, MarkSet mark, BlockStyle block, BlockSink sink, int depth)
        {
            switch (element.LocalName)
            {
                case "br":
                    // A newline inside a paragraph is what <pre> already produces and what the
                    // model accepts, so a line break can stay a line break instead of being
                    // promoted to a paragraph break. That matters: mail signatures are mostly
                    // <br>, and splitting them into paragraphs changes how they read.
                    sink.AddLineBreak(mark);
                    return;

                case "hr":
                    sink.AddBlock(block, RuleNode.Instance);
                    return;

                case "img":
                    AddImage(element, mark, sink);
                    return;

                case "ul":
                case "ol":
                    AddList(element, mark, block, sink, depth);
                    return;

                case "table":
                    AddTable(element, mark, block, sink, depth);
                    return;

                case "li":
                    // A list item outside a list. The parser produces these from malformed input;
                    // treating it as a paragraph keeps the text.
                    break;
            }

            var style = CssStyles.StyleOf(element);
            var inner = ApplyElementMark(element, mark);

            if (style is not null)
            {
                inner = CssStyles.ApplyInline(inner, style);
            }

            if (!IsBlockLevel(element.LocalName))
            {
                WalkChildren(element, inner, block, sink, depth);
                return;
            }

            var innerBlock = BlockFor(element.LocalName, block);

            if (style is not null)
            {
                innerBlock = CssStyles.ApplyBlock(innerBlock, style);
            }

            // Whatever was being written belongs to the paragraph that was already open, not to
            // the one this element is about to start.
            sink.Flush(block);

            var before = sink.Blocks.Count;

            WalkChildren(element, inner, innerBlock, sink, depth);

            // An empty <p> is how every editor on the platform spells a blank line, so it has to
            // survive. An empty <div> is layout — pages are full of wrappers, spacers and
            // clearfixes — so it must not. Requiring that the element produced nothing at all is
            // what keeps <div><p>a</p></div> from also emitting a blank after the paragraph.
            var blank = IsParagraphElement(element.LocalName)
                && sink.Blocks.Count == before
                && !sink.HasPending;

            sink.Flush(innerBlock, blank);
        }

        /// <summary>Whether an element exists to hold a paragraph of text.</summary>
        /// <param name="name">The lowercased tag name.</param>
        /// <returns>True for a paragraph or a heading.</returns>
        private static bool IsParagraphElement(string name) => name switch
        {
            "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => true,
            _ => false,
        };

        private MarkSet ApplyElementMark(IElement element, MarkSet mark) => element.LocalName switch
        {
            "b" or "strong" => mark with { Style = mark.Style | TextStyle.Bold },
            "i" or "em" or "cite" or "var" or "dfn" or "address"
                => mark with { Style = mark.Style | TextStyle.Italic },
            "u" or "ins" => mark with { Style = mark.Style | TextStyle.Underline },
            "s" or "strike" or "del" => mark with { Style = mark.Style | TextStyle.Strikethrough },
            "code" or "tt" or "kbd" or "samp" => mark with { Style = mark.Style | TextStyle.Code },

            // Sub and Super are mutually exclusive and the model enforces it, so each must clear
            // the other rather than simply being added.
            "sup" => mark with { Style = (mark.Style & ~TextStyle.Sub) | TextStyle.Super },
            "sub" => mark with { Style = (mark.Style & ~TextStyle.Super) | TextStyle.Sub },

            "mark" => mark with { Highlight = mark.Highlight ?? new Rgba(255, 255, 0, 255) },
            "a" => ApplyLink(element, mark),
            "font" => ApplyFontElement(element, mark),
            _ => mark,
        };

        private static MarkSet ApplyLink(IElement element, MarkSet mark)
        {
            // The sanitizer has already removed an href it did not approve of, so an anchor
            // without one is an anchor whose target was rejected, or a named anchor. Either way
            // there is nothing to link to.
            var href = element.GetAttribute("href");

            if (string.IsNullOrWhiteSpace(href))
            {
                return mark;
            }

            var title = element.GetAttribute("title");

            return mark with
            {
                Link = new LinkMark(href, string.IsNullOrWhiteSpace(title) ? null : title),
            };
        }

        private static MarkSet ApplyFontElement(IElement element, MarkSet mark)
        {
            if (CssStyles.TryColor(element.GetAttribute("color"), out var color))
            {
                mark = mark with { Foreground = color };
            }

            if (CssStyles.TryFontFamily(element.GetAttribute("face"), out var family))
            {
                mark = mark with { FontFamily = family };
            }

            // The size attribute is a step on a scale from 1 to 7, not a measurement. These are the
            // pixel sizes browsers have settled on for it.
            var size = element.GetAttribute("size");

            if (int.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var step)
                && step is >= 1 and <= 7)
            {
                double[] sizes = [10, 13, 16, 18, 24, 32, 48];
                mark = mark with { FontSize = sizes[step - 1] };
            }

            return mark;
        }

        private static bool IsBlockLevel(string name) => name switch
        {
            "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "blockquote" or "pre" or "li" or "dt" or "dd" or "dl"
            or "section" or "article" or "main" or "header" or "footer" or "aside" or "nav"
            or "figure" or "figcaption" or "center" or "address" or "caption" => true,
            _ => false,
        };

        private static BlockStyle BlockFor(string name, BlockStyle parent) => name switch
        {
            "h1" => parent with { Kind = ParagraphKind.Heading1 },
            "h2" => parent with { Kind = ParagraphKind.Heading2 },
            "h3" => parent with { Kind = ParagraphKind.Heading3 },
            "h4" => parent with { Kind = ParagraphKind.Heading4 },
            "h5" => parent with { Kind = ParagraphKind.Heading5 },
            "h6" => parent with { Kind = ParagraphKind.Heading6 },
            "blockquote" => parent.Kind == ParagraphKind.Quote

                // A quote inside a quote is a deeper quote, not the same one again.
                ? parent with { Indent = Math.Min(parent.Indent + 1, 8) }
                : parent with { Kind = ParagraphKind.Quote },
            "pre" => parent with { Kind = ParagraphKind.Code, Preformatted = true },

            // A description list indents its descriptions but not its terms.
            "dd" => parent with { Indent = Math.Min(parent.Indent + 1, 8) },

            // A paragraph inside a quote is still quoted; a heading inside one is not a heading
            // that stops being quoted either. Everything else simply inherits.
            _ => parent,
        };

        private void AddImage(IElement element, MarkSet mark, BlockSink sink)
        {
            var source = element.GetAttribute("src");

            if (string.IsNullOrWhiteSpace(source))
            {
                // The sanitizer took the source away, and has already said why.
                return;
            }

            var alt = element.GetAttribute("alt");

            sink.AddEmbed(
                new ImageEmbed(
                    source,
                    Dimension(element, "width"),
                    Dimension(element, "height"),
                    string.IsNullOrWhiteSpace(alt) ? null : alt),
                mark);
        }

        /// <summary>The largest width or height, in pixels, an image is allowed to declare.</summary>
        /// <remarks>
        /// As with font size, "finite and positive" is not a bound — <c>width="1e300"</c> satisfies
        /// it. An image that claims to be astronomically wide would be laid out at that width. The
        /// ceiling is the usual maximum texture dimension; beyond it the declaration is ignored and
        /// the image falls back to its intrinsic size.
        /// </remarks>
        private const double MaxImageDimension = 32768.0;

        private static double? Dimension(IElement element, string name)
        {
            var attribute = element.GetAttribute(name);

            if (double.TryParse(attribute, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value)
                && value > 0
                && value <= MaxImageDimension)
            {
                return value;
            }

            var style = CssStyles.StyleOf(element);

            if (style is not null
                && CssStyles.TryLength(style.GetPropertyValue(name), out var length)
                && length > 0
                && length <= MaxImageDimension)
            {
                return length;
            }

            return null;
        }

        /// <summary>The largest number an imported ordered list may start from.</summary>
        private const int MaxListStart = 999_999_999;

        private void AddList(IElement element, MarkSet mark, BlockStyle block, BlockSink sink, int depth)
        {
            var items = new List<ListItemNode>();

            foreach (var child in element.Children)
            {
                if (!string.Equals(child.LocalName, "li", StringComparison.Ordinal))
                {
                    continue;
                }

                var inner = new BlockSink(Downgrade);
                var itemStyle = block with { Kind = ParagraphKind.Body };

                WalkChildren(child, mark, itemStyle, inner, depth);
                inner.Flush(itemStyle);

                // An item with nothing in it is still an item, and the model needs a paragraph in
                // it to have anywhere to put the caret.
                items.Add(new ListItemNode(
                    inner.Blocks.Count > 0 ? inner.Blocks : [ParagraphNode.Empty]));
            }

            if (items.Count == 0)
            {
                // A list with no items. Whatever was inside it, if anything, was not a list item;
                // walking it as ordinary content keeps the text.
                WalkChildren(element, mark, block, sink, depth);
                return;
            }

            var ordered = string.Equals(element.LocalName, "ol", StringComparison.Ordinal);
            var start = 1;

            if (ordered
                && int.TryParse(
                    element.GetAttribute("start"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var declared))
            {
                // Clamped, not passed through. A negative start is valid HTML and the model
                // cannot hold one, so handing it over throws - and the importer's outer catch
                // turns any throw into an empty document. One attribute nobody looks at would
                // otherwise discard the whole paste.
                if (declared < 0)
                {
                    Report(
                        DiagnosticSeverity.Downgraded,
                        "A list numbered from a negative value starts from zero instead.",
                        declared.ToString(CultureInfo.InvariantCulture));

                    start = 0;
                }
                else if (declared > MaxListStart)
                {
                    // Nine digits is what CommonMark allows an ordered-list marker, and what every
                    // exporter here can write back. Above it the number is not a document's choice
                    // but a probe for overflow in whatever counts on from it.
                    Report(
                        DiagnosticSeverity.Downgraded,
                        $"A list numbered from beyond {MaxListStart} starts from {MaxListStart} instead.",
                        declared.ToString(CultureInfo.InvariantCulture));

                    start = MaxListStart;
                }
                else
                {
                    start = declared;
                }
            }

            sink.AddBlock(
                block,
                new ListNode(items, ordered ? ListKind.Ordered : ListKind.Unordered, start));
        }

        private void AddTable(IElement element, MarkSet mark, BlockStyle block, BlockSink sink, int depth)
        {
            var rows = new List<TableRowNode>();
            var filled = 0;

            foreach (var rowElement in RowsOf(element))
            {
                var cells = new List<TableCellNode>();

                foreach (var cellElement in rowElement.Children)
                {
                    var isHeader = string.Equals(cellElement.LocalName, "th", StringComparison.Ordinal);

                    if (!isHeader && !string.Equals(cellElement.LocalName, "td", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    cells.Add(BuildCell(cellElement, mark, block, depth, isHeader));
                }

                rows.Add(new TableRowNode(cells));

                if (cells.Count > 0)
                {
                    filled++;
                }
            }

            if (filled == 0)
            {
                WalkChildren(element, mark, block, sink, depth);
                return;
            }

            // Charged before the table is built or validated, because laying it out is the cost
            // being budgeted. A span is charged only for the rows the table has, which is what
            // layout costs; a span hanging off the bottom fails the schema below anyway.
            var slots = 0L;

            for (var r = 0; r < rows.Count; r++)
            {
                foreach (var cell in rows[r].Cells)
                {
                    slots += (long)Math.Min(cell.RowSpan, rows.Count - r) * cell.ColumnSpan;
                }
            }

            // Validating the table walks the whole grid it spans, holes included, so the bounding
            // rectangle is what is charged: one wide row over a thousand narrow ones occupies
            // little but is a thousand rows of the wide row's width. The width comes from the
            // same placement the schema uses, so the two cannot disagree; it is worked out only
            // once the occupied area is known to fit, because placing costs that area.
            if (slots <= _tableSlotsLeft)
            {
                TableGeometry.Place(new TableNode(rows), out var width);
                slots = (long)rows.Count * width;
            }

            if (slots > _tableSlotsLeft)
            {
                Downgrade(
                    rows,
                    block,
                    sink,
                    $"it spans {slots} grid slots, more than the {options.MaxTableSlots} one import may lay out");
                return;
            }

            _tableSlotsLeft -= slots;

            try
            {
                // An empty row is meaningful exactly when the rows above span into it, which is
                // what merging across a whole rectangle produces and what this library's own
                // exporter therefore writes. It is meaningless, and makes the table ragged, when
                // nothing covers it - which is what a stray <tr> from a word processor is. Both
                // arrive, and only the table itself can tell them apart, so the shape the author
                // wrote is tried first and the empty rows are dropped only if it does not stand.
                if (Rectangular(rows) is { } asWritten)
                {
                    sink.AddBlock(block, asWritten);
                    return;
                }

                if (filled < rows.Count
                    && Rectangular([.. rows.Where(row => row.Cells.Length > 0)]) is { } trimmed)
                {
                    sink.AddBlock(block, trimmed);
                    return;
                }

                Downgrade(rows, block, sink, "its rows do not form a rectangle");
            }
            catch (ArgumentException ex)
            {
                Downgrade(rows, block, sink, ex.Message);
            }
        }

        /// <summary>The table these rows make, or null if they do not tile a grid.</summary>
        /// <remarks>
        /// The constructor only rejects a table that is malformed outright; whether the rows
        /// actually tile a rectangle is a schema rule, and a table that breaks it would be built
        /// happily here and then refused when the paste is applied.
        /// </remarks>
        private static TableNode? Rectangular(List<TableRowNode> rows)
        {
            var table = new TableNode(rows);

            return DocumentSchema.IsValid(table) ? table : null;
        }

        /// <summary>
        /// Turns a table the model will not accept into the text it contained.
        /// </summary>
        /// <remarks>
        /// HTML tables are allowed to be ragged, and the ones that arrive on a clipboard
        /// frequently are. Losing the grid is a real loss; losing the words is a worse one.
        /// </remarks>
        private void Downgrade(List<TableRowNode> rows, BlockStyle block, BlockSink sink, string why)
        {
            Report(
                DiagnosticSeverity.Downgraded,
                $"A table could not be represented and became plain paragraphs: {why}",
                "table");

            foreach (var row in rows)
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var cellBlock in cell.Blocks)
                    {
                        sink.AddBlock(block, cellBlock);
                    }
                }
            }
        }

        private TableCellNode BuildCell(
            IElement cellElement,
            MarkSet mark,
            BlockStyle block,
            int depth,
            bool isHeader)
        {
            var inner = new BlockSink(Downgrade);

            // A cell starts a fresh formatting context: an indent from outside the table would
            // otherwise be applied again inside every cell of it.
            var cellStyle = block with { Kind = ParagraphKind.Body, Indent = 0 };

            WalkChildren(cellElement, mark, cellStyle, inner, depth);
            inner.Flush(cellStyle);

            var style = CssStyles.StyleOf(cellElement);
            Rgba? background = null;

            if (style is not null
                && CssStyles.TryColor(style.GetPropertyValue("background-color"), out var declared)
                && !declared.IsTransparent)
            {
                background = declared;
            }

            return new TableCellNode(
                inner.Blocks.Count > 0 ? inner.Blocks : [ParagraphNode.Empty],
                Span(cellElement, "rowspan"),
                Span(cellElement, "colspan"),
                background,
                isHeader);
        }

        private static int Span(IElement element, string name)
        {
            // A span is at least one, and a span of a thousand is a denial of service rather than
            // a table. Browsers cap these too.
            if (int.TryParse(
                    element.GetAttribute(name),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return Math.Clamp(value, 1, 1000);
            }

            return 1;
        }

        /// <summary>
        /// The rows of one table, without the rows of any table nested inside it.
        /// </summary>
        /// <remarks>
        /// A selector would be shorter and would be wrong: it would reach through a cell into a
        /// nested table and steal its rows, producing a flattened table that matches neither.
        /// </remarks>
        private static IEnumerable<IElement> RowsOf(IElement table)
        {
            foreach (var child in table.Children)
            {
                if (string.Equals(child.LocalName, "tr", StringComparison.Ordinal))
                {
                    yield return child;
                }
                else if (child.LocalName is "thead" or "tbody" or "tfoot")
                {
                    foreach (var row in child.Children)
                    {
                        if (string.Equals(row.LocalName, "tr", StringComparison.Ordinal))
                        {
                            yield return row;
                        }
                    }
                }
            }
        }

        private void Downgrade(string message, string context) =>
            Report(DiagnosticSeverity.Downgraded, message, context);

        private void Report(DiagnosticSeverity severity, string message, string context)
        {
            if (_reported.Add($"{severity}|{context}"))
            {
                diagnostics.Add(new ImportDiagnostic(severity, message, context));
            }
        }
    }
}
