# Adversarial-review verification — three challenged claims re-tested
_generated 2026-08-11 15:24:06_

## 1. C1 — can a DrawingBrush render real content on the textStyleOverrides path?


FINDINGS.md said the string path can draw nothing but a tofu glyph. A `DrawingBrush` is renderable content, and `BackgroundBrush` is painted across the run's bounds, so test whether arbitrary drawing reaches the surface.

- U+FFFC run rect with a DrawingBrush background: `15.48046875, 0, 57.609375, 63.84375`
- rendered to `review-c1-drawingbrush.png`; contains non-greyscale (brush-painted) pixels: **True**

- PASS — CONFIRMED: a DrawingBrush on BackgroundBrush does paint real content — the 'can draw nothing but tofu' wording was too strong (not reproduced: the DrawingBrush produced no visible painted pixels)

## 2. bidi — is StringInfo a sound oracle for Avalonia's cluster boundaries?


The bidi probe asserts every Avalonia caret stop is a StringInfo boundary — a *subset* test. If StringInfo permits boundaries Avalonia correctly refuses, the assertion can pass while hiding a real regression.

- text: `क्ष` (U+0915 U+094D U+0937), length 3
- StringInfo boundaries : {0,2,3}
- Avalonia caret stops  : {0,3}

- Avalonia stops ⊆ StringInfo stops (what the probe asserts): **True**
- the two oracles actually disagree: **True**

- PASS — CONFIRMED: the subset assertion passes even though the oracles disagree — StringInfo admits a boundary Avalonia refuses, so the check is one-way and can false-pass (not reproduced: StringInfo and Avalonia agree on this text, so the oracle concern is unproven here)

## 3. backspace — does component-peel leave dangling ZWJs mid-sequence?


The user has chosen component-peel. The probe logged caret *positions* but never applied the deletion, so this checks what the string actually becomes.

| from | backspace target | deleted | resulting emoji region | ends with ZWJ? |
|---|---|---|---|---|
| 18 | 16 | `\uD83D\uDC66` | `\uD83D\uDC68<ZWJ>\uD83D\uDC69<ZWJ>\uD83D\uDC67<ZWJ>` | **yes** |
| 16 | 15 | `<ZWJ>` | `\uD83D\uDC68<ZWJ>\uD83D\uDC69<ZWJ>\uD83D\uDC67` | no |
| 15 | 13 | `\uD83D\uDC67` | `\uD83D\uDC68<ZWJ>\uD83D\uDC69<ZWJ>` | **yes** |
| 13 | 12 | `<ZWJ>` | `\uD83D\uDC68<ZWJ>\uD83D\uDC69` | no |
| 12 | 10 | `\uD83D\uDC69` | `\uD83D\uDC68<ZWJ>` | **yes** |

- deletion steps that left a trailing ZWJ: **3**

- PASS — CONFIRMED: component-peel produces 3 intermediate state(s) ending in a dangling ZWJ — an editor must decide whether to absorb the ZWJ in the same keystroke (not reproduced: no deletion step left a dangling ZWJ)
