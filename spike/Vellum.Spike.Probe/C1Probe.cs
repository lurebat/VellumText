using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.Probe;

public static class C1Probe
{
    public static Report Run()
    {
        var r = new Report("C1 — the `textStyleOverrides` shortcut");

        // -------------------------------------------------- 1: does character formatting work?
        r.Section("1. Character formatting via textStyleOverrides");

        var overrides = Scenarios.StyleOverrides(Scenarios.StyledText);
        var layout = new TextLayout(
            Scenarios.StyledText,
            Typeface.Default,
            fontSize: 16,
            foreground: Brushes.Black,
            textWrapping: TextWrapping.Wrap,
            maxWidth: 600,
            textStyleOverrides: overrides);

        r.Line($"Text: \"{Scenarios.StyledText}\"");
        r.Line($"Overrides: {overrides.Count} spans; lines: {layout.TextLines.Count}; "
               + $"width {layout.Width:0.##}, height {layout.Height:0.##}");
        r.Line();

        var line = layout.TextLines[0];
        r.Table("run|type|length|typeface|size|foreground",
            line.TextRuns.Select(run =>
            {
                var p = run.Properties;
                return $"{run.GetType().Name}|{run.GetType().BaseType?.Name}|{run.Length}|"
                       + $"{(p is null ? "-" : $"{p.Typeface.Weight}/{p.Typeface.Style}")}|"
                       + $"{(p is null ? "-" : p.FontRenderingEmSize.ToString("0.##"))}|"
                       + $"{(p?.ForegroundBrush as ISolidColorBrush)?.Color.ToString() ?? "-"}";
            }).ToArray());

        var distinctSizes = line.TextRuns
            .Where(x => x.Properties is not null)
            .Select(x => x.Properties!.FontRenderingEmSize).Distinct().ToList();
        var distinctWeights = line.TextRuns
            .Where(x => x.Properties is not null)
            .Select(x => x.Properties!.Typeface.Weight).Distinct().ToList();

        r.Line();
        r.Check(distinctSizes.Count > 1, "textStyleOverrides produced runs with differing font sizes",
            string.Join(",", distinctSizes));
        r.Check(distinctWeights.Count > 1, "textStyleOverrides produced runs with differing weights",
            string.Join(",", distinctWeights));
        r.Check(line.TextRuns.Count > 1, "the string was split into multiple styled runs",
            $"{line.TextRuns.Count} runs");

        Host.RenderToPng("c1-styled.png",
            (int)Math.Ceiling(layout.Width) + 20, (int)Math.Ceiling(layout.Height) + 20,
            ctx => layout.Draw(ctx, new Point(10, 10)));

        // -------------------------------------------------- 2: can it express an embed?
        r.Section("2. Can this path express an embed?");

        r.Sub("2a — the constructor's own surface");

        var ctors = typeof(TextLayout).GetConstructors();
        foreach (var c in ctors)
        {
            var ps = c.GetParameters();
            r.Line($"- `TextLayout({string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name))})`");
        }

        var stringCtors = ctors.Where(c => c.GetParameters()[0].ParameterType == typeof(string)).ToList();
        r.Line();
        r.Check(stringCtors.Count > 0, "a string-based constructor exists (the C1 path)");
        r.Check(stringCtors.All(c => c.GetParameters().All(p =>
                    !typeof(TextRun).IsAssignableFrom(p.ParameterType) &&
                    !typeof(ITextSource).IsAssignableFrom(p.ParameterType))),
            "**no string-based constructor accepts a TextRun or ITextSource** — content is a string, "
            + "so no DrawableTextRun can ever be injected");

        r.Sub("2b — the TextRunProperties surface");

        r.Line("Every public member of `TextRunProperties`, the only thing an override can carry:");
        r.Line();
        foreach (var m in typeof(TextRunProperties)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(p => p.Name))
        {
            r.Line($"- `{m.PropertyType.Name} {m.Name}`");
        }

        var drawableCarrying = typeof(TextRunProperties)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(DrawableTextRun).IsAssignableFrom(p.PropertyType)
                        || typeof(IImage).IsAssignableFrom(p.PropertyType)
                        || p.PropertyType == typeof(Action<DrawingContext, Point>))
            .ToList();

        r.Line();
        r.Check(drawableCarrying.Count == 0,
            "**no TextRunProperties member can carry drawn content** (no IImage, no DrawableTextRun, "
            + "no draw callback) — formatting only");

        r.Sub("2c — what U+FFFC actually renders as on this path");

        var objOverrides = Scenarios.StyleOverrides(Scenarios.StyledTextWithObj);
        var objLayout = new TextLayout(
            Scenarios.StyledTextWithObj, Typeface.Default, fontSize: 16, foreground: Brushes.Black,
            textWrapping: TextWrapping.Wrap, maxWidth: 600, textStyleOverrides: objOverrides);

        var objIndex = Scenarios.StyledTextWithObj.IndexOf(Scenarios.OBJ);
        var objRect = objLayout.HitTestTextPosition(objIndex);

        r.Line($"String contains U+FFFC at index {objIndex}. Its rect is "
               + $"({objRect.X:0.##}, {objRect.Y:0.##}) {objRect.Width:0.##}x{objRect.Height:0.##}.");
        r.Line();
        r.Line("It is shaped as an ordinary character by whatever font the fallback chain picks — "
               + "it occupies a glyph advance, not an embed box, and nothing draws there but a glyph.");
        r.Line();
        var drawables = objLayout.TextLines.SelectMany(l => l.TextRuns).OfType<DrawableTextRun>().ToList();
        var foreign = drawables.Where(d => d.GetType().Assembly != typeof(TextLayout).Assembly).ToList();

        r.Line($"Runs on this layout that are `DrawableTextRun`s: {drawables.Count} — "
               + $"{string.Join(", ", drawables.Select(d => d.GetType().Name).Distinct())}.");
        r.Line();
        r.Line("Note: Avalonia's own `ShapedTextRun` **derives from `DrawableTextRun`**, so the base type "
               + "is not exotic — ordinary shaped text uses it too. What matters is whether *our* run type "
               + "can ever appear here.");
        r.Line();
        r.Check(foreign.Count == 0,
            "no run from outside Avalonia's own assembly appears in the C1 layout — the placeholder stays "
            + "a glyph and there is no injection point for a custom embed",
            foreign.Count == 0 ? "" : string.Join(",", foreign.Select(f => f.GetType().Name)));

        Host.RenderToPng("c1-objectreplacement.png",
            (int)Math.Ceiling(objLayout.Width) + 20, (int)Math.Ceiling(objLayout.Height) + 20,
            ctx =>
            {
                objLayout.Draw(ctx, new Point(10, 10));
                // outline where the U+FFFC glyph landed
                ctx.DrawRectangle(null, new Pen(Brushes.Red, 1),
                    new Rect(objRect.X + 10, objRect.Y + 10, Math.Max(objRect.Width, 1), objRect.Height));
            });

        r.Section("C1 result");
        r.Line($"Checks failed: {r.Failures}");
        return r;
    }
}
