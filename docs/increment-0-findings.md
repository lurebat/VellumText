# Increment 0 — validation spike findings

Spike for [`docs/architecture.md`](architecture.md) §10 increment 0. This is throwaway
code; it exists to answer three questions with measurement rather than reasoning, and it should be
deleted once its conclusions are absorbed.

**Bottom line**

| # | Assumption | Verdict |
|---|---|---|
| **A2** | `DrawableTextRun` in a custom `ITextSource` participates correctly in line breaking, baseline alignment, and `GetTextBounds` | ✅ **Validated** — with three traps worth knowing about |
| **A1** | `TextRunCache.InvalidateFrom` gives usable incremental reshaping | ⚠️ **Partly invalidated** — useless for *edits* (`InvalidateFrom(k>0)` returns a **stale** layout; used correctly it is 1.00×), but a **3.5× win for width-only relayout**. See §2 |
| **C1** | The `textStyleOverrides` shortcut | ✅ **Validated as a ceiling** — good for character formatting; cannot inject a run or control embed metrics/identity (it *can* fake visuals with a brush — see §3) |
| — | Bidi & grapheme caret sanity | ✅ **Validated**, except backspace, which is intentionally sub-cluster (see §4) |

**Recommendation: Increment 1 proceeds as designed.** No architectural change is required. Three
small documentation corrections are proposed in §6 — proposed only; `docs/architecture.md` has not
been modified.

> **This report was adversarially reviewed after it was first written, and several claims below did
> not survive.** The A1 recommendation was too broad, the C1 impossibility proof was too strong, the
> A2 baseline "anomaly" turned out to have an exact explanation, and the bidi oracle is weaker than
> claimed. Each correction is marked **[revised]** and was re-verified by running new probes, not by
> accepting the reviewer's word. §8 lists what remains unverified.
>
> **`docs/architecture.md` has since been updated** with the corrections in §6.

---

## 0. How this was measured

### Environment

| | |
|---|---|
| Avalonia | 12.1.1 |
| Target framework | **`net10.0`** (every result also independently reproduced on `net8.0`) |
| Runtime | .NET 10.0.10 (SDK 10.0.302), Release configuration |
| OS | Windows, x64 (Hyper-V guest — noted because it widens benchmark variance) |
| Rendering | real Skia, not the headless no-op drawing backend |

**On the target framework:** the brief asked for .NET 9 with .NET 8 as fallback. **.NET 9 is not
installed on this machine** (SDKs present: 5.0.408, 6.0.428, 8.0.423, 10.0.302).

The spike was first written against `net8.0`, then retargeted to `net10.0`. **Avalonia 12.1.1
ships first-class `lib/net8.0` *and* `lib/net10.0` assemblies** — verified for every package used
here (`Avalonia`, `.Desktop`, `.Skia`, `.Themes.Fluent`, `.Fonts.Inter`, `.Headless`) — so there
was no packaging reason to avoid .NET 10.

Having run the full probe set on both is a bonus rather than a cost: it upgrades "these findings
are not framework-sensitive" from an assertion to a measurement. **Every probe reports 0 failures
on both frameworks, and the A1 layout fingerprints are byte-for-byte identical between them.**
Where the two differ is only in timing noise, discussed in §2.

### Layout

> **The `spike/` tree described here has been deleted**, as §10 intends and as §8 records. This
> document and [`increment-0-evidence/`](increment-0-evidence/) are what survive. The layout and
> commands below describe the code **as it was when these measurements were taken**, and are kept so
> the results are reproducible from git history rather than implied to still be runnable. The spike
> is recoverable at commit `766c3ef`.

```
spike/
  Vellum.Spike.sln
  Directory.Build.props        common TFM / Avalonia version
  Shared/                      SpikeTextSource, BoxRun, ImageRun, properties subclasses
  Vellum.Spike.Probe/          console; deterministic assertion probes + benchmarks
  Vellum.Spike.App/            Avalonia window; live interaction + PNG capture
  results/                     committed evidence: 8 markdown reports + 29 PNGs
```

Reproduce (after `git checkout 766c3ef -- spike`):

```
cd spike/Vellum.Spike.Probe
dotnet run -c Release -- a2       # A2 probes
dotnet run -c Release -- c1       # C1 probes
dotnet run -c Release -- bidi     # bidi/grapheme probes
dotnet run -c Release -- a1diag   # A1 cache diagnostic   <- the decisive one
dotnet run -c Release -- a1       # A1 stopwatch timings
dotnet run -c Release -- a1width  # A1 width-only relayout
dotnet run -c Release -- review   # re-tests of the three challenged claims
dotnet run -c Release -- bench    # A1 BenchmarkDotNet

cd ../Vellum.Spike.App
dotnet run -c Debug               # the interactive window
```

All probes report **0 failed checks**, and as of the final commit the process exits non-zero if any
check fails (it previously always exited 0 — see §8). Note that "0 failures" means *the probe's
assertions hold*, not *the assumption is validated* — where behaviour turned out to be
disappointing, the assertion was rewritten to assert the disappointing behaviour precisely, so
that a future Avalonia upgrade that changes it will fail loudly. A1 is the clearest example.

### The spike was run, not merely compiled

Headless probes produce the numbers. The app was then launched and driven interactively — mouse
clicks into the text, arrow keys, Backspace, and an extend-selection mode — and its live readout
matched the headless numbers exactly. Specific live confirmations are recorded inline below and
marked **[live]**.

The interactive pass was repeated after retargeting to net10.0, and every live reading was
identical to the net8.0 session:

| live check | net8.0 | net10.0 |
|---|---|---|
| click left half of embed | pos 18, `IsTrailing` False | pos 18, `IsTrailing` False |
| click right half of embed | pos 19 | pos 19 |
| Right arrow before family emoji | 7 → 18 (one press) | 7 → 18 (one press) |
| Backspace at 18 | → 16 (component peel) | → 16 (component peel) |

---

## 1. A2 — `DrawableTextRun` inside a custom `ITextSource`

**Verdict: ✅ validated.** The architecture's §4.7 design works. An `ITextSource` returning
`TextCharacters` with varying `TextRunProperties`, plus custom `DrawableTextRun` subclasses at
`\uFFFC` positions with `Length => 1`, formats, wraps, hit-tests and measures correctly.

Raw report: [`increment-0-evidence/a2-drawabletextrun.md`](increment-0-evidence/a2-drawabletextrun.md).

Setup: a 148-character paragraph with embeds at indices 18 and 142, `BoxRun` (a 40×24 colored box)
and `ImageRun` (a real bitmap), both `Length => 1`.

### (a) Line breaking and wrapping — correct

Sweeping the wrap width from 180 to 2000px, the embed always moves as an unbreakable unit and
**no line ever exceeds the wrap width**:

| maxWidth | lines | widest line | lines containing an embed |
|---|---|---|---|
| 180 | 7 | 175.68 | 0, 6 |
| 250 | 5 | 244.45 | 0, 4 |
| 320 | 4 | 301.89 | 0, 3 |
| 400 | 3 | 364.65 | 0, 2 |
| 800 | 2 | 793.05 | 0, 1 |
| 2000 | 1 | 1097.88 | 0 |

Every embed appears exactly once across the formatted lines at every width — no duplication, no
loss. **[live]** confirmed visually: the embed wraps to the next line rather than straddling a
break ([`increment-0-evidence/a2-wrapped.png`](increment-0-evidence/a2-wrapped.png)).

One boundary case worth recording: **an embed wider than the entire wrap width overflows.** A
500px embed in a 300px wrap produced a 500px-wide line. It is not clipped, not scaled, and no text
positions are lost — but it *does* escape the wrap width. Vellum will need its own policy for
oversized inline images (scale to fit the content width, most likely) because the formatter will
not impose one.

### (b) Baseline alignment — correct, with one anomaly

The line box grows to accommodate a tall embed (46.02px versus 21.28px for a plain 16px text
line). Varying `DrawableTextRun.Baseline` under `BaselineAlignment.Baseline` behaves exactly as
you would expect: reducing the baseline pushes the run down and grows the line.

To determine what `Draw`'s `origin` actually means, the same layout was drawn at two different
Y origins and the delta compared against prediction:

| alignment | offset @ origin.Y=10 | offset @ origin.Y=60 | constant? | predicted |
|---|---|---|---|---|
| Baseline | 1.898 | 1.898 | yes | 1.898 ✅ |
| Center | 3.961 | 3.961 | yes | 3.961 ✅ |
| Bottom | 7.922 | 7.922 | yes | 7.922 ✅ |
| **Top** | **−10.078** | **−10.078** | yes | **0** ❌ |
| **TextTop** | **−10.078** | **−10.078** | yes | **0** ❌ |

So: **`Draw`'s origin is the top-left of the run's box, and `DrawableTextRun.Baseline` is measured
downward from that top edge.** Painting content downward from the origin is correct.

But `Top` and `TextTop` place the run **10.078px above the line box** — a constant offset, not
drift, reproducible at both origins. `Center`, `Bottom` and `Baseline` all match prediction
exactly, so this is specific to the top-anchored alignments.

**[revised] This is not a bug — it is exactly what Avalonia's formula computes.** The first version
of this report called it a probable upstream defect and left it un-root-caused. Adversarial review
supplied the arithmetic, and it reproduces the number exactly. `TextLineImpl.GetBaselineOffset`
computes, for the top-anchored cases:

```
-run.Baseline + line.Height - line.Extent + run.Height / 2
= -24     + 31.921875   - 30          + 12
= -10.078125          ← matches the measured −10.078 exactly
```

So the probe's "predicted 0" was the wrong prediction, not Avalonia's wrong answer: the prediction
was asserted from intuition rather than derived from the implementation's contract. Calling this a
bug would need separate evidence that the formula violates the documented contract, which this spike
does not have. The practical guidance is unchanged — **use `BaselineAlignment.Baseline`**, and treat
`Top`/`TextTop` as offset by this formula rather than as broken.

### ⚠️ Trap 1 — a null `Properties` crashes at paint time, not at format time

`TextRun.Properties` is virtual and **defaults to null**. A `DrawableTextRun` that leaves it null:

- **formats successfully** — `new TextLayout(...)` returns a valid single line, 1097.88px wide;
- **throws when drawn**:

```
ArgumentOutOfRangeException (Parameter 'baselineAlignment')
  at Avalonia.Media.TextFormatting.TextLineImpl.GetBaselineOffset(TextLine, DrawableTextRun)
```

This is a nasty failure mode: measurement passes, tests that only inspect metrics pass, and the
crash appears only on the render path. The `EmbedRun` sketch in §4.7 does not mention `Properties`,
so this is worth encoding as a requirement — see §6.

### ⚠️ Trap 2 — `TextEndOfParagraph` has `Length == 1`

The synthesized paragraph terminator occupies a text-source position of its own, so **the sum of
`TextLine.Length` over a layout equals the source text length + 1** (149 versus 148). Anything in
Vellum that reconciles line lengths against model offsets must account for it. This bit the A1
harness first — the tell that the cached layout was stale was a length sum of 101 rather than 102.

### (c) `GetTextBounds` across the embed — correct

| range | rect | run bounds |
|---|---|---|
| `(15,3)` — 3 chars before the embed | (97.5, 0) 15.5×31.92 | [15+2] [17+1] |
| `(18,1)` — exactly the embed | (113, 0) **40**×31.92 | [18+1] |
| `(15,4)` — 3 chars + embed | (97.5, 0) **55.5**×31.92 | [15+2] [17+1] [18+1] |
| `(18,4)` — embed + 3 chars | (113, 0) 58.86×31.92 | [18+1] [19+3] |
| `(13,11)` — spanning both sides | (84.73, 0) 104.55×31.92 | [13+4] [17+1] [18+1] [19+5] |

`TextRunBounds` lengths sum to the requested length in every case, all rects are non-empty, and
the embed's advance is counted **exactly once**: 55.5 − 15.5 = 40.0 = `BoxRun.Size.Width`.

### (d) `HitTestPoint` — the embed behaves exactly like a one-character run

Sweeping x across the embed's box at 1px steps:

| x | TextPosition | IsTrailing |
|---|---|---|
| 110 | 17 | False |
| 111 | **18** | False |
| 133 | **19** | False |
| 156 | 20 | False |

Every point inside the embed maps to one of the two positions bracketing the placeholder (18 or
19) and nothing else, switching at the midpoint (x = 133.5 against a computed midpoint of 133).
A control sweep over an ordinary 1-char run at index 17 produced the identical two-position
pattern — so **no embed-specific hit-testing logic is needed**.

**[live]** Clicking the gold box in the running app at x=122 reported position 18; at x=148,
position 19. The live app agrees with the headless midpoint.

### ⚠️ Trap 3 — `IsTrailing` is always `False`

Across both sweeps `IsTrailing` never became true. `TextHitTestResult.TextPosition` already has
the trailing length folded in. An editor must use `TextPosition` **directly** and must *not* add
`IsTrailing` to it — doing so would double-count and put the caret one position too far right.

**[revised] Scope of the "always `False`" claim.** The sweeps sampled only *interior* points of a
single LTR line. Avalonia does set `IsTrailing` in cases this spike never exercised — notably a hit
past the line's trailing edge, or a point below the layout. The safe statement is "`IsTrailing`
stayed `False` across every interior point tested"; the advice to use the already-folded
`TextPosition` is unaffected either way.

### (e) Caret movement over the embed — correct

```
Next     from 16:  16 -> 17 -> 19 -> 20 -> 21     (steps over the embed as one unit)
Previous from 20:  20 -> 19 -> 18 -> 17 -> 16
Backspace from 19: 19 -> 18 -> 17 -> 16
```

The embed is a single caret stop. Positions map to model positions by a simple additive block
offset exactly as §4.7 predicts — **no index translation table is required**.

### What A2 means for the architecture

§4.7 is sound and Increment 4's `EmbedRun` can be built as drawn. The only additions are the three
traps above, none of which are structural.

---

## 2. A1 — `TextRunCache.InvalidateFrom`

**Verdict: ❌ invalidated.** Not "no measurable benefit" — worse than that: **`InvalidateFrom(k)`
for any k ≥ 1 returns the entire pre-edit layout.** It is fast because it is wrong.

Raw reports: [`increment-0-evidence/a1-diagnostic.md`](increment-0-evidence/a1-diagnostic.md) (decisive),
[`increment-0-evidence/a1-textruncache-stopwatch.md`](increment-0-evidence/a1-textruncache-stopwatch.md),
[`increment-0-evidence/a1-benchmarkdotnet.md`](increment-0-evidence/a1-benchmarkdotnet.md).

### How this was nearly reported as a win

The first timing run showed `InvalidateFrom` at roughly **3× faster** than building a fresh
layout, with **half the allocations**. That is a headline result, and BenchmarkDotNet independently
reproduced it:

| Method | Length | Edit% | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|---|---|
| Fresh `TextLayout`, no cache | 800 | 50 | 365.40 µs | 1.00 | 63.54 KB | 1.00 |
| **Persistent cache + `InvalidateFrom(offset)`** | 800 | 50 | **111.71 µs** | **0.31** | **31.93 KB** | **0.50** |
| Persistent cache + `Invalidate()` | 800 | 50 | 504.99 µs | 1.38 | 63.83 KB | 1.00 |
| Fresh cache each edit (control) | 800 | 50 | 470.30 µs | 1.29 | 63.89 KB | 1.01 |

Ratios of 0.25–0.37× held across all nine length/position combinations. **That number is an
artefact.** It survived only because the harness carried a geometry cross-check, which reported a
mismatch on **360 of 360** edits: cached line lengths summed to 101 where the fresh layout summed
to 102. The cached arm was laying out the text as it was *before* the edit.

This is exactly what a naive benchmark of this API would conclude, which is why
`increment-0-evidence/a1-benchmarkdotnet.md` is committed rather than deleted.

### The diagnostic

A dedicated probe (`A1Diag.cs`) established whether this was Avalonia's behaviour or spike misuse.

**Control** — with the text *unchanged*, a reused cache reproduces the uncached layout exactly. So
the cache is wired up correctly.

**Offset sweep** — insert one character at index 10 of a 100-char paragraph, then vary `k`:

| `InvalidateFrom(k)` | sum of line lengths | matches uncached? |
|---|---|---|
| **0** | **102** | **yes** |
| 1 | 101 | no |
| 5 | 101 | no |
| 9 | 101 | no |
| **10** (the edit position) | 101 | **no** |
| 11 | 101 | no |
| 25 | 101 | no |
| 50 | 101 | no |

Note k = 50 is *past* the edit at 10 and still returns the stale layout — so this is not an
off-by-one in choosing the offset. Only `InvalidateFrom(0)` is correct, and that is equivalent to
invalidating everything.

**Full `Invalidate()`** — correct on 15/15 edits. **Fresh cache per edit** — correct on 15/15.

**The staleness is in the text, not merely in cached metrics:**

```
source text now : "the quick Zbrown fox jumps ove..."  (len 101)
text in the runs: "the quick brown fox jumps over..."  (len 100)
```

The inserted `'Z'` is simply absent from the shaped runs.

**The mechanism**, via an instrumented `ITextSource` that counts `GetTextRun` calls:

| mode | `GetTextRun` calls | indices requested | layout correct? |
|---|---|---|---|
| no cache | 6 | 0, 25, 50, 75, 100, 101 | yes |
| `Invalidate()` | 6 | 0, 25, 50, 75, 100, 101 | yes |
| `InvalidateFrom(0)` | 6 | 0, 25, 50, 75, 100, 101 | yes |
| `InvalidateFrom(10)` | **0** | — | no |
| `InvalidateFrom(50)` | **0** | — | no |

A mode that re-queries **zero** positions cannot possibly reflect an edit. The "3× speedup" is the
speed of returning a stale answer.

### The honest numbers

Comparing the uncached baseline against the only cached usages that produce a **correct** layout
(20,000 measured iterations after 2,000 warmup, 480px wrap, style-run boundary every 25 chars).
This table is the **net8.0** run:

| chars | edit at | NoCache µs | `InvalidateFrom(0)` µs | `Invalidate()` µs | FreshCache µs |
|---|---|---|---|---|---|
| 100 | 10% | 57.05 | 57.14 (1.00×) | 51.61 (0.90×) | 52.94 (0.93×) |
| 100 | 50% | 52.58 | 53.65 (1.02×) | 50.76 (0.97×) | 56.21 (1.07×) |
| 100 | 90% | 51.94 | 55.69 (1.07×) | 52.10 (1.00×) | 53.29 (1.03×) |
| 400 | 10% | 216.64 | 195.27 (0.90×) | 196.75 (0.91×) | 189.51 (0.87×) |
| 400 | 50% | 193.57 | 193.69 (1.00×) | 194.21 (1.00×) | 202.53 (1.05×) |
| 400 | 90% | 188.50 | 194.11 (1.03×) | 189.07 (1.00×) | 214.64 (1.14×) |
| 800 | 10% | 373.10 | 395.24 (1.06×) | 381.58 (1.02×) | 411.15 (1.10×) |
| 800 | 50% | 378.52 | 375.79 (0.99×) | 373.05 (0.99×) | 373.03 (0.99×) |
| 800 | 90% | 370.81 | 373.31 (1.01×) | 372.51 (1.00×) | 372.98 (1.01×) |

Percentiles at 800 chars / 50%:

| arm | mean µs | p50 µs | p95 µs |
|---|---|---|---|
| NoCache | 375.98 | 348.1 | 441.0 |
| `InvalidateFrom(0)` | 376.03 | 350.4 | 432.1 |

**Speedup from the correct cached path: 1.00×.** Within noise at every size and every edit
position. Allocation is also unchanged (63.54 KB versus 63.83 KB at 800 chars).

### Cross-framework confirmation, and how noisy this measurement is

Re-running everything on **net10.0** reproduces the defect exactly. The offset sweep, the
zero-`GetTextRun`-call result, and the stale-text proof are all unchanged, and the layout
fingerprints are **byte-for-byte identical** to the net8.0 run:

```
InvalidateFrom(0)  -> 0,62,460.2656,... len sum 102  correct
InvalidateFrom(k>=1) -> 0,65,479.6641,... len sum 101  stale   (identical string on both TFMs)
```

The *timings*, however, deserve a caveat. Three consecutive net10.0 runs of the correct-path
speedup at 800 chars / 50% gave:

| run | speedup | NoCache mean µs |
|---|---|---|
| 1 | 1.28× | 448.9 |
| 2 | 0.94× | 585.6 |
| 3 | 0.94× | 374.2 |

*(**[revised]** the third row previously read `1.00×` against this `374.2 µs` baseline. Those two
numbers came from **different measurements** — the `1.00×` is from the percentile block, whose
NoCache baseline is `334.5 µs`. Both runs support the same conclusion, but pairing them in one row
was wrong. The row now uses the `0.94×`/`374.163 µs` pair that actually belong together.)*

That is a ±30% swing on the ratio and a 1.6× swing on the absolute baseline, on an unchanged
binary — this is a loaded Hyper-V guest, and the harness is not isolated from it. **No individual
speedup figure here should be read to better than about ±30%.** The conclusion survives easily,
because the claim is only that the correct path is *not usefully faster*, and three runs scattered
around 1.00× say exactly that.

One detail is worth noting for its own sake: the **stale** arm measured 0.25–0.32× in *every* run
on *both* frameworks, with far tighter variance than the honest arms. A path that does no work is
more reproducible than one that does. In hindsight that stability was itself a warning sign.

### Two structural notes

1. **Avalonia exposes no re-layout-in-place API.** `TextLayout` has no "reflow" method. The only
   reuse mechanism is passing the same `TextRunCache` into a **new** `TextLayout`. So even in the
   best case this measures shaping/run reuse, never layout-object reuse — a ceiling on how much
   A1 could ever have been worth.
2. **[revised] The staleness is entry-granularity, not necessarily a bug.** The first version of
   this report called `InvalidateFrom(k>0)` "broken". A reviewer pointed out the more defensible
   reading, which matches every observation here: cache entries are keyed by the index a shaped run
   *starts* at, and `InvalidateFrom(k)` drops entries whose **key** is `>= k`. A single-paragraph
   `TextLayout` formats from index 0, producing essentially one entry keyed 0 — so
   `InvalidateFrom(50)` correctly finds nothing at or after key 50 to drop and leaves entry 0 intact.
   The observable consequence is unchanged and still a trap: after an edit, any `k > 0` yields the
   **pre-edit layout**. But the correct characterisation is "invalidation is entry-granular and a
   paragraph is one entry", not "Avalonia has an off-by-one bug".

### ⚠️ [revised] A1 is only half-invalidated: width-only relayout is a real 3.5× win

The original benchmark only ever measured **edits** — every arm mutated the text. That missed the
other thing an editor does constantly: relayout **unchanged** text at a **new wrap width**, which is
what happens on every frame of a pane resize. Shaping is width-independent, so a run cache should
help there, and it does.

The trap that killed the edit arm applies here too — a cache that returns the *previous* width's
layout would also look fast — so correctness was tested as "matches an uncached layout built at the
**same new width**", not "matches itself":

| wrap width | line 1 length | cached matches uncached at that width? |
|---|---|---|
| 320 | 40 | yes |
| 360 | 44 | yes |
| 400 | 50 | yes |
| 440 | 58 | yes |
| 480 | 65 | yes |
| 520 | 70 | yes |
| 560 | 75 | yes |
| 600 | 81 | yes |

The line breaks genuinely differ per width, and each matches its uncached reference — so the cache
is really re-breaking lines while reusing shaped runs, not returning a stale answer. `GetTextRun`
calls confirm the mechanism: **33** on the cold build, **0** on a later build at a different width.

Timing, 800 chars, 8 widths cycled, 200 iterations/rep, 7 reps after a discarded warmup:

| rep | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|
| no cache (µs/op) | 981.1 | 546.7 | 544.1 | 441.4 | 404.8 | 364.2 | 390.2 |
| persistent cache (µs/op) | 187.3 | 186.4 | 140.8 | 126.0 | 133.2 | 110.2 | 108.8 |
| ratio | 5.24× | 2.93× | 3.87× | 3.50× | 3.04× | 3.30× | 3.59× |

**Median 3.50×** (range 2.93×–5.24×). Unlike the ±30% noise that swamped the edit measurement, the
*smallest* observed ratio is still ~2.9×, so this survives the noise on this VM comfortably.

Evidence: [`increment-0-evidence/a1-width-only-relayout.md`](increment-0-evidence/a1-width-only-relayout.md).

### What A1 means for the architecture

**§9 R3's stated fallback is the right path for edits, and it costs nothing.** Rebuild the
paragraph's `TextLayout` wholesale on edit. Concretely:

- a full rebuild of an 800-char paragraph costs roughly **0.35–0.6 ms** (the spread across runs on
  a noisy VM; ~0.05 ms at 100 chars), leaving at least ~25× headroom inside a 16 ms frame budget,
  which is why B2's paragraph granularity was the right choice;
- **[revised] Vellum should keep a per-paragraph `TextRunCache` after all** — but drive it by
  *lifetime*, not by offset. The rule that matches the measurements is:
  - **on a text edit**, call `Invalidate()` (or equivalently discard the cache). Never
    `InvalidateFrom(k>0)` — it silently returns the pre-edit layout.
  - **on a width change with unchanged text**, reuse the cache untouched: correct, and ~3.5× faster.
  This is worth having, because resizing rewraps *every* paragraph at once, which is exactly the
  case where a 3.5× difference is felt.
- if paragraph rebuild ever *does* become the bottleneck for edits, the lever is smaller paragraphs
  or caching whole `TextLayout` objects per unmodified paragraph — not `InvalidateFrom`.

So A1 **as literally stated in §2.3** — "incremental reshaping for single-paragraph *edits*" —
remains **invalidated**. The first version of this report then over-generalised that into "don't use
`TextRunCache` at all", which the width-only measurement disproves.

---

## 3. C1 — the `textStyleOverrides` shortcut

**Verdict: ✅ validated as a ceiling.** It does what §5.3 says, and its limit is structural rather
than merely awkward.

Raw report: [`increment-0-evidence/c1-textstyleoverrides.md`](increment-0-evidence/c1-textstyleoverrides.md).

### It works for character formatting

`TextLayout(string, Typeface, ..., IReadOnlyList<ValueSpan<TextRunProperties>>)` over
`"Plain bold italic big red plain again."` with 3 override spans produced 7 correctly styled runs:

| run | length | typeface | size | foreground |
|---|---|---|---|---|
| 1 | 6 | Normal/Normal | 16 | Black |
| 2 | 4 | **Bold**/Normal | 16 | Black |
| 4 | 6 | Normal/*Italic* | 16 | Black |
| 6 | 7 | Normal/Normal | **26** | **Crimson** |
| 7 | 13 | Normal/Normal | 16 | Black |

Weight, style, size and brush all attribute correctly. **[live]** confirmed visually
([`increment-0-evidence/c1-styled.png`](increment-0-evidence/c1-styled.png)).

### It cannot express embeds — three independent proofs

1. **The constructor's own surface.** Enumerated: `TextLayout` has exactly four public
   constructors — two string-based and two `ITextSource`-based. **No string-based constructor
   accepts a `TextRun` or `ITextSource`.** Content is a `string`; there is no injection point.

2. **`TextRunProperties` has exactly 8 public members** — `BackgroundBrush`, `BaselineAlignment`,
   `CultureInfo`, `FontFeatures`, `FontRenderingEmSize`, `ForegroundBrush`, `TextDecorations`,
   `Typeface`. No `IImage`, no `DrawableTextRun`, no draw callback. An override can carry
   *formatting* and nothing else.

3. **`\uFFFC` stays a glyph.** Placed in the string, it renders as an ordinary character via font
   fallback — a 19.2px tofu advance, not an embed box
   ([`increment-0-evidence/c1-objectreplacement.png`](increment-0-evidence/c1-objectreplacement.png)).

### ⚠️ [revised] Proof 3 was too strong: a brush *can* paint arbitrary content

The first version of this report said the C1 path "can draw nothing but the tofu glyph". That is
wrong. `TextRunProperties.BackgroundBrush` is painted across the run's bounds, and a brush is not
restricted to a flat colour — a `DrawingBrush` (or `VisualBrush`/`ImageBrush`) carries renderable
content. Supplying one over a styled `\uFFFC` run visibly paints a drawing between the neighbouring
characters:

```
U+FFFC run rect with a DrawingBrush background: 15.48, 0, 57.61, 63.84
rendered output contains brush-painted (non-greyscale) pixels: True
```

([`increment-0-evidence/review-c1-drawingbrush.png`](increment-0-evidence/review-c1-drawingbrush.png),
[`increment-0-evidence/review-verification.md`](increment-0-evidence/review-verification.md).)

So the honest ceiling is narrower than "cannot draw", but it is still a real ceiling, and it still
points the same way:

- you **cannot inject a custom `TextRun`** — proofs 1 and 2 stand unmodified;
- you therefore **cannot control the embed's metrics or identity**: its advance width and height are
  whatever the *glyph* is, so the "embed" cannot size itself to its content, and there is no object
  to hit-test, select, or resize as a unit;
- the painted content is clipped to the glyph's box, and `ForegroundBrush` is additionally clipped to
  the glyph *shape*.

A custom font supplying an icon glyph with a tuned advance is a further variation on the same
workaround; it was not tested. Both are hacks that would fail AC4's real requirements.

### One correction worth flagging

The naive check "does the layout contain a `DrawableTextRun`?" **passes on the C1 path** — and
means nothing, because **Avalonia's own `ShapedTextRun` derives from `DrawableTextRun`**. All 7
runs above are `DrawableTextRun`s. The meaningful question is whether a run type from *outside*
Avalonia's assembly can appear, and it cannot. Anyone writing tests in this area should assert on
the concrete type, not the base type.

### What C1 means for the architecture

§5.3's C1-then-C2 reasoning holds exactly as written. Because A2 validated, **C1 is not needed as
the fallback** — but it remains a fine path for read-only styled text (a future `RichTextBlock`
display control could use it) provided embeds are never required.

---

## 4. Bidi and grapheme clusters

**Verdict: ✅ validated for arrow keys and selection; backspace behaves differently from §8
scenario 3's description.**

Raw report: [`increment-0-evidence/bidi-graphemes.md`](increment-0-evidence/bidi-graphemes.md).

Test strings: `"Hello שלום world עולם 123"` (25 UTF-16 units) and
`"Family 👨‍👩‍👧‍👦 then é done"` with a 4-person ZWJ family emoji and a combining acute (31 units).
Every caret stop was cross-checked against reference grapheme boundaries from
`System.Globalization.StringInfo`.

Font resolution was recorded to rule out a missing-glyph artefact: the ZWJ run resolves to
**Segoe UI Emoji**, so these are genuine shaping results.

### Arrow keys never split a cluster ✅

For the emoji string, reference boundaries are `0,1,…,7,18,19,…,24,26,27,…,31`.

```
Next     from 0:  0 -> ... -> 7 -> 18 -> 19 -> ... -> 24 -> 26 -> ... -> 31
Previous from 31: 31 -> ... -> 26 -> 24 -> ... -> 18 -> 7 -> ... -> 0
```

The 11-unit family emoji is traversed in a single step in both directions (7 ↔ 18), and the
combining sequence in one step (24 ↔ 26). **Every** forward and backward stop lands on a reference
grapheme boundary, in both the bidi and the cluster string. **[live]** confirmed: one Right press
at position 7 moved the caret to 18.

### ⚠️ [revised] The `StringInfo` oracle is one-way, and can false-pass

The assertion above is a **subset** test: every Avalonia stop must be a `StringInfo` boundary. That
catches Avalonia stopping somewhere `StringInfo` forbids, but *not* Avalonia stopping somewhere
`StringInfo` wrongly permits — and the two disagree, because .NET's segmentation predates UAX #29's
GB9c Indic-conjunct rule while Avalonia 12.1.1 implements it. Measured on `क्ष`
(U+0915 U+094D U+0937):

```
StringInfo boundaries : {0,2,3}
Avalonia caret stops  : {0,3}
subset assertion holds: True      ← the probe passes
oracles actually agree: False     ← but they disagree
```

Avalonia is the *more* correct of the two here. The consequence is that the passing result above is
weaker evidence than it looked: a regression in which Avalonia started stopping at 2 would still
pass this check. Devanagari is called out in `docs/architecture.md` §8 and was **not** among the
tested strings, so **Indic conjunct behaviour is untested by this spike**.

Increment 1 should assert *equality* against boundaries from Unicode `GraphemeBreakTest` data at a
pinned Unicode version, not subset membership against `StringInfo`, and should include Devanagari.
Evidence: [`increment-0-evidence/review-verification.md`](increment-0-evidence/review-verification.md).

### `GetTextBounds` splits correctly across the bidi boundary ✅

`GetTextBounds(3, 8)` — one logically contiguous range — returns **3 disjoint rects**:

| rect | flow | runs |
|---|---|---|
| (29.5, 0) 22.04×26.6 | LeftToRight | [3+3] |
| (51.54, 0) 45.95×26.6 | **RightToLeft** | [6+4] |
| (97.49, 0) 5.48×26.6 | LeftToRight | [10+1] |

The whole line yields 5 rects. Selection rendering must therefore iterate `TextBounds` and never
assume a single rectangle per line — which §8 scenario 4 already anticipates.

**[live]** Extending a selection across the boundary showed 1 rect → 2 rects → 3 rects, with the
RTL rect growing *leftward* (x = 81.8 → 70.8 → 65.4 → 51.5) as the selection extended logically
forward. Visually correct bidi selection behaviour
([`increment-0-evidence/bidi-selection.png`](increment-0-evidence/bidi-selection.png)).

### ⚠️ `GetBackspaceCaretCharacterHit` is deliberately sub-cluster

On the bidi string it is uneventful (every stop on a boundary). On the cluster string it stops at
**7 intra-cluster positions**: 25, 16, 15, 13, 12, 10, 9.

Decomposing them:

| stop | inside cluster | code unit before it |
|---|---|---|
| 25 | `é` = `e` + U+0301 | U+0065 (`e`) |
| 16, 13, 10 | the family emoji | U+200D (ZWJ) |
| 15, 12, 9 | the family emoji | low surrogate of a person emoji |

This is not a bug — it is the standard "backspace peels one **component** off a ZWJ or combining
sequence" behaviour. Pressing Backspace on 👨‍👩‍👧‍👦 removes the boy and leaves 👨‍👩‍👧, matching most
platform text editors. It never lands **inside a surrogate pair** (so it never produces invalid
UTF-16), and it advances monotonically toward 0.

**[live]** confirmed: Backspace at 18 landed on 16; a subsequent Left arrow from 16 recovered
cleanly to 7.

But §8 scenario 3 currently reads: *"arrow keys and Backspace across '👨‍👩‍👧‍👦' … never split a
cluster. Uses `GetBackspaceCaretCharacterHit`."* **As written, that test would fail** — and it
would fail because the assertion is wrong, not because Avalonia is. Vellum must consciously choose
a policy:

- **(i) component-peel** — pass `GetBackspaceCaretCharacterHit` through as-is. Matches Word,
  browsers, and most native editors. Recommended.
- **(ii) whole-cluster delete** — snap the result back to the enclosing `StringInfo` boundary.
  Simpler to explain; diverges from platform convention.

> **DECIDED (2026-08-11): (i) component-peel.** Vellum passes
> `GetBackspaceCaretCharacterHit` through unmodified. Backspace on 👨‍👩‍👧‍👦 removes the boy and
> leaves 👨‍👩‍👧; Backspace on `é` (combining) removes the acute and leaves `e`.
>
> Consequences for Increment 2:
> - No snapping layer is needed — this is the *cheaper* option as well as the conventional one.
> - The §8 scenario 3 test must assert **component-peel plus UTF-16 validity**, not whole-cluster
>   deletion. Concretely: every backspace stop is a valid UTF-16 index (never inside a surrogate
>   pair), the sequence is monotonic toward 0, and clusters are peeled one component at a time.
> - **Arrow keys remain whole-cluster** — this decision applies to Backspace only. That asymmetry
>   is intentional and matches platform behaviour; it should be stated in the test's name so it
>   is not later "fixed" into consistency.
> - Delete-forward is *not* covered by this measurement. Avalonia exposes no
>   `GetDeleteCaretCharacterHit`, so Increment 2 must choose its own forward-delete policy;
>   symmetry with component-peel is the obvious default but is unverified here.
>
> **[revised] One consequence discovered after this decision was recorded.** The probe logged caret
> *positions* but never applied the deletions, so it never saw what the string becomes. Applying
> them shows component-peel passes through **intermediate states ending in a dangling ZWJ**:
>
> | from | to | deleted | resulting emoji region | dangling? |
> |---|---|---|---|---|
> | 18 | 16 | boy | `👨<ZWJ>👩<ZWJ>👧<ZWJ>` | **yes** |
> | 16 | 15 | `<ZWJ>` | `👨<ZWJ>👩<ZWJ>👧` | no |
> | 15 | 13 | girl | `👨<ZWJ>👩<ZWJ>` | **yes** |
> | 13 | 12 | `<ZWJ>` | `👨<ZWJ>👩` | no |
> | 12 | 10 | woman | `👨<ZWJ>` | **yes** |
>
> So a single Backspace on 👨‍👩‍👧‍👦 does **not** cleanly yield 👨‍👩‍👧 as stated above — it yields
> `👨‍👩‍👧‍` with a trailing ZWJ, and it takes a *second* Backspace to drop that ZWJ. Whether the
> trailing ZWJ is visible depends on the font's fallback rendering.
>
> This does not reverse the decision — component-peel is still the platform-conventional choice —
> but Increment 2 should **absorb a trailing ZWJ into the same keystroke** so one Backspace moves
> 👨‍👩‍👧‍👦 → 👨‍👩‍👧 in one visible step. That is a small addition to the "no snapping layer needed"
> claim above: no *cluster* snapping is needed, but ZWJ absorption is.
> Evidence: [`increment-0-evidence/review-verification.md`](increment-0-evidence/review-verification.md).


---

## 5. Increment 1 recommendation

**Proceed with Increment 1 exactly as designed in §10.** Nothing found here touches the model
layer, positions, `Step`/`Transaction`/`Mapping`, schema validation, or history — Increment 1 is
entirely headless and entirely unaffected.

The premises that Increment 1 rests on are intact:

- **B2 (per-paragraph `TextLayout`) is confirmed and is now better justified than before.** A1's
  failure *for edits* makes wholesale paragraph rebuild the only path on the edit route, and the
  measurement shows it is cheap: roughly 0.35–0.6 ms for 800 characters and ~0.05 ms for 100, i.e.
  at least ~25× inside a frame budget.
- **C2 (custom `ITextSource` + `EmbedRun`) is confirmed viable**, so §4.7's design and the single
  additive block offset from `HitTestPoint` to model positions both stand. Increment 4's inline
  images have a validated foundation.
- **AC11's grapheme and bidi requirements are achievable** with the APIs cited, subject to the
  backspace policy choice above.

Carry-forward items for later increments, none blocking:

| when | item |
|---|---|
| Increment 2 | **[revised]** Keep a per-paragraph `TextRunCache`, driven by lifetime: `Invalidate()` on a text edit, reuse untouched across width-only relayout (≈3.5×). **Never** `InvalidateFrom(k>0)` — it returns the pre-edit layout. |
| Increment 2 | Use `TextHitTestResult.TextPosition` directly; never add `IsTrailing`. |
| Increment 2 | Account for `TextEndOfParagraph.Length == 1` when reconciling line lengths to model offsets. |
| Increment 2 | Backspace is component-peel (decided, §4) — and must **absorb a trailing ZWJ** into the same keystroke. |
| Increment 2 | Grapheme tests must assert **equality** against pinned Unicode `GraphemeBreakTest` data, not subset membership against `StringInfo`, and must include Devanagari. |
| Increment 4 | `EmbedRun` **must** override `Properties`; a null there crashes only at paint time. |
| Increment 4 | Use `BaselineAlignment.Baseline`; `Top`/`TextTop` are offset by Avalonia's baseline formula (−10.078px in the tested case). |
| Increment 4 | Define a policy for embeds wider than the wrap width — the formatter lets them overflow. |

---

## 6. Changes made to `docs/architecture.md`

> **Status: applied (2026-08-11), on the author's instruction.** These were proposals while this
> spike was in review; all three have now been made, plus two consequential updates the review
> surfaced. The wording below is what was proposed — see `docs/architecture.md` for what was
> actually written, which is slightly expanded in places.

Applied:

| # | where | change |
|---|---|---|
| **P1** | §2.3 A1, §9 R3 | A1 recorded as *partly* invalidated; R3 dropped Medium → Low as realised-and-resolved |
| **P2** | §4.7 | `EmbedRun.Properties` added to the sketch, with the paint-time crash and draw-origin notes |
| **P3** | §8 scenario 3 | Backspace corrected to component-peel + ZWJ absorption; grapheme oracle changed to `GraphemeBreakTest` equality |
| — | §2.3 A2 | marked validated, with the three traps referenced |
| — | §10 | Increment 0 marked complete and linked here |

**P1 — §2.3 / §9 R3: record A1 as *partly* invalidated.** Suggested wording for the A1 bullet:

> **A1** — ~~`TextRunCache.InvalidateFrom` gives usable incremental reshaping for single-paragraph
> edits.~~ **Invalidated for edits (Increment 0).** `InvalidateFrom(k>0)` returns a stale, pre-edit
> layout — invalidation is entry-granular and a paragraph is one entry keyed 0 — and the correct
> usages (`InvalidateFrom(0)` / `Invalidate()`) measure 1.00× against no cache. R3's fallback,
> wholesale paragraph rebuild, is adopted **for edits**.
> **However**, a persistent `TextRunCache` reused across a *width* change with unchanged text is
> correct and ~3.5× faster, so Vellum keeps one per paragraph and drives it by lifetime rather than
> by offset.

R3's severity could reasonably drop from Medium to Low: the risk has been realised, the fallback
measured, and the cost is zero.

**P2 — §4.7: `EmbedRun` must override `Properties`.** Suggested addition to the sketch:

```csharp
internal sealed class EmbedRun : DrawableTextRun
{
    public override TextRunProperties Properties { get; }   // REQUIRED: null here throws in Draw
    public override Size Size { get; }
    public override double Baseline { get; }
    public override int Length => 1;
    public override void Draw(DrawingContext ctx, Point origin) { /* origin = top-left of the box */ }
}
```

Plus a note that `Draw`'s origin is the run box's top-left, and that `BaselineAlignment.Baseline`
is the only reliably-placed alignment in 12.1.1.

**P3 — §8 scenario 3: correct the backspace claim.** Current text asserts Backspace never splits a
cluster; measurement shows `GetBackspaceCaretCharacterHit` intentionally peels ZWJ and combining
components. Suggested replacement:

> 3. **Grapheme integrity** — arrow keys across "👨‍👩‍👧‍👦", "é" (combining), and Devanagari clusters
>    never split a cluster. Backspace follows `GetBackspaceCaretCharacterHit`, which peels one
>    component off a ZWJ/combining sequence (platform-conventional); the test asserts that
>    behaviour and that no operation ever produces invalid UTF-16.

---

## 7. Evidence index

| file | contents |
|---|---|
| [`increment-0-evidence/a2-drawabletextrun.md`](increment-0-evidence/a2-drawabletextrun.md) | A2: wrapping sweep, alignment matrix, draw-origin derivation, `GetTextBounds`, hit-test sweeps, null-`Properties` crash |
| [`increment-0-evidence/a1-diagnostic.md`](increment-0-evidence/a1-diagnostic.md) | **A1's decisive evidence**: offset sweep, stale-text proof, `GetTextRun` call counts |
| [`increment-0-evidence/a1-textruncache-stopwatch.md`](increment-0-evidence/a1-textruncache-stopwatch.md) | A1 timings incl. the correct-usage 1.00× table and percentiles |
| [`increment-0-evidence/a1-benchmarkdotnet.md`](increment-0-evidence/a1-benchmarkdotnet.md) | A1 via BenchmarkDotNet — retained because it reproduces the *illusion* |
| [`increment-0-evidence/c1-textstyleoverrides.md`](increment-0-evidence/c1-textstyleoverrides.md) | C1: styled runs, constructor surface, `TextRunProperties` surface, U+FFFC glyph |
| [`increment-0-evidence/bidi-graphemes.md`](increment-0-evidence/bidi-graphemes.md) | Caret walks vs `StringInfo`, backspace decomposition, `GetTextBounds` rects |
| [`increment-0-evidence/a1-width-only-relayout.md`](increment-0-evidence/a1-width-only-relayout.md) | **[revised]** A1 part 2: width-only relayout correctness per width, `GetTextRun` counts, 7-rep timing |
| [`increment-0-evidence/review-verification.md`](increment-0-evidence/review-verification.md) | **[revised]** Re-tests of three challenged claims: DrawingBrush loophole, `StringInfo` oracle, dangling ZWJ |
| `increment-0-evidence/*.png` | 29 rendered captures: wrapping, alignment matrix, baseline sweep, bitmap embed, C1 styled + tofu, bidi and cluster rendering, selection rects |

---

## 8. Limitations — what this spike does *not* establish

Recorded honestly, because the first version of this report stated several things more confidently
than the evidence supported. Adversarial review found these; they are listed rather than quietly
fixed.

**Untested cases that a reader might assume were covered:**

- **Embeds in RTL/bidi context** — every A2 measurement used a single LTR line. Embeds inside
  right-to-left runs, selection across an embed on a *wrapped* line, and hit-testing exactly at line
  boundaries are all untested.
- **Indic conjuncts** — no Devanagari string was in the caret walk, despite §8 naming it.
- **`IsTrailing` at line ends / below the layout** — only interior points were sampled.
- **The oversized-embed case was never rendered**, so "not clipped" is inferred from geometry, not
  observed.
- **Wrapping was sampled at 7 widths**, not swept continuously; the "embed appears exactly once"
  assertion was checked on the 400px layout only.

**Harness weaknesses — these mean "0 failures" is weaker evidence than it looks:**

- The probe process **always exits 0**, whatever the failure count, and an unrecognised mode runs
  nothing and reports success. Every "0 failures" in this document was read from stdout, not from an
  exit code, but a CI harness built this way would be worthless.
- Several checks are **too broad to fail usefully**: the null-`Properties` check accepts *any*
  exception rather than the specific `ArgumentOutOfRangeException`; the `Top`/`TextTop` displacement
  is printed but never asserted; the bidi walk would pass if a caret API simply returned its input.
- The documented `bench` command **does not regenerate** `increment-0-evidence/a1-benchmarkdotnet.md`; that file
  was transformed by hand from BenchmarkDotNet's artifacts.
- The bidi evidence is **Windows-font-dependent** (`Typeface.Default` resolving emoji to Segoe UI
  Emoji); results on another platform are unverified.

None of these change the three verdicts, which rest on the direct measurements quoted above. They do
mean Increment 1's test suite should not be modelled on this harness.

**Disposal.** Per §10 this spike was throwaway, and **it has been deleted**. This document and
[`increment-0-evidence/`](increment-0-evidence/) are the retained artefacts. The code is recoverable
from git history at commit `766c3ef` if a measurement ever needs re-running.
