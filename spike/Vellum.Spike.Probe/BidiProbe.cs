using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.Probe;

public static class BidiProbe
{
    /// <summary>Reference grapheme boundaries from the BCL, independent of Avalonia.</summary>
    private static SortedSet<int> GraphemeBoundaries(string text)
    {
        var set = new SortedSet<int> { 0 };
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
            set.Add(e.ElementIndex + ((string)e.Current).Length);
        return set;
    }

    private static TextLayout Build(string text, double maxWidth = 2000)
        => new(text, Typeface.Default, fontSize: 20, foreground: Brushes.Black,
            textWrapping: TextWrapping.Wrap, maxWidth: maxWidth);

    public static Report Run()
    {
        var r = new Report("Bidi and grapheme-cluster sanity check");

        foreach (var (label, text) in new[]
                 {
                     ("bidi (Hebrew + English + digits)", Scenarios.BidiText),
                     ("clusters (family ZWJ + combining acute)", Scenarios.ClusterText),
                 })
        {
            r.Section(label);
            r.Line($"Text (escaped): `{Scenarios.Escape(text)}`");
            r.Line($"UTF-16 length: {text.Length}");

            var boundaries = GraphemeBoundaries(text);
            r.Line($"Reference grapheme boundaries (StringInfo): {string.Join(",", boundaries)}");
            r.Line();

            var layout = Build(text);
            var line = layout.TextLines[0];

            r.Line($"Lines: {layout.TextLines.Count}; runs on line 0: {line.TextRuns.Count}");
            r.Table("run|length|typeface",
                line.TextRuns.Select(run =>
                    $"{run.GetType().Name}|{run.Length}|{run.Properties?.Typeface.FontFamily.Name ?? "-"}")
                    .ToArray());

            // ---- forward
            r.Sub("GetNextCaretCharacterHit — walking forward from 0");
            var forward = Walk(line, new CharacterHit(0), h => line.GetNextCaretCharacterHit(h), text.Length);
            r.Line($"Stops: {string.Join(" -> ", forward)}");
            var offForward = forward.Where(p => !boundaries.Contains(p)).ToList();
            r.Line();
            r.Check(offForward.Count == 0, "every forward caret stop is on a grapheme boundary",
                offForward.Count == 0 ? "" : "off-boundary: " + string.Join(",", offForward));

            // ---- backward
            r.Sub("GetPreviousCaretCharacterHit — walking backward from the end");
            var backward = Walk(line, new CharacterHit(text.Length),
                h => line.GetPreviousCaretCharacterHit(h), text.Length, forward: false);
            r.Line($"Stops: {string.Join(" -> ", backward)}");
            var offBack = backward.Where(p => !boundaries.Contains(p)).ToList();
            r.Line();
            r.Check(offBack.Count == 0, "every backward caret stop is on a grapheme boundary",
                offBack.Count == 0 ? "" : "off-boundary: " + string.Join(",", offBack));

            // ---- backspace
            r.Sub("GetBackspaceCaretCharacterHit — from the end");
            var backspace = Walk(line, new CharacterHit(text.Length),
                h => line.GetBackspaceCaretCharacterHit(h), text.Length, forward: false);
            r.Line($"Stops: {string.Join(" -> ", backspace)}");
            var offBs = backspace.Where(p => !boundaries.Contains(p)).ToList();
            r.Line();

            if (offBs.Count > 0)
            {
                r.Line($"Backspace stops **inside** grapheme clusters at: {string.Join(",", offBs)}.");
                r.Line();
                r.Line("Decomposing those positions:");
                foreach (var p in offBs)
                {
                    var prev = boundaries.Last(b => b < p);
                    var next = boundaries.First(b => b > p);
                    r.Line($"- {p} is inside the cluster [{prev},{next}) = "
                           + $"`{Scenarios.Escape(text[prev..next])}`; the code unit before it is "
                           + $"U+{(int)text[p - 1]:X4}");
                }
                r.Line();
                r.Line("This is deliberate: backspace peels one **component** off a ZWJ sequence or "
                       + "combining sequence, rather than deleting the whole cluster.");
            }

            var midSurrogate = backspace
                .Where(p => p > 0 && p < text.Length && char.IsLowSurrogate(text[p]))
                .ToList();

            r.Line();
            r.Check(midSurrogate.Count == 0,
                "no backspace stop ever lands **inside a surrogate pair** (never produces invalid UTF-16)",
                midSurrogate.Count == 0 ? "" : string.Join(",", midSurrogate));

            r.Check(backspace.SequenceEqual(backspace.OrderByDescending(x => x)),
                "backspace stops advance monotonically toward 0 (no oscillation)");

            if (offBs.Count > 0)
            {
                r.Line($"- NOTE — backspace is **not** whole-cluster: it stops at {offBs.Count} "
                       + "intra-cluster position(s). Arrow-key movement above is unaffected. See the "
                       + "verdict section for what this means for AC11 / §8 scenario 3.");
            }

            Host.RenderToPng($"bidi-{(text == Scenarios.BidiText ? "hebrew" : "clusters")}.png",
                (int)Math.Ceiling(layout.Width) + 20, (int)Math.Ceiling(layout.Height) + 20,
                ctx => layout.Draw(ctx, new Point(10, 10)));
        }

        // ---------------------------------------------------------------- disjoint selection rects
        r.Section("GetTextBounds across the bidi boundary");

        var bidiLayout = Build(Scenarios.BidiText);
        var bidiLine = bidiLayout.TextLines[0];
        var hebrewStart = Scenarios.BidiText.IndexOf('\u05E9');

        r.Line($"Hebrew begins at index {hebrewStart}.");
        r.Line();

        foreach (var (start, len, label) in new[]
                 {
                     (hebrewStart - 3, 8, "3 Latin chars into the Hebrew word"),
                     (hebrewStart - 2, 12, "spanning the Latin/Hebrew boundary widely"),
                     (0, Scenarios.BidiText.Length, "the whole line"),
                 })
        {
            r.Sub($"GetTextBounds({start}, {len}) — {label}");
            var bounds = bidiLine.GetTextBounds(start, len);
            r.Table("rect|flow|runs",
                bounds.Select(b =>
                    $"({b.Rectangle.X:0.##}, {b.Rectangle.Y:0.##}) {b.Rectangle.Width:0.##}x{b.Rectangle.Height:0.##}"
                    + $"|{b.FlowDirection}|"
                    + string.Join(" ", b.TextRunBounds.Select(rb => $"[{rb.TextSourceCharacterIndex}+{rb.Length}]")))
                    .ToArray());
            r.Line();
            r.Line($"Rect count: {bounds.Count}; distinct flow directions: "
                   + string.Join(",", bounds.Select(b => b.FlowDirection).Distinct()));
        }

        var crossing = bidiLine.GetTextBounds(hebrewStart - 3, 8);
        r.Line();
        r.Check(crossing.Count > 1,
            "a logically contiguous selection across the bidi boundary yields multiple rects",
            $"{crossing.Count} rects");
        r.Check(crossing.Select(b => b.FlowDirection).Distinct().Count() > 1,
            "those rects report differing FlowDirection");

        // selection visual
        Host.RenderToPng("bidi-selection.png",
            (int)Math.Ceiling(bidiLayout.Width) + 20, (int)Math.Ceiling(bidiLayout.Height) + 20,
            ctx =>
            {
                foreach (var b in crossing)
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 0, 90, 255)), null,
                        new Rect(b.Rectangle.X + 10, b.Rectangle.Y + 10,
                            b.Rectangle.Width, b.Rectangle.Height));
                }
                bidiLayout.Draw(ctx, new Point(10, 10));
            });

        r.Section("Bidi/grapheme result");
        r.Line($"Checks failed: {r.Failures}");
        return r;
    }

    private static List<int> Walk(
        TextLine line, CharacterHit start, Func<CharacterHit, CharacterHit> step, int length,
        bool forward = true)
    {
        var stops = new List<int>();
        var hit = start;

        for (var i = 0; i < length + 4; i++)
        {
            var pos = hit.FirstCharacterIndex + hit.TrailingLength;
            if (stops.Count > 0 && stops[^1] == pos) break;
            stops.Add(pos);

            if (forward && pos >= length) break;
            if (!forward && pos <= 0) break;

            hit = step(hit);
        }

        return stops;
    }
}
