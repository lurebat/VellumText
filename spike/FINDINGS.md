# Increment 0 — validation spike findings

Spike for [`docs/architecture.md`](../docs/architecture.md) §10 increment 0. This is throwaway
code; it exists to answer three questions with measurement rather than reasoning, and it should be
deleted once its conclusions are absorbed.

**Bottom line**

| # | Assumption | Verdict |
|---|---|---|
| **A2** | `DrawableTextRun` in a custom `ITextSource` participates correctly in line breaking, baseline alignment, and `GetTextBounds` | ✅ **Validated** — with three traps worth knowing about |
| **A1** | `TextRunCache.InvalidateFrom` gives usable incremental reshaping | ❌ **Invalidated** — `InvalidateFrom(k>0)` returns a *stale* layout; used correctly it is 1.00× |
| **C1** | The `textStyleOverrides` shortcut | ✅ **Validated as a ceiling** — good for character formatting, structurally cannot express embeds |
| — | Bidi & grapheme caret sanity | ✅ **Validated**, except backspace, which is intentionally sub-cluster (see §4) |

**Recommendation: Increment 1 proceeds as designed.** No architectural change is required. Three
small documentation corrections are proposed in §6 — proposed only; `docs/architecture.md` has not
been modified.

---

## 0. How this was measured

### Environment

| | |
|---|---|
| Avalonia | 12.1.1 |
| Target framework | **`net8.0`** |
| Runtime | .NET 8.0.423, Release configuration |
| OS | Windows, x64 (Hyper-V guest — noted because it widens benchmark variance) |
| Rendering | real Skia, not the headless no-op drawing backend |

**On the target framework:** the brief asked for .NET 9 with .NET 8 as fallback. **.NET 9 is not
installed on this machine** (SDKs present: 5.0.408, 6.0.428, 8.0.423, 10.0.302). Avalonia 12.1.1
ships `lib/net8.0` and `lib/net10.0`. `net8.0` was chosen as the stated fallback rather than
net10.0, so the spike measures the runtime Vellum is most likely to support. Nothing in these
findings is framework-sensitive: they are all about Avalonia's text formatter.

### Layout

```
spike/
  Vellum.Spike.sln
  Directory.Build.props        common TFM / Avalonia version
  Shared/                      SpikeTextSource, BoxRun, ImageRun, properties subclasses
  Vellum.Spike.Probe/          console; deterministic assertion probes + benchmarks
  Vellum.Spike.App/            Avalonia window; live interaction + PNG capture
  results/                     committed evidence: 6 markdown reports + 25 PNGs
```

Reproduce:

```
cd spike/Vellum.Spike.Probe
dotnet run -c Release -- a2       # A2 probes
dotnet run -c Release -- c1       # C1 probes
dotnet run -c Release -- bidi     # bidi/grapheme probes
dotnet run -c Release -- a1diag   # A1 cache diagnostic   <- the decisive one
dotnet run -c Release -- a1       # A1 stopwatch timings
dotnet run -c Release -- bench    # A1 BenchmarkDotNet

cd ../Vellum.Spike.App
dotnet run -c Debug               # the interactive window
```

All five probes currently report **0 failed checks**. Note that "0 failures" means *the probe's
assertions hold*, not *the assumption is validated* — where behaviour turned out to be
disappointing, the assertion was rewritten to assert the disappointing behaviour precisely, so
that a future Avalonia upgrade that changes it will fail loudly. A1 is the clearest example.

### The spike was run, not merely compiled

Headless probes produce the numbers. The app was then launched and driven interactively — mouse
clicks into the text, arrow keys, Backspace, and an extend-selection mode — and its live readout
matched the headless numbers exactly. Specific live confirmations are recorded inline below and
marked **[live]**.

---

## 1. A2 — `DrawableTextRun` inside a custom `ITextSource`

**Verdict: ✅ validated.** The architecture's §4.7 design works. An `ITextSource` returning
`TextCharacters` with varying `TextRunProperties`, plus custom `DrawableTextRun` subclasses at
`\uFFFC` positions with `Length => 1`, formats, wraps, hit-tests and measures correctly.

Raw report: [`results/a2-drawabletextrun.md`](results/a2-drawabletextrun.md).

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
break ([`results/a2-wrapped.png`](results/a2-wrapped.png)).

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
exactly, so this is specific to the top-anchored alignments. This looks like an upstream Avalonia
bug; it was not root-caused, since the practical answer is simply **use
`BaselineAlignment.Baseline`** (which is what §4.7 implies anyway).

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

Raw reports: [`results/a1-diagnostic.md`](results/a1-diagnostic.md) (decisive),
[`results/a1-textruncache-stopwatch.md`](results/a1-textruncache-stopwatch.md),
[`results/a1-benchmarkdotnet.md`](results/a1-benchmarkdotnet.md).

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
`results/a1-benchmarkdotnet.md` is committed rather than deleted.

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
(20,000 measured iterations after 2,000 warmup, 480px wrap, style-run boundary every 25 chars):

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

### Two structural notes

1. **Avalonia exposes no re-layout-in-place API.** `TextLayout` has no "reflow" method. The only
   reuse mechanism is passing the same `TextRunCache` into a **new** `TextLayout`. So even in the
   best case this measures shaping/run reuse, never layout-object reuse — a ceiling on how much
   A1 could ever have been worth.
2. Whether the staleness is an Avalonia bug or an undocumented contract (perhaps `InvalidateFrom`
   assumes the source's *content before the offset* is byte-identical, which an insert violates by
   shifting everything after it) was not determined. It does not change the conclusion: there is no
   usage of this API that is both correct and faster.

### What A1 means for the architecture

**§9 R3's stated fallback is the right path, and it costs nothing.** Rebuild the paragraph's
`TextLayout` wholesale on edit. Concretely:

- a full rebuild of an 800-char paragraph costs **~376 µs**, comfortably inside a 16 ms frame
  budget with ~40× headroom, which is why B2's paragraph granularity was the right choice;
- **Vellum should not use `TextRunCache` at all** in Increment 2. It adds an object to manage, a
  correctness hazard, and no measurable benefit;
- if paragraph rebuild ever *does* become the bottleneck, the lever is smaller paragraphs or
  caching whole `TextLayout` objects per unmodified paragraph — not `InvalidateFrom`.

A1 being invalidated is a **cheap** negative: it removes an optimization from the design without
requiring any structural change.

---

## 3. C1 — the `textStyleOverrides` shortcut

**Verdict: ✅ validated as a ceiling.** It does what §5.3 says, and its limit is structural rather
than merely awkward.

Raw report: [`results/c1-textstyleoverrides.md`](results/c1-textstyleoverrides.md).

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
([`results/c1-styled.png`](results/c1-styled.png)).

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
   ([`results/c1-objectreplacement.png`](results/c1-objectreplacement.png)).

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

Raw report: [`results/bidi-graphemes.md`](results/bidi-graphemes.md).

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
([`results/bidi-selection.png`](results/bidi-selection.png)).

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

This is a scope decision, so it is proposed in §6 rather than made here.

---

## 5. Increment 1 recommendation

**Proceed with Increment 1 exactly as designed in §10.** Nothing found here touches the model
layer, positions, `Step`/`Transaction`/`Mapping`, schema validation, or history — Increment 1 is
entirely headless and entirely unaffected.

The premises that Increment 1 rests on are intact:

- **B2 (per-paragraph `TextLayout`) is confirmed and is now better justified than before.** A1's
  failure makes wholesale paragraph rebuild the *only* path, and the measurement shows it is
  cheap: ~376 µs for 800 characters, ~53 µs for 100.
- **C2 (custom `ITextSource` + `EmbedRun`) is confirmed viable**, so §4.7's design and the single
  additive block offset from `HitTestPoint` to model positions both stand. Increment 4's inline
  images have a validated foundation.
- **AC11's grapheme and bidi requirements are achievable** with the APIs cited, subject to the
  backspace policy choice above.

Carry-forward items for later increments, none blocking:

| when | item |
|---|---|
| Increment 2 | Do **not** introduce `TextRunCache`. Rebuild paragraph layouts wholesale. |
| Increment 2 | Use `TextHitTestResult.TextPosition` directly; never add `IsTrailing`. |
| Increment 2 | Account for `TextEndOfParagraph.Length == 1` when reconciling line lengths to model offsets. |
| Increment 2 | Decide the backspace policy (§4); recommendation is component-peel. |
| Increment 4 | `EmbedRun` **must** override `Properties`; a null there crashes only at paint time. |
| Increment 4 | Use `BaselineAlignment.Baseline`; `Top`/`TextTop` misplace by −10.078px. |
| Increment 4 | Define a policy for embeds wider than the wrap width — the formatter lets them overflow. |
| Increment 8 | Consider filing the `Top`/`TextTop` offset upstream with the repro in `results/a2-drawabletextrun.md`. |

---

## 6. Proposed changes to `docs/architecture.md`

**`docs/architecture.md` has not been modified.** These are proposals; the decision is the
human's.

**P1 — §2.3 / §9 R3: record A1 as invalidated.** Suggested wording for the A1 bullet:

> **A1** — ~~`TextRunCache.InvalidateFrom` gives usable incremental reshaping for single-paragraph
> edits.~~ **Invalidated (Increment 0).** `InvalidateFrom(k>0)` returns a stale, pre-edit layout;
> used correctly (`InvalidateFrom(0)` / `Invalidate()`) it measures 1.00× against no cache.
> R3's fallback — wholesale paragraph rebuild — is adopted. Vellum does not use `TextRunCache`.

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
| [`results/a2-drawabletextrun.md`](results/a2-drawabletextrun.md) | A2: wrapping sweep, alignment matrix, draw-origin derivation, `GetTextBounds`, hit-test sweeps, null-`Properties` crash |
| [`results/a1-diagnostic.md`](results/a1-diagnostic.md) | **A1's decisive evidence**: offset sweep, stale-text proof, `GetTextRun` call counts |
| [`results/a1-textruncache-stopwatch.md`](results/a1-textruncache-stopwatch.md) | A1 timings incl. the correct-usage 1.00× table and percentiles |
| [`results/a1-benchmarkdotnet.md`](results/a1-benchmarkdotnet.md) | A1 via BenchmarkDotNet — retained because it reproduces the *illusion* |
| [`results/c1-textstyleoverrides.md`](results/c1-textstyleoverrides.md) | C1: styled runs, constructor surface, `TextRunProperties` surface, U+FFFC glyph |
| [`results/bidi-graphemes.md`](results/bidi-graphemes.md) | Caret walks vs `StringInfo`, backspace decomposition, `GetTextBounds` rects |
| `results/*.png` | 25 rendered captures: wrapping, alignment matrix, baseline sweep, bitmap embed, C1 styled + tofu, bidi and cluster rendering, selection rects |

**Disposal.** Per §10 this spike is throwaway. Delete `spike/` once Increment 1 begins; this
document and `results/` are the only artefacts worth keeping, and are best relocated into `docs/`
if the findings are to outlive the code.
