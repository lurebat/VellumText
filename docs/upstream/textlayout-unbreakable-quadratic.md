# TextLayout is quadratic in paragraph length for unbreakable text

**Component:** `Avalonia.Media.TextFormatting`
**Version:** 12.1.1
**Status:** draft, not filed. See [`../architecture.md`](../architecture.md) §10.5 — Avalonia's
`CONTRIBUTING.md` asks autonomous agents not to open issues.

## Summary

With `TextWrapping.Wrap`, laying out a single paragraph containing one long unbreakable run — no
spaces, no break opportunities — costs time quadratic in the length of that run. A 600 KB paste of
such text is not slow but effectively hung, on the UI thread, at low CPU.

It affects both the convenience constructor and a custom `ITextSource`, so it is in the formatter's
line-breaking rather than in either source path.

## Measured

Release, .NET 10, `maxWidth: 400`, 12pt default typeface, after warm-up. Each row doubles the input:

| Characters | `TextLayout(string, …)` | Custom `ITextSource` | Lines |
|---|---|---|---|
| 20,000 | 625 ms | 221 ms | 393 |
| 40,000 | 783 ms | 776 ms | 785 |
| 80,000 | 3,016 ms | 3,042 ms | 1,569 |

The ratio between successive rows approaches **4.0** as the input doubles, which is quadratic. For
comparison, the same 80,000 characters as ordinary breakable words costs **~110 ms**, and the same
unbreakable text laid out at `maxWidth: 20000` — a twentieth the line count — costs **~30 ms**. So
the cost tracks *characters × lines* rather than characters.

A custom `ITextSource` is asked for only **2** runs in the 80,000-character case, so the time is
not in the source.

## Reproduction

```csharp
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

var text = new string('A', 80_000);

var sw = Stopwatch.StartNew();
var layout = new TextLayout(
    text,
    new Typeface(FontFamily.Default),
    fontRenderingEmSize: 12,
    foreground: Brushes.Black,
    textWrapping: TextWrapping.Wrap,
    maxWidth: 400);

_ = layout.Height;

Console.WriteLine($"{sw.Elapsed.TotalMilliseconds:F0} ms for {layout.TextLines.Count} lines");
```

Halve the length and the time falls by roughly four.

## Why it matters

Unbreakable runs are not contrived. They arrive from base64 payloads, minified JavaScript, long
URLs, file paths, hashes, and any CJK text a break iterator declines to split — and from a
clipboard, where the content is whatever another application produced. A viewer that renders pasted
text has no opportunity to validate its shape first.

The failure mode is the awkward one: not an exception, not visible CPU load, just an unresponsive
window for minutes.

## Note for whoever picks this up

**A measurement of this that leaves `TextWrapping` at its default proves nothing**, because the
default produces a single line and performs no break search at all. That mistake was made while
investigating this and produced a confident, wrong conclusion that the convenience constructor was
linear and only custom sources were affected. Both are affected.
