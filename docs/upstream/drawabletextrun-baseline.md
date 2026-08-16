# Upstream draft 1 — bug report

**Status: DRAFT. Not filed.** Avalonia's `CONTRIBUTING.md` has an explicit AI Guidelines section
telling autonomous agents not to open issues or PRs. A human must read, own and submit this, and
answer review comments afterwards.

**Where:** https://github.com/AvaloniaUI/Avalonia/issues/new (Bug Report form)

**Searched before drafting (2026-08-16):** no existing report. Nearest is
[#21792](https://github.com/AvaloniaUI/Avalonia/issues/21792) — same area, different defect (a
text-less `DrawableTextRun` flipping an RTL paragraph's embedding level).
[#17403](https://github.com/AvaloniaUI/Avalonia/issues/17403) is about `BaselineAlignment` being
applied *wrongly*, not about a crash.

---

## Title

`TextLayout.Draw` throws `ArgumentOutOfRangeException` for a `DrawableTextRun` that does not override `Properties`

## Describe the bug

`TextRun.Properties` is declared nullable and defaults to null:

```csharp
// src/Avalonia.Base/Media/TextFormatting/TextRun.cs:27
public virtual TextRunProperties? Properties => null;
```

So a `DrawableTextRun` subclass that does not override it is legal. Such a run **measures and
formats successfully** — `TextLayout` reports sensible width, height and line count, and the run
participates in line breaking — and then throws on the render path:

```
System.ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'baselineAlignment')
   at Avalonia.Media.TextFormatting.TextLineImpl.GetBaselineOffset(TextLine textLine, DrawableTextRun textRun)
   at Avalonia.Media.TextFormatting.TextLineImpl.Draw(DrawingContext drawingContext, Point lineOrigin)
   at Avalonia.Media.TextFormatting.TextLayout.Draw(DrawingContext context, Point origin)
```

The cause is in `TextLineImpl.GetBaselineOffset`:

```csharp
// src/Avalonia.Base/Media/TextFormatting/TextLineImpl.cs:125-154
var baselineAlignment = textRun.Properties?.BaselineAlignment;   // null when Properties is null

switch (baselineAlignment)
{
    case BaselineAlignment.Baseline: ...
    // ... no `case null:` ...
    default:
        throw new ArgumentOutOfRangeException(nameof(baselineAlignment), baselineAlignment, null);
}
```

The null-conditional `?.` on line 128 shows the null case was anticipated, but the switch has no
`null` arm, so the anticipated value falls through to `default` and throws. The exception also
names a parameter (`baselineAlignment`) that the caller never passed, which makes it hard to trace
back to the real cause — an unset `Properties` on a custom run.

Two things make this worse than an ordinary argument check:

1. **It fails late.** Measure and arrange both succeed, so the mistake surfaces as a render-time
   crash, potentially far from the code that created the run.
2. **The API contract suggests it should work.** `Properties` is nullable with a null default, so
   not overriding it looks deliberate rather than an omission.

## To Reproduce

Complete, self-contained repro. .NET 10, `Avalonia` + `Avalonia.Headless` + `Avalonia.Skia`
12.1.1.

```csharp
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

// A DrawableTextRun that never sets Properties, which defaults to null.
internal sealed class Box : DrawableTextRun
{
    public override Size Size => new(20, 20);
    public override double Baseline => -20;
    public override void Draw(DrawingContext ctx, Point origin) =>
        ctx.FillRectangle(Brushes.Red, new Rect(origin.X, origin.Y - 20, 20, 20));
}

internal sealed class Source : ITextSource
{
    private readonly TextRunProperties _props = new GenericTextRunProperties(
        new Typeface("Arial"), fontRenderingEmSize: 14, foregroundBrush: Brushes.Black);

    public TextRun? GetTextRun(int index) => index switch
    {
        0 => new TextCharacters("ab\uFFFC".AsMemory(0, 2), _props),
        2 => new Box(),
        3 => new TextCharacters("cd".AsMemory(), _props),
        _ => null,
    };
}

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var para = new GenericTextParagraphProperties(
            new GenericTextRunProperties(new Typeface("Arial"), 14, foregroundBrush: Brushes.Black));

        var layout = new TextLayout(new Source(), para);
        Console.WriteLine($"formats fine: lines={layout.TextLines.Count} " +
                          $"width={layout.Width:F1} height={layout.Height:F1}");

        var bmp = new RenderTargetBitmap(new PixelSize(200, 60), new Vector(96, 96));
        using var ctx = bmp.CreateDrawingContext();
        layout.Draw(ctx, new Point(0, 0));   // throws here
        return 0;
    }
}
```

Output:

```
formats fine: lines=1 width=50.4 height=53.1
Unhandled exception. System.ArgumentOutOfRangeException: ... (Parameter 'baselineAlignment')
   at Avalonia.Media.TextFormatting.TextLineImpl.GetBaselineOffset(...)
```

## Expected behaviour

A `DrawableTextRun` with no `Properties` should draw, treating the alignment as
`BaselineAlignment.Baseline` — the same default `GenericTextRunProperties` uses. Failing that, the
error should be raised when the run is *formatted*, and should say that `Properties` is null rather
than naming a parameter the caller did not supply.

## Suggested fix

One line in `TextLineImpl.GetBaselineOffset`:

```csharp
case null:
case BaselineAlignment.Baseline:
    baselineOffset += textLine.Baseline;
    break;
```

## Workaround

Override `Properties` on the run with any non-null `TextRunProperties`. Verified: with the override
in place the same repro draws without error.

## Environment

- Avalonia 12.1.1
- .NET 10, Windows 11 x64
- Reproduced with the Skia backend under `Avalonia.Headless`
  (`UseHeadlessDrawing = false`); the throw is in `Avalonia.Base` and is backend-independent.
