# A1 — `TextRunCache.InvalidateFrom` (stopwatch harness)
_generated 2026-08-11 15:24:08_

Avalonia exposes no re-layout-in-place API: the only reuse mechanism is passing the same `TextRunCache` into a **new** `TextLayout`. So this measures shaping/run reuse, not layout-object reuse.

Warmup 2,000 iterations, measured 20,000 iterations per arm, wrap width 480px, style-run boundary every 25 chars.

## Correctness cross-check — do the arms produce identical geometry?

- MISMATCH len=100 pct=10 edit=0
  fresh : 0,62,459.2813,21.2813,17.2656;62,40,295.71,23.9414,19.4238|459.2813x45.2227
  cached: 0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227
- MISMATCH len=100 pct=10 edit=1
  fresh : 0,62,460.5469,21.2813,17.2656;62,40,295.71,23.9414,19.4238|460.5469x45.2227
  cached: 0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227
- MISMATCH len=100 pct=10 edit=2
  fresh : 0,62,458.5313,21.2813,17.2656;62,40,295.71,23.9414,19.4238|458.5313x45.2227
  cached: 0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227
- MISMATCH len=100 pct=10 edit=3
  fresh : 0,62,460.5625,21.2813,17.2656;62,40,295.71,23.9414,19.4238|460.5625x45.2227
  cached: 0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227
- MISMATCH len=100 pct=10 edit=4
  fresh : 0,62,459.5078,21.2813,17.2656;62,40,295.71,23.9414,19.4238|459.5078x45.2227
  cached: 0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227

Mismatches: 360/360. Every arm here used `InvalidateFrom(offset)` with `offset > 0`. The A1 diagnostic section establishes that this returns the **pre-edit** layout, so a mismatch is the expected — and disqualifying — outcome, not a harness fault.

- PASS — `InvalidateFrom(offset > 0)` reproducibly yields a stale layout on **every** edit (a consistent, reproducible defect rather than a flaky one)

## Timings

| chars|edit at|NoCache µs|InvalidateFrom(edit) µs **[STALE]**|InvalidateFrom(0) µs|Invalidate() µs|FreshCache µs |
|---|---|---|---|---|---|---|
| 100|10%|44.84|10.224 (0.23x)|43.421 (0.97x)|44 (0.98x)|43.656 (0.97x) |
| 100|50%|44.021|10.709 (0.24x)|45.594 (1.04x)|44.778 (1.02x)|46.546 (1.06x) |
| 100|90%|43.03|10.195 (0.24x)|43.51 (1.01x)|44.029 (1.02x)|44.29 (1.03x) |
| 400|10%|167.252|49.024 (0.29x)|166.545 (1.00x)|167.666 (1.00x)|169.322 (1.01x) |
| 400|50%|173.355|49.371 (0.28x)|170.876 (0.99x)|175.942 (1.01x)|169.093 (0.98x) |
| 400|90%|170.258|51.11 (0.30x)|169.461 (1.00x)|167.649 (0.98x)|168.128 (0.99x) |
| 800|10%|327.816|98.662 (0.30x)|327.19 (1.00x)|328.439 (1.00x)|330.907 (1.01x) |
| 800|50%|328.689|98.065 (0.30x)|337.09 (1.03x)|332.436 (1.01x)|329.283 (1.00x) |
| 800|90%|326.552|98.506 (0.30x)|335.328 (1.03x)|329.128 (1.01x)|336.032 (1.03x) |

**The `InvalidateFrom(edit)` column is not a real result.** The cross-check above and the diagnostic report show that arm returns the pre-edit layout; it is timing the cost of returning a stale answer. The honest comparison is NoCache versus `InvalidateFrom(0)` / `Invalidate()`, which are the only cached usages that produce a correct layout.

Percentiles for the two arms that matter — the uncached baseline versus the fastest *correct* cached usage — at 800 chars / 50%:
| arm|mean µs|p50 µs|p95 µs |
|---|---|---|---|
| NoCache|333.789|300.4|377.1 |
| InvalidateFrom(0)|348.045|302.4|485.3 |

Speedup from the correct cached path: **0.96x**.

## A1 (stopwatch) result

Checks failed: 0
