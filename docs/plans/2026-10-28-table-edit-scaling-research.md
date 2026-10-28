# Table-edit scaling research

Research only. Nothing here is implemented; this document asks for a decision (section 9).

**Question asked.** "Research how we can solve the slower Vellum edit": a cell keystroke in a
10,000-row table costs about 9 ms and 6 MB, attributed to `DocumentSchema.ValidateTable` and
reported as growing quadratically. Also: does any *other* edit path grow superlinearly at the 1.0
target sizes (50k paragraphs, 10k-row tables, 100k-character paragraphs)?

**Short answer.**

- The table keystroke is **linear in the table, not quadratic**, but with a large constant.
  About 97% of its allocation is one call, `TableGeometry.Place`, which re-places every cell
  in the table on every keystroke. Two exact changes remove it from the typing path: a same-shape
  skip (option B′) and an array-based `Place` (option A). Together they take validation from
  about 6 MB to under 1 KB per keystroke, with no change to what is accepted or refused.
- One other path is **genuinely quadratic, and it is severe**: replace-all, and the undo of a
  replace-all. With one match per paragraph, 8,000 paragraphs take 11.9 s and allocate 1.6 GB, and
  undoing takes 19 s. Extrapolated to 50k paragraphs, it does not finish in any reasonable time. It
  is quadratic inside one long paragraph too: 100k characters take 1.6 s / 865 MB. Half of the
  allocation is `Mapping.Append` copying an immutable array on every step.
- Lists, paste and long-paragraph formatting are linear. Structural table edits are linear but slow
  in absolute terms (170 ms to insert a row at 10k rows).

## 1. How this was measured

Scenarios live in `bench/VellumText.Bench/ResearchScenarios.cs`. Run them with:

```
dotnet run -c Release --project bench/VellumText.Bench -- r-table        # table keystroke breakdown
dotnet run -c Release --project bench/VellumText.Bench -- r-tablestruct  # insert/delete row, insert column, and their undo
dotnet run -c Release --project bench/VellumText.Bench -- r-paths        # lists
dotnet run -c Release --project bench/VellumText.Bench -- r-heavy        # replace-all over many paragraphs (small sizes)
dotnet run -c Release --project bench/VellumText.Bench -- r-paste        # paste, long-paragraph bold and replace-all
```

Each scenario measures at true doublings (tables 2.5k → 20k rows × 5 columns; paragraphs and list
items 6.25k → 50k; one paragraph 12.5k → 100k characters). It reports the median of three to seven
rounds and the growth factor per doubling. A factor of about 2× is linear, and about 4× is
quadratic.

**Caveat.** These runs were made at BelowNormal priority on a shared machine, outside a quiet
window, so the **times are indicative**. The **byte counts** come from the allocation counter and
are stable run to run; treat them as the primary signal. Re-time at a quiet window before quoting
any millisecond figure as a result.

The earlier "quadratic" reading came from comparing 1k rows with 10k rows and reading that 10×
step as one doubling. At true doublings, the factor is 2.03×.

## 2. Profile breakdown: a cell keystroke in an R × 5 table

Keystroke in a middle cell; model only (transaction + `Apply`), and through the view.

| rows | model keystroke | `Validate(after, before)` | `TableGeometry.Place` alone | through the view |
|---:|---:|---:|---:|---:|
| 2,500 | 2.5 ms / 1.46 MB | 1.9 ms / 1.41 MB | 1.3 ms / 1.40 MB | 1.6 ms / 1.57 MB |
| 5,000 | 4.1 ms / 2.96 MB | 4.6 ms / 2.87 MB | 2.9 ms / 2.85 MB | 3.4 ms / 3.17 MB |
| 10,000 | 9.5 ms / 6.02 MB | 9.6 ms / 5.86 MB | 7.9 ms / 5.81 MB | 5.9 ms / 6.43 MB |
| 20,000 | 21.1 ms / 12.26 MB | 14.3 ms / 11.93 MB | 11.6 ms / 11.83 MB | 12.7 ms / 13.07 MB |
| per doubling | 2.06× / 2.03× | 1.98× / 2.04× | 2.15× / 2.04× | 2.01× / 2.03× |

At 10k rows, validation accounts for 5.86 of the keystroke's 6.02 MB (97%), and `Place` alone for
5.81 MB (96%). Where the timed parts sum to more than the whole, that is noise from the shared
machine. `Place`'s cost is two things:

- a `HashSet<(int Row, int Column)>` with one entry per occupied slot (50,000 at 10k × 5), grown
  by doubling resizes;
- an `ImmutableArray<CellPlacement>.Builder` with no initial capacity.

`ValidateTable` then allocates a `bool[height, width]` coverage grid (50 KB at 10k × 5) and walks
it once.

## 3. Why every keystroke pays for the whole table

`DocumentSchema.Validate(node, previous)` already prunes by reference: an unchanged subtree is
the same object as before the edit, and the edit was valid, so the walk skips it. A keystroke in a
cell rebuilds the spine from the document to that cell. That makes the `TableNode` a new object,
so `Visit` reaches `case TableNode` and calls `ValidateTable`, which re-places **every** cell to
check that the placements tile the grid. Below the table, the rows are pruned by reference again,
except the edited row.

So a one-character edit in one cell costs O(cells in the table). That is linear per keystroke, and
it is the table-level analogue of re-validating a whole document. The rule being re-checked
(tiling: no holes, no span hanging off the bottom, and `ColumnWidths` matching the width) depends
only on the table's *shape*, and a keystroke never changes the shape.

## 4. Options

Gains are at 10k rows × 5 columns. "Measured" means a prototype was timed in a scratch project
against the real `VellumText.Core`. "Estimated" means arithmetic from the profile above.

### A. Array-based `Place`: an exact rewrite

Track a "busy until row" value per column in an `int[]` instead of a hash set of occupied slots,
and size the placement builder up front. This is equivalent because the walk is row-major and
every span starts on the row being placed. A slot (r, c) is occupied exactly when an earlier cell
covering column c has its bottom below r.

- **Gain (measured):** `Place` at 10k rows drops from 6.5 ms / 5.8 MB to 2.05 ms / 0.8 MB. The
  remaining 0.8 MB is the placement array itself. It is still linear, so on its own it is a 7×
  constant-factor cut, not a fix.
- **Who else benefits:** all seven callers of `Place` — the view (`TableBlockView`),
  `CellSelection`, both exporters (RTF, Markdown), `TableEdits` (twice) and the schema. That
  speeds structural table edits, cell selection and export too.
- **Equivalence (measured):** 0 mismatches against the current implementation over 3,000
  randomized tables with random row and column spans.
- **Correctness risk:** the equivalence rests on "spans start at the row being placed". A future
  placement rule that breaks that (for example, placing right-to-left, or a cell anchored above
  its row) would make the array silently wrong where the hash set was not. Mitigation: keep the
  current hash-set algorithm in the test project as an oracle and property-test against it.

### B. Same-shape skip: validate only when the table's shape changed

In `Visit`, when both `node` and `previous` are `TableNode`s with the same shape, skip
`ValidateTable`. Same shape means:

- the same row count;
- the same `ColumnWidths.Length`, which is the only `ColumnWidths` fact `ValidateTable` reads;
- every row either the same object, or holding the same number of cells with the same
  `(RowSpan, ColumnSpan)` at each index.

The walk into the rows still happens. The edited row and cell are still visited, so the per-node
rules (for example, "a cell must contain at least one block") still run on them.

- **Gain (measured):** 0.033 ms and 40 B at 10k rows, against 9.6 ms and 5.86 MB. It is O(rows)
  reference comparisons, which is flat in practice.
- **Correctness risk 1, the "previous was valid" precondition.** This is the real one. The skip
  inherits `Validate(node, previous)`'s assumption that `previous` was valid. Pruning already
  relies on that assumption for untouched subtrees, but for tables it is **not always true**:
  `EditorState.Create` does not validate, the `TableNode` constructor does not enforce tiling, and
  a ragged table can arrive through JSON import or a host that builds documents directly.
  - Today, every edit inside such a table is refused, because `ValidateTable` runs on the spine
    and finds the hole. Verified with a scratch program: a two-row table with a hole, put in
    `EditorState.Create`, refuses a one-character insert with "No cell covers column 1".
  - With plain B, a same-shape edit would be **accepted**, and the editor would quietly let users
    type into a table it refuses everywhere else. That is a behaviour change.
- **Option B′ (recommended) removes that risk.** Skip only when `previous` is a table already known
  to be valid: a `ConditionalWeakTable<TableNode, …>` that `ValidateTable` adds a table to when it
  finds no violations. Behaviour then matches today's exactly:
  - The first edit in a table that has never been validated pays one full validation (about 9 ms
    at 10k rows) and is refused if the table is ragged.
  - Every edit after that is O(rows).
  - The memo keys on immutable nodes, holds them weakly, and is thread-safe, so it cannot keep old
    documents alive or go stale.
- **Correctness risk 2, rule drift.** If `ValidateTable` later gains a rule that reads something
  outside the shape key (say, a maximum column count, or a rule about cell content), the skip
  silently stops enforcing it on edits. Mitigation:
  - define the shape key beside `ValidateTable`, with a comment listing exactly the inputs it
    reads;
  - add an oracle property test asserting `Validate(after, before)` equals `Validate(after)` for
    valid `before`, over generated edits including corrupting ones.

  A rule the generator never exercises can still slip through, so the test is the guard, not a
  proof.

**"Touched rows plus span-affected neighbours"** is the general form of B: re-place only the band
of rows a non-same-shape change can affect. It is **not recommended**:

- Placement is a greedy, row-major walk. A changed span in row r moves the cells of every row it
  reaches, which can move those rows' spans in turn. The band is bounded only by the longest chain
  of row spans, so the worst case is the whole table.
- Getting the band wrong produces a silently wrong placement, which is exactly what validation
  exists to prevent.
- Its only gain over B′ is on structural edits, which are rare, already O(table) for other
  reasons (section 7), and helped by A.

### C. Cache geometry on the table node

Memoize `Place`'s result on the immutable `TableNode`.

- **Gain:** none for a keystroke. The edited table is a new node, so its cache is cold.
  Carrying the old geometry across to the new node when the shape is unchanged is option B in
  another form. It does help callers that ask about the same table repeatedly (the view,
  `CellSelection` during a drag, an export after an edit).
- **Correctness risk:** low. The nodes are immutable, so a cache cannot go stale.
- **Cost risk:** memory. The undo history keeps old table versions alive, so 100 undoable
  keystrokes in a 50k-cell table would retain roughly 100 × 0.8 MB of placements (estimated).
  If it is wanted later, it should be a weak or bounded cache.

### D. Validate at commit or debounce instead of per step

- **Gain:** removes validation from typing entirely.
- **Correctness risk: high; not recommended.** `ReplaceStep.Validated` is how a step that would
  produce an invalid document is *refused* before it is applied. Validating later means invalid
  documents can enter `EditorState`, be rendered, be exported or put on the clipboard, and
  then have to be detected and rolled back with no defined rollback story. A keystroke is already
  its own transaction, so "at commit" changes nothing; debounce needs undo-on-failure.

### E. A structural invariant the model guarantees; validation as a debug-only assert

Make tiling a constructor invariant of `TableNode`, and reduce `ValidateTable` to `Debug.Assert`.

- **Gain:** nothing in release builds; the check is gone.
- **Correctness risks:**
  - The check moves, it does not disappear. Enforcing tiling in the constructor costs the same
    O(cells), and a keystroke constructs a new `TableNode`, so the cost moves into the
    constructor unless that path reuses same-shape reasoning, which is B again.
  - It is a breaking public-API change: the constructor would throw on ragged input that the JSON
    importer and hosts can hand it today and get a diagnostic for.
  - Debug-only means release builds lose the net. A future bug in any table edit would write a
    corrupt document to the user's file instead of refusing the edit.
- Not recommended.

## 5. Recommendation

**B′ followed by A**, as two units, both exact and neither changing what is accepted or
refused.

Expected results at 10k rows × 5 (estimated from the measurements above; re-time at a quiet
window):

| | today | after B′ | after B′ + A |
|---|---:|---:|---:|
| validation per keystroke | 9.6 ms / 5.86 MB | ~0.03 ms / < 1 KB | same |
| model keystroke | 9.5 ms / 6.0 MB | dominated by spine rebuild; well under 1 ms and about 0.2 MB (estimated) | same |
| `Place` (view, selection, export, structural edits) | 6.5 ms / 5.8 MB | unchanged | ~2 ms / 0.8 MB |

B′ fixes the keystroke. A is worth doing on its own merits: it is the per-call cost of every
other table operation, and structural edits call `Place` several times (section 7).

### Units

**T1: Same-shape skip with a known-valid memo (B′).** Change `DocumentSchema.Visit` and
`ValidateTable` and add a shape-key helper beside them.

- **Red-first test:** a model keystroke in a 10k × 5 table allocates under 100 KB on the calling
  thread. It allocates 6 MB today.
  - Use `GC.GetAllocatedBytesForCurrentThread`, not the process-wide counter, to avoid the
    `DropSizeTests` class of flake.
- **Oracle property test:** over generated tables (including spans) and generated steps,
  whenever `before` is valid, `Validate(after, before)` must produce the same violations as
  `Validate(after)`. Steps include:
  - text edits;
  - emptying a cell;
  - span changes;
  - row insert/delete through `TableStructureStep`;
  - `ColumnWidths` changes;
  - hand-built corrupt `after` tables.
- **Named corruption tests.** Each must still be refused after T1:
  1. the edited row's cell `RowSpan` grows past the last row;
  2. its `ColumnSpan` grows (a hole or a widened grid);
  3. a cell is removed from the edited row (a hole);
  4. the row count changes with a short row;
  5. the `ColumnWidths` length no longer matches the width;
  6. the edited cell is emptied of blocks (children must still be visited);
  7. a ragged table created with `EditorState.Create` (never validated): a same-shape keystroke
     is still refused, preserving today's behaviour.
- **Mutants:**
  - drop the `RowSpan` comparison;
  - drop the `ColumnSpan` comparison;
  - drop the per-row cell-count comparison;
  - drop the row-count comparison;
  - drop the `ColumnWidths.Length` comparison;
  - skip without consulting the known-valid memo;
  - `return` instead of continuing into the children after a skip;
  - add a table to the memo even when it had violations.

  Each must turn a named test or the oracle red.

**T2: Array-based `TableGeometry.Place` (A).** Change `TableNodes.cs` only.

- **Red-first test:** `Place` on a 10k × 5 table allocates under 1.5 MB. It allocates 5.8 MB
  today.
- **Oracle property test:** the current hash-set algorithm moves into the test project, and the
  new `Place` must agree with it on placements and width. The generated tables are deliberately
  malformed, with:
  - overhanging row spans;
  - holes;
  - spans that push later cells right;
  - empty rows;
  - very wide spans.
- **The existing large-span guard test** (32 cells of rowspan = colspan = 1000) stays green, and
  must stay cheap.
- **Mutants:**
  - `busy[c] > row` changed to `>=`;
  - not growing the busy array when a span widens the grid;
  - inverting the early exit in the free-column scan;
  - not advancing `column` to the placement's right edge.

**Bench:** extend `r-table` with an "after" target, and keep the scenarios so the gain can be
re-measured with one command.

## 6. Other edit paths at the 1.0 target sizes

| path | size | cost at the largest size | growth per doubling (time / bytes) | verdict |
|---|---|---|---|---|
| keystroke at the end of an N-item list | 50k items | 10.3 ms / 12.4 MB | 1.56× / 2.00× | linear |
| Tab (indent) on the middle item of an N-item list | 50k items | 17.0 ms / 21.0 MB | 1.81× / 2.00× | linear, just over a 16 ms frame |
| select all + bulleted list | 50k paragraphs | 37.1 ms / 23.7 MB | 1.69× / 1.99× | linear |
| undo of that | 50k paragraphs | 8.6 ms / 5.8 MB | 1.90× / 1.98× | linear |
| paste 100 paragraphs into the middle | 50k paragraphs | 19.0 ms / 7.4 MB | 1.64× / 1.82× | linear |
| paste the whole document into its middle | 50k paragraphs | 144 ms / 32 MB | 2.20× / 1.96× | linear |
| select all + bold in one paragraph | 100k chars | 128 ms / 2.6 MB | 2.35× / 1.91× | linear |
| **replace all, 1 match per paragraph** | **8k paragraphs** | **11.9 s / 1.6 GB** | **5.46× / 3.68×** | **quadratic or worse** |
| **undo of that replace all** | **8k paragraphs** | **19.2 s / 1.6 GB** | **6.36× / 3.71×** | **quadratic or worse** |
| **replace all "word1" in one paragraph** (~1,670 matches) | **100k chars** | **1.58 s / 865 MB** | **3.31× / 3.84×** | **quadratic** |
| **undo of that** | **100k chars** | **2.0 s / 864 MB** | **4.60× / 3.86×** | **quadratic** |
| insert row below, through the view | 10k rows (20k) | 169 ms / 81 MB (377 ms) | 2.11× / 2.02× | linear, slow |
| delete row, through the view | 10k rows (20k) | 148 ms / 81 MB (341 ms) | 1.97× / 2.02× | linear, slow |
| insert column right, through the view | 10k rows (20k) | 254 ms / 85 MB (392 ms) | 2.21× / 2.00× | linear, slow |
| undo of insert row / delete row / insert column | 10k rows | 42 / 36 / 51 ms; 28–31 MB | 2.81× / 2.66× / 3.31× time; 2.02× / 2.02× / 1.99× bytes | linear in bytes (see note) |

**Replace-all was split into `r-heavy` at 1k → 8k paragraphs.** At the 6.25k → 50k sizes, the
first run had not finished the 50k point after more than a quarter of an hour.

### 6.1 Replace-all is quadratic

`RichTextView.ReplaceAll` builds one transaction of M `ReplaceStep`s, one per match, applied
back to front. Each step costs O(document) plus O(steps so far):

- **`Mapping.Append` is O(M) per step, so O(M²) overall.** `Mapping` wraps an
  `ImmutableArray<StepMap>`, and `Append` is `_maps.Add(map)`, which copies the whole array.
  Measured in isolation: 1k / 2k / 4k / 8k appends allocate 12 MB / 48 MB / 192 MB / **768 MB**.
  That is about half of replace-all's 1.6 GB at 8k matches.
- **Each step rebuilds the spine to its match.** A new `DocumentNode` copies the top-level block
  array (8 bytes per block) and re-sums `ContentSize`, and `Validate(after, before)` iterates every
  top-level child for its reference check. These were read from the code, not profiled one by one.
  The remaining ~830 MB at 8k is about 104 KB per step, or about 13 bytes per block per step.
- **Inside one long paragraph,** each step rebuilds that paragraph's inline content, which is
  O(paragraph length). So it is O(M × L): 1,670 matches × 100k characters.
- **Undo replays the inverted steps** as another M-step transaction, so it has the same shape.

At 50k paragraphs with a common word (tens of thousands of matches), extrapolating the byte
growth gives tens of gigabytes allocated and minutes of UI-thread time. **In practice that is a
hang at the 1.0 target size.**

**Options (for a decision; not designed in depth here):**

- **R1: a mapping builder.** The transaction accumulates `StepMap`s in a list and materialises
  the `Mapping` lazily when it is read.
  - Exact; no change to semantics.
  - Removes the O(M²) half of the allocation: 768 MB at 8k.
  - Small and local. It also speeds every multi-step transaction (for example the four-step
    cross-depth delete, and list toggles).
- **R2: one multi-range replace step for replace-all.** A step that applies all non-overlapping
  replacements in a single rebuild, with a multi-range `StepMap` (which `StepMap` already
  supports: `_first` plus `_rest`). Its inverse is the same kind of step carrying the original
  slices.
  - O(document + matches) for the replace and for its undo.
  - Mapping stays per-match precise, so the caret, comments and match positions map exactly as
    they do now.
  - **Risks:**
    - It is a new `Step` type. Every place that switches on step types (history inversion,
      `Transaction` bookkeeping, any future collaboration or serialisation) must learn it.
    - Its preconditions must be checked, not assumed: sorted, non-overlapping ranges, each inside
      a single text block. That is the same "a step refuses what it cannot do" contract the
      existing steps have.
- **R3 (rejected): one coarse step per paragraph that has matches.** It needs no new step type,
  but mapping becomes per-paragraph: a caret or marker between two matches would jump to the
  paragraph edge. It also does nothing for one match per paragraph.

**Recommendation: R1 then R2,** test-first, with the same per-thread allocation-bound red tests
(replace-all at 8k paragraphs under, say, 50 MB) and an equivalence test (same document, same
undo result, same mapped selection as today's per-match steps, over generated documents and
queries).

### 6.2 Structural table edits are linear but slow

Inserting a row at 10k rows takes 169 ms and allocates 81 MB through the view. That is linear,
but over a 100 ms responsiveness target. It was not decomposed in this research.

From the code, one structural edit calls `Place` at least three times (`TableEdits`, the schema,
and `TableBlockView` on rebuild). At the measured 5.8 MB per call, that is about 17 MB of the
81 MB. T2 cuts that part about 7×. **The rest needs its own profile; this is a recorded
follow-up, not part of T1/T2.**

**Note on undo timings.** The undo rows show time growth of 2.7–3.3× per doubling while bytes grow
at a clean 2.0×, and the jump is concentrated at 20k rows (for example, undo of insert column: 51 →
311 ms). Steady byte growth with a late time jump points to GC and working-set effects on a shared
machine, not an algorithmic term. Confirm at a quiet window before treating it as a finding.

### 6.3 Within budget, worth watching

- A keystroke at the end of a 50k-item list allocates 12.4 MB. That is linear in items, while the
  time stays within a frame.
- Tab on a 50k-item list takes 17 ms.

Both are dominated by the list being on the spine, the same pattern as the table. Neither is a
1.0 blocker.

## 7. Summary of findings

1. **Table keystroke:** linear, not quadratic. 97% of its allocation is validation re-placing every
   cell. **Fix: T1 (B′) + T2 (A); exact; recommended.**
2. **Replace-all and its undo:** quadratic. It is effectively a hang at 50k paragraphs and takes
   1.6–2 s in one 100k-character paragraph. **Fix: R1 + R2; needs a decision**, because R2 adds a
   step type.
3. **Structural table edits:** linear, 150–250 ms at 10k rows. **Follow-up profile;** T2 helps.
4. **Lists, paste, long-paragraph formatting:** linear and within or near budget.

## 8. Not done in this research

- No implementation, and no change to any product code.
- The prototypes for A and B lived in a scratch project and are not committed. The bench
  scenarios above are committed, so the numbers can be reproduced.
- No quiet-window timing; see the caveat in section 1.
- Structural table edits were not decomposed.

## 9. Decision requested

1. **Table keystroke:** approve T1 (same-shape skip with a known-valid memo) and T2 (array
   `Place`), in that order? *Recommended.*
2. **Replace-all:** approve R1 (mapping builder) and R2 (a multi-range replace step, which is a new
   `Step` type), or R1 only, or defer?
3. **Structural table edits:** schedule the profile as a follow-up, or leave it recorded?