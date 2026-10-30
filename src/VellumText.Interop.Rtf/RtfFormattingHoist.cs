using System.Globalization;
using System.Net;

using OfficeIMO.Rtf;

namespace VellumText.Interop.Rtf;

/// <summary>
/// Moves the font family and size every run of a document states onto one element around it,
/// before the reader writes its HTML (phase 8, owner decision 2026-10-28).
/// </summary>
/// <remarks>
/// <para>
/// The reader writes a <c>&lt;span&gt;</c> for each run, stating its font family and size even
/// when the whole document shares them, so a Word document's HTML was about 3.5 times the size it
/// needed to be and the HTML stage spent most of an import on the copies. Here the shared value is
/// taken off the runs in the reader's model and written once, on a wrapper the HTML stage inherits
/// it from, as it does from any container.
/// </para>
/// <para>
/// <b>The import must come out exactly as it would have.</b> A run that keeps its own span keeps
/// its own value, so the wrapper can only change what a run with <em>no</em> value inherits — and
/// so a property is hoisted only when every run with text states one. Anything else the reader
/// writes with no span of its own (a line break, an image, a field, a shape) would inherit the
/// wrapper's font where it had none, so a document holding one is left alone; so is one with a
/// footnote, whose text the reader writes inside the paragraph that refers to it. Where a stylesheet
/// is in play, a run is cleared only if clearing it leaves the style with nothing to say, since
/// the reader writes what the style resolves to.
/// </para>
/// </remarks>
internal static class RtfFormattingHoist
{
    /// <summary>
    /// Takes the shared family and size off the runs of <paramref name="document"/>, and returns the
    /// CSS that states them once, or null when nothing was hoisted.
    /// </summary>
    internal static string? Hoist(RtfDocument document)
    {
        var runs = new List<(RtfParagraph Paragraph, RtfRun Run)>();

        if (!Collect(document.Blocks, runs) || runs.Count == 0)
        {
            return null;
        }

        var css = string.Empty;

        if (HoistOne(document, runs, Family, run => run.FontId = null, (run, saved) => run.FontId = saved.FontId) is { } family)
        {
            css += "font-family:" + family + ";";
        }

        if (HoistOne(document, runs, Size, run => run.FontSize = null, (run, saved) => run.FontSize = saved.FontSize) is { } size)
        {
            css += "font-size:" + size + "pt;";
        }

        return css.Length == 0 ? null : css;
    }

    /// <summary>Wraps the reader's HTML in an element carrying <paramref name="css"/>.</summary>
    internal static string Wrap(string html, string? css) =>
        css is null ? html : "<div style=\"" + WebUtility.HtmlEncode(css) + "\">" + html + "</div>";

    /// <summary>
    /// Every run with text, with the paragraph it is resolved against; false if the document holds
    /// something the wrapper could reach that is not a run.
    /// </summary>
    private static bool Collect(IReadOnlyList<IRtfBlock> blocks, List<(RtfParagraph, RtfRun)> runs)
    {
        var pending = new Stack<IReadOnlyList<IRtfBlock>>();

        pending.Push(blocks);

        while (pending.TryPop(out var list))
        {
            foreach (var block in list)
            {
                switch (block)
                {
                    case RtfParagraph paragraph:
                        foreach (var inline in paragraph.Inlines)
                        {
                            switch (inline)
                            {
                                case RtfRun run:
                                    // The reader writes a footnote's text inside the paragraph,
                                    // where it would inherit the wrapper's font.
                                    if (run.Note is not null)
                                    {
                                        return false;
                                    }

                                    if (!string.IsNullOrEmpty(run.Text))
                                    {
                                        runs.Add((paragraph, run));
                                    }

                                    break;
                                case RtfBookmarkMarker:
                                    break;
                                default:
                                    return false;
                            }
                        }

                        break;
                    case RtfTable table:
                        foreach (var row in table.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                pending.Push(cell.Blocks);
                            }
                        }

                        break;
                    default:
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Hoists one property: the value most runs state, provided every run states one. Returns that
    /// value as the reader would write it, or null.
    /// </summary>
    private static string? HoistOne(
        RtfDocument document,
        List<(RtfParagraph Paragraph, RtfRun Run)> runs,
        Func<RtfDocument, RtfRun, string?> written,
        Action<RtfRun> clear,
        Action<RtfRun, (int? FontId, double? FontSize)> restore)
    {
        var values = new string?[runs.Count];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < runs.Count; i++)
        {
            var value = written(document, Effective(document, runs[i].Paragraph, runs[i].Run));

            if (value is null)
            {
                return null;
            }

            values[i] = value;
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        var hoisted = counts.MaxBy(pair => pair.Value).Key;

        for (var i = 0; i < runs.Count; i++)
        {
            if (values[i] != hoisted)
            {
                continue;
            }

            var (paragraph, run) = runs[i];
            var saved = (run.FontId, run.FontSize);

            clear(run);

            // With a stylesheet the reader writes what the style resolves to, so a run whose style
            // still states the property keeps it.
            if (written(document, Effective(document, paragraph, run)) is not null)
            {
                restore(run, saved);
            }
        }

        return hoisted;
    }

    /// <summary>The run as the reader writes it: resolved against the stylesheet when there is one.</summary>
    /// <remarks>
    /// The reader resolves with an internal method that applies the same inheritance as the public
    /// <see cref="RtfDocument.ResolveRunFormatting"/>; only the copy it works on is shallower.
    /// </remarks>
    private static RtfRun Effective(RtfDocument document, RtfParagraph paragraph, RtfRun run) =>
        document.Styles.Count > 0 ? document.ResolveRunFormatting(paragraph, run) : run;

    /// <summary>The font family the reader writes for a run, or null when it writes none.</summary>
    private static string? Family(RtfDocument document, RtfRun run)
    {
        if (run.FontId is not { } id || document.Fonts.FirstOrDefault(font => font.Id == id) is not { } font)
        {
            return null;
        }

        return "\"" + font.Name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>The font size the reader writes for a run, in points, or null when it writes none.</summary>
    private static string? Size(RtfDocument document, RtfRun run) =>
        run.FontSize?.ToString("0.###", CultureInfo.InvariantCulture);
}
