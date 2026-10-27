# fix: codebase audit remediation — import hardening, async import pipeline, undo/typing performance, exporter and reader rewrites
audited_commit: 0282015f9e578da2e886b48b7c23b4485cf8e09c
origin: artifacts/codebase-audit/2026-10-25/report.md (git-ignored; finding IDs F-nn below refer to it)
status: revision 3, **approved 2026-10-26** — the owner approved all phases (1–10) in dependency order, the late-evidence units in the placements below, and decided unit 3.10 as *convert* (the toolbar box shows points; picking 12 sets 16 DIP). Each phase ships as its own branch and draft pull request; nothing is released until the owner says so.

## Revision history
- rev 3 (2026-10-26, late E2E evidence): F-18 upgraded to P1; its test uses the real Excel capture. F-14 and F-15 downgraded to P2. F-36 dropped, so unit 6.4 is removed. F-29's test gains real WebP, JPEG and BMP bombs. F-40's manual IME check needs a human at a physical keyboard. 25 new findings (F-49…F-73) are placed as new units 3.10–3.16, 4.6–4.8, 5.6–5.7, 6.7–6.8 and 10.4 (see *Late-evidence units*). No owner decision is re-opened. One unit (3.10) changes visible toolbar behaviour to match its own documentation, so it is flagged for the owner.
- rev 1 (2026-10-25): plan with 11 open decisions.
- rev 2.1 (2026-10-26): Q-1 (static importers become async-only too) and Q-2 (single 0.8.0 release) answered.
- rev 2 (2026-10-26): owner resolved OD-1…OD-11. Changes: new breaking async-only import API with editor-side background dispatch (Phase 7, now L); RTF reader replaced instead of filtered (new Phase 8, gated on library selection); Markdown exporter escaping restructured instead of patched (Phase 4 → L); both structural layout rewrites added (new Phase 9); toolbar disables on read-only (3.9); sink-level sanitisation (5.4); paste/drop resolver and the paste `try` narrowing moved into Phase 7 so the paste pipeline is rewritten once; old Phase 8 (tests/docs/guards) is now Phase 10.

## Summary

72 findings after the late E2E pass (2 P0, 13 P1, 31 P2, 26 P3; F-36 dropped), plus one Suspicion. The original 48 were 2 P0, 14 P1, 21 P2 and 11 P3. Ten PR-sized phases. Phase 1 (import cannot crash or hang the host) goes first and alone. Phases 2–5 are independent and can run in parallel after it. Phase 6 (performance) follows Phase 1. Phase 7 (async import, breaking) follows Phases 1 and 3. Phase 8 (RTF reader) follows Phase 7. Phase 9 (layout rewrites) follows Phase 6. Phase 10 (tests, docs, guards) is last.

Execution rules (codebase-audit Phase 6): per unit — write the test → watch it fail on 0282015 → fix → mutate the fix by hand (restore with a timestamp-advancing write; a mutant that does not compile is not a survivor; green control between mutants) → full suite → commit. Approving this plan is not approval to open PRs; those follow the repo's own rules.

## Owner decisions (2026-10-26)

| OD | Decision | Effect on the plan |
|---|---|---|
| OD-1 | (c) Large-stack thread **and** pre-parse guards only where cost is superlinear | Units 1.1 + 1.3 as written. |
| OD-2 | **Owner's own choice:** remove the synchronous importer API entirely — `IDocumentImporter` becomes async-only (public breaking change). The editor **always** dispatches every importer (built-in and custom) onto a background thread regardless of its implementation, and applies the result on the UI thread behind the existing `StillAt` origin check. | Phase 7 rewritten and enlarged (L): API, all formats, paste, drop, `ContentImporting`, tests, version bump to 0.8.0, CHANGELOG and migration notes. Absorbs F-10 (old 5.6) and F-41 (old 3.7). See "Questions these decisions raise" Q-1. |
| OD-3 | (b) Configurable options with strict defaults | Units 1.3, 1.4, 2.1 expose options; defaults below. |
| OD-4 | (b) Image wins when the rich import lost an image to the sanitizer; drop uses the same resolver | Unit 7.4. |
| OD-5 | Undo history survives a retemplate | Unit 3.2 moves `History`. |
| OD-6 | **Owner's own choice:** do both rewrites — line-range sub-layouts for long paragraphs (F-33) and full table row virtualization (F-35); profile the superlinear term first | New Phase 9 (L), gated by a profiling unit. Old 6.7 folds into 9.3 as its first slice. |
| OD-7 | **Owner's own choice:** restructure the Markdown exporter's escaping model now; keep the round-trip property as its regression net | Phase 4 → L: 4.0 property guard, 4.1 restructure closes F-07, F-22, F-25, F-27, F-34. |
| OD-8 | **Owner's own choice:** replace the RTF reader. "No hand-rolled parsers" still holds, so the first unit chooses a maintained library; **if none fits, stop and report back — do not decide.** | New Phase 8, gated by 8.0. Old 5.4 (`\uc` clamp around RtfPipe) is dropped; F-30 is re-verified against the new reader.  **Resolved 2026-10-27 (8.0 gate): option 1 — `OfficeIMO.Rtf` + `OfficeIMO.Html.Rtf`, exact pin, accepting AngleSharp 1.8.3 / AngleSharp.Css 1.1.2; risks (young library, one principal maintainer, `\shppict` pictures not read) recorded in architecture §5.4.** |
| OD-9 | (a) Raise the JSON reader's `MaxDepth` | Unit 5.1. |
| OD-10 | Disable the toolbar when `IsReadOnly` | New unit 3.9 (re-opens the "always enabled" decision for the read-only case only). |
| OD-11 | Sanitise at the sink so the rest of a paste survives | New unit 5.4. |

### Follow-up questions (answered by the owner, 2026-10-26)
- **Q-1 — Scope of "remove the sync importer API": the static entry points go too.** `HtmlImporter.Import/ImportClipboard`, `MarkdownImporter.Import` and `RtfImporter.Import` become async-only alongside `IDocumentImporter`, the format classes and `RichTextClipboard.Read`/`Decode`. The library has one import shape.
- **Q-2 — Release: a single 0.8.0.** No 0.7.1 split; Phases 1–10 ship together in 0.8.0.

## Settled decisions respected
- JSON is deliberately unsanitized and trusted (0282015): F-17 changes only its depth handling.
- Transactions are best-effort; one transaction = one undo entry; typing coalesces at 500 ms. F-12 changes how a coalesced entry is replayed, not what is grouped.
- Total input size is unbounded by decision; any size cap belongs at `DocumentPayload`. No unit adds a size cap. Phase 1 adds work budgets (nesting, span area, decoded pixels) per OD-3.
- Remote images off by default; `data:` images on and bounded; the library never opens URLs.
- Keystroke cost is linear in document size (§8 item 8). F-14/F-15 remove the constant factors that break 16 ms at 50k.
- Find bar per-keystroke search left alone (§8.12). F-13/F-36 touch paint and per-edit re-search only.
- "No hand-rolled parsers" (§12.1) — held by OD-8: the replacement RTF reader must be a maintained library. The hand-written RTF **writer** stays (§5.4).
- Formats are constructor-less (`Instance`/`With(options)`); no XAML `Formats`; one-way `Document` binding; Avalonia 12 / net10.0 only.
- `TextRunCache.InvalidateFrom(k>0)` is stale (A1). Phase 9's sub-layouts must not rely on it; they split a paragraph into independent layouts instead.

### Settled decisions re-opened
By measurement (details in the report): span clamp bounds cost (F-02); Markdown/RTF need no depth bound (F-03, F-04); per-image renderer limits bound memory (F-06); Flatten is 6% of a keystroke (F-14); join is ~8 ms at 50k (F-15); a long paragraph costs 7–12 ms (F-33); per-edit re-search is the same order as layout (F-36); importers are "linear-ish" (F-31).

By owner decision (2026-10-26): `IDocumentImporter` is synchronous with a derived string overload (§4.9, interface remarks) → async-only; RTF import on RtfPipe (§5.4, §12.1) → replaced; toolbar commands "always enabled" (`RichTextToolbar.cs:34`) → disabled when read-only; R3 mitigation "paragraph layouts rebuilt wholesale on edit" → line-range sub-layouts for long paragraphs; R2's "constrain v1 tables" stays for widths, but the table view becomes row-virtualized. Phase 10 records each in `docs/architecture.md`.

## Option defaults (OD-3b)
| Option | Lives on | Default | Bounds |
|---|---|---|---|
| `MaxTableSlots` | `HtmlImportOptions` (forwarded by Markdown/RTF options) | 100,000 | total rowspan×colspan slots per import; over budget → table becomes paragraphs + `Downgraded` |
| `MaxElementDepth` (pre-parse) | `HtmlImportOptions` | 512 open tags | superlinear parse (F-31); over → flattened + diagnostic |
| `MaxAttributesPerElement` | `HtmlImportOptions` | 256 | F-31 |
| `MaxStyleNesting` | `HtmlImportOptions` | 32 bracket levels in a `style` value | F-01 belt-and-braces; over → declaration dropped |
| `CacheBudgetBytes` | `BitmapEmbedRenderer` | 256 MiB decoded | F-06; LRU eviction until the budget fits |
| (existing) `MaximumPixels`, `MaximumSourceBytes`, `MaxDepth`, `MaxDataImageBytes` | unchanged | unchanged | now enforced before decode for every format (F-29) |

The existing reflection test (`EveryRtfOptionReachesTheHtmlStage` and its Markdown twin) must cover the new forwarded options, and is strengthened in 10.1 to compare all-default and all-non-default values.

## Phases

| Phase | Findings closed | Theme | Depends on | Parallel? | Size |
|---|---|---|---|---|---|
| 1 | F-01, F-02, F-03, F-04, F-05, F-21, F-31 | Import cannot crash or hang the host | — | first, alone | M |
| 2 | F-06, F-29, F-46, F-48 (MaximumPixels) | Image memory budgets | 1 | ∥ 3, 4, 5 | M |
| 3 | F-08, F-09, F-19, F-20, F-39, F-40, F-42, F-43, C14 (OD-10) | Editor state hand-off, input, read-only toolbar | — | ∥ 2, 4, 5 | M |
| 4 | F-07, F-22, F-25, F-26, F-27, F-34, F-23, F-38, F-28 | Exporter rewrite (Markdown) and fixes (RTF, CSS) | — | ∥ 2, 3, 5 | L |
| 5 | F-17, F-18, F-24, F-44, OD-11 (dropped C21/SA5) | Importer fidelity and sink sanitisation | 1 | ∥ 2, 3, 4 | M |
| 6 | F-11, F-12, F-13, F-36, F-14, F-15, F-32, F-47 | Undo, typing and find within AC12 | 1 | after 1; ∥ 7 | L |
| 7 | F-16, F-10, F-41 (+ preserves F-21) | **Async-only import API + background dispatch (breaking, 0.8.0)** | 1, 3 | after 1 and 3; ∥ 6 | L |
| 8 | F-30, F-16 (RTF part), RTF import limitations | Replace the RTF reader | 7 (and 8.0 gate) | after 7 | L (gated) |
| 9 | F-33, F-35 | Line-range sub-layouts; table row virtualization | 6 | after 6 | L + L |
| 10 | F-37, F-48 (remaining), F-45, RP guards, re-opened decisions in docs | Tests, docs, mechanical guards | 1–9 | last | M |

Why Phase 7 follows Phase 3: unit 3.2 moves the `ContentImporting`/`ContentImported`/`ImageRefused` handlers into the shell and re-pushes them on retemplate; Phase 7 changes when and on which thread those events are raised. Doing 3.2 first keeps one owner for the event plumbing when the threading changes.

## Units

### Phase 1 — import cannot crash or hang the host
- **1.1** · F-01, F-04 (F-03 belt-and-braces) · Every built-in importer runs its parse and walk on a dedicated worker thread with a large reserved stack (e.g. 256 MB virtual; one reused worker), inside the importer, so direct callers outside the editor are protected too. · Test: `HostileNestingTests`, run **in a child process** asserting exit code 0 — `calc(`×3,300, `<template>`×5,000 through HTML and default Markdown. · Mutant: parse on the caller's thread → child exits `0xC00000FD`. · Size M.
- **1.2** · F-03 · `MarkdownImporter.RemoveRawHtml(ContainerInline)` becomes iterative. · Test: theory over `[`, `>`, `*` at 4k–16k in a child process. · Mutant: restore the recursion. · Size S.
- **1.3** · F-31, F-01 (OD-1c, OD-3b) · Pre-parse guards only where cost is superlinear or recursion is unbounded in a dependency: `MaxElementDepth`, `MaxAttributesPerElement`, `MaxStyleNesting`, each with a `Downgraded` diagnostic. · Test: 50 KB vs 200 KB nested `<div>` within 3× time; 349 KB of attributes within 2 s; the guards' option values honoured. · Mutant: each limit raised to `int.MaxValue`. · Size M.
- **1.4** · F-02 (OD-3b) · Clip `RowSpan` to existing rows in `TableGeometry.Place`; `MaxTableSlots` budget downgrades oversize tables. · Test: 32×`<td colspan=1000 rowspan=1000>` imports in < 500 ms and < 100 MB allocated. · Mutant: remove the clip. · Risk: `Place` is shared by schema/selection/edits — run all table suites. · Size S–M.
- **1.5** · F-05 · Clamp `<ol start>` at import (≤ 999,999,999) and use saturating arithmetic at `RichTextView.Lists.Commands.cs:113,160`. · Test: imported `start=2147483646` + Shift+Tab and ToggleList via headless keys; a second test with a model-built start (command arithmetic alone must not throw). · Mutants: remove each guard. · Size S.
- **1.6** · F-21 · `PasteAsync`/`PasteTextAsync` refuse before the first clipboard read when `IsReadOnly || Rectangle is not null`. · Test: counting importer runs 0 times on a read-only view. · Mutant: move the check after decode. · Note: Phase 7 rewrites this method and must keep this test green. · Size S.

### Phase 2 — image memory budgets
- **2.1** · F-06 (OD-3b) · `BitmapEmbedRenderer` evicts by `CacheBudgetBytes` with true LRU; `CacheLimit` stays as a secondary cap. · Test: render 16 × 7999² GIFs; private bytes after GC < budget + 100 MB. · Mutant: count-only eviction. · Size M.
- **2.2** · F-29, F-48 (MaximumPixels) · Read dimensions from the header for every format before decoding. · Test: GIF/BMP/JPEG/WebP declaring > `MaximumPixels` refused with no allocation above 100 MB. Use the measured bombs in `artifacts/codebase-audit/2026-10-25/evidence/e2e-fuzz/image-bombs/`: a 38 B WebP that costs 642 MB today, a 287 B JPEG that costs 2.07 GB, and a 64 B RLE8 BMP that costs 2.07 GB; an 8×8 BMP with `MaximumPixels=16` gets the fallback. · Mutants: PNG-only header check; post-decode check removed (B3/B3b). · Size S.
- **2.3** · F-46 · Stop copying a dropped file past the store/renderer cap + 1 byte. · Test: 64 MB proxy file allocates < 20 MB and raises `ImageRefused`. · Mutant: remove the cap. · Size S.

### Phase 3 — editor state hand-off, input, read-only toolbar
- **3.1** · F-09 · Narrow the `Block is null` guard in `OnKeyDown`/`OnTextInput` to paragraph-only cases. · Tests: `E7b`, `E5`, `E5b`, `E8`. · Mutant: blanket guard. · Size M.
- **3.2** · F-08 (OD-5) · The shell owns forwarded state and handler delegates; `OnApplyTemplate` re-pushes them **and moves `History`** to the new view; the old toolbar is detached. · Tests: `E3`, `E3b` (undo survives), design `Constructor_time_setup_survives_an_application_style_that_sets_the_template`. · Mutants: skip re-pushing `ContentImporting`; drop the `History` move. · Size M.
- **3.3** · F-19 · Push to the view before `RaiseStateParts`. · Tests: `E2`, `E2b`. · Size S.
- **3.4** · F-20 · Syntax highlighting in `Reconcile` and table-cell updates. · Tests: `E1`, `E6`. · Size S.
- **3.5** · F-39 · `CutAsync` captures `CurrentOrigin()`; `DeleteCutRange` requires `StillAt`. · Tests: `E4` + end-only probe (kills C6). · Size S.
- **3.6** · F-40 · Anchor preedit in the paragraph containing `Selection.From`. · Test: `E12` plus a manual Windows IME check by a person at a physical keyboard. In the E2E pass the Microsoft IME would not compose synthetic keystrokes even in a native EDIT control. · Size M.
- **3.8** · F-42, F-43 · Clear `_goalX` on non-derived `SetState`; `AffectsRender` for `SelectionBrush`. · Tests: `E9`, `E10`. · Size S.
- **3.9** · C14 (OD-10) · `RichTextToolbar` commands report `CanExecute = false` while the bound editor `IsReadOnly`, and raise `CanExecuteChanged` when it toggles; the "always enabled" rule still holds for editable editors. · Test: `E13` (all actions disabled on a read-only editor; re-enabled after toggling back) and the existing "awkward caret" tests stay green. · Mutant: ignore `IsReadOnly`. · Size S.
- (old 3.7, F-41, moved to 7.4.)

### Phase 4 — exporter rewrite and fixes (OD-7)
- **4.0** · RP-6 guard · Seeded Markdown export→import round-trip property: when the exporter reports no loss, text and marks survive. Extend the generator with links, images, code, tables and adjacent differently-styled runs. Add RTF and HTML "recipient parse" tests (RtfPipe or the Phase 8 reader; AngleSharp CSSOM). · Must fail on 0282015 (F-07, F-22, F-25, F-27 classes). · Size M.
- **4.1** · F-07, F-22, F-25, F-27, F-34 · **Restructure the Markdown exporter's inline escaping.** Replace per-character, context-free escaping and per-segment flanking with one inline serializer that (1) builds a token stream of text, delimiter runs, code spans and links for a whole paragraph; (2) decides flanking for every delimiter against the characters actually written on both sides, including the neighbour's delimiters; (3) chooses code-span fences longer than any adjacent backtick run; (4) handles block-final breaks with a `Downgraded` diagnostic; (5) strips edge whitespace in one pass. Keep the four single-unit crash inputs, `**Price***$5*`, the trailing break and the backtick case as fixed regression tests. · Mutants: revert each rule in turn; 4.0 must go red each time. · Risk: highest churn file — land behind 4.0 and keep all 285 Markdown tests. · Size L.
- **4.2** · F-26 · Blank line before a nested ordered list with `Start ≠ 1` (block-level; not part of 4.1). · Test: CC `md-nested-ol`. · Size S.
- **4.3** · F-23 · `RtfExporter.WriteTable` from `TableGeometry.Place` with `\clvmgf/\clvmrg`, `\par` between a cell's paragraphs, `\intbl` on every in-cell paragraph, nested tables flattened with a stated loss. · Tests via recipient re-import. · Size M.
- **4.4** · F-38 · Percent-encode `"` in HYPERLINK; strip or replace `;` in font names. · Size S.
- **4.5** · F-28 · One CSS string serializer (escape `\`, `"`, controls); import reads the unescaped CSSOM value. · Size S.

### Phase 5 — importer fidelity and sink sanitisation
- **5.1** · F-17 (OD-9a) · `JsonDocumentOptions.MaxDepth` covering what the writer can emit (e.g. 2048). · Test: 19-deep lists and 11-deep tables round-trip. · Size S.
- **5.2** · F-18 (**P1** since rev 3) · Fragment-in-table-context fallback in `ClipboardHtml`/`ImportClipboard`. In the real Excel capture, `<!--StartFragment-->` sits after `<table>` and before `<col>`. · Test: the real capture `artifacts/codebase-audit/2026-10-25/e2e/fixtures/excel-range-A1D6/10-HTML_Format.bin` (copy it into the test project; also B2C3 and A2) imports a 6×4 table, against one paragraph today. · Size M.
- **5.3** · F-24 · Drop one trailing break-originated `\n` in `BlockSink.Flush`. · Size S.
- **5.4** · OD-11 (dropped C21, SA5) · Sanitise values at the sink instead of failing the whole import: replace unpaired surrogates with U+FFFD and snap mark-span edges to whole characters before `InlineContent.Create`; clamp the indent level computed in `CssStyles` (and any other numeric attribute → model value; see RP-2) before arithmetic. The importer catch-all stays as the last resort. · Tests: RTF/HTML with a lone surrogate and a split pair keep the rest of the paste with a `Downgraded`; nested `margin-left:1e300px` keeps "Important text"/"more". · Mutants: remove the replacement; remove the clamp. · Size S–M.
- **5.5** · F-44 · `DataUri.TryParse` in Core, used by sanitizer, renderer, RTF exporter, `DataUriImageStore`. · Size S.
- (old 5.4 `\uc` clamp dropped — OD-8; old 5.6 resolver moved to 7.4.)

### Phase 6 — undo, typing and find within AC12
- **6.0** · RP-5 guard · Audit scenarios added to `bench/VellumText.Bench`; CI smoke fails when a growth ratio exceeds 2.5×/doubling or an AC12 row fails (allocation-based where possible). Must fail on 0282015. · Size M.
- **6.1** F-11 window `RestoreMarksStep.Spans` · **6.2** F-12 merge contiguous inverse `ReplaceStep`s when coalescing · **6.3** F-13 binary-search find matches to the realized range · ~~**6.4** F-36~~ (dropped in rev 3: 11.8 ms at 50k is within budget, and whole-document re-search is the documented strategy) · **6.5** F-14, F-15 (P2 since rev 3: the lag is not reproduced serially, but the 4.2–10 MB per-keystroke allocation is Confirmed) incremental flattened slots + 1↔2 child splice route (highest risk; full suite + undo fuzz) · **6.6** F-32, F-47 span-based `MarksAcross`/`Runs()`. Tests, mutants and budgets as in rev 1. · Size L overall.
- (old 6.7 table clip-skip folded into 9.3.)

### Phase 7 — async-only import API and background dispatch (OD-2; breaking → 0.8.0)
- **7.0** · Design note in `docs/architecture.md` §4.9 replacing the synchronous contract: `Task<ImportResult> ImportAsync(Stream source, CancellationToken)` is the member every format writes; `ImportAsync(string, CancellationToken)` is derived for text formats as today; "must not throw" stays (cancellation is the only exception). · Size S.
- **7.1** · API · Remove `IDocumentImporter.Import(Stream)` and `Import(string)`; add the async members. Migrate `HtmlFormat`, `RtfFormat`, `MarkdownFormat`, `JsonFormat`, and (Q-1) make the static `HtmlImporter.Import/ImportClipboard`, `MarkdownImporter.Import` and `RtfImporter.Import` async-only — the demo app and the bench move with them. `RichTextClipboard.Read` (sync) and `Decode` are removed; `ReadAsync` and a new `DecodeAsync` remain. Exporters are unchanged (OD-2 covers import only). · Test: public-surface tests assert no synchronous import member exists on `IDocumentImporter` or the format types (reflection). · Size M.
- **7.2** · Editor dispatch · Every importer call from the editor goes through one `ImportOnBackgroundAsync(importer, bytes, token)` that **always** hops to a background thread (`Task.Run`, even when the importer is genuinely async), then resumes on the UI thread. Payload bytes are read on the UI thread first; only decoding moves. A newer paste, a drop, or a document swap cancels the in-flight token; a stale result is discarded by `StillAt(origin)` regardless. · Tests: (a) a test importer records its thread ID → never the UI thread, for built-in and custom importers; (b) a gated importer (`TaskCompletionSource`) lets the test swap the document mid-import → nothing lands (this finally stages the race behind mutants C7/D4 that headless could not stage before); (c) the UI dispatcher keeps processing jobs while a 1 MB RTF imports. · Mutants: call the importer inline; drop the `StillAt` check after the await. · Size M.
- **7.3** · `ContentImporting` / `ContentImported` · Both are raised on the UI thread after the background import completes. `ContentImporting` sees the exact document and its `Cancel` still vetoes; `StillAt` is re-checked after the handler returns (a handler can mutate state). Read-only and rectangle refusals (1.6) still happen before any read. Retemplate (3.2) must re-push the handlers onto the new view while an import is in flight; the in-flight import's result is dropped because the origin changed. · Tests: veto from a handler still refuses; handler sees `Dispatcher.UIThread.CheckAccess() == true`; retemplate during a gated import → nothing lands, no exception. · Size M.
- **7.4** · F-10 (OD-4b), F-41 · One resolver `RichTextClipboard.ResolveAsync(transfer, importers)` returning document-or-image, used by both paste and drop: rich formats first, but the image wins when the rich import lost an image to the sanitizer (a removed `<img>` diagnostic) or is empty; plain text through one seam (same slice as paste, so the empty-heading case agrees). Narrow the paste `try` to read + decode so a host handler's exception after apply propagates and the return value reflects the edit. Fix the `Drop.cs` remark. · Tests: design `Pasting_a_browser_copied_image_inserts_the_image`, `Paste_and_drop_prefer_the_same_flavour…`, `…empty_heading_agree`; CV `E14`; 1.6's read-only test still green. · Size M.
- **7.5** · Drop · `OnDrop` snapshots every flavour it may need **synchronously inside the drop event** (Avalonia's `DragEventArgs.DataTransfer` may not outlive the handler on every platform), then awaits file reads and the background import, re-checking `StillAt` per file as today. · Tests: existing drop suite + a drop whose import is gated and whose document is swapped. · Risk: platform drag-data lifetime — verify on Windows with a real drag (E2E). · Size M.
- **7.6** · F-16 · Pre-filter style declarations to allowed properties and memoise per distinct `style` string. · Test: RtfPipe-style 1.7 M-char HTML within 2× of the style-stripped control, < 200 MB allocated. · Size M.
- **7.7** · Tests migration · ≈120 direct `Import(` call sites across Html, Markdown, Rtf, Json, Core interop and Avalonia tests become `await …ImportAsync(…)`; Avalonia paste/drop tests pump the dispatcher until the background import completes (one helper, not ad-hoc waits — no sleeps). · Size M.
- **7.8** · Release and migration · Single release (Q-2): `VersionPrefix` → `0.8.0` in `Directory.Build.props` at the release cut after Phase 10 (CONTRIBUTING); every phase adds its entries under `## [Unreleased]`; CHANGELOG `## [Unreleased]` gains `### Migrating` (before/after snippets: implementing a custom importer; calling a format directly; `RichTextClipboard.Read` → `ResolveAsync`; handlers now run after an await, so code must not assume the paste completed synchronously), `### Removed from the public API`, `### Changed` (background dispatch; toolbar read-only from 3.9). Update `docs/guide.md` (Import and export, The clipboard, Inspecting and refusing incoming content, Recipe: a clipboard manager) and `docs/consuming.md`. Add a note for Clipthrough's vendored 0.7.0 → 0.8.0 upgrade. · Size S.

### Phase 8 — replace the RTF reader (OD-8)
- **8.0 · Selection gate (stop condition).** Evaluate maintained .NET RTF readers against: licence compatible with §11 (MIT/BSD/Apache; no GPL/AGPL/commercial EULA — `NRtfTree` is already excluded); a release within the last 12 months; managed, net10.0-compatible, trimming-safe; reads Word/Outlook/WordPad/LibreOffice clipboard RTF including tables (incl. merged cells), `\pict` PNG/JPEG, HYPERLINK fields, colour tables (including explicit black — today's known loss); survives the Phase 1 hostile corpus (deep `{` nesting, `\uc2147483647`, huge `\bin`, `\object`, INCLUDEPICTURE/INCLUDETEXT) with bounded time and no stack overflow; and an output shape that can go through `SanitizingReader` (HTML, or a DOM we can map to HTML) so that one component still decides what a paste may contain. Produce a scored comparison in `artifacts/`. **If no candidate meets the licence, maintenance and robustness criteria, stop and report back to the owner with the comparison — do not pick one, do not hand-roll.** · Size M. **Outcome (2026-10-27):** one candidate passed licence, maintenance and robustness (OfficeIMO); RtfPipe, DocSharp and NewLife.Office failed; the owner chose option 1 with these conditions: exact pin; sanitizer and HTML suites green after the AngleSharp bump; trimming analyzer run; `\shppict` loss as a `Dropped` diagnostic; F-30 test accepts a spec-conformant skip with a diagnostic; RtfPipe removed; 4.8 and the 100k-`\cellx` hang re-verified; nothing filed upstream.
- **8.1** · Implement `RtfFormat.ImportAsync` on the chosen reader, keeping `RtfImportOptions` (and the forwarded OD-3 budgets) and routing output through the HTML stage. · Tests: all 114 RTF tests, the real clipboard fixtures from the E2E pass, the Phase 1 hostile corpus in a child process. · Size L.
- **8.2** · F-30 re-verified (32-byte `\uc2147483647` imports with its tail in < 1 s) and F-16's RTF half re-measured. Update the pinned "explicit black" limitation test to whatever the new reader does, with a diagnostic if it still loses it. · Size S.
- **8.3** · Remove the RtfPipe dependency; architecture §5.4 and §12.1 record the replacement. · Size S.

### Phase 9 — structural layout rewrites (OD-6)
- **9.0 · Profile first.** Trace a keystroke in a 25k- and 50k-character formatted paragraph (`dotnet-trace`, CPU sampling) and a 1,000×5 table frame; find the superlinear term in F-33 (`ParagraphView` relayout vs Avalonia shaping vs mark→run conversion). Record numbers and the flame-graph summary in `docs/increment-…` style evidence. The design of 9.1 depends on what this shows. · Size S.
- **9.1** · F-33 · **Line-range sub-layouts.** A paragraph longer than a threshold (e.g. 4k characters) is held as consecutive chunks, each an independent `TextLayout` over a character range ending at a line boundary. An edit relayouts from the chunk containing it and stops when a chunk's start offset and first-line start reconverge with the old layout. Caret, hit-testing, selection geometry, IME preedit and bidi (AC11) must work across chunk seams; the A1 rule holds (no `InvalidateFrom`). · Tests: keystroke at 50k/100k formatted characters < 16 ms; caret/selection/bidi/IME equivalence tests run against a long paragraph both chunked and unchunked (must match). · Risk: L — seam correctness; ship behind the threshold so short paragraphs keep today's path. · Size L.
- **9.2** · F-35 (first slice, old 6.7) · `TableBlockView.Render` skips cells whose row band misses the clip; `Update` rebuilds only the touched row. · Test: 1000×5 frame < 3 ms; keystroke layout < 5 ms. · Size M.
- **9.3** · F-35 · **Row virtualization.** Realize only the row bands intersecting the viewport (plus overscan), with a per-table row-height index (same pattern as `BlockHeightIndex`) so that scrolling, caret movement, cell selection, column resize and rowspan cells that cross the realized window behave as today. · Tests: 10,000-row table first render, scroll, keystroke within AC12 budgets; existing table, cell-selection, column-resize and nested-table suites green; rowspan crossing the viewport edge. · Size L.

### Phase 10 — tests, docs, guards
- **10.1** · F-37, F-48 (remaining sites) · Promote the probes into the suites (M22, M01, M02, MD4, M15, M18, C8, MV1, D4 via 7.2's gated importer, B6); option-mapping reflection tests compare all-default and all-non-default values. · Size S.
- **10.2** · F-45 + re-opened decisions · Rewrite architecture §7 rows, l.531, SECURITY.md (`data:` wording; "bounded in depth" — true after Phase 1); record every decision re-opened by measurement or by the owner (list above) with its new basis. · Size S.
- **10.3** · RP guards · Contributor rules for RP-1, RP-2, RP-4; ast-grep rule for RP-3; RP-7 state-replacing and retemplating fixture (now including "during an in-flight import"). · Size S.

### Late-evidence units (rev 3; placement needs owner approval)
Each unit follows the same rules as the rest of the plan: write the test, watch it fail on 0282015, fix, mutate. Evidence paths are relative to `artifacts/codebase-audit/2026-10-25/`.
- **3.10** · F-50 · Toolbar font size: either convert points to DIPs in `SetFontSizeCommand`/`ToolbarConverters.Points`, or relabel the box as DIPs. **Owner decision, because it changes visible behaviour.** The recommended option converts, so the box matches its own documentation and Word's numbers (a 20 pt heading shows 20). · Test: a pasted `\fs40` heading shows "20"; picking 12 sets 16 DIP. · Size S.
- **3.11** · F-63 · Text input, IME commit and replace text strip or map U+FFFC instead of throwing. · Test: trace-2 U+FFFC cases. · Size S.
- **3.12** · F-64 · Heading, alignment and indent commands apply to the paragraphs inside the selected cell or cells. · Test: trace-2 `SetAlign` in a cell returns true and changes the cell paragraph. · Size M.
- **3.13** · F-65 · Find-and-replace keeps the marks and link of the first replaced character. · Size S.
- **3.14** · F-69 · `SelectionLink` and `ApplyLink`/`RemoveLink` agree on which link sits at a junction. · Size S.
- **3.15** · F-66, F-67, F-72 · Automation: `SetValue` on a table-only document, block-image alt text in `DocumentText`, and `IsReadOnly` property-changed events. · Tests: trace-2 and `AuditTrace3Tests.A1`. · Size S.
- **3.16** · F-68 · `TableStructureStep` maps by the explicit inserted or deleted index, not by prefix/suffix diffing. · Size M.
- **4.6** · F-51 · Write block images in a form that can be told apart (`<figure>` or `display:block`; Markdown: a lone image paragraph) and import it back as `BlockImageNode`. · Test: round-trip property 4.0 with block images enabled. · Size M.
- **4.7** · F-52, F-54, F-55, F-56, F-57, F-59, F-60 · RTF exporter fidelity: no heading scale on explicit sizes; a real rule; `\listtable`/`\ls`/`\ilvl` lists with start numbers; a separator between adjacent tables and lists; list-item paragraph properties; alt text and links on pictures (`\*\shppict` description, HYPERLINK around `\pict`); link `\o` titles. Fold this into 4.3's `WriteTable` rewrite where it overlaps. · Tests: the fuzz probes in `evidence/e2e-fuzz/probe/`. · Size M–L.
- **4.8** · F-53, F-58, F-61 · Reader-side RTF losses: links painted black, empty cell as NBSP, white highlight. **Re-verify against the Phase 8 reader before fixing.** Fix only what still fails there. · Size S.
- **5.6** · F-49 · Word CF_HTML lists: map `MsoListParagraph*` / `mso-list:lN levelM` paragraphs to `ListNode` (kind from the `<![if !supportLists]>` marker; level from `levelM`), and drop the `mso-list:Ignore` bullet text. · Test: the real capture `e2e/fixtures/word-docx-all/03-HTML_Format.bin` imports a two-item bulleted list. · Size M.
- **5.7** · F-62 · Markdown link destinations keep a backslash as written (or report a diagnostic). · Size S.
- **6.7** · F-70 · Whole-word search classifies by `Rune`, not `char`. · Size S.
- **6.8** · F-71 · `IsLongerThan` stops inside a container once the budget is exceeded. · Test: allocation for a 50k-row leading table is under 64 KB. · Size S.
- **10.4** · F-73, F-48 (allow-list additions) · Promote the surviving mutants from `evidence/e2e-mutation/r2/results.md` into tests. Sanitizer additions (`onclick`, `data-*`, `position`, `background-image`) must fail a test. Also cover HTML exporter entity escaping and repeated images, CSS unit, keyword and family conversion, RTF link-target escaping, and Markdown title and alt escaping. · Mutants: re-run the r2 batch, which must be all killed apart from the 3 equivalents. · Size M.

## Guards
| Pattern | Guard | Unit |
|---|---|---|
| RP-1 recursion over hostile trees | child-process `HostileNestingTests` per importer (including the Phase 8 reader) + contributor rule | 1.1, 1.2, 8.1, 10.3 |
| RP-2 unsaturated attacker numbers | clamp at the sink; `HostileValueTests` row per numeric attribute × list/table commands | 1.5, 5.4, 10.3 |
| RP-3 broad catch | ast-grep rule; sink sanitisation keeps the catch-all a last resort | 5.4, 7.4, 10.3 |
| RP-4 fix at one of N twins | one resolver, one data-URI parser, one forwarded-state bag; parity tests | 3.2, 4.3, 5.5, 7.4 |
| RP-5 whole-range scans | bench growth-ratio CI smoke | 6.0 |
| RP-6 context-free exporter escaping | export→import property + recipient-parse tests | 4.0 |
| RP-7 host state replacement | state-replacing/retemplating fixture incl. in-flight import | 7.2, 7.3, 10.3 |

## Out of scope
- Any total input-size cap (settled; would belong at `DocumentPayload`).
- Sanitising JSON (settled); DOCX (architecture OD3, still open there).
- Async **exporters** (OD-2 covers import only).
- Rich accessibility (`ITextProvider`, upstream); touch tests (headless limitation).
- Upstream reports (AngleSharp parse growth, RtfPipe) — drafting allowed, filing needs a human (§10.5).
- Suspicion S-01 (`ApplyMarks` gap scan) until measured.
