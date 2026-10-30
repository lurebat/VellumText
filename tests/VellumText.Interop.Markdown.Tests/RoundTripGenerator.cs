using System.Globalization;
using System.Text;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// Seeded random documents in the subset of the model that Markdown can express, and a canonical
/// dump to compare them by.
/// </summary>
/// <remarks>
/// <para>
/// Adapted from the generator of the 2026-10-25 audit's end-to-end fuzz pass, which found the
/// exporter's flanking, merging and fence defects that hand-picked examples had missed for months.
/// </para>
/// <para>
/// Shapes whose loss is documented rather than reported are left out by construction rather than
/// filtered afterwards, so a failure is always a real one: whitespace at a paragraph's edges or
/// doubled (the HTML stage collapses it, as it does for every importer), and a backslash in a link
/// destination (reported separately as F-62).
/// </para>
/// </remarks>
internal static class RoundTripGenerator
{
    public const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static readonly string[] Words =
        ["a", "b", "Zed", "12", "x.y", "é", "日本", "\U0001F600", "ok", "Ünï", "R&D", "AT&T"];

    private static readonly string[] Punct =
    [
        "*", "_", "`", "``", "```", "~", "~~~", "\\", "[", "]", "(", ")", "<", ">", "|", "#", "!", "&",
        "&amp;", "\"", "'", ":", "-", "+", "=", "{", "}", "$", "%", "~~", "1.", "- ", "$5", "!!",
    ];

    private static readonly string[] HrefBases =
        ["https://ex.com/", "http://a.b/c/", "mailto:x@y.z?s=", "https://ex.com/p?q=1&r=2#"];

    private static readonly string[] HrefTails =
        ["", "a", "a(b)", "(x", "y)", "a b", "<t>", "é", "日本", "%20", "a_b*c", "|", "a\"b", "'", "[1]"];

    private static readonly string[] Titles = ["T", "a \"q\" b", "(p)", "x'y", "é", "a|b", "a)b", "*t*"];

    private static readonly string[] Alts = ["pic", "a*b", "[x]", "é", "a \"q\" b", "a|b", "x)y", "`c`"];

    /// <summary>Generator switches, so a later unit can widen what is generated.</summary>
    public sealed record Options
    {
        public bool BlockImages { get; init; }
    }

    public static DocumentNode Document(Random r, Options? options = null)
    {
        options ??= new Options();

        while (true)
        {
            try
            {
                var blocks = Enumerable.Range(0, r.Next(1, 5)).Select(_ => Block(r, options)).ToList();
                var doc = new DocumentNode(blocks);

                if (DocumentSchema.IsValid(doc))
                {
                    return doc;
                }
            }
            catch (ArgumentException)
            {
            }
        }
    }

    /// <summary>A paragraph of one to five runs, each with its own Markdown-expressible marks.</summary>
    public static InlineContent Content(Random r, int maxRuns, bool allowEmpty, bool code = false)
    {
        var units = new List<(string Text, MarkSet Mark, InlineEmbed? Embed)>();
        var links = new[] { Link(r), Link(r) };
        var runs = r.Next(allowEmpty ? 0 : 1, maxRuns + 1);

        for (var i = 0; i < runs; i++)
        {
            var mark = code ? MarkSet.Empty : r.Next(3) == 0 ? MarkSet.Empty : Mark(r, links);

            for (var j = r.Next(1, 4); j > 0; j--)
            {
                if (!code && r.Next(7) == 0)
                {
                    var imageMark = mark with { Style = mark.Style & ~TextStyle.Code };

                    units.Add((InlineContent.Placeholder.ToString(), imageMark, Image(r)));
                }
                else
                {
                    units.Add((Atom(r, code), mark, null));
                }
            }
        }

        // Whitespace at the edges, doubled, or after a line break is collapsed by the HTML stage on
        // the way back in — documented, so never generated.
        var clean = new List<(string Text, MarkSet Mark, InlineEmbed? Embed)>();

        foreach (var unit in units)
        {
            var text = unit.Text;

            if (unit.Embed is null)
            {
                var afterSpace = clean.Count == 0 || clean[^1].Text.EndsWith(' ')
                    || clean[^1].Text.EndsWith('\n');

                if (afterSpace)
                {
                    text = text.TrimStart(' ');
                }

                if (text.Length == 0)
                {
                    continue;
                }

                // A space before a line break is collapsed into the break, the same rule.
                while (text.StartsWith('\n') && clean.Count > 0 && clean[^1].Embed is null
                    && clean[^1].Text.EndsWith(' '))
                {
                    var previous = clean[^1].Text.TrimEnd(' ');

                    if (previous.Length == 0)
                    {
                        clean.RemoveAt(clean.Count - 1);
                    }
                    else
                    {
                        clean[^1] = (previous, clean[^1].Mark, null);
                    }
                }
            }

            clean.Add((text, unit.Mark, unit.Embed));
        }

        while (clean.Count > 0 && clean[^1].Embed is null
            && (clean[^1].Text.EndsWith(' ') || clean[^1].Text.EndsWith('\n')))
        {
            var text = clean[^1].Text.TrimEnd(' ', '\n');

            if (text.Length == 0)
            {
                clean.RemoveAt(clean.Count - 1);
            }
            else
            {
                clean[^1] = (text, clean[^1].Mark, null);
            }
        }

        if (clean.Count == 0 && !allowEmpty)
        {
            clean.Add(("a", MarkSet.Empty, null));
        }

        return Build(clean);
    }

    public static InlineContent Build(IEnumerable<(string Text, MarkSet Mark, InlineEmbed? Embed)> units)
    {
        var text = new StringBuilder();
        var spans = new List<ValueSpan<MarkSet>>();
        var embeds = new List<InlineEmbed>();

        foreach (var (s, mark, embed) in units)
        {
            if (!mark.IsEmpty)
            {
                spans.Add(new ValueSpan<MarkSet>(text.Length, s.Length, mark));
            }

            text.Append(s);

            if (embed is not null)
            {
                embeds.Add(embed);
            }
        }

        return InlineContent.Create(text.ToString(), spans, embeds);
    }

    private static string Href(Random r) =>
        HrefBases[r.Next(HrefBases.Length)] + HrefTails[r.Next(HrefTails.Length)];

    private static LinkMark Link(Random r) =>
        new(Href(r), r.Next(3) == 0 ? Titles[r.Next(Titles.Length)] : null);

    private static ImageEmbed Image(Random r) =>
        new(Png, null, null, r.Next(2) == 0 ? Alts[r.Next(Alts.Length)] : null);

    private static MarkSet Mark(Random r, LinkMark[] links)
    {
        var style = TextStyle.None;

        if (r.Next(3) == 0)
        {
            style |= TextStyle.Bold;
        }

        if (r.Next(3) == 0)
        {
            style |= TextStyle.Italic;
        }

        if (r.Next(6) == 0)
        {
            style |= TextStyle.Strikethrough;
        }

        if (r.Next(7) == 0)
        {
            style |= TextStyle.Code;
        }

        var mark = new MarkSet(style);

        return r.Next(3) == 0 ? mark with { Link = links[r.Next(links.Length)] } : mark;
    }

    private static string Atom(Random r, bool code)
    {
        var k = r.Next(12);

        if (k < 5)
        {
            return Words[r.Next(Words.Length)];
        }

        if (k < 9)
        {
            return code
                ? new[] { "`", "``", "```", "~~~", "````", "\\", "*", "<" }[r.Next(8)]
                : Punct[r.Next(Punct.Length)];
        }

        return k == 11 && !code ? "\n" : " ";
    }

    private static ParagraphNode Paragraph(Random r, bool inCell)
    {
        var kind = ParagraphKind.Body;

        if (!inCell)
        {
            var k = r.Next(12);

            kind = k switch
            {
                < 2 => (ParagraphKind)r.Next(1, 7),
                2 => ParagraphKind.Quote,
                3 => ParagraphKind.Code,
                _ => ParagraphKind.Body,
            };
        }

        var content = Content(r, kind == ParagraphKind.Code ? 3 : 5, inCell, kind == ParagraphKind.Code);

        return new ParagraphNode(content, kind);
    }

    private static ListNode List(Random r, int depth)
    {
        var kind = (ListKind)r.Next(2);
        var start = kind == ListKind.Unordered || r.Next(2) == 0 ? 1 : r.Next(0, 5);
        var items = new List<ListItemNode>();

        for (var i = r.Next(1, 4); i > 0; i--)
        {
            var blocks = new List<BlockNode> { Paragraph(r, false) };

            if (depth < 3 && r.Next(3) == 0)
            {
                blocks.Add(List(r, depth + 1));
            }
            else if (r.Next(6) == 0)
            {
                blocks.Add(Paragraph(r, false));
            }

            items.Add(new ListItemNode(blocks));
        }

        return new ListNode(items, kind, start);
    }

    private static TableNode Table(Random r)
    {
        var rows = r.Next(1, 4);
        var columns = r.Next(1, 4);
        var list = new List<TableRowNode>();

        for (var i = 0; i < rows; i++)
        {
            var cells = new List<TableCellNode>();

            for (var j = 0; j < columns; j++)
            {
                var blocks = new List<BlockNode>
                {
                    new ParagraphNode(Content(r, 3, allowEmpty: true)),
                };

                cells.Add(new TableCellNode(blocks, 1, 1, null, i == 0));
            }

            list.Add(new TableRowNode(cells));
        }

        return new TableNode(list);
    }

    private static BlockNode Block(Random r, Options options) => r.Next(12) switch
    {
        < 5 => Paragraph(r, false),
        < 7 => List(r, 0),
        < 9 => Table(r),
        9 when options.BlockImages => new BlockImageNode(Image(r)),
        10 => RuleNode.Instance,
        _ => Paragraph(r, false),
    };

    /// <summary>
    /// The document as text, with every difference that matters to a Markdown round trip visible
    /// and every documented one normalised away.
    /// </summary>
    /// <remarks>
    /// Normalised on both sides: an empty body paragraph directly in the document is dropped
    /// (Markdown has no notation for one, a settled loss); a fenced code block reads back with the
    /// Code mark and one trailing newline; link destinations are compared percent-decoded, because
    /// the reader percent-encodes what the writer wrote as-is and both mean the same address.
    /// </remarks>
    public static string Dump(DocumentNode doc)
    {
        var text = new StringBuilder();

        foreach (var block in doc.Blocks)
        {
            if (block is ParagraphNode { Kind: ParagraphKind.Body, Content.Length: 0 })
            {
                continue;
            }

            Node(text, block, 0);
        }

        return text.ToString();
    }

    private static string Img(ImageEmbed image) =>
        $"IMG(src={(image.Source == Png ? "png" : image.Source)},alt={image.AltText ?? "-"})";

    private static string MarkText(MarkSet mark)
    {
        if (mark.IsEmpty)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (mark.Style != TextStyle.None)
        {
            parts.Add(mark.Style.ToString().Replace(", ", "+", StringComparison.Ordinal));
        }

        if (mark.Link is { } link)
        {
            parts.Add("link=" + Uri.UnescapeDataString(link.Href)
                + (link.Title is null ? string.Empty : " title=" + link.Title));
        }

        return "{" + string.Join(";", parts) + "}";
    }

    public static string Content(InlineContent content, ParagraphKind kind = ParagraphKind.Body)
    {
        var text = new StringBuilder();
        var i = 0;

        while (i < content.Length)
        {
            var mark = MarkAt(content, i);
            var run = new StringBuilder();

            while (i < content.Length && MarkAt(content, i).Equals(mark))
            {
                if (content.Text[i] == InlineContent.Placeholder && content.TryGetEmbedAt(i, out var embed))
                {
                    run.Append(embed is ImageEmbed image ? "⟦" + Img(image) + "⟧" : "⟦" + embed + "⟧");
                }
                else
                {
                    run.Append(content.Text[i] == '\n' ? "↵" : content.Text[i].ToString());
                }

                i++;
            }

            if (kind == ParagraphKind.Code)
            {
                mark = mark with { Style = mark.Style & ~TextStyle.Code };

                if (i == content.Length && run.Length > 0 && run[^1] == '↵')
                {
                    run.Length--;
                }
            }

            if (run.Length > 0)
            {
                text.Append('«').Append(run).Append('»').Append(MarkText(mark));
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The mark at <paramref name="index"/>, without what whitespace cannot show: a delimiter
    /// cannot touch a space, so the exporter writes edge whitespace outside it by design, and a
    /// line break has no text for a style or a link to be seen on.
    /// </summary>
    private static MarkSet MarkAt(InlineContent content, int index)
    {
        var mark = content.MarkAt(index);

        return content.Text[index] switch
        {
            '\n' => MarkSet.Empty,
            ' ' => mark with
            {
                Style = mark.Style & ~(TextStyle.Bold | TextStyle.Italic | TextStyle.Strikethrough),
            },
            _ => mark,
        };
    }

    private static void Node(StringBuilder text, Node node, int depth)
    {
        text.Append(' ', depth * 2);

        switch (node)
        {
            case ParagraphNode p:
                text.Append(CultureInfo.InvariantCulture, $"P[{p.Kind}] ")
                    .Append(Content(p.Content, p.Kind)).Append('\n');
                return;

            case ListNode l:
                text.Append(CultureInfo.InvariantCulture, $"L[{l.Kind},{l.Start}]");
                break;

            case BlockImageNode image:
                text.Append("BI ").Append(Img(image.Image)).Append('\n');
                return;

            case RuleNode:
                text.Append("HR\n");
                return;

            default:
                text.Append(node.TypeName);
                break;
        }

        text.Append('\n');

        foreach (var child in node.Children)
        {
            Node(text, child, depth + 1);
        }
    }
}
