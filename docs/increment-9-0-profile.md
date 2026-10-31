# Increment 9.0 — profiling the long-paragraph and table costs

Phase 9 of `docs/plans/2026-10-25-codebase-audit-remediation.md` starts with a profile, because
the design of its rewrites depends on which term is superlinear (OD-6). Measured on 2026-10-27 on
a quiet machine (CPU idle apart from about 5% background UI, confirmed before each run), Release
build of the phase 10 integration branch (`9be6d26`), `bench/VellumText.Bench`.

**Bottom line: both rewrites are justified.**

| Question | Answer |
|---|---|
| Where does a keystroke in a long formatted paragraph go? | Into Avalonia's `TextLayout` rebuilding the whole paragraph (A1's documented wholesale rebuild). The model side is linear and small. |
| Which term is superlinear? | Avalonia's line wrapping copies the remaining text-run list once per line: work proportional to lines × runs. Unformatted text has one run, so it is linear; formatting makes the number of runs grow with the paragraph, so the product is quadratic. |
| Can VellumText fix it without 9.1? | No. The copy is inside `TextFormatterImpl`, and runs cannot be merged — each carries different formatting. The only lever is giving each layout fewer lines and fewer runs, which is what line-range sub-layouts do. |
| Where does a large table's frame go? | `TableBlockView.Render` draws every cell's paragraph whatever the viewport shows (93% of the frame), and `Measure` measures every cell. Linear in rows, so not a scanning bug, but at 10,000 rows it is about 130 ms a frame. |

## Measurements

`a-longpara`, keystroke in the middle of one paragraph, through the view:

| characters | no marks | every other word bold (~len/12 spans) | model insert only |
|---|---|---|---|
| 12,500 | 4.3 ms | 15.8 ms | 0.59 ms |
| 25,000 | 8.4 ms | 37.2 ms | 1.08 ms |
| 50,000 | 17.1 ms | 116.9 ms | 2.08 ms |
| 100,000 | 34.8 ms | 326.2 ms | 4.26 ms |
| per doubling | 2.00× | **2.76×** | 1.94× |

`a-table`, R × 5 table:

| rows | keystroke in the first cell | one rendered frame showing the top |
|---|---|---|
| 125 | 0.85 ms | 1.97 ms |
| 250 | 1.89 ms | 3.55 ms |
| 500 | 3.07 ms | 6.75 ms |
| 1,000 | 5.35 ms | 13.35 ms |
| per doubling | 1.87× | 1.89× |

## Profiles

`dotnet-trace collect --profile dotnet-sampled-thread-time -- dotnet VellumText.Bench.dll <scenario>`,
then attributed by replaying the speedscope export's stacks. The traces are not committed; the
breakdown below is what they showed.

**Long paragraph** (`a-longpara`, all four sizes, both variants):

```text
ParagraphView.Measure                         8,604 ms
└─ TextLayout.CreateTextLines                 99.9%
   └─ TextFormatterImpl.FormatLine            100%
      ├─ PerformTextWrapping                   71.2%
      │  ├─ TextLineImpl.FinalizeLine          46.5%  → CreateLineMetrics → GlyphRun.CreateGlyphRunImpl
      │  ├─ SplitTextRuns                      19.3%  (87% of it copying run references: CastHelpers.StelemRef)
      │  ├─ CastHelpers.StelemRef(_Helper)     18.9%  (more run-list copying, inline)
      │  └─ MeasureLength                      10.0%  → ShapedBuffer.EnsureClusterCache → ArrayPool.Rent
      ├─ ShapeTextRuns                         21.5%
      └─ FetchTextRuns                          7.1%
```

About 38% of wrapping is copying run references per line, which is the lines × runs term. The
mark-to-run conversion (`InlineContent` building the runs) is linear and is not the problem.

**Table** (`a-table`):

```text
DocumentPresenter.Render                     11,801 ms
├─ TableBlockView.Render                       83.6%
│  ├─ ParagraphView.Render (every cell)        93.3%
│  └─ DrawRectangle (borders)                   6.7%
└─ ParagraphView.Render (outside tables)       16.3%
TableBlockView.Measure                        3,436 ms  → ParagraphView.Measure and .Size for every cell
```

## What follows for the design

- **9.1** bounds every `TextLayout` to a chunk of the paragraph that ends at a line boundary, so
  both factors of lines × runs are bounded by the chunk and an edit relays out only the chunk it
  touches (plus any that reflow). A threshold keeps short paragraphs on today's single layout.
- **9.2** clips `TableBlockView.Render` to the row bands the clip rectangle meets and makes an
  edit rebuild only the touched row: the frame cost becomes proportional to visible rows.
- **9.3** realizes only the row bands near the viewport, with a per-table row-height index, so
  measuring a 10,000-row table stops measuring 50,000 cells.
