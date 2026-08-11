# Bidi and grapheme-cluster sanity check
_generated 2026-08-11 15:24:06_

## bidi (Hebrew + English + digits)

Text (escaped): `Hello שלום world עולם 123`
UTF-16 length: 25
Reference grapheme boundaries (StringInfo): 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25

Lines: 1; runs on line 0: 5
| run|length|typeface |
|---|---|---|
| ShapedTextRun|6|$Default |
| ShapedTextRun|4|$Default |
| ShapedTextRun|7|$Default |
| ShapedTextRun|3|$Default |
| ShapedTextRun|5|$Default |

### GetNextCaretCharacterHit — walking forward from 0

Stops: 0 -> 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9 -> 10 -> 11 -> 12 -> 13 -> 14 -> 15 -> 16 -> 17 -> 18 -> 19 -> 20 -> 21 -> 22 -> 23 -> 24 -> 25

- PASS — every forward caret stop is on a grapheme boundary

### GetPreviousCaretCharacterHit — walking backward from the end

Stops: 25 -> 24 -> 23 -> 22 -> 21 -> 20 -> 19 -> 18 -> 17 -> 16 -> 15 -> 14 -> 13 -> 12 -> 11 -> 10 -> 9 -> 8 -> 7 -> 6 -> 5 -> 4 -> 3 -> 2 -> 1 -> 0

- PASS — every backward caret stop is on a grapheme boundary

### GetBackspaceCaretCharacterHit — from the end

Stops: 25 -> 24 -> 23 -> 22 -> 21 -> 20 -> 19 -> 18 -> 17 -> 16 -> 15 -> 14 -> 13 -> 12 -> 11 -> 10 -> 9 -> 8 -> 7 -> 6 -> 5 -> 4 -> 3 -> 2 -> 1 -> 0


- PASS — no backspace stop ever lands **inside a surrogate pair** (never produces invalid UTF-16)
- PASS — backspace stops advance monotonically toward 0 (no oscillation)

## clusters (family ZWJ + combining acute)

Text (escaped): `Family \uD83D\uDC68[ZWJ]\uD83D\uDC69[ZWJ]\uD83D\uDC67[ZWJ]\uD83D\uDC66 then e\u0301 done`
UTF-16 length: 31
Reference grapheme boundaries (StringInfo): 0,1,2,3,4,5,6,7,18,19,20,21,22,23,24,26,27,28,29,30,31

Lines: 1; runs on line 0: 3
| run|length|typeface |
|---|---|---|
| ShapedTextRun|7|$Default |
| ShapedTextRun|12|Segoe UI Emoji |
| ShapedTextRun|12|$Default |

### GetNextCaretCharacterHit — walking forward from 0

Stops: 0 -> 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 18 -> 19 -> 20 -> 21 -> 22 -> 23 -> 24 -> 26 -> 27 -> 28 -> 29 -> 30 -> 31

- PASS — every forward caret stop is on a grapheme boundary

### GetPreviousCaretCharacterHit — walking backward from the end

Stops: 31 -> 30 -> 29 -> 28 -> 27 -> 26 -> 24 -> 23 -> 22 -> 21 -> 20 -> 19 -> 18 -> 7 -> 6 -> 5 -> 4 -> 3 -> 2 -> 1 -> 0

- PASS — every backward caret stop is on a grapheme boundary

### GetBackspaceCaretCharacterHit — from the end

Stops: 31 -> 30 -> 29 -> 28 -> 27 -> 26 -> 25 -> 24 -> 23 -> 22 -> 21 -> 20 -> 19 -> 18 -> 16 -> 15 -> 13 -> 12 -> 10 -> 9 -> 7 -> 6 -> 5 -> 4 -> 3 -> 2 -> 1 -> 0

Backspace stops **inside** grapheme clusters at: 25,16,15,13,12,10,9.

Decomposing those positions:
- 25 is inside the cluster [24,26) = `e\u0301`; the code unit before it is U+0065
- 16 is inside the cluster [7,18) = `\uD83D\uDC68[ZWJ]\uD83D\uDC69[ZWJ]\uD83D\uDC67[ZWJ]\uD83D\uDC66`; the code unit before it is U+200D
- 15 is inside the cluster [7,18) = `\uD83D\uDC68[ZWJ]\uD83D\uDC69[ZWJ]\uD83D\uDC67[ZWJ]\uD83D\uDC66`; the code unit before it is U+DC67
- 13 is inside the cluster [7,18) = `\uD83D\uDC68[ZWJ]\uD83D\uDC69[ZWJ]\uD83D\uDC67[ZWJ]\uD83D\uDC66`; the code unit before it is U+200D
- 12 is inside the cluster [7,18) = `\uD83D\uDC68[ZWJ]\uD83D\uDC69[ZWJ]\uD83D\uDC67[ZWJ]\uD83D\uDC66`; the code unit before it is U+DC69
- 10 is inside the cluster [7,18) = `\uD83D\uDC68[ZWJ]\uD83D\uDC69[ZWJ]\uD83D\uDC67[ZWJ]\uD83D\uDC66`; the code unit before it is U+200D
- 9 is inside the cluster [7,18) = `\uD83D\uDC68[ZWJ]\uD83D\uDC69[ZWJ]\uD83D\uDC67[ZWJ]\uD83D\uDC66`; the code unit before it is U+DC68

This is deliberate: backspace peels one **component** off a ZWJ sequence or combining sequence, rather than deleting the whole cluster.

- PASS — no backspace stop ever lands **inside a surrogate pair** (never produces invalid UTF-16)
- PASS — backspace stops advance monotonically toward 0 (no oscillation)
- NOTE — backspace is **not** whole-cluster: it stops at 7 intra-cluster position(s). Arrow-key movement above is unaffected. See the verdict section for what this means for AC11 / §8 scenario 3.

## GetTextBounds across the bidi boundary

Hebrew begins at index 6.


### GetTextBounds(3, 8) — 3 Latin chars into the Hebrew word

| rect|flow|runs |
|---|---|---|
| (29.5, 0) 22.04x26.6|LeftToRight|[3+3] |
| (51.54, 0) 45.95x26.6|RightToLeft|[6+4] |
| (97.49, 0) 5.48x26.6|LeftToRight|[10+1] |

Rect count: 3; distinct flow directions: LeftToRight,RightToLeft

### GetTextBounds(4, 12) — spanning the Latin/Hebrew boundary widely

| rect|flow|runs |
|---|---|---|
| (34.35, 0) 17.2x26.6|LeftToRight|[4+2] |
| (51.54, 0) 45.95x26.6|RightToLeft|[6+4] |
| (97.49, 0) 55.22x26.6|LeftToRight|[10+6] |

Rect count: 3; distinct flow directions: LeftToRight,RightToLeft

### GetTextBounds(0, 25) — the whole line

| rect|flow|runs |
|---|---|---|
| (0, 0) 51.54x26.6|LeftToRight|[0+6] |
| (51.54, 0) 45.95x26.6|RightToLeft|[6+4] |
| (97.49, 0) 60.7x26.6|LeftToRight|[10+7] |
| (158.19, 0) 32.34x26.6|LeftToRight|[22+3] |
| (190.54, 0) 47.33x26.6|RightToLeft|[17+5] |

Rect count: 5; distinct flow directions: LeftToRight,RightToLeft

- PASS — a logically contiguous selection across the bidi boundary yields multiple rects (3 rects)
- PASS — those rects report differing FlowDirection

## Bidi/grapheme result

Checks failed: 0
