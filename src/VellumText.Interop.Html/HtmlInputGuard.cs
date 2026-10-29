using System.Text;

using AngleSharp.Html;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;
using AngleSharp.Html.Parser.Tokens.Struct;
using AngleSharp.Text;

namespace VellumText.Interop.Html;

/// <summary>
/// Bounds the shapes of HTML whose parse cost grows faster than its size, before the parser sees
/// it.
/// </summary>
/// <remarks>
/// <para>
/// AngleSharp's tree builder is quadratic in element nesting: every block start tag looks for an
/// open <c>p</c> by scanning the stack of open elements, so 200 KB of nested <c>div</c> took 7 s
/// against 0.3 s for 50 KB. Its tokenizer is quadratic in attributes per element (40,000
/// attributes, 349 KB: 12 s), because each new attribute is checked against every earlier one.
/// And AngleSharp.Css parses a <c>style</c> attribute during <c>ParseDocument</c>, by recursion,
/// one level per bracket. None of these can be fixed from outside after parsing starts:
/// <c>HtmlParserOptions.OnToken</c> fires after the tree builder has consumed a token, and the CSS
/// is parsed as the element is created.
/// </para>
/// <para>
/// So this runs first. It reads the input with AngleSharp's own tokenizer — linear, 7 ms for the
/// 200 KB case — and keeps a model of the open elements, including the specification's implied
/// closes for the elements whose end tags are optional (<c>li</c>, <c>p</c>, cells, rows), so that
/// well-formed and ordinarily sloppy markup is measured as the parser will build it. It is not a
/// parser and does not try to be: where its model and the tree builder could disagree it errs
/// towards counting <em>more</em> depth than the parser will build, never less, so the bound holds
/// even when the model is wrong. The costs of being wrong in that direction are a few tags dropped
/// from markup that was malformed to begin with.
/// </para>
/// <para>
/// Nothing is rewritten unless a limit is exceeded, and even then the output is the input with
/// spans removed — the tokens' own source ranges — so no markup is re-serialised except a tag
/// that loses a <c>style</c> attribute. Past <see cref="HtmlImportOptions.MaxElementDepth"/>,
/// start tags and their end tags are dropped and the text between them is kept, which is what the
/// importer's own <see cref="HtmlImportOptions.MaxDepth"/> walk would have reduced them to anyway.
/// An element whose contents are never text and are markup (<c>template</c>, <c>noscript</c>) is
/// dropped together with its contents, because dropping only its tags would release its body into
/// the document. Elements whose contents are raw text cannot nest and are always kept, so that the
/// tokenizer here and the parser later agree on where their text ends.
/// </para>
/// </remarks>
internal static class HtmlInputGuard
{
    /// <summary>What the guard did.</summary>
    /// <param name="Html">The input, or a copy with the over-limit parts removed.</param>
    /// <param name="DroppedDeepTags">Some tags beyond the nesting limit were dropped.</param>
    /// <param name="DroppedAttributes">Some element had more attributes than the limit.</param>
    /// <param name="DroppedStyles">Some <c>style</c> attribute nested brackets beyond the limit.</param>
    internal readonly record struct Outcome(
        string Html,
        bool DroppedDeepTags,
        bool DroppedAttributes,
        bool DroppedStyles);

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal)
    {
        "area", "base", "basefont", "bgsound", "br", "col", "embed", "frame", "hr", "img", "input",
        "keygen", "link", "meta", "param", "source", "track", "wbr",

        // Merged into the elements the parser creates for itself rather than pushed.
        "html", "head", "body", "frameset",
    };

    /// <summary>The specification's "special" category, which stops most end-tag and implied-close scans.</summary>
    private static readonly HashSet<string> Special = new(StringComparer.Ordinal)
    {
        "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote",
        "body", "br", "button", "caption", "center", "col", "colgroup", "dd", "details", "dir", "div",
        "dl", "dt", "embed", "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset",
        "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hgroup", "hr", "html", "iframe", "img",
        "input", "keygen", "li", "link", "listing", "main", "marquee", "menu", "meta", "nav",
        "noembed", "noframes", "noscript", "object", "ol", "p", "param", "plaintext", "pre", "script",
        "search", "section", "select", "source", "style", "summary", "table", "tbody", "td",
        "template", "textarea", "tfoot", "th", "thead", "title", "tr", "track", "ul", "wbr", "xmp",
    };

    /// <summary>The elements that bound "has a <c>p</c> element in button scope".</summary>
    private static readonly HashSet<string> ButtonScope = new(StringComparer.Ordinal)
    {
        "applet", "caption", "html", "table", "td", "th", "marquee", "object", "template", "button",
    };

    /// <summary>The elements that bound "in table scope".</summary>
    private static readonly HashSet<string> TableScope = new(StringComparer.Ordinal)
    {
        "html", "table", "template",
    };

    /// <summary>Start tags that close an open <c>p</c> in button scope.</summary>
    /// <remarks>
    /// <c>table</c> is left out because it closes a paragraph only in no-quirks mode, and a
    /// clipboard fragment has no doctype; not closing is the direction that over-counts.
    /// </remarks>
    private static readonly HashSet<string> ClosesParagraph = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div",
        "dl", "fieldset", "figcaption", "figure", "footer", "header", "hgroup", "main", "menu",
        "nav", "ol", "p", "search", "section", "summary", "ul", "h1", "h2", "h3", "h4", "h5", "h6",
        "pre", "listing", "form", "plaintext", "xmp", "hr", "li", "dd", "dt",
    };

    private static readonly HashSet<string> Headings = new(StringComparer.Ordinal)
    {
        "h1", "h2", "h3", "h4", "h5", "h6",
    };

    /// <summary>Elements whose content is text up to their own end tag, and how it is tokenized.</summary>
    private static readonly Dictionary<string, HtmlParseMode> TextContent = new(StringComparer.Ordinal)
    {
        ["script"] = HtmlParseMode.Script,
        ["style"] = HtmlParseMode.Rawtext,
        ["xmp"] = HtmlParseMode.Rawtext,
        ["iframe"] = HtmlParseMode.Rawtext,
        ["noembed"] = HtmlParseMode.Rawtext,
        ["noframes"] = HtmlParseMode.Rawtext,
        ["textarea"] = HtmlParseMode.RCData,
        ["title"] = HtmlParseMode.RCData,
        ["plaintext"] = HtmlParseMode.Plaintext,
    };

    /// <summary>Elements whose contents are markup that must never become text.</summary>
    /// <remarks>
    /// <c>noscript</c> is parsed as markup because the importer's browsing context has scripting
    /// off. Both are removed with their subtree by <see cref="SanitizingReader"/>, so dropping one
    /// with its contents here changes nothing a document could show.
    /// </remarks>
    private static readonly HashSet<string> MarkupNonContent = new(StringComparer.Ordinal)
    {
        "template", "noscript",
    };

    /// <summary>Bounds <paramref name="html"/> by the limits in <paramref name="options"/>.</summary>
    /// <param name="html">The markup.</param>
    /// <param name="options">The limits.</param>
    /// <returns>The markup the parser should see, and which limits were hit.</returns>
    internal static Outcome Bound(string html, HtmlImportOptions options)
    {
        var droppedAttributes = false;
        var maxAttributes = options.MaxAttributesPerElement;

        var tokenizer = new HtmlTokenizer(new TextSource(html), HtmlEntityProvider.Resolver)
        {
            // The same cap the parser applies, and the reason this scan stays linear: an attribute
            // that is not emitted is never compared against the others.
            ShouldEmitAttribute = (ref StructHtmlToken token, ReadOnlyMemory<char> name) =>
            {
                if (token.Attributes.Count < maxAttributes)
                {
                    return true;
                }

                droppedAttributes = true;
                return false;
            },
        };

        var open = new OpenElements();
        var edits = new List<(int Start, int End, string? Replacement)>();
        var droppedDeep = false;
        var droppedStyles = false;

        var pendingStart = -1;
        string? pendingReplacement = null;
        var previous = -1;

        string? skipping = null;
        var skipNesting = 0;

        while (true)
        {
            var token = tokenizer.Get();
            var start = token.Position.Position - 1;

            // The edit list is built from token start offsets, so they have to be what they look
            // like. Failing closed — the importer turns this into an empty result with a
            // diagnostic — is better than slicing the input at the wrong places.
            if (start < previous || start > html.Length)
            {
                throw new InvalidOperationException("The HTML tokenizer reported positions out of order.");
            }

            previous = start;

            if (pendingStart >= 0)
            {
                edits.Add((pendingStart, start, pendingReplacement));
                pendingStart = -1;
                pendingReplacement = null;
            }

            if (token.Type == HtmlTokenType.EndOfFile)
            {
                break;
            }

            if (token.Type is not (HtmlTokenType.StartTag or HtmlTokenType.EndTag))
            {
                if (skipping is not null)
                {
                    pendingStart = start;
                }

                continue;
            }

            var tag = token.AsTag();
            var name = tag.Name;

            if (start >= html.Length || html[start] != '<')
            {
                throw new InvalidOperationException("The HTML tokenizer reported a tag that does not start with '<'.");
            }

            if (token.Type == HtmlTokenType.StartTag)
            {
                var inSelect = skipping is null && open.InSelect;
                var rawText = false;

                // Inside a select the tree builder ignores these start tags or makes them ordinary
                // elements, so its tokenizer stays where it was; only a script is still raw text.
                // Switching here regardless would stop this scan reading tags the parser goes on
                // nesting.
                if (open.ForeignDepth == 0
                    && (!inSelect || name == "script")
                    && TextContent.TryGetValue(name, out var mode))
                {
                    tokenizer.State = mode;
                    rawText = true;
                }

                if (skipping is not null)
                {
                    if (name == skipping && !tag.IsSelfClosing)
                    {
                        skipNesting++;
                    }

                    pendingStart = start;
                    continue;
                }

                if (open.ForeignDepth == 0)
                {
                    open.CloseImplied(name);
                }

                // In a select the tree builder pushes even a void or self-closed name as an element.
                var isVoid = !inSelect
                    && (Void.Contains(name)
                        || (tag.IsSelfClosing && (open.ForeignDepth > 0 || name is "svg" or "math")));

                var keep = isVoid
                    || open.EmittedDepth < options.MaxElementDepth
                    || rawText;

                if (!keep)
                {
                    droppedDeep = true;
                    pendingStart = start;

                    if (open.ForeignDepth == 0 && MarkupNonContent.Contains(name))
                    {
                        skipping = name;
                        skipNesting = 1;
                    }
                    else
                    {
                        open.Push(name, emitted: false);
                    }

                    continue;
                }

                if (tag.GetAttribute("style") is { Length: > 0 } style
                    && BracketDepth(style) > options.MaxStyleNesting)
                {
                    droppedStyles = true;
                    pendingStart = start;
                    pendingReplacement = WithoutStyle(tag);
                }

                if (!isVoid)
                {
                    open.Push(name, emitted: true);
                }

                continue;
            }

            if (skipping is not null)
            {
                if (name == skipping && --skipNesting == 0)
                {
                    skipping = null;
                }

                pendingStart = start;
                continue;
            }

            if (open.CloseFor(name) is false)
            {
                // Its start tag was dropped, so its end tag goes too.
                pendingStart = start;
            }
        }

        if (edits.Count == 0)
        {
            return new Outcome(html, droppedDeep, droppedAttributes, droppedStyles);
        }

        var output = new StringBuilder(html.Length);
        var copied = 0;

        foreach (var (start, end, replacement) in edits)
        {
            output.Append(html, copied, start - copied);
            output.Append(replacement);
            copied = end;
        }

        output.Append(html, copied, html.Length - copied);

        return new Outcome(output.ToString(), droppedDeep, droppedAttributes, droppedStyles);
    }

    /// <summary>The deepest bracket nesting in a CSS value.</summary>
    /// <remarks>
    /// Quotes and escapes are not understood, deliberately: a bracket inside a string counts too,
    /// which can only make a hostile value look deeper. Real stylesheets nest two or three levels.
    /// </remarks>
    internal static int BracketDepth(string css)
    {
        var depth = 0;
        var deepest = 0;

        foreach (var c in css)
        {
            switch (c)
            {
                case '(' or '[' or '{':
                    deepest = Math.Max(deepest, ++depth);
                    break;

                case ')' or ']' or '}':
                    depth = Math.Max(0, depth - 1);
                    break;
            }
        }

        return deepest;
    }

    /// <summary>A start tag written back out without its <c>style</c> attribute.</summary>
    /// <remarks>
    /// Attribute names that are not plain identifiers are left out rather than escaped: the
    /// sanitizer keeps only a short allow-list of ordinary names, so nothing it would keep is lost.
    /// </remarks>
    private static string WithoutStyle(HtmlTagToken tag)
    {
        var text = new StringBuilder("<").Append(tag.Name);

        foreach (var attribute in tag.Attributes)
        {
            if (attribute.Name == "style" || !IsPlainName(attribute.Name))
            {
                continue;
            }

            text.Append(' ').Append(attribute.Name).Append("=\"");

            foreach (var c in attribute.Value)
            {
                text.Append(c switch
                {
                    '&' => "&amp;",
                    '"' => "&quot;",
                    _ => c.ToString(),
                });
            }

            text.Append('"');
        }

        return text.Append(tag.IsSelfClosing ? " />" : ">").ToString();
    }

    private static bool IsPlainName(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.');

    /// <summary>The open elements, with constant-time "nearest open X" lookups.</summary>
    /// <remarks>
    /// The tree builder's own scans of its stack are what make it quadratic, so this cannot scan
    /// either: each name and each category keeps the indexes of its open entries, and popping an
    /// entry is paid for once.
    /// </remarks>
    private sealed class OpenElements
    {
        private readonly List<(string Name, bool Emitted)> _entries = [];
        private readonly Dictionary<string, List<int>> _byName = new(StringComparer.Ordinal);
        private readonly List<int> _special = [];
        private readonly List<int> _specialBarringListItems = [];
        private readonly List<int> _buttonScope = [];
        private readonly List<int> _tableScope = [];

        internal int EmittedDepth { get; private set; }

        internal int ForeignDepth { get; private set; }

        /// <summary>A <c>select</c> is open, so the tree builder may be in its select mode.</summary>
        /// <remarks>
        /// <para>
        /// In that mode the tree builder implies no closes but an option's, closes an element on
        /// its end tag only when it is the current node, and pushes any other start tag as an
        /// ordinary element unless the select itself is the current node; this model pushes it
        /// then too. It is not told which of the parser's paths out of the mode it took — a table
        /// tag, an input, a template — so it stays in its own select mode until the select is the
        /// current node and is closed. Where the two disagree this model has more elements open
        /// than the parser, never fewer.
        /// </para>
        /// <para>
        /// The cost is that markup after a select left open in an unusual way is measured
        /// without implied closes, as deeper than it is. A browser writes every select closed.
        /// </para>
        /// </remarks>
        internal bool InSelect => Nearest("select") >= 0;

        internal void Push(string name, bool emitted)
        {
            var index = _entries.Count;

            _entries.Add((name, emitted));

            if (!_byName.TryGetValue(name, out var indexes))
            {
                _byName[name] = indexes = [];
            }

            indexes.Add(index);

            if (Special.Contains(name))
            {
                _special.Add(index);

                if (name is not ("address" or "div" or "p"))
                {
                    _specialBarringListItems.Add(index);
                }
            }

            if (ButtonScope.Contains(name))
            {
                _buttonScope.Add(index);
            }

            if (TableScope.Contains(name))
            {
                _tableScope.Add(index);
            }

            if (emitted)
            {
                EmittedDepth++;
            }

            if (name is "svg" or "math")
            {
                ForeignDepth++;
            }
        }

        /// <summary>Applies the closes a start tag implies, as the tree builder would.</summary>
        internal void CloseImplied(string name)
        {
            if (InSelect)
            {
                if (name is "option" or "optgroup" && Top() == "option")
                {
                    PopTo(_entries.Count - 1);
                }

                return;
            }

            switch (name)
            {
                case "li":
                    CloseListItem(Nearest("li"));
                    break;

                case "dd" or "dt":
                    CloseListItem(Math.Max(Nearest("dd"), Nearest("dt")));
                    break;

                case "td" or "th":
                    CloseInTableScope(Math.Max(Nearest("td"), Nearest("th")));
                    break;

                case "tr":
                    CloseInTableScope(Nearest("tr"));
                    break;

                case "tbody" or "thead" or "tfoot":
                    CloseInTableScope(Math.Max(Nearest("tbody"), Math.Max(Nearest("thead"), Nearest("tfoot"))));
                    break;

                case "option" or "optgroup" when Top() == "option":
                    PopTo(_entries.Count - 1);
                    break;
            }

            if (ClosesParagraph.Contains(name))
            {
                var paragraph = Nearest("p");

                if (paragraph >= 0 && paragraph > Last(_buttonScope))
                {
                    PopTo(paragraph);
                }
            }

            if (Headings.Contains(name) && Top() is { } top && Headings.Contains(top))
            {
                PopTo(_entries.Count - 1);
            }
        }

        /// <summary>
        /// Closes the nearest open element named <paramref name="name"/> for its end tag, unless a
        /// special element is open inside it.
        /// </summary>
        /// <returns>
        /// Whether the closed element's start tag was kept; null when nothing was closed, in which
        /// case the end tag is left for the parser to judge.
        /// </returns>
        /// <remarks>
        /// The parser sometimes closes through a special element (<c>&lt;div&gt;&lt;ul&gt;&lt;/div&gt;</c>)
        /// and sometimes refuses to (most inline end tags). Never closing through one is the side
        /// that can only over-count.
        /// </remarks>
        internal bool? CloseFor(string name)
        {
            var index = InSelect
                ? (Top() == name ? _entries.Count - 1 : -1)
                : Nearest(name);

            if (index < 0 || Last(_special) > index)
            {
                return null;
            }

            var emitted = _entries[index].Emitted;

            PopTo(index);

            return emitted;
        }

        private void CloseListItem(int index)
        {
            // The list-item algorithm stops at any special element other than address, div and p.
            if (index >= 0 && Last(_specialBarringListItems) == index)
            {
                PopTo(index);
            }
        }

        private void CloseInTableScope(int index)
        {
            if (index >= 0 && index > Last(_tableScope))
            {
                PopTo(index);
            }
        }

        private string? Top() => _entries.Count > 0 ? _entries[^1].Name : null;

        private int Nearest(string name) =>
            _byName.TryGetValue(name, out var indexes) && indexes.Count > 0 ? indexes[^1] : -1;

        private static int Last(List<int> indexes) => indexes.Count > 0 ? indexes[^1] : -1;

        /// <summary>Pops entries until the one at <paramref name="index"/> has been popped.</summary>
        private void PopTo(int index)
        {
            while (_entries.Count > index)
            {
                var last = _entries.Count - 1;
                var (name, emitted) = _entries[last];

                _entries.RemoveAt(last);
                _byName[name].RemoveAt(_byName[name].Count - 1);

                RemoveIfLast(_special, last);
                RemoveIfLast(_specialBarringListItems, last);
                RemoveIfLast(_buttonScope, last);
                RemoveIfLast(_tableScope, last);

                if (emitted)
                {
                    EmittedDepth--;
                }

                if (name is "svg" or "math")
                {
                    ForeignDepth--;
                }
            }
        }

        private static void RemoveIfLast(List<int> indexes, int index)
        {
            if (indexes.Count > 0 && indexes[^1] == index)
            {
                indexes.RemoveAt(indexes.Count - 1);
            }
        }
    }
}
