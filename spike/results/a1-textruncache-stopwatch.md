# A1 — `TextRunCache.InvalidateFrom` (stopwatch harness)
_generated 2026-08-11 14:18:56_

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
| 100|10%|69.351|15.455 (0.22x)|63.238 (0.91x)|67.869 (0.98x)|46.904 (0.68x) |
| 100|50%|51.143|14.623 (0.29x)|49.985 (0.98x)|49.623 (0.97x)|56.426 (1.10x) |
| 100|90%|47.218|10.495 (0.22x)|44.067 (0.93x)|44.969 (0.95x)|43.51 (0.92x) |
| 400|10%|171.508|50.26 (0.29x)|173.922 (1.01x)|184.874 (1.08x)|226.171 (1.32x) |
| 400|50%|266.344|52.857 (0.20x)|219.408 (0.82x)|196.213 (0.74x)|176.684 (0.66x) |
| 400|90%|175.093|54.418 (0.31x)|169.185 (0.97x)|171.631 (0.98x)|169.754 (0.97x) |
| 800|10%|427.632|115.879 (0.27x)|404.815 (0.95x)|361.855 (0.85x)|376.495 (0.88x) |
| 800|50%|374.163|105.349 (0.28x)|353.113 (0.94x)|333.094 (0.89x)|403.063 (1.08x) |
| 800|90%|334.237|101.599 (0.30x)|351.66 (1.05x)|335.226 (1.00x)|332.677 (1.00x) |

**The `InvalidateFrom(edit)` column is not a real result.** The cross-check above and the diagnostic report show that arm returns the pre-edit layout; it is timing the cost of returning a stale answer. The honest comparison is NoCache versus `InvalidateFrom(0)` / `Invalidate()`, which are the only cached usages that produce a correct layout.

Percentiles for the two arms that matter — the uncached baseline versus the fastest *correct* cached usage — at 800 chars / 50%:
| arm|mean µs|p50 µs|p95 µs |
|---|---|---|---|
| NoCache|334.456|299.7|372.5 |
| InvalidateFrom(0)|335.078|300.2|384.6 |

Speedup from the correct cached path: **1.00x**.

## A1 (stopwatch) result

Checks failed: 0
