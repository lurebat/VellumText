# A1 — `TextRunCache.InvalidateFrom` (stopwatch harness)
_generated 2026-08-11 13:40:12_

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
| 100|10%|57.048|13.248 (0.23x)|57.143 (1.00x)|51.61 (0.90x)|52.936 (0.93x) |
| 100|50%|52.578|12.672 (0.24x)|53.646 (1.02x)|50.756 (0.97x)|56.213 (1.07x) |
| 100|90%|51.944|13.66 (0.26x)|55.689 (1.07x)|52.103 (1.00x)|53.286 (1.03x) |
| 400|10%|216.64|58.543 (0.27x)|195.27 (0.90x)|196.752 (0.91x)|189.509 (0.87x) |
| 400|50%|193.57|57.808 (0.30x)|193.692 (1.00x)|194.21 (1.00x)|202.528 (1.05x) |
| 400|90%|188.499|57.537 (0.31x)|194.114 (1.03x)|189.068 (1.00x)|214.638 (1.14x) |
| 800|10%|373.098|118.894 (0.32x)|395.241 (1.06x)|381.582 (1.02x)|411.153 (1.10x) |
| 800|50%|378.523|116.95 (0.31x)|375.786 (0.99x)|373.048 (0.99x)|373.027 (0.99x) |
| 800|90%|370.808|116.061 (0.31x)|373.307 (1.01x)|372.51 (1.00x)|372.984 (1.01x) |

**The `InvalidateFrom(edit)` column is not a real result.** The cross-check above and the diagnostic report show that arm returns the pre-edit layout; it is timing the cost of returning a stale answer. The honest comparison is NoCache versus `InvalidateFrom(0)` / `Invalidate()`, which are the only cached usages that produce a correct layout.

Percentiles for the two arms that matter — the uncached baseline versus the fastest *correct* cached usage — at 800 chars / 50%:
| arm|mean µs|p50 µs|p95 µs |
|---|---|---|---|
| NoCache|375.984|348.1|441 |
| InvalidateFrom(0)|376.027|350.4|432.1 |

Speedup from the correct cached path: **1.00x**.

## A1 (stopwatch) result

Checks failed: 0
