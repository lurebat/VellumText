# C1 — the `textStyleOverrides` shortcut
_generated 2026-08-11 14:11:01_

## 1. Character formatting via textStyleOverrides

Text: "Plain bold italic big red plain again."
Overrides: 3 spans; lines: 1; width 281.55, height 34.58

| run|type|length|typeface|size|foreground |
|---|---|---|---|---|---|
| ShapedTextRun|DrawableTextRun|6|Normal/Normal|16|Black |
| ShapedTextRun|DrawableTextRun|4|Bold/Normal|16|Black |
| ShapedTextRun|DrawableTextRun|1|Normal/Normal|16|Black |
| ShapedTextRun|DrawableTextRun|6|Normal/Italic|16|Black |
| ShapedTextRun|DrawableTextRun|1|Normal/Normal|16|Black |
| ShapedTextRun|DrawableTextRun|7|Normal/Normal|26|Crimson |
| ShapedTextRun|DrawableTextRun|13|Normal/Normal|16|Black |

- PASS — textStyleOverrides produced runs with differing font sizes (16,26)
- PASS — textStyleOverrides produced runs with differing weights (Normal,Bold)
- PASS — the string was split into multiple styled runs (7 runs)

## 2. Can this path express an embed?


### 2a — the constructor's own surface

- `TextLayout(String text, Typeface typeface, Double fontSize, IBrush foreground, TextAlignment textAlignment, TextWrapping textWrapping, TextTrimming textTrimming, TextDecorationCollection textDecorations, FlowDirection flowDirection, Double maxWidth, Double maxHeight, Double lineHeight, Double letterSpacing, Int32 maxLines, FontFeatureCollection fontFeatures, IReadOnlyList`1 textStyleOverrides, TextRunCache textRunCache)`
- `TextLayout(String text, Typeface typeface, Double fontSize, IBrush foreground, TextAlignment textAlignment, TextWrapping textWrapping, TextTrimming textTrimming, TextDecorationCollection textDecorations, FlowDirection flowDirection, Double maxWidth, Double maxHeight, Double lineHeight, Double letterSpacing, Int32 maxLines, FontFeatureCollection fontFeatures, IReadOnlyList`1 textStyleOverrides)`
- `TextLayout(ITextSource textSource, TextParagraphProperties paragraphProperties, TextTrimming textTrimming, Double maxWidth, Double maxHeight, Int32 maxLines, TextRunCache textRunCache)`
- `TextLayout(ITextSource textSource, TextParagraphProperties paragraphProperties, TextTrimming textTrimming, Double maxWidth, Double maxHeight, Int32 maxLines)`

- PASS — a string-based constructor exists (the C1 path)
- PASS — **no string-based constructor accepts a TextRun or ITextSource** — content is a string, so no DrawableTextRun can ever be injected

### 2b — the TextRunProperties surface

Every public member of `TextRunProperties`, the only thing an override can carry:

- `IBrush BackgroundBrush`
- `BaselineAlignment BaselineAlignment`
- `CultureInfo CultureInfo`
- `FontFeatureCollection FontFeatures`
- `Double FontRenderingEmSize`
- `IBrush ForegroundBrush`
- `TextDecorationCollection TextDecorations`
- `Typeface Typeface`

- PASS — **no TextRunProperties member can carry drawn content** (no IImage, no DrawableTextRun, no draw callback) — formatting only

### 2c — what U+FFFC actually renders as on this path

String contains U+FFFC at index 18. Its rect is (114.38, 0) 19.2x34.58.

It is shaped as an ordinary character by whatever font the fallback chain picks — it occupies a glyph advance, not an embed box, and nothing draws there but a glyph.

Runs on this layout that are `DrawableTextRun`s: 7 — ShapedTextRun.

Note: Avalonia's own `ShapedTextRun` **derives from `DrawableTextRun`**, so the base type is not exotic — ordinary shaped text uses it too. What matters is whether *our* run type can ever appear here.

- PASS — no run from outside Avalonia's own assembly appears in the C1 layout — the placeholder stays a glyph and there is no injection point for a custom embed

## C1 result

Checks failed: 0
