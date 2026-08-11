# A1 diagnostic — is the cached layout stale, and is that our fault?
_generated 2026-08-11 13:44:11_

The timing harness found the `InvalidateFrom` arm returning identical geometry for every edit. This section establishes what the cache actually does.

## 1. Control — cache reused with the text completely unchanged

- prime  : 0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227
- reuse  : 0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227
- nocache: 0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227

- PASS — with unchanged text, a reused cache reproduces the uncached layout exactly

## 2. After a 1-char insert, sweep the InvalidateFrom offset

Insert one character at index 10 of a 100-char paragraph, then build with a primed cache after calling `InvalidateFrom(k)` for each k below. `expected` is the uncached layout of the edited text.

expected (uncached, edited text): `0,62,460.2656,21.2813,17.2656;62,40,295.71,23.9414,19.4238|460.2656x45.2227`
edited text length: 101; sum of line lengths expected: 102 (incl. TextEndOfParagraph)

| InvalidateFrom(k)|sum of line lengths|matches uncached?|fingerprint |
|---|---|---|---|
| 0|102|**yes**|`0,62,460.2656,21.2813,17.2656;62,40,295.71,23.9414,19.4238|460.2656x45.2227` |
| 1|101|no|`0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227` |
| 5|101|no|`0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227` |
| 9|101|no|`0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227` |
| 10|101|no|`0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227` |
| 11|101|no|`0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227` |
| 25|101|no|`0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227` |
| 50|101|no|`0,65,479.6641,21.2813,17.2656;65,36,266.9043,23.9414,19.4238|479.6641x45.2227` |

- PASS — *some* InvalidateFrom offset yields a layout matching the uncached result

## 3. Does full `Invalidate()` produce a correct layout?

Correct layouts after full `Invalidate()`: 15/15

- PASS — full `Invalidate()` always reproduces the uncached layout after an edit

## 4. Does a fresh cache per edit produce a correct layout?

Correct layouts with a fresh cache per edit: 15/15

- PASS — a fresh `TextRunCache` per build reproduces the uncached layout

## 5. Confirming the failure mode is stale *text*, not stale metrics

- source text now  : `the quick Zbrown fox jumps ove...` (len 101)
- text in the runs : `the quick brown fox jumps over...` (len 100)

- PASS — confirmed: after `InvalidateFrom(10)` the shaped runs hold the **pre-edit** text — 100 chars, and the inserted 'Z' is absent. The stale result is in the text itself, not merely in cached metrics

## 6. How much does the formatter re-query the source in each mode?

An instrumented source counts `GetTextRun` calls. This shows directly whether the formatter re-reads the edited text or serves the whole paragraph from the cache.

| mode|GetTextRun calls|indices requested|layout correct? |
|---|---|---|---|
| no cache|6|0,25,50,75,100,101|**yes** |
| Invalidate()|6|0,25,50,75,100,101|**yes** |
| InvalidateFrom(0)|6|0,25,50,75,100,101|**yes** |
| InvalidateFrom(10)|0|—|no |
| InvalidateFrom(50)|0|—|no |

A mode that re-queries **zero** positions cannot possibly reflect the edit — its speed is the speed of returning a stale result.

## A1 diagnostic result

Checks failed: 0
