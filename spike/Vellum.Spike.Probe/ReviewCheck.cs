using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Avalonia;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Vellum.Spike.Probe;

/// <summary>
/// Independent verification of three adversarial-review findings that would change what
/// FINDINGS.md claims. Each is checked by running it, not by reading the reviewer's summary.
/// </summary>
public static class ReviewCheck
{
    public static Report Run()
    {
        var r = new Report("Adversarial-review verification — three challenged claims re-tested");

        CheckDrawingBrushLoophole(r);
        CheckIndicOracle(r);
        CheckBackspaceDanglingZwj(r);

        return r;
    }

    // ---------------------------------------------------------------- 1. C1 brush loophole
    private static void CheckDrawingBrushLoophole(Report r)
    {
        r.Section("1. C1 — can a DrawingBrush render real content on the textStyleOverrides path?");
        r.Line();
        r.Line("FINDINGS.md said the string path can draw nothing but a tofu glyph. A `DrawingBrush` "
               + "is renderable content, and `BackgroundBrush` is painted across the run's bounds, "
               + "so test whether arbitrary drawing reaches the surface.");

        var drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(new Rect(0, 0, 10, 10)),
            Brush = Brushes.Red,
        };
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.None, Stretch = Stretch.Fill };

        const string text = "A\uFFFCB";
        var typeface = new Typeface("Segoe UI");
        var overrides = new[]
        {
            new ValueSpan<TextRunProperties>(1, 1, new GenericTextRunProperties(
                typeface, fontRenderingEmSize: 48,
                foregroundBrush: Brushes.Transparent,
                backgroundBrush: brush)),
        };

        using var layout = new TextLayout(text, typeface, 24, Brushes.Black,
            textStyleOverrides: overrides);

        var bounds = layout.TextLines[0].GetTextBounds(1, 1);
        var rect = bounds[0].Rectangle;
        r.Line();
        r.Line($"- U+FFFC run rect with a DrawingBrush background: `{rect}`");

        // Render and look for red pixels, which can only come from the DrawingBrush.
        var size = new PixelSize(Math.Max(1, (int)layout.Width + 20), Math.Max(1, (int)layout.Height + 20));
        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.White, new Rect(0, 0, size.Width, size.Height));
            layout.Draw(ctx, new Point(4, 4));
        }

        var path = Path.Combine(Host.ResultsDir, "review-c1-drawingbrush.png");
        rtb.Save(path);

        var hasRed = HasNonGreyPixel(rtb);
        r.Line($"- rendered to `{Path.GetFileName(path)}`; contains non-greyscale (brush-painted) pixels: **{hasRed}**");
        r.Line();
        r.Check(hasRed,
            "CONFIRMED: a DrawingBrush on BackgroundBrush does paint real content — the "
            + "'can draw nothing but tofu' wording was too strong",
            "not reproduced: the DrawingBrush produced no visible painted pixels");
    }

    private static bool HasNonGreyPixel(Avalonia.Media.Imaging.RenderTargetBitmap rtb)
    {
        var w = rtb.PixelSize.Width;
        var h = rtb.PixelSize.Height;
        var length = w * h * 4;
        var buffer = new byte[length];
        var ptr = Marshal.AllocHGlobal(length);
        try
        {
            rtb.CopyPixels(new PixelRect(0, 0, w, h), ptr, length, w * 4);
            Marshal.Copy(ptr, buffer, 0, length);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        for (var i = 0; i < buffer.Length; i += 4)
        {
            var b = buffer[i]; var g = buffer[i + 1]; var rr = buffer[i + 2];
            if (Math.Abs(rr - g) > 40 || Math.Abs(g - b) > 40) return true;
        }
        return false;
    }

    // ---------------------------------------------------------- 2. StringInfo as an oracle
    private static void CheckIndicOracle(Report r)
    {
        r.Section("2. bidi — is StringInfo a sound oracle for Avalonia's cluster boundaries?");
        r.Line();
        r.Line("The bidi probe asserts every Avalonia caret stop is a StringInfo boundary — a "
               + "*subset* test. If StringInfo permits boundaries Avalonia correctly refuses, the "
               + "assertion can pass while hiding a real regression.");

        const string indic = "\u0915\u094D\u0937"; // क्ष — Devanagari conjunct, UAX #29 GB9c
        var siStops = StringInfoBoundaries(indic);
        var avStops = AvaloniaCaretStops(indic);

        r.Line();
        r.Line($"- text: `क्ष` (U+0915 U+094D U+0937), length {indic.Length}");
        r.Line($"- StringInfo boundaries : {{{string.Join(",", siStops)}}}");
        r.Line($"- Avalonia caret stops  : {{{string.Join(",", avStops)}}}");

        var subsetHolds = avStops.All(siStops.Contains);
        var setsDiffer = !siStops.SequenceEqual(avStops);

        r.Line();
        r.Line($"- Avalonia stops ⊆ StringInfo stops (what the probe asserts): **{subsetHolds}**");
        r.Line($"- the two oracles actually disagree: **{setsDiffer}**");
        r.Line();
        r.Check(subsetHolds && setsDiffer,
            "CONFIRMED: the subset assertion passes even though the oracles disagree — StringInfo "
            + "admits a boundary Avalonia refuses, so the check is one-way and can false-pass",
            "not reproduced: StringInfo and Avalonia agree on this text, so the oracle concern is unproven here");
    }

    private static List<int> StringInfoBoundaries(string s)
    {
        var stops = new List<int> { 0 };
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext())
        {
            var end = e.ElementIndex + ((string)e.Current).Length;
            if (!stops.Contains(end)) stops.Add(end);
        }
        stops.Sort();
        return stops;
    }

    private static List<int> AvaloniaCaretStops(string s)
    {
        using var layout = new TextLayout(s, new Typeface("Segoe UI"), 24, Brushes.Black);
        var line = layout.TextLines[0];
        var stops = new List<int> { 0 };
        var hit = new CharacterHit(0);
        for (var guard = 0; guard < 50; guard++)
        {
            var next = line.GetNextCaretCharacterHit(hit);
            var pos = next.FirstCharacterIndex + next.TrailingLength;
            if (pos <= stops[^1] || pos > s.Length) break;
            stops.Add(pos);
            hit = next;
            if (pos == s.Length) break;
        }
        return stops;
    }

    // ------------------------------------------------- 3. backspace leaves a dangling ZWJ?
    private static void CheckBackspaceDanglingZwj(Report r)
    {
        r.Section("3. backspace — does component-peel leave dangling ZWJs mid-sequence?");
        r.Line();
        r.Line("The user has chosen component-peel. The probe logged caret *positions* but never "
               + "applied the deletion, so this checks what the string actually becomes.");

        const string text = "Family \U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 done";
        using var layout = new TextLayout(text, new Typeface("Segoe UI"), 24, Brushes.Black);
        var line = layout.TextLines[0];

        r.Line();
        r.Line("| from | backspace target | deleted | resulting emoji region | ends with ZWJ? |");
        r.Line("|---|---|---|---|---|");

        var dangling = 0;
        var pos = 18; // end of the family sequence
        for (var step = 0; step < 5 && pos > 7; step++)
        {
            var hit = line.GetBackspaceCaretCharacterHit(new CharacterHit(pos));
            var target = hit.FirstCharacterIndex + hit.TrailingLength;
            if (target >= pos) break;

            var result = text.Remove(target, pos - target);
            var region = result.Substring(7, Math.Max(0, target - 7));
            var endsZwj = region.Length > 0 && region[^1] == '\u200D';
            if (endsZwj) dangling++;

            r.Line($"| {pos} | {target} | `{Escape(text.Substring(target, pos - target))}` "
                   + $"| `{Escape(region)}` | {(endsZwj ? "**yes**" : "no")} |");
            pos = target;
        }

        r.Line();
        r.Line($"- deletion steps that left a trailing ZWJ: **{dangling}**");
        r.Line();
        r.Check(dangling > 0,
            $"CONFIRMED: component-peel produces {dangling} intermediate state(s) ending in a "
            + "dangling ZWJ — an editor must decide whether to absorb the ZWJ in the same keystroke",
            "not reproduced: no deletion step left a dangling ZWJ");
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            sb.Append(c == '\u200D' ? "<ZWJ>" : char.IsSurrogate(c) ? $"\\u{(int)c:X4}" : c.ToString());
        return sb.ToString();
    }
}
