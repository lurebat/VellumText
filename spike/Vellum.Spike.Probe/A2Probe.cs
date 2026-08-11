using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.Probe;

public static class A2Probe
{
    private static TextLayout Build(SpikeTextSource src, double maxWidth, TextRunCache? cache = null)
    {
        var props = new SpikeParagraphProperties(Scenarios.Default());
        return cache is null
            ? new TextLayout(src, props, TextTrimming.None, maxWidth, double.PositiveInfinity, int.MaxValue)
            : new TextLayout(src, props, TextTrimming.None, maxWidth, double.PositiveInfinity, int.MaxValue, cache);
    }

    private static string RunKinds(TextLine line)
        => string.Join(" ", line.TextRuns.Select(run => run switch
        {
            BoxRun b => $"BOX({b.Size.Width}x{b.Size.Height})",
            ImageRun => "IMG",
            TextEndOfParagraph => "EOP",
            TextEndOfLine => "EOL",
            TextCharacters tc => $"TXT({tc.Length})",
            _ => run.GetType().Name + $"({run.Length})",
        }));

    public static Report Run()
    {
        var r = new Report("A2 — `DrawableTextRun` inside a custom `ITextSource`");
        var embedIndices = Scenarios.EmbedIndices;

        r.Line();
        r.Line($"Text length: {Scenarios.EmbedText.Length}; embeds at indices "
               + $"{string.Join(", ", embedIndices)} (both U+FFFC).");

        // ---------------------------------------------------------------- 0: does it build at all
        r.Section("0. Baseline sanity — does a custom source with embeds format?");

        var (source, spans) = Scenarios.BuildEmbedSource();
        TextLayout layout;
        try
        {
            layout = Build(source, 400);
            r.Check(true, "TextLayout constructed from a custom ITextSource containing DrawableTextRuns");
        }
        catch (Exception ex)
        {
            r.Check(false, "TextLayout construction", ex.ToString());
            return r;
        }

        r.Line();
        r.Line($"Lines: {layout.TextLines.Count}, layout width {layout.Width:0.##}, height {layout.Height:0.##}");
        r.Line();
        r.Table("line|first|len|width|height|baseline|runs",
            layout.TextLines.Select((l, i) =>
                $"{i}|{l.FirstTextSourceIndex}|{l.Length}|{l.Width:0.##}|{l.Height:0.##}|{l.Baseline:0.##}|{RunKinds(l)}")
                .ToArray());

        var totalLen = layout.TextLines.Sum(l => l.Length);
        var eop = layout.TextLines.SelectMany(l => l.TextRuns).OfType<TextEndOfParagraph>().FirstOrDefault();
        r.Line();
        r.Line($"The synthesized `TextEndOfParagraph` has Length = {eop?.Length.ToString() ?? "n/a"}, so it "
               + "occupies one text-source position of its own.");
        r.Check(totalLen == Scenarios.EmbedText.Length + (eop?.Length ?? 0),
            "sum of TextLine.Length equals source text length + the paragraph terminator",
            $"{totalLen} vs {Scenarios.EmbedText.Length} + {eop?.Length ?? 0}");

        var boxRuns = layout.TextLines.SelectMany(l => l.TextRuns).OfType<BoxRun>().Count();
        r.Check(boxRuns == embedIndices.Length,
            "every embed appears exactly once across the formatted lines",
            $"found {boxRuns}, expected {embedIndices.Length}");

        // ---------------------------------------------------------------- a: line breaking
        r.Section("a. Line breaking and wrapping");

        var widths = new double[] { 180, 250, 320, 400, 550, 800, 2000 };
        r.Table("maxWidth|lines|widest line|embed line indices|any line wider than maxWidth?",
            widths.Select(w =>
            {
                var l = Build(Scenarios.BuildEmbedSource().Source, w);
                var widest = l.TextLines.Max(x => x.Width);
                var embedLines = l.TextLines
                    .Select((line, i) => (line, i))
                    .Where(t => t.line.TextRuns.OfType<BoxRun>().Any())
                    .Select(t => t.i);
                return $"{w}|{l.TextLines.Count}|{widest:0.##}|{string.Join(",", embedLines)}|{(widest > w + 0.5 ? "YES" : "no")}";
            }).ToArray());

        r.Line();
        foreach (var w in widths)
        {
            var l = Build(Scenarios.BuildEmbedSource().Source, w);
            var widest = l.TextLines.Max(x => x.Width);
            r.Check(widest <= w + 0.5, $"maxWidth={w}: no line exceeds the wrap width",
                $"widest {widest:0.##}");
        }

        r.Sub("a.2 — an embed wider than the whole wrap width");

        var wideDef = Scenarios.Default();
        const string wideText = "before " + Scenarios.OBJS + " after some trailing words here";
        var wideSource = new SpikeTextSource(wideText, wideDef, null,
            new Dictionary<int, DrawableTextRun>
            {
                [wideText.IndexOf(Scenarios.OBJ)] = new BoxRun(500, 24, 24, Colors.Tomato, "wide"),
            });

        var wideLayout = Build(wideSource, 300);
        r.Line($"Embed 500px wide, wrap width 300px → {wideLayout.TextLines.Count} lines, "
               + $"layout width {wideLayout.Width:0.##}");
        r.Table("line|first|len|width|runs",
            wideLayout.TextLines.Select((l, i) =>
                $"{i}|{l.FirstTextSourceIndex}|{l.Length}|{l.Width:0.##}|{RunKinds(l)}").ToArray());
        r.Line();
        r.Check(wideLayout.TextLines.Sum(l => l.Length) == wideText.Length + 1,
            "oversized embed does not lose or duplicate any text positions",
            $"{wideLayout.TextLines.Sum(l => l.Length)} vs {wideText.Length} + 1 (EOP)");
        r.Line($"Note: the embed's own line is {wideLayout.TextLines.Max(l => l.Width):0.##}px wide against a "
               + "300px wrap width — an unbreakable embed **overflows** rather than being clipped or scaled.");

        // ---------------------------------------------------------------- b: baseline
        r.Section("b. Baseline alignment");

        r.Sub("b.0 — a DrawableTextRun with null Properties");

        r.Line("`TextRun.Properties` is virtual and defaults to null. What happens if a "
               + "`DrawableTextRun` subclass leaves it that way?");
        r.Line();

        var nullPropsLayout = Build(Scenarios.BuildEmbedSource(alignment: null).Source, 2000);
        r.Line($"Formatting succeeds: {nullPropsLayout.TextLines.Count} line(s), "
               + $"width {nullPropsLayout.Width:0.##}. Now drawing it:");
        r.Line();

        string? drawError = null;
        try
        {
            Host.RenderToPng("a2-nullprops.png", 40, 40,
                ctx => nullPropsLayout.Draw(ctx, new Point(10, 10)));
        }
        catch (Exception ex)
        {
            drawError = $"{ex.GetType().Name}: {ex.Message}";
            var frame = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
            r.Line($"```\n{drawError}\n{frame}\n```");
        }

        r.Line();
        r.Check(drawError is not null,
            "**drawing throws when a DrawableTextRun has null Properties** — measurement succeeds, "
            + "rendering does not, so the failure surfaces only at paint time",
            drawError ?? "no exception (behaviour changed)");

        r.Sub("b.1 — BaselineAlignment matrix");

        r.Line("A 24px-tall box in 16px text. `Baseline` on the run is held at 24 while "
               + "`Properties.BaselineAlignment` varies.");
        r.Line();

        var alignRows = new List<string>();
        foreach (var alignment in Enum.GetValues<BaselineAlignment>())
        {
            var (src, _) = Scenarios.BuildEmbedSource(alignment: alignment);
            TextLayout al;
            try
            {
                al = Build(src, 2000);
            }
            catch (Exception ex)
            {
                alignRows.Add($"{alignment}|format threw: {ex.GetType().Name}|-|-|-|-");
                continue;
            }

            var ln = al.TextLines[0];
            Point? origin = null;
            string? err = null;
            try
            {
                Host.RenderToPng($"a2-align-{alignment}.png",
                    (int)Math.Ceiling(al.Width) + 20, (int)Math.Ceiling(al.Height) + 20, ctx =>
                    {
                        al.Draw(ctx, new Point(10, 10));
                        var y = 10 + ln.Baseline;
                        ctx.DrawLine(new Pen(Brushes.Red, 1), new Point(0, y), new Point(al.Width + 20, y));
                    });
                origin = ln.TextRuns.OfType<BoxRun>().FirstOrDefault()?.LastDrawOrigin;
            }
            catch (Exception ex)
            {
                err = ex.GetType().Name;
            }

            alignRows.Add(
                $"{alignment}|{ln.Height:0.##}|{ln.Baseline:0.##}|{ln.Extent:0.##}|"
                + $"{ln.OverhangAfter:0.##}|"
                + (err is not null ? $"draw threw: {err}"
                    : origin is null ? "n/a" : $"({origin.Value.X:0.##}, {origin.Value.Y:0.##})"));
        }

        r.Table("BaselineAlignment|line Height|line Baseline|Extent|OverhangAfter|Draw origin",
            alignRows.ToArray());

        r.Sub("b.2 — varying DrawableTextRun.Baseline with BaselineAlignment.Baseline");

        var baselineRows = new List<string>();
        foreach (var baseline in new double[] { 24, 18, 12, 0, -6 })
        {
            var (src, _) = Scenarios.BuildEmbedSource(boxBaseline: baseline);
            var bl = Build(src, 2000);
            var line0 = bl.TextLines[0];

            Host.RenderToPng($"a2-baseline-{baseline:0}.png",
                (int)Math.Ceiling(bl.Width) + 20, (int)Math.Ceiling(bl.Height) + 30, ctx =>
                {
                    bl.Draw(ctx, new Point(10, 10));
                    var y = 10 + bl.TextLines[0].Baseline;
                    ctx.DrawLine(new Pen(Brushes.Red, 1), new Point(0, y), new Point(bl.Width + 20, y));
                });

            var origin = line0.TextRuns.OfType<BoxRun>().FirstOrDefault()?.LastDrawOrigin;
            baselineRows.Add(
                $"{baseline}|{line0.Height:0.##}|{line0.Baseline:0.##}|{line0.Extent:0.##}|"
                + $"{line0.OverhangAfter:0.##}|"
                + $"{(origin is null ? "n/a" : $"({origin.Value.X:0.##}, {origin.Value.Y:0.##})")}");
        }

        r.Table("run Baseline|line Height|line Baseline|Extent|OverhangAfter|Draw origin",
            baselineRows.ToArray());

        r.Sub("b.3 — what does the Draw origin actually mean?");

        r.Line("The same layout drawn at two different origins, to separate a constant offset from "
               + "an absolute quirk. `rel` is the draw origin minus the origin passed to `Draw`.");
        r.Line();

        var originRows = new List<string>();
        foreach (var alignment in new[]
                 {
                     BaselineAlignment.Baseline, BaselineAlignment.Top,
                     BaselineAlignment.Center, BaselineAlignment.Bottom,
                 })
        {
            var offsets = new List<double>();
            foreach (var dy in new double[] { 10, 60 })
            {
                var (s, _) = Scenarios.BuildEmbedSource(alignment: alignment);
                var l = Build(s, 2000);
                Host.RenderToPng($"a2-origin-{alignment}-{dy:0}.png",
                    (int)Math.Ceiling(l.Width) + 20, (int)Math.Ceiling(l.Height) + 80,
                    ctx => l.Draw(ctx, new Point(10, dy)));
                var o = l.TextLines[0].TextRuns.OfType<BoxRun>().First().LastDrawOrigin!.Value;
                offsets.Add(o.Y - dy);
            }

            var ln0 = Build(Scenarios.BuildEmbedSource(alignment: alignment).Source, 2000).TextLines[0];
            var expected = alignment switch
            {
                BaselineAlignment.Baseline => ln0.Baseline - 24,
                BaselineAlignment.Top => 0,
                BaselineAlignment.Center => (ln0.Height - 24) / 2,
                _ => ln0.Height - 24,
            };

            originRows.Add(
                $"{alignment}|{offsets[0]:0.###}|{offsets[1]:0.###}|"
                + $"{(Math.Abs(offsets[0] - offsets[1]) < 0.01 ? "yes" : "NO")}|{expected:0.###}");
        }

        r.Table("alignment|rel @ origin.Y=10|rel @ origin.Y=60|constant?|predicted",
            originRows.ToArray());

        r.Line();
        r.Line("With `BaselineAlignment.Baseline`, the run is drawn at "
               + "`origin.Y + line.Baseline - run.Baseline`, i.e. **`Draw`'s origin is the top-left of the "
               + "run's box, and `DrawableTextRun.Baseline` is measured downward from that top edge**. "
               + "Painting the content downward from the origin (as `BoxRun` does) is therefore correct.");

        var plainLine = new TextLayout(
            "no embed here", Typeface.Default, fontSize: 16, foreground: Brushes.Black).TextLines[0];
        r.Line();
        r.Line($"For reference, a plain 16px line without any embed: Height={plainLine.Height:0.##}, "
               + $"Baseline={plainLine.Baseline:0.##}");

        var withEmbed = Build(Scenarios.BuildEmbedSource(boxBaseline: 40, boxHeight: 40).Source, 2000);
        r.Line();
        r.Check(withEmbed.TextLines[0].Height > plainLine.Height,
            "a 40px-tall embed makes its line taller than a plain text line",
            $"{withEmbed.TextLines[0].Height:0.##} vs {plainLine.Height:0.##}");

        // ---------------------------------------------------------------- c: GetTextBounds
        r.Section("c. `GetTextBounds` across the embed");

        var (cSrc, _) = Scenarios.BuildEmbedSource();
        var cLayout = Build(cSrc, 2000);   // single line keeps the arithmetic legible
        var e0 = embedIndices[0];
        var cLine = cLayout.TextLines.First(l =>
            e0 >= l.FirstTextSourceIndex && e0 < l.FirstTextSourceIndex + l.Length);

        r.Line($"Embed at index {e0}; probing on line starting at {cLine.FirstTextSourceIndex} "
               + $"(length {cLine.Length}).");

        foreach (var (start, len, label) in new[]
                 {
                     (e0 - 3, 3, "3 chars ending just before the embed"),
                     (e0, 1, "exactly the embed"),
                     (e0 - 3, 4, "3 chars + the embed"),
                     (e0, 4, "the embed + 3 following chars"),
                     (e0 - 5, 11, "spanning the embed on both sides"),
                 })
        {
            r.Sub($"c — {label}: GetTextBounds({start}, {len})");
            var bounds = cLine.GetTextBounds(start, len);
            r.Table("rect|flow|run bounds",
                bounds.Select(b =>
                    $"{Fmt(b.Rectangle)}|{b.FlowDirection}|"
                    + string.Join(" ", b.TextRunBounds.Select(rb =>
                        $"[idx {rb.TextSourceCharacterIndex} len {rb.Length} {Fmt(rb.Rectangle)}]"))).ToArray());

            var covered = bounds.SelectMany(b => b.TextRunBounds).Sum(rb => rb.Length);
            r.Line();
            r.Check(covered == len, "TextRunBounds lengths sum to the requested length",
                $"{covered} vs {len}");
            r.Check(bounds.All(b => b.Rectangle.Width > 0), "all returned rects are non-empty");
        }

        r.Sub("c.2 — is the embed's advance counted exactly once?");
        var beforeOnly = cLine.GetTextBounds(e0 - 3, 3).Sum(b => b.Rectangle.Width);
        var withBox = cLine.GetTextBounds(e0 - 3, 4).Sum(b => b.Rectangle.Width);
        var boxWidth = cLine.TextRuns.OfType<BoxRun>().First().Size.Width;
        r.Line($"width(3 chars) = {beforeOnly:0.##}, width(3 chars + embed) = {withBox:0.##}, "
               + $"delta = {withBox - beforeOnly:0.##}, embed Size.Width = {boxWidth:0.##}");
        r.Check(Math.Abs((withBox - beforeOnly) - boxWidth) < 0.5,
            "adding the embed to the range grows the selection by exactly the embed's width");

        // ---------------------------------------------------------------- d: hit testing
        r.Section("d. `HitTestPoint` vs the U+FFFC placeholder index");

        var embedRect = cLayout.HitTestTextPosition(e0);
        r.Line($"HitTestTextPosition({e0}) = {Fmt(embedRect)} (embed Size = {boxWidth} x "
               + $"{cLine.TextRuns.OfType<BoxRun>().First().Size.Height})");
        r.Check(Math.Abs(embedRect.Width - boxWidth) < 0.5,
            "the caret rect at the placeholder index is the embed's width",
            $"{embedRect.Width:0.##} vs {boxWidth:0.##}");

        r.Line();
        r.Line("Sampling x across the embed box at 1px steps (y = box centre):");
        r.Line();

        var y0 = embedRect.Center.Y;
        var transitions = new List<string>();
        (int pos, bool trailing)? previous = null;
        for (var x = embedRect.Left - 3; x <= embedRect.Right + 3; x += 1.0)
        {
            var hit = cLayout.HitTestPoint(new Point(x, y0));
            var current = (hit.TextPosition, hit.IsTrailing);
            if (previous is null || previous.Value != current)
            {
                transitions.Add($"{x:0.##}|{hit.TextPosition}|{hit.IsTrailing}|{hit.IsInside}");
                previous = current;
            }
        }

        r.Table("x|TextPosition|IsTrailing|IsInside", transitions.ToArray());

        var insideHits = new List<TextHitTestResult>();
        for (var x = embedRect.Left + 0.5; x < embedRect.Right; x += 1.0)
            insideHits.Add(cLayout.HitTestPoint(new Point(x, y0)));

        var distinct = insideHits.Select(h => h.TextPosition).Distinct().OrderBy(p => p).ToList();
        r.Line();
        r.Check(distinct.SequenceEqual(new[] { e0, e0 + 1 }),
            $"every point inside the embed maps to one of the two caret positions bracketing the "
            + $"placeholder ({e0} or {e0 + 1}) and nothing else",
            $"distinct: {string.Join(",", distinct)}");

        var firstAfter = insideHits.FindIndex(h => h.TextPosition == e0 + 1);
        var threshold = embedRect.Left + 0.5 + firstAfter;
        r.Check(Math.Abs(threshold - embedRect.Center.X) <= 1.5,
            "the switch from 'before the embed' to 'after the embed' happens at the embed's midpoint",
            $"switch at x={threshold:0.##}, midpoint {embedRect.Center.X:0.##}");

        r.Sub("d.2 — control: the same sweep over an ordinary 1-char run");

        var plainIndex = e0 - 1;                          // the character just before the embed
        var plainRect = cLayout.HitTestTextPosition(plainIndex);
        var plainHits = new List<TextHitTestResult>();
        for (var x = plainRect.Left + 0.1; x < plainRect.Right; x += 0.5)
            plainHits.Add(cLayout.HitTestPoint(new Point(x, y0)));

        var plainDistinct = plainHits.Select(h => h.TextPosition).Distinct().OrderBy(p => p).ToList();
        r.Line($"Ordinary char at index {plainIndex}: rect {Fmt(plainRect)}, "
               + $"positions inside: {string.Join(",", plainDistinct)}, "
               + $"IsTrailing values: {string.Join(",", plainHits.Select(h => h.IsTrailing).Distinct())}");
        r.Line();
        r.Check(plainDistinct.SequenceEqual(new[] { plainIndex, plainIndex + 1 }),
            "an ordinary character shows the identical two-position pattern — **the embed is hit-tested "
            + "exactly like a one-character run**");
        r.Line();
        r.Line("Note: `IsTrailing` is False across both sweeps. `TextHitTestResult.TextPosition` already "
               + "has the trailing length folded in, so an editor should use `TextPosition` directly and "
               + "not try to add `IsTrailing` to it.");

        // ---------------------------------------------------------------- e: caret movement
        r.Section("e. Caret movement over the embed");

        var hits = new List<string>();
        var hit2 = new CharacterHit(e0 - 2);
        for (var i = 0; i < 6; i++)
        {
            hits.Add($"{hit2.FirstCharacterIndex}+{hit2.TrailingLength}");
            hit2 = cLine.GetNextCaretCharacterHit(hit2);
        }
        r.Line($"GetNextCaretCharacterHit from {e0 - 2} (embed at {e0}): {string.Join(" -> ", hits)}");

        hits.Clear();
        hit2 = new CharacterHit(e0 + 2);
        for (var i = 0; i < 6; i++)
        {
            hits.Add($"{hit2.FirstCharacterIndex}+{hit2.TrailingLength}");
            hit2 = cLine.GetPreviousCaretCharacterHit(hit2);
        }
        r.Line($"GetPreviousCaretCharacterHit from {e0 + 2}: {string.Join(" -> ", hits)}");

        hits.Clear();
        hit2 = new CharacterHit(e0 + 1);
        for (var i = 0; i < 4; i++)
        {
            hits.Add($"{hit2.FirstCharacterIndex}+{hit2.TrailingLength}");
            hit2 = cLine.GetBackspaceCaretCharacterHit(hit2);
        }
        r.Line($"GetBackspaceCaretCharacterHit from {e0 + 1}: {string.Join(" -> ", hits)}");

        var fwd = cLine.GetNextCaretCharacterHit(new CharacterHit(e0));
        var back = cLine.GetPreviousCaretCharacterHit(new CharacterHit(e0 + 1));
        r.Line();
        r.Check(fwd.FirstCharacterIndex + fwd.TrailingLength == e0 + 1,
            "one forward caret step from the embed lands exactly one position later",
            $"{fwd.FirstCharacterIndex}+{fwd.TrailingLength}");
        r.Check(back.FirstCharacterIndex + back.TrailingLength == e0,
            "one backward caret step from just after the embed lands on the placeholder",
            $"{back.FirstCharacterIndex}+{back.TrailingLength}");

        // ---------------------------------------------------------------- f: real image embed
        r.Section("f. A real bitmap embed (AC4 shape)");

        const string imgText = "An image " + Scenarios.OBJS + " inline, wrapped in ordinary text that continues.";
        var imgSource = new SpikeTextSource(imgText, Scenarios.Default(), null,
            new Dictionary<int, DrawableTextRun>
            {
                [imgText.IndexOf(Scenarios.OBJ)] = new ImageRun(ImageRun.CreateCheckerboard(), 28, 22),
            });

        var imgLayout = Build(imgSource, 360);
        r.Line($"Lines: {imgLayout.TextLines.Count}; ImageRun present: "
               + imgLayout.TextLines.SelectMany(l => l.TextRuns).OfType<ImageRun>().Any());
        Host.RenderToPng("a2-image-embed.png",
            (int)Math.Ceiling(imgLayout.Width) + 20, (int)Math.Ceiling(imgLayout.Height) + 20,
            ctx => imgLayout.Draw(ctx, new Point(10, 10)));
        r.Check(imgLayout.TextLines.SelectMany(l => l.TextRuns).OfType<ImageRun>().Count() == 1,
            "a bitmap-drawing DrawableTextRun formats and renders inline");

        // wrapped-with-embeds screenshot for the record
        var visual = Build(Scenarios.BuildEmbedSource().Source, 320);
        Host.RenderToPng("a2-wrapped.png",
            (int)Math.Ceiling(visual.Width) + 20, (int)Math.Ceiling(visual.Height) + 20,
            ctx => visual.Draw(ctx, new Point(10, 10)));

        r.Section("A2 result");
        r.Line($"Checks failed: {r.Failures}");
        return r;
    }

    private static string Fmt(Rect rect)
        => $"({rect.X:0.##}, {rect.Y:0.##}) {rect.Width:0.##}x{rect.Height:0.##}";
}
