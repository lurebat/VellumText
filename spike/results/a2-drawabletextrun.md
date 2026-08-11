# A2 — `DrawableTextRun` inside a custom `ITextSource`
_generated 2026-08-11 13:30:11_

Text length: 148; embeds at indices 18, 142 (both U+FFFC).

## 0. Baseline sanity — does a custom source with embeds format?

- PASS — TextLayout constructed from a custom ITextSource containing DrawableTextRuns

Lines: 3, layout width 364.65, height 81.22

| line|first|len|width|height|baseline|runs |
|---|---|---|---|---|---|---|
| 0|0|45|362.58|31.92|25.9|ShapedTextRun(6) ShapedTextRun(4) ShapedTextRun(1) ShapedTextRun(6) ShapedTextRun(1) BOX(40x24) ShapedTextRun(5) ShapedTextRun(12) ShapedTextRun(9) |
| 1|45|54|361.89|21.28|17.27|ShapedTextRun(54) |
| 2|99|50|364.65|28.02|24|ShapedTextRun(43) BOX(40x24) ShapedTextRun(5) EOP |

The synthesized `TextEndOfParagraph` has Length = 1, so it occupies one text-source position of its own.
- PASS — sum of TextLine.Length equals source text length + the paragraph terminator (149 vs 148 + 1)
- PASS — every embed appears exactly once across the formatted lines (found 2, expected 2)

## a. Line breaking and wrapping

| maxWidth|lines|widest line|embed line indices|any line wider than maxWidth? |
|---|---|---|---|---|
| 180|7|175.68|0,6|no |
| 250|5|244.45|0,4|no |
| 320|4|301.89|0,3|no |
| 400|3|364.65|0,2|no |
| 550|3|543.16|0,1|no |
| 800|2|793.05|0,1|no |
| 2000|1|1097.88|0|no |

- PASS — maxWidth=180: no line exceeds the wrap width (widest 175.68)
- PASS — maxWidth=250: no line exceeds the wrap width (widest 244.45)
- PASS — maxWidth=320: no line exceeds the wrap width (widest 301.89)
- PASS — maxWidth=400: no line exceeds the wrap width (widest 364.65)
- PASS — maxWidth=550: no line exceeds the wrap width (widest 543.16)
- PASS — maxWidth=800: no line exceeds the wrap width (widest 793.05)
- PASS — maxWidth=2000: no line exceeds the wrap width (widest 1097.88)

### a.2 — an embed wider than the whole wrap width

Embed 500px wide, wrap width 300px → 3 lines, layout width 500
| line|first|len|width|runs |
|---|---|---|---|---|
| 0|0|7|46.09|ShapedTextRun(7) |
| 1|7|2|500|BOX(500x24) ShapedTextRun(1) |
| 2|9|31|211.63|ShapedTextRun(30) EOP |

- PASS — oversized embed does not lose or duplicate any text positions (40 vs 39 + 1 (EOP))
Note: the embed's own line is 500px wide against a 300px wrap width — an unbreakable embed **overflows** rather than being clipped or scaled.

## b. Baseline alignment


### b.0 — a DrawableTextRun with null Properties

`TextRun.Properties` is virtual and defaults to null. What happens if a `DrawableTextRun` subclass leaves it that way?

Formatting succeeds: 1 line(s), width 1097.88. Now drawing it:

```
ArgumentOutOfRangeException: Exception of type 'System.ArgumentOutOfRangeException' was thrown. (Parameter 'baselineAlignment')
at Avalonia.Media.TextFormatting.TextLineImpl.GetBaselineOffset(TextLine textLine, DrawableTextRun textRun)
```

- PASS — **drawing throws when a DrawableTextRun has null Properties** — measurement succeeds, rendering does not, so the failure surfaces only at paint time (ArgumentOutOfRangeException: Exception of type 'System.ArgumentOutOfRangeException' was thrown. (Parameter 'baselineAlignment'))

### b.1 — BaselineAlignment matrix

A 24px-tall box in 16px text. `Baseline` on the run is held at 24 while `Properties.BaselineAlignment` varies.

| BaselineAlignment|line Height|line Baseline|Extent|OverhangAfter|Draw origin |
|---|---|---|---|---|---|
| Top|31.92|25.9|30|-0.02|(123, -0.08) |
| Center|31.92|25.9|30|-0.02|(123, 13.96) |
| Bottom|31.92|25.9|30|-0.02|(123, 17.92) |
| Baseline|31.92|25.9|30|-0.02|(123, 11.9) |
| TextTop|31.92|25.9|30|-0.02|(123, -0.08) |
| TextBottom|31.92|25.9|30|-0.02|(123, 17.92) |
| Subscript|31.92|25.9|30|-0.02|(123, 17.92) |
| Superscript|31.92|25.9|30|-0.02|(123, 10) |

### b.2 — varying DrawableTextRun.Baseline with BaselineAlignment.Baseline

| run Baseline|line Height|line Baseline|Extent|OverhangAfter|Draw origin |
|---|---|---|---|---|---|
| 24|31.92|25.9|30|-0.02|(123, 11.9) |
| 18|31.92|25.9|24|-0.02|(123, 17.9) |
| 12|37.9|25.9|30|0|(123, 23.9) |
| 0|49.9|25.9|42|0|(123, 35.9) |
| -6|55.9|25.9|48|0|(123, 41.9) |

### b.3 — what does the Draw origin actually mean?

The same layout drawn at two different origins, to separate a constant offset from an absolute quirk. `rel` is the draw origin minus the origin passed to `Draw`.

| alignment|rel @ origin.Y=10|rel @ origin.Y=60|constant?|predicted |
|---|---|---|---|---|
| Baseline|1.898|1.898|yes|1.898 |
| Top|-10.078|-10.078|yes|0 |
| Center|3.961|3.961|yes|3.961 |
| Bottom|7.922|7.922|yes|7.922 |

With `BaselineAlignment.Baseline`, the run is drawn at `origin.Y + line.Baseline - run.Baseline`, i.e. **`Draw`'s origin is the top-left of the run's box, and `DrawableTextRun.Baseline` is measured downward from that top edge**. Painting the content downward from the origin (as `BoxRun` does) is therefore correct.

For reference, a plain 16px line without any embed: Height=21.28, Baseline=17.27

- PASS — a 40px-tall embed makes its line taller than a plain text line (46.02 vs 21.28)

## c. `GetTextBounds` across the embed

Embed at index 18; probing on line starting at 0 (length 149).

### c — 3 chars ending just before the embed: GetTextBounds(15, 3)

| rect|flow|run bounds |
|---|---|---|
| (97.5, 0) 15.5x31.92|LeftToRight|[idx 15 len 2 (97.5, 0) 11.12x31.92] [idx 17 len 1 (108.62, 0) 4.38x31.92] |

- PASS — TextRunBounds lengths sum to the requested length (3 vs 3)
- PASS — all returned rects are non-empty

### c — exactly the embed: GetTextBounds(18, 1)

| rect|flow|run bounds |
|---|---|---|
| (113, 0) 40x31.92|LeftToRight|[idx 18 len 1 (113, 0) 40x31.92] |

- PASS — TextRunBounds lengths sum to the requested length (1 vs 1)
- PASS — all returned rects are non-empty

### c — 3 chars + the embed: GetTextBounds(15, 4)

| rect|flow|run bounds |
|---|---|---|
| (97.5, 0) 55.5x31.92|LeftToRight|[idx 15 len 2 (97.5, 0) 11.12x31.92] [idx 17 len 1 (108.62, 0) 4.38x31.92] [idx 18 len 1 (113, 0) 40x31.92] |

- PASS — TextRunBounds lengths sum to the requested length (4 vs 4)
- PASS — all returned rects are non-empty

### c — the embed + 3 following chars: GetTextBounds(18, 4)

| rect|flow|run bounds |
|---|---|---|
| (113, 0) 58.86x31.92|LeftToRight|[idx 18 len 1 (113, 0) 40x31.92] [idx 19 len 3 (153, 0) 18.86x31.92] |

- PASS — TextRunBounds lengths sum to the requested length (4 vs 4)
- PASS — all returned rects are non-empty

### c — spanning the embed on both sides: GetTextBounds(13, 11)

| rect|flow|run bounds |
|---|---|---|
| (84.73, 0) 104.55x31.92|LeftToRight|[idx 13 len 4 (84.73, 0) 23.89x31.92] [idx 17 len 1 (108.62, 0) 4.38x31.92] [idx 18 len 1 (113, 0) 40x31.92] [idx 19 len 5 (153, 0) 36.28x31.92] |

- PASS — TextRunBounds lengths sum to the requested length (11 vs 11)
- PASS — all returned rects are non-empty

### c.2 — is the embed's advance counted exactly once?

width(3 chars) = 15.5, width(3 chars + embed) = 55.5, delta = 40, embed Size.Width = 40
- PASS — adding the embed to the range grows the selection by exactly the embed's width

## d. `HitTestPoint` vs the U+FFFC placeholder index

HitTestTextPosition(18) = (113, 0) 40x31.92 (embed Size = 40 x 24)
- PASS — the caret rect at the placeholder index is the embed's width (40 vs 40)

Sampling x across the embed box at 1px steps (y = box centre):

| x|TextPosition|IsTrailing|IsInside |
|---|---|---|---|
| 110|17|False|True |
| 111|18|False|True |
| 133|19|False|True |
| 156|20|False|True |

- PASS — every point inside the embed maps to one of the two caret positions bracketing the placeholder (18 or 19) and nothing else (distinct: 18,19)
- PASS — the switch from 'before the embed' to 'after the embed' happens at the embed's midpoint (switch at x=133.5, midpoint 133)

### d.2 — control: the same sweep over an ordinary 1-char run

Ordinary char at index 17: rect (108.62, 0) 4.38x31.92, positions inside: 17,18, IsTrailing values: False

- PASS — an ordinary character shows the identical two-position pattern — **the embed is hit-tested exactly like a one-character run**

Note: `IsTrailing` is False across both sweeps. `TextHitTestResult.TextPosition` already has the trailing length folded in, so an editor should use `TextPosition` directly and not try to add `IsTrailing` to it.

## e. Caret movement over the embed

GetNextCaretCharacterHit from 16 (embed at 18): 16+0 -> 16+1 -> 17+1 -> 19+0 -> 20+0 -> 21+0
GetPreviousCaretCharacterHit from 20: 20+0 -> 19+0 -> 18+0 -> 17+0 -> 16+0 -> 15+0
GetBackspaceCaretCharacterHit from 19: 19+0 -> 18+0 -> 17+0 -> 16+0

- PASS — one forward caret step from the embed lands exactly one position later (19+0)
- PASS — one backward caret step from just after the embed lands on the placeholder (18+0)

## f. A real bitmap embed (AC4 shape)

Lines: 2; ImageRun present: True
- PASS — a bitmap-drawing DrawableTextRun formats and renders inline

## A2 result

Checks failed: 0
