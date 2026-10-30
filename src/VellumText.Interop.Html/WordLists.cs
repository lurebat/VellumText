using System.Globalization;

using AngleSharp.Css.Parser;
using AngleSharp.Dom;

namespace VellumText.Interop.Html;

/// <summary>
/// Turns Word's list paragraphs into real lists, before the HTML is sanitized.
/// </summary>
/// <remarks>
/// <para>
/// Word's HTML, on the clipboard and in a saved <c>.htm</c>, has no <c>ul</c> or <c>ol</c>. Each
/// list item is a paragraph whose inline style says <c>mso-list:l0 level1 lfo1</c> - which list,
/// at which level - with its bullet or number written out as text inside
/// <c>&lt;![if !supportLists]&gt;</c> … <c>&lt;![endif]&gt;</c>. The parser keeps those two as
/// comments around ordinary nodes. Read as paragraphs, a pasted list arrived as "·" paragraphs in
/// the Symbol font.
/// </para>
/// <para>
/// This has to run on the parsed document before the sanitizer, which removes the <c>mso-</c>
/// declarations and the comments that say what the paragraphs are. Everything it reads comes from
/// the DOM, and the style through AngleSharp's CSS parser, which keeps declarations it does not
/// know when asked to.
/// </para>
/// </remarks>
internal static class WordLists
{
    private readonly record struct Item(IElement Paragraph, string List, int Level, bool Ordered);

    /// <summary>Rewrites every run of Word list paragraphs in <paramref name="document"/> as lists.</summary>
    /// <param name="document">The parsed document.</param>
    internal static void Rewrite(IDocument document)
    {
        if (document.Body is not { } body)
        {
            return;
        }

        CssParser? parser = null;
        var consumed = new HashSet<IElement>();

        foreach (var paragraph in body.QuerySelectorAll("p").ToList())
        {
            if (consumed.Contains(paragraph) || Read(paragraph, ref parser) is not { } first)
            {
                continue;
            }

            var run = new List<Item> { first };

            consumed.Add(paragraph);

            for (var next = NextSignificant(paragraph);
                next is IElement { LocalName: "p" } element && Read(element, ref parser) is { } item;
                next = NextSignificant(element))
            {
                run.Add(item);
                consumed.Add(element);
            }

            Build(document, run);
        }
    }

    /// <summary>Reads a paragraph as a Word list item, taking its written-out marker away.</summary>
    private static Item? Read(IElement paragraph, ref CssParser? parser)
    {
        var style = paragraph.GetAttribute("style");

        if (style is null || !style.Contains("mso-list", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        parser ??= new CssParser(new CssParserOptions { IsIncludingUnknownDeclarations = true });

        // "l0 level1 lfo1": which list, at which level, and Word's override table entry.
        string? list = null;
        var level = 0;

        foreach (var part in MsoList(parser, style).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("level", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var declared))
            {
                level = declared;
            }
            else if (part.Length > 1 && part[0] is 'l' or 'L' && char.IsAsciiDigit(part[1]))
            {
                list = part;
            }
        }

        if (level < 1)
        {
            return null;
        }

        return new Item(paragraph, list ?? string.Empty, level, IsNumbering(TakeMarker(paragraph, parser)));
    }

    /// <summary>Removes the bullet or number Word wrote as text, and returns it.</summary>
    private static string TakeMarker(IElement paragraph, CssParser parser)
    {
        var marker = new List<INode>();
        var open = paragraph.Descendants<IComment>().FirstOrDefault(c => IsComment(c, "[if !supportLists]"));

        if (open is not null)
        {
            var node = open.NextSibling;

            while (node is not null && !(node is IComment close && IsComment(close, "[endif]")))
            {
                marker.Add(node);
                node = node.NextSibling;
            }

            if (node is null)
            {
                // An unclosed fence fences nothing that can be trusted to be the marker.
                marker.Clear();
            }
            else
            {
                marker.Add(open);
                marker.Add(node);
            }
        }

        if (marker.Count == 0)
        {
            // Outlook and older Word write the marker without the fence, in a span of its own.
            marker.AddRange(paragraph.Descendants<IElement>().Where(e =>
                e.GetAttribute("style") is { } style
                && style.Contains("mso-list", StringComparison.OrdinalIgnoreCase)
                && MsoList(parser, style).Equals("Ignore", StringComparison.OrdinalIgnoreCase)));
        }

        var text = string.Concat(marker.Select(node => node is IComment ? string.Empty : node.TextContent));

        foreach (var node in marker)
        {
            node.Parent?.RemoveChild(node);
        }

        return text.Trim();
    }

    private static string MsoList(CssParser parser, string style) =>
        parser.ParseDeclaration(style)?.GetPropertyValue("mso-list") ?? string.Empty;

    private static bool IsComment(IComment comment, string data) =>
        comment.Data.Trim().Equals(data, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a written-out marker numbers its item: "1.", "a)", "iv.", "(3)", "1.2.".</summary>
    /// <remarks>Anything else - "·", "o", "§", a dash - is a bullet drawn from some symbol font.</remarks>
    private static bool IsNumbering(string marker)
    {
        var core = marker.TrimStart('(').TrimEnd('.', ')');

        if (core.Length == 0)
        {
            return false;
        }

        if (core.Length < marker.Length && (core.All(char.IsAsciiDigit) || core.All(char.IsAsciiLetter)))
        {
            return true;
        }

        // Outline and legal numbering write every level's number: "1.2." or "2.3.1". The dots
        // between them are punctuation enough, so no trailing one is needed.
        var parts = core.Split('.');

        return parts.Length > 1 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    /// <summary>The next sibling that is neither a comment nor whitespace.</summary>
    private static INode? NextSignificant(INode node)
    {
        var next = node.NextSibling;

        while (next is IComment || (next is IText text && string.IsNullOrWhiteSpace(text.Data)))
        {
            next = next.NextSibling;
        }

        return next;
    }

    private static void Build(IDocument document, List<Item> run)
    {
        // The lists open, outermost first, each with the Word level it was opened for and the
        // item it last took, which a deeper list nests under. Levels are Word's, not depths: a copy
        // can start part-way down a list, and Word allows a level to skip ahead, so the two only
        // agree for a list written from level 1 without gaps. Comparing them is what nested two
        // level-2 items one inside the other (rubber-duck review 4.2).
        var open = new List<(IElement List, bool Ordered, IElement? Last, int Level)>();
        string? current = null;

        foreach (var item in run)
        {
            if (item.List != current)
            {
                open.Clear();
                current = item.List;
            }

            while (open.Count > 0 && open[^1].Level > item.Level)
            {
                open.RemoveAt(open.Count - 1);
            }

            if (open.Count > 0 && open[^1].Level == item.Level && open[^1].Ordered != item.Ordered)
            {
                // A bullet after a number at the same level is a new list beside the old one.
                open.RemoveAt(open.Count - 1);
            }

            // One list at most is opened per item, so a level that skips ahead nests only one
            // deeper: Word allows a jump the model has no item to hang the lists between on.
            if (open.Count == 0 || open[^1].Level < item.Level)
            {
                var list = document.CreateElement(item.Ordered ? "ol" : "ul");

                if (open.Count == 0 || open[^1].Last is not { } parentItem)
                {
                    item.Paragraph.Parent!.InsertBefore(list, item.Paragraph);
                }
                else
                {
                    parentItem.AppendChild(list);
                }

                open.Add((list, item.Ordered, null, item.Level));
            }

            var element = document.CreateElement("li");

            while (item.Paragraph.FirstChild is { } child)
            {
                element.AppendChild(child);
            }

            open[^1].List.AppendChild(element);
            open[^1] = open[^1] with { Last = element };
            item.Paragraph.Parent?.RemoveChild(item.Paragraph);
        }
    }
}
