# A1 (width-only relayout) — does a run cache help when the text does not change?
_generated 2026-08-11 15:24:06_

Scenario: an 800-char paragraph whose text is **never modified**, relaid out at a sequence of different wrap widths — i.e. the user dragging the editor pane wider and narrower. No `InvalidateFrom` is called, because nothing was edited.

## 1. Correctness — is the cached layout right for the NEW width?


| width | cached fingerprint | matches uncached? |
|---|---|---|
| 320 | `0,40,288.1719,21.2813,17.2656;40,41,303.0957…` | yes |
| 360 | `0,44,322.1797,21.2813,17.2656;44,49,358.042,…` | yes |
| 400 | `0,50,366.7344,21.2813,17.2656;50,47,348.3623…` | yes |
| 440 | `0,58,425.1953,21.2813,17.2656;58,59,434.8574…` | yes |
| 480 | `0,65,479.6641,21.2813,17.2656;65,66,479.7246…` | yes |
| 520 | `0,70,513.8125,21.2813,17.2656;70,67,489.6934…` | yes |
| 560 | `0,75,542.3984,21.2813,17.2656;75,69,514.4355…` | yes |
| 600 | `0,81,595.6816,23.9414,19.4238;81,80,591.6123…` | yes |

- PASS — cached width-only relayout matches an uncached layout at every width (cached relayout returned a layout for the wrong width — the cache is stale here too)

## 2. Does the cached build re-query the text source?


- GetTextRun calls, first build (cold cache): **33**
- GetTextRun calls, later build at a new width (warm cache): **0**

The warm build asks the source for nothing — the shaped runs are reused wholesale and only line breaking is redone. That is exactly the intended behaviour here, and unlike the edit case it is **also correct**, because the text really has not changed.

## 3. Timing


Interleaved arms, repeated, reporting the median of the per-repetition ratios — this VM's timing noise is roughly ±30%, so a single ratio means little.

| rep | no cache (µs/op) | persistent cache (µs/op) | ratio |
|---|---|---|---|
| 1 | 801.8 | 174.8 | 4.59× |
| 2 | 489.8 | 143.9 | 3.40× |
| 3 | 433.1 | 130.1 | 3.33× |
| 4 | 364.3 | 120.9 | 3.01× |
| 5 | 342.0 | 97.3 | 3.51× |
| 6 | 346.0 | 96.9 | 3.57× |
| 7 | 413.1 | 98.1 | 4.21× |

- median no-cache : **413.1 µs/op**
- median cached   : **120.9 µs/op**
- **median speedup: 3.51×**  (range 3.01×–4.59×)

- PASS — width-only relayout is both correct and materially faster with a persistent cache (3.51×) (width-only relayout shows no material speedup (3.51×) — treat the cache as not worth it)
