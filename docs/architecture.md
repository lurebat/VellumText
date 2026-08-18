# A Free Rich Text Editor for Avalonia — Architecture & Design

**Status:** Draft for review
**Date:** 2026-08-11
**Target:** Avalonia 12.1.1 (current stable), .NET 8/9
**Proposed license:** MIT
**Name:** VellumText (packages `VellumText.Core`, `VellumText.Avalonia`, `VellumText.Interop.*`)

> Deliberately not named `Avalonia.*` — this is an independent community project and must not read as first-party.

---

## 1. User-visible outcome

A drop-in Avalonia control:

```xml
<VellumText:RichTextEditor Document="{Binding Doc}" />
```

…that a developer can put in an app and get a WYSIWYG editor comparable to the commercial Avalonia Accelerate Rich Text Editor, under a permissive license.

### 1.1 Acceptance criteria (v1.0)

| # | Criterion |
|---|---|
| AC1 | Character formatting: bold, italic, underline, strikethrough, sub/superscript, font family, size, foreground, highlight. |
| AC2 | Block formatting: paragraphs, H1–H6, alignment (left/center/right/justify), indent/outdent, bulleted & numbered lists (nested), blockquote, code block, horizontal rule. |
| AC3 | Hyperlinks: insert, edit, remove, ctrl-click activate. |
| AC4 | Inline images (PNG/JPEG), resizable, selectable as a unit. |
| AC5 | Tables: insert, insert/delete row & column, merge/split cells, cell background, column resize, tab-to-next-cell, rectangular cell selection. |
| AC6 | Editing: caret, mouse/keyboard/touch selection, word/line/paragraph navigation, drag-select, double/triple click, undo/redo with sensible grouping, cut/copy/paste, select-all. |
| AC7 | Clipboard interop: paste from Word / Google Docs / browsers retains formatting; copy is pasteable into Word and Google Docs. |
| AC8 | Export: HTML, Markdown, RTF, DOCX. Import: **RTF**, HTML, Markdown, DOCX. RTF import is not optional — the driving use case is a clipboard manager, and RTF is what Windows applications actually put on the clipboard. |
| AC9 | IME: CJK composition (preedit) works on Windows, macOS, Linux. |
| AC10 | Light/dark theme via Avalonia theme variants; Fluent + Simple theme resources shipped. |
| AC11 | Correct caret/selection for bidi (Hebrew/Arabic) text and grapheme clusters (emoji ZWJ sequences, combining marks). |
| AC12 | 10 MB / 50k-paragraph document scrolls and types without perceptible lag (virtualized). |
| AC13 | MVVM-friendly: bindable `Document`, `Selection`, `IsReadOnly`, commands for every formatting action. |

> **Clarified 2026-08-18 — AC13 is met, and "bindable" means one-way out.** The commands and `IsReadOnly` were in place; `Document` and `Selection` were not, and a view model had no way to hear that the user had typed. Both controls now expose `Document`, `Selection`, the `DocumentProperty` and `SelectionProperty` behind them, and `DocumentChanged`/`SelectionChanged`.
>
> The direct properties are read-only and default to `OneWayToSource`, so the binding a view model writes is the natural one. Assigning a document *in* is deliberately not offered: reconciling an external assignment against in-flight edits and the undo history is a design with real hazards, and no consumer has asked for it. A document is still loaded through `State`.
>
> The events exist alongside the properties for a measured reason rather than a stylistic one. `OneWayToSource` never pushes the value that is already there, only subsequent changes, so a view model that binds after a document is loaded holds `null` until the first keystroke. `GetObservable` does prime, but it needs an `AnonymousObserver<T>` rather than a lambda, which is not the API a host reaches for. Subscribing to `DocumentChanged` and reading `Document` once is.
>
> Both halves are raised from one place, `RaiseStateParts`, for the reason §12 gives: the state is set from more than one path, and wiring the announcement at each is how the fourth one gets missed. The shell raises its own from the same choke point and does so whether or not it has been templated, because a host that loads a document before the control is on screen must still be heard.

### 1.2 Explicit non-goals

- **Not** a code editor. Syntax highlighting, folding, completion, multi-caret → that is `AvaloniaEdit`'s domain; do not duplicate it.
- **Not** an HTML rendering engine. HTML is an interchange format, not the internal model.
- **Not** byte-fidelity Word compatibility. Target "opens correctly and looks right", not "round-trips every Word feature".
- **Not** collaborative editing in v1. The transaction model is designed so OT/CRDT *can* be layered later, but no such infrastructure ships now.
- **Not** pagination/print layout in v1.
- **No** compatibility shim for WPF `FlowDocument` XAML.

---

## 2. Facts, constraints, assumptions, open decisions

### 2.1 Established facts (verified against Avalonia 12.1.1 source)

Avalonia already provides the hardest infrastructure, MIT-licensed. This is the single most important fact in this document — it changes the project from "write a text engine" to "write an editor".

| Capability | API | Source |
|---|---|---|
| Line breaking, shaping (HarfBuzz), bidi reordering, font fallback | `TextFormatter` / `TextLayout` | `src/Avalonia.Base/Media/TextFormatting/` |
| Rich layout from arbitrary runs | `TextLayout(ITextSource, TextParagraphProperties, TextTrimming?, double maxWidth, double maxHeight, int maxLines, TextRunCache?)` | `TextLayout.cs` |
| Cheap styled layout without a custom source | `TextLayout(string, Typeface, …, IReadOnlyList<ValueSpan<TextRunProperties>>? textStyleOverrides)` | `TextLayout.cs` |
| Run production hook | `interface ITextSource { TextRun? GetTextRun(int textSourceIndex); }` | `ITextSource.cs` |
| Styled text run | `TextCharacters(ReadOnlyMemory<char>, TextRunProperties)` | `TextCharacters.cs` |
| Embedded/drawn content in the text flow | `abstract class DrawableTextRun : TextRun { Size Size; double Baseline; void Draw(DrawingContext, Point); }` | `DrawableTextRun.cs` |
| Paragraph terminator | `TextEndOfParagraph : TextEndOfLine` | `TextEndOfParagraph.cs` |
| **Grapheme-aware caret movement** | `TextLine.GetNextCaretCharacterHit` / `GetPreviousCaretCharacterHit` / `GetBackspaceCaretCharacterHit` | `TextLine.cs` |
| **Bidi-aware selection geometry** | `TextLine.GetTextBounds(int, int)` → `IReadOnlyList<TextBounds>` (each with `FlowDirection`) | `TextLine.cs`, `TextBounds.cs` |
| Point ↔ position | `TextLayout.HitTestPoint(in Point) → TextHitTestResult`, `HitTestTextPosition(int) → Rect`, `HitTestTextRange(int,int)`, `GetLineIndexFromCharacterIndex` | `TextLayout.cs` |
| Incremental reshaping cache | `TextRunCache` with `Invalidate()` / `InvalidateFrom(int textSourceIndex)` | `TextRunCache.cs` |
| IME integration | `abstract class TextInputMethodClient` — `TextViewVisual`, `SupportsPreedit`, `SupportsSurroundingText`, `SurroundingText`, `CursorRectangle`, `Selection`, `SetPreeditText(string?, int?)` | `src/Avalonia.Base/Input/TextInput/TextInputMethodClient.cs` |
| Caret blink + selection painting reference implementation | `TextPresenter` (`DispatcherTimer` blink, `FillRectangle` selection, `PixelRect.FromRect(rect,1).ToRect(1)` snapping) | `src/Avalonia.Controls/Presenters/TextPresenter.cs` |
| Inline display model (display only, no editing) | `Run`, `Span`, `Bold`, `Italic`, `Underline`, `LineBreak`, `InlineUIContainer` | `src/Avalonia.Controls/Documents/` |
| Clipboard with arbitrary platform formats | `DataFormat.CreateBytesPlatformFormat(id)` / `CreateStringPlatformFormat(id)` / `CreateBytesApplicationFormat(id)` / `CreateInProcessFormat<T>(id)`; `IClipboard.SetDataAsync/TryGetDataAsync` with `IAsyncDataTransfer` | `src/Avalonia.Base/Input/DataFormat.cs`, `Input/Platform/IClipboard.cs` |

### 2.2 Hard constraints

- **C1 — No UIA `ITextProvider` in Avalonia.** `src/Avalonia.Controls/Automation/Provider/` contains only `IInvokeProvider`, `IValueProvider`, `IToggleProvider`, `ISelectionProvider`, `IRangeValueProvider`, `IScrollProvider`, `IExpandCollapseProvider`, `IRootProvider`, `IEmbeddedRootProvider`, `ISelectionItemProvider`. There is **no text-pattern provider**. Screen readers therefore cannot get ranges, run attributes, or caret-move events from any Avalonia control. This is a ceiling we cannot lift from outside the framework. See §9 R1.
- **C2 — No table layout in `TextFormatting`.** `TextLayout` formats a single paragraph-ish flow. Tables must be composed by us.
- **C3 — `DataFormats` is `[Obsolete(error: true)]` in Avalonia 12.** Design against `DataFormat`/`IAsyncDataTransfer` only. (11.3.x already ships the new API, so an 11.3 LTS build is feasible from the same source.)
- **C4 — Clean-room.** Avalonia Accelerate is not source-available. We reimplement a *feature list* from public marketing copy. No decompilation, no reference to its binaries.

### 2.3 Assumptions (to validate in Increment 0)

- **A1** — ~~`TextRunCache.InvalidateFrom` gives usable incremental reshaping for single-paragraph edits.~~ **Partly invalidated (Increment 0 — see [`increment-0-findings.md`](increment-0-findings.md) §2).** `InvalidateFrom(k>0)` returns a stale, *pre-edit* layout: invalidation is entry-granular and a paragraph is a single entry keyed 0. The correct usages (`InvalidateFrom(0)` / `Invalidate()`) measure **1.00×** against no cache, so R3's fallback — wholesale paragraph rebuild — is adopted **for edits**. **However**, reusing a persistent cache across a *wrap-width* change with unchanged text is correct and **≈3.5× faster** (median of 7 reps, range 2.9–5.2×), which matters because a resize rewraps every paragraph at once. VellumText therefore keeps a per-paragraph `TextRunCache` and drives it by **lifetime, not offset**: `Invalidate()` on a text edit, reuse untouched on width-only relayout.
- **A2** — ~~`DrawableTextRun` inside a custom `ITextSource` correctly participates in line breaking, baseline alignment, and `GetTextBounds`.~~ **Validated (Increment 0).** Three traps are documented in the findings: `Properties` must be overridden (§4.7), `TextEndOfParagraph.Length == 1`, and `Top`/`TextTop` are offset by Avalonia's baseline formula.
- **A3** — X11/Wayland compositors in practice honour `text/html` and `text/rtf` targets. *API-level support is confirmed; real-world behaviour varies by compositor.*
- **A4** — Per-paragraph `TextLayout` construction is cheap enough that a 50k-paragraph document can be virtualized with measure-on-demand plus estimated heights.

### 2.4 Open decisions for the user

- ~~**OD1** — Target Avalonia 12 only, or multi-target 11.3 LTS + 12?~~ **Decided 2026-08-11: Avalonia 12 only.** See §12.1.
- **OD2** — Ship a default toolbar/ribbon control, or editor surface only + demo toolbar? (Recommendation: surface + fully-commanded API + an *optional* `RichTextToolBar` in the Controls package.)
- **OD3** — Is DOCX in v1.0, or v1.1? It roughly doubles interop effort.

> **Decided:** tables (AC5) are a firm v1.0 requirement — see §12.1. This is load-bearing on the model and view design; consequences are spelled out in §12.3.

---

## 3. Closest working pattern

`TextBox` → `TextPresenter` → `TextLayout` is the pattern to extend, not replace:

- `TextPresenter` owns exactly one `TextLayout`, one caret index, one selection range, a `DispatcherTimer` blink, and renders selection rects before text.
- `TextBlock` demonstrates the two `ITextSource` shapes we need — `SimpleTextSource` (plain text + default properties, returning `TextEndOfParagraph` past the end) and `InlinesTextSource` (a `IReadOnlyList<TextRun>` walked by index, with an optional `IReadOnlyList<ValueSpan<TextRunProperties>>` modifier).

**The design is: `TextPresenter`, generalized from one styled string to a tree of blocks.** Everything below follows from that.

What we must *not* do is reuse `Avalonia.Controls.Documents.InlineCollection` as the editable model. It is an `AvaloniaObject` tree built for display and styling inheritance: mutation is O(tree), it has no positions, no transactions, no invertible operations, and no clean undo story.

---

## 4. Architecture

### 4.1 Boundaries

```
┌────────────────────────────────────────────────────────────────┐
│ VellumText.Avalonia                   (Avalonia dependency)        │
│   RichTextEditor (TemplatedControl)                            │
│   RichTextView (Control, ILogicalScrollable)  ← block host     │
│   BlockView / ParagraphView / TableView / ImageView            │
│   RichTextInputMethodClient : TextInputMethodClient            │
│   Commands, key bindings, RichTextToolBar (optional)           │
│   Themes/Fluent + Themes/Simple                                │
└───────────────┬────────────────────────────────────────────────┘
                │ depends on
┌───────────────▼────────────────────────────────────────────────┐
│ VellumText.Core                       (zero dependencies)          │
│   Model:  DocumentNode, BlockNode, InlineContent, marks        │
│   State:  EditorState, Selection, Transaction, Step, Mapping   │
│   Ops:    typing, formatting, list/indent, table ops           │
│   History: undo stack over inverted steps                      │
│   Schema: node/mark registry + validation                      │
└───────────────┬────────────────────────────────────────────────┘
                │ used by
┌───────────────▼────────────────────────────────────────────────┐
│ VellumText.Interop.Html                 (AngleSharp, MIT)          │
│ VellumText.Interop.Markdown             (Markdig, BSD-2)           │
│ VellumText.Interop.Rtf                  (no dependency, own code)  │
│ VellumText.Interop.Docx                 (DocumentFormat.OpenXml,   │
│                                      MIT, 3.5.1)               │
└────────────────────────────────────────────────────────────────┘
```

**Why the core has no dependencies at all:** the model, transactions, undo, and all interop are testable headlessly, in a plain `dotnet test`, with no windowing system — and, as decided on 2026-08-11, without even `Avalonia.Base`. That is the difference between a project that survives and one that doesn't. Interop packages are separate so an app that only needs Markdown does not drag in the Open XML SDK.

The cost of a zero-dependency core is two small primitives Avalonia would otherwise have supplied, which `VellumText.Core` therefore owns:

- **`Rgba` colour** — a `readonly record struct` of four bytes, replacing `Avalonia.Media.Color` in `MarkSet`.
- **`ValueSpan<T>`** — `(int Start, int Length, T Value)`, replacing `Avalonia.Utilities.ValueSpan<T>` in `InlineContent.Marks`.

`VellumText.Avalonia` owns the conversions in both directions. This is a deliberate trade: a few dozen lines of duplication in exchange for a core that is consumable from a server, a CLI, a source generator, or a test harness with no UI stack present.

### 4.2 Document model — hybrid tree + flat inline runs

Two levels, deliberately different:

**Block level: an immutable typed tree.** Needed because tables, list nesting, and blockquotes are genuinely structural.

**Inline level: flat UTF-16 text + sorted attribute spans.** Needed because typing is the hot path; a run-object tree would allocate and re-splice on every keystroke, and shaping caches key off contiguous text.

```csharp
public abstract class Node
{
    public abstract int ContentSize { get; }  // ProseMirror-style: size in positions
}

public sealed class DocumentNode : Node          // children: ImmutableArray<BlockNode>
public abstract class BlockNode : Node
public sealed class ParagraphNode : BlockNode    // ParagraphKind (Body/H1..H6/Quote/Code), InlineContent
public sealed class ListNode : BlockNode         // ordered/unordered, start, children: ListItemNode[]
public sealed class ListItemNode : BlockNode     // children: BlockNode[]  (nesting for free)
public sealed class TableNode : BlockNode        // ColumnWidths, children: TableRowNode[]
public sealed class TableRowNode : BlockNode     // children: TableCellNode[]
public sealed class TableCellNode : BlockNode    // RowSpan, ColSpan, Background, children: BlockNode[]
public sealed class RuleNode : BlockNode         // horizontal rule (leaf)
public sealed class BlockImageNode : BlockNode   // block-level image (leaf)

public sealed class InlineContent
{
    public string Text { get; }                                   // '\uFFFC' placeholder per embed
    public ImmutableArray<ValueSpan<MarkSet>> Marks { get; }      // sorted, non-overlapping, normalized; VellumText's own ValueSpan
    public ImmutableArray<InlineEmbed> Embeds { get; }            // aligned to placeholder offsets
}

[Flags] public enum TextStyle { None=0, Bold=1, Italic=2, Underline=4, Strikethrough=8, Sub=16, Super=32, Code=64 }

public readonly record struct MarkSet(
    TextStyle Style,
    string? FontFamily,
    double? FontSize,
    Rgba? Foreground,          // VellumText's own colour type — the core takes no Avalonia dependency
    Rgba? Highlight,
    LinkMark? Link);
```

`MarkSet` is a value type, so span normalization is a trivial equality merge and `TextRunProperties` can be memoized per distinct `MarkSet`. `\uFFFC` (OBJECT REPLACEMENT CHARACTER) as the embed placeholder means embeds occupy exactly one position and all offset arithmetic stays uniform.

### 4.3 Positions

A single flat integer position over the whole document (ProseMirror's scheme): each character is 1, each node boundary is 1, each leaf node is 1.

This is the pivotal choice. It gives:
- A total order over the document → selections are just two integers.
- Position *mapping* through edits: a `Mapping` composed of per-step `StepMap`s rewrites stale positions (selections, link anchors, collaborative cursors) with no tree walking.
- Trivially serializable operations.

`ResolvedPos Resolve(int pos)` yields the ancestor chain, depth, parent, and index — cached, invalidated per transaction.

> **A `StepMap` holds several ranges, not one** (built 2026-08-18). Almost every step replaces one
> stretch of the document, and that case is stored inline so a keystroke still allocates nothing to
> describe itself — measured at 8 bytes more per edit than the one-range version, flat at every
> document size. Several ranges are what a table edit needs: inserting a column adds a cell to
> every row, and merging cells takes content out of several and puts it in one.
>
> That 8 bytes was first measured against an edit that allocated 1,160,848 bytes, where anything
> would have looked free. It was **re-measured after the `ReplaceStep` pivot cut the same edit to
> 263,880** — a path 4.4x leaner, where a fixed cost is 4.4x more of the total — and it is still
> exactly 8 bytes, on all four rows of `-- model`: insert at the start, insert at the end, delete,
> and a paragraph join. It is the struct gaining one reference field, once per transaction, and
> not a per-block cost. Confirmed at all five document sizes of each row, twenty measurements, all
> +8; the arithmetic agrees, since a one-element `Mapping` array goes from 40 to 48 bytes when
> `StepMap` grows from 12 to 24.
>
> **It costs no measurable time.** Against `origin/main` on one machine, five runs each, AC12's
> keystroke at the document end is a median 9.32 ms here and 9.99 ms there, ranges 9.13–9.59 and
> 9.24–10.33, no run over budget on either. This branch measuring lower is ordering and load, not
> a speedup — the two are inside each other's spread. Re-run it rather than trusting these
> numbers: the box is shared, and §8 records what the tail does on a loaded one.
>
> The single-range answer was available and is arithmetically sound — call the whole table one
> replaced range — and it was rejected because of what it does to positions *inside* the table. All
> of them collapse onto the table's edge, including in rows the edit never touched, so a caret
> three rows below an inserted row jumps to the top of the table. Everything holding a position
> through the edit sees the same.
>
> Ranges that touch are merged when a map is built, so no position can be claimed by two of them
> and mapping needs no tie-break.

### 4.4 Transactions and steps

```csharp
public abstract record Step
{
    public abstract StepResult Apply(Node doc);   // Ok(newDoc) | Failed(reason)
    public abstract Step Invert(Node docBefore);
    public abstract StepMap GetMap();
    public abstract Step? Map(Mapping mapping);   // for rebasing
}

public sealed record ReplaceStep(int From, int To, Slice Content) : Step;
public sealed record AddMarkStep(int From, int To, MarkSet Mark, MarkFields Fields) : Step;
public sealed record RemoveMarkStep(int From, int To, MarkFields Fields) : Step;
public sealed record SetBlockAttrStep(int Pos, BlockAttr Attr, int Value) : Step;
public sealed record TableStructureStep(int Pos, TableNode Before, TableNode After) : Step;

public sealed class Transaction
{
    public EditorState Before { get; }
    public ImmutableArray<Step> Steps { get; }
    public Mapping Mapping { get; }
    public Selection SelectionAfter { get; }
    public MarkSet? StoredMarks { get; }          // "bold is armed but nothing typed yet"
    public TransactionKind Kind { get; }          // Typing | Delete | Format | Structure | Paste | Ime
}

public sealed class EditorState
{
    public DocumentNode Doc { get; }
    public Selection Selection { get; }
    public MarkSet? StoredMarks { get; }
    public EditorState Apply(Transaction tr);
}
```

Every mutation is a `Transaction`. Nothing mutates the document directly — not typing, not paste, not table ops. Consequences, all of them load-bearing:

- **Undo is nearly free.** `history` stores `Invert(before)` per step, reversed. No snapshots, no diffing.
- **Undo grouping is a policy, not a mechanism.** Coalesce adjacent `Typing` transactions within 500 ms *and* contiguous positions *and* no intervening selection jump; never coalesce across `Structure` or `Paste`.
- **The view updates incrementally.** `Transaction.Mapping` tells `RichTextView` exactly which block range changed → invalidate only those `BlockView`s and only that `TextRunCache`.
- **Collaboration is later, not never.** `Step.Map` is the rebase primitive OT needs. We ship no collab code, but we do not design it out. `Transaction.Steps` stays public for that reason and no other — nothing in this repository reads it outside its own tests.

> **`Transaction.DocsBefore` removed** (2026-08-18). It exposed the per-step document snapshots the
> undo bookkeeping keeps, had no caller anywhere — `Invert()` reads the field directly — and would
> have frozen an internal representation into the public API at 1.0. `Steps` and `Failures` were
> examined at the same time and both kept: `Steps` is the collaboration primitive above, and
> `Failures` is what §7 tells a host to check, because a transaction is best-effort rather than
> atomic.

### 4.5 Selection

```csharp
public abstract record Selection(int Anchor, int Head)
{
    public int From => Math.Min(Anchor, Head);
    public int To   => Math.Max(Anchor, Head);
    public abstract Selection Map(Node doc, Mapping mapping);
}
public sealed record TextSelection(int Anchor, int Head) : Selection;
public sealed record NodeSelection(int Pos) : Selection;                    // an image selected as a unit
public sealed record CellSelection(int AnchorCell, int HeadCell) : Selection; // rectangular table region
```

Three selection kinds is not over-engineering — it is the minimum that makes "click an image and press Delete" and "drag across table cells and set a background" expressible without special-casing them in every command.

> **Three, and exactly three** (2026-08-18). `Selection`'s constructor is `private protected`, so
> the hierarchy is closed outside this assembly. Commands are written as "which of the three is
> this, and can I serve it" — a fourth shape reaches them as something they do not recognise and is
> refused or, worse, handled as if it were text. That is extensibility that fails in the host's
> application at run time instead of in ours at compile time, which is strictly worse than none.
> `Node`, `BlockNode` and `InlineEmbed` are closed for the same reason: `DocumentPresenter`'s block
> factory, the HTML and RTF exporters and the JSON writer all switch and throw on the default.
>
> `Step` is deliberately **not** closed, and that is the contrast that makes the rule a rule rather
> than a reflex. Nothing switches on which step it is; all four obligations are on the base, so a
> step written elsewhere works everywhere a step is consumed. Open where the consumer is generic,
> closed where it enumerates.
>
> One hole cannot be closed and is stated rather than hidden: a record's copy constructor must be
> `public` or `protected` (CS8878), so an outside caller can still derive from `InlineEmbed` or
> `Selection` by declaring one that chains to it. It cannot happen by accident, which is what the
> closure is for.

> **No virtual "is this a rectangle" member, and closing the hierarchy is why** (2026-08-18).
> `is CellSelection` is open-coded at 17 sites, with the private `RichTextView.Rectangle` covering
> another 6 — 23 in all. That looks like an abstraction asking to be extracted. It is not, and the
> reason is worth writing down because it will be filed again otherwise.
>
> The case *for* a member was that a type test is not exhaustive: a fourth shape would fall through
> to the range path and be treated as text, which is the silently destructive outcome this section
> exists to prevent. That case died when the constructor became `private protected`. Three shapes
> are now the whole set, so the type test is exhaustive by construction. A virtual `bool` would have
> made the fallthrough polite; closing the hierarchy makes it impossible. The weaker guarantee is
> not worth a public member.
>
> What is left does not cohere. The 23 sites are 11 refusals, 7 dispatches to a cell-specific
> implementation, 4 reads of `AnchorCell`/`HeadCell`, plus the property itself. The refusals share
> an *answer*, not a *predicate*: there is no block for a heading to land on, no place for a typed
> character, no caret for an IME composition, no ends for a touch handle to drag, no image at a cell
> boundary, no enclosing list, no run of sibling blocks to wrap. One `CanReplaceRange` would name
> none of those eleven reasons and would only be true because it means `is CellSelection` spelled
> longer. The 7 dispatches need a transaction, the presenter's geometry and `Rect` — `VellumText.Core`
> has no dependencies and an MSBuild target that fails the build if it acquires one, so they cannot
> move. The 4 index reads want an `int?` that is null for two of the three shapes.
>
> The invariant they defend was audited rather than assumed: every read of `From`/`To` in
> `VellumText.Avalonia` is downstream of one of the 23 guards, and there is no `switch` over the
> three shapes anywhere. `Selection.Map` is the one place where behaviour genuinely varies by shape,
> and it is already `abstract`. That is the member that earned its place; a second one would not.

### 4.6 View layer

```csharp
public class RichTextEditor : TemplatedControl        // public API, template: ScrollViewer > RichTextView
public class RichTextView : Control, ILogicalScrollable
{
    private readonly List<BlockView> _blockViews;      // sparse; realized window only
    private readonly BlockHeightIndex _heights;        // measured + estimated, prefix-sum for scroll
}

internal abstract class BlockView
{
    internal abstract Size Measure(double availableWidth);
    internal abstract void Render(DrawingContext ctx, Point origin);
    internal abstract bool HitTest(Point local, out int docPosition);
    internal abstract Rect GetCaretRect(int docPosition);
    internal abstract IEnumerable<Rect> GetSelectionRects(int from, int to);
}

internal sealed class ParagraphView : BlockView
{
    private TextLayout _layout;
    private readonly TextRunCache _runCache;   // width-only relayout; see A1 in §9 R3
    private InlineTextSource _source;
}
```

> **Correction (measured, Increments 2–3).** Two details above were wrong as originally
> written, and are corrected in the code:
>
> - **`RichTextView` is public, not internal.** A host embedding the editor surface directly —
>   without the `RichTextEditor` template — is a real use, and the demo does exactly that. The
>   commands are on the view, so making it internal would put the entire formatting API out of
>   reach of anyone not using the templated control.
> - **`TextRunCache.InvalidateFrom(offset)` is not used for edits.** Increment 0 measured it
>   returning stale runs for any `offset > 0`. Paragraph layouts are rebuilt wholesale on edit.
>   The cache is kept only for width-only relayout, where it is correct and ≈3.5× faster.

> **The sketch above says `internal` and the code finally agrees** (2026-08-18). `BlockView`,
> `ParagraphView`, `RuleView`, `BlockImageView`, `TableBlockView`, `InlineTextSource` and `TextAt`
> shipped `public`. They are layout objects: a `TextLayout`, a run cache, a measured size, all with
> lifetimes the presenter owns and invalidation rules nothing outside it can honour. Freezing them
> at 1.0 would have made the rendering machinery a contract by accident.
>
> `DocumentPresenter.Blocks` and `Slots` and `RichTextView.Block` went with them, since a property
> is only as internal as its type. **`DocumentPresenter.BlockCount` replaces `Blocks.Count`** and is
> the piece a host actually needed: `BlockOrigin`, `BlockIndexOf`, `BlockIndexAt` and
> `CaretBlockIndex` all speak in block indices, so the range those indices live in is public and the
> objects behind them are not. The demo's status bar was the only external caller and now reads
> `BlockCount`.

> **What deliberately stayed public.** A control library exposes things it never calls itself:
> `IEmbedRenderer`, `IImageStore`, `ITextStyleResolver`, `IParagraphStyleResolver`, `ISyntaxTheme`
> and their default implementations are extension points; `ToolbarConverters` is used from XAML;
> the commands, geometry and events exist for a host. Nothing here was hidden merely because this
> repository has no caller for it. `InternalsVisibleTo` covers the tests instead, which is why
> `VellumText.Core` now grants it to `VellumText.Core.Tests` and `VellumText.Avalonia.Tests` — the
> tests that prove a closed switch throws on an unknown node have to be able to write one, and that
> is friendship, not API.

> **The shell forwards setup, not commands** (2026-08-18). `RichTextEditor` forwarded five of the
> view's surfaces, so every other one had to be reached through `Editor.View!` — a null-forgiving
> operator on a property that genuinely is null until the template is applied. That is fine for a
> command, which runs in response to a user action long after templating, and it is a race for
> setup, where the losing side is silent: a format registered on a view that does not exist throws,
> and an event subscribed on one is never heard from.
>
> So the split is by *when a host reaches it*, not by what it does. The shell forwards `Formats`,
> `ImageStore`, `Embeds`, `TextStyles`, `ParagraphStyles`, `SyntaxHighlighter`, `SyntaxTheme`,
> `ContentImported` and `ImageRefused` — everything configured before the control is on screen.
> The forty-odd editing commands are not forwarded; duplicating them would double the shell's
> surface for no benefit a host can name.
>
> **Amended 2026-08-18.** `ContentImporting` joined that list for the same reason as its two
> neighbours, and `Document`, `Selection`, `DocumentChanged` and `SelectionChanged` joined it for a
> stronger one. Those four are not merely reached before templating — they must *work* before it. A
> view model that binds in its constructor and a host that loads a document into `State` before the
> control is on screen are both ordinary, so the shell raises its own change notifications from its
> own state rather than relaying the view's, and pushes to the view only once there is one.
>
> `EnsureView()` is the escape hatch and is honest about its lifetime: it applies the template if
> that has not happened and returns a view that is never null. That is safe because the default
> template is registered as `TemplateProperty`'s default value rather than living in a theme
> dictionary, so it resolves with no application styles loaded at all. `View` stays nullable and
> answers the different question — has this been templated yet.

**Virtualization:** `BlockHeightIndex` keeps a Fenwick/prefix-sum tree of block heights. Unmeasured blocks get an estimate (average measured height); measuring on realization corrects the estimate and adjusts scroll offset. This is the standard approach and it is why AC12 is achievable.

> **As built (Increment 3, P3).** `RichTextView` implements `ILogicalScrollable` over the height
> index, in `RichTextView.Scroll.cs`. Two things are worth knowing before touching it, and one is
> a limitation rather than a design:
>
> - **The estimate is a feedback loop, and the anchor is what closes it.** `SetHeight` re-estimates
>   every unmeasured block, so laying out one screenful moves the whole document, which moves the
>   window, which lays out more blocks. `Settle()` repeats "capture the block at the top of the
>   viewport → lay the window out → put that block back" until the window stops changing.
>   The fractional offset *into* the anchor is clamped to the height the block turned out to have;
>   without that clamp a scroll lands one block past where the scroll bar pointed. Measured at
>   33 px of visible jump before anchoring, 0 after.
> - **The view must be the scroll viewer's own content.** `ScrollContentPresenter` inspects its
>   immediate child and nothing deeper, so a `Border` between the two — for a page effect, say —
>   silently reverts to laying the whole document out. `RichTextEditor`'s template gets this right;
>   the demo deliberately does not, and is therefore not virtualized.
> - **Layouts are not released** for blocks scrolled out of the window. Only initial layout and
>   the measure pass are virtualized, which is what AC12 asks for; reclaiming the memory of a
>   scrolled-away paragraph needs new `BlockView` surface and was not worth the risk here.

**Caret and selection** are drawn by `RichTextView`, not by block views — selection can span blocks, and the caret is a single view-level concern with one blink timer. Blocks only supply geometry via `GetCaretRect` / `GetSelectionRects`.

> **Correction (measured, Increment 2).** The pixel-snapping recipe originally given here,
> `PixelRect.FromRect(rect, 1).ToRect(1)`, is wrong: `FromRect` rounds outward, so a caret one
> device pixel wide becomes two at fractional DPI, and the width grows with the scale factor.
> Snap the caret's leading edge to a device pixel and keep the width, rather than snapping the
> rectangle.

> **As built (P4).** `RichTextToolbar` is a `TemplatedControl` bound to an editor, with a default
> theme in `Themes/RichTextToolbar.axaml` and a light/dark palette in `Themes/VellumText.axaml`. The
> same control serves twice: `RichTextEditor`'s template puts a second instance in an overlay
> panel above the scroll viewer, and a `VellumText-selection` class swaps in a compact nine-button
> template. A `Style` setter outranks a `ControlTheme` setter, which is the mechanism that lets one
> control have two arrangements.
>
> Three things about the floating bar are not obvious and were each found by a failing test or a
> live accessibility read:
>
> - **It is positioned by `RenderTransform`, never by `Margin`.** Avalonia's `DesiredSize` includes
>   the margin, so a bar positioned by margin inflates its own desired size, which moves it, which
>   inflates it again — `InvalidOperationException: Infinite layout loop detected`, every frame.
>   A render transform does not participate in layout, so the position cannot feed back into the
>   measurement it was derived from. This surfaces in xUnit as a *cleanup* failure, not a body
>   failure, so the test appears to pass.
> - **It is hidden, not made transparent.** `Opacity = 0` does not affect `IsEffectivelyVisible`,
>   which is what `AutomationPeer.IsOffscreen` reports — a transparent bar announces nine buttons
>   that are not on screen. Because a hidden control is never measured, the code that shows it
>   measures it by hand before reading `DesiredSize`.
> - **Every toolbar button is `Focusable="False"`.** Otherwise clicking Bold moves the keyboard out
>   of the document, and the selection the command was meant to act on is gone by the time it runs.

> **As built (P5).** `RichTextViewer` renders a document with no caret, no keyboard and no history.
> It is a distinct control rather than an `IsReadOnly` flag on the editor, and that choice was
> settled by measurement rather than taste: constructing a `RichTextView` costs **19.7 KiB and
> 0.050 ms** more than a bare `Control` before it lays out a single line — the blink timer, the
> input-method client and the context flyout — which is **50% of the allocation** of a laid-out
> view of a clipboard-sized document. Fifty history entries would spend about a megabyte on
> editing machinery no reader can reach. Composition (a viewer hosting a `RichTextView` with
> editing switched off) was rejected for the same reason: it pays the cost and then hides it.
>
> Both controls derive from **`DocumentPresenter`**, which owns everything that is not editing:
> block flattening and reconciliation, the height index, virtualization, `ILogicalScrollable`, and
> measure/arrange. The subclass seams are `PresentedDocument`, `Anchor`/`OnReconciled` (following
> a block across a rebuild), `ScrollAnchorIntoView`, and `RenderContent`. Sharing the layout code
> rather than duplicating it is what makes a document render identically in either control — an
> invariant asserted directly, down to the caret gutter, which the viewer pays for so that moving
> a document between the two cannot reflow it by a pixel.
>
> Two things worth carrying forward:
>
> - **The presenter declares its font properties with `AddOwner`, not just `TextElement`
>   inheritance.** A viewer is typically a leaf inside someone else's `DataTemplate` with nothing
>   above it carrying a font, so `FontSize="12"` has to resolve on the control itself. Without it
>   the failure is a XAML *compile* error (`AVLN2000`), and — found the hard way — an incremental
>   `dotnet build` can report success while XAML compilation has failed, leaving a stale binary
>   running. `AddOwner` returns the same property instance, so the existing font-invalidation set
>   still matches; that is asserted by a test rather than assumed.
> - **Text is not selectable in the viewer yet.** A reader who cannot copy part of an entry will
>   copy the whole thing and delete the rest. Closing it means sharing the editor's pointer
>   handling and clipboard write, which is larger than the control itself, so it is recorded as a
>   known gap rather than quietly omitted.

> **Correction (2026-08-18, viewer selection).** The sentence above — *"Text is not selectable in the
> viewer yet"* — is no longer true. The viewer now supports pointer selection (drag, double-click
> word, triple-click paragraph), keyboard selection (Shift+arrows, Shift+Home/End, Ctrl+A) and
> copy (Ctrl+C / Ctrl+Insert), and takes focus (`Focusable = true`) while staying out of the tab
> order (`IsTabStop = false`) so a viewer embedded in a list does not become a tab stop.
>
> The estimate that closing it "is larger than the control itself" was wrong, and the reason is
> the `DocumentPresenter` split described above: hit-testing, `Resolve`, `ScrollAnchorIntoView`
> and the range enumeration were already shared, so what the viewer needed was a selection pair,
> a renderer pass and a clipboard *write*. The clipboard formats guard was the only real
> duplication and was lifted into a shared `DocumentFormats`.
>
> **The read-only guarantee is structural, not a flag.** The viewer has no `Apply`, no
> transaction, no history and no `TextInputMethodClient`, so there is no edit path to reach —
> unlike `RichTextView`, where `IsReadOnly` is a single check inside `Apply`. Copy is a pure read:
> it exports the cut document, it never mutates one. Cut and paste are absent by construction
> rather than by rejection.
>
> One bug fell out of writing the tests, and is worth recording because it would have been
> invisible to a reviewer: the caret initialised to raw position `0`, which is *outside* the first
> paragraph's text, so the first Shift+Right selected the second character and `SelectAll` covered
> nothing. Selection endpoints are now snapped through a `NearestText(position, forward)` helper
> mirroring the editor's `NearestTextPosition`.

> **As built (P6).** A table is drawn as a real grid by **`TableBlockView`** — the only `BlockView`
> that hosts other block views. It is named for the collision: Avalonia 12 ships its own
> `Avalonia.Controls.TableView`, and a consumer with both usings should never have to disambiguate.
>
> The design turns on one distinction that is easy to miss and expensive to get wrong:
>
> - **`DocumentBlocks` now answers two different questions.** `Flatten` answers *what gets drawn* —
>   a table is **one** slot whose range spans everything inside it, so virtualization, hit testing
>   and reconciliation treat it as a unit. `Leaves` answers *what a caret can be inside* — a table's
>   paragraphs are separate and adjacent to each other, however deeply nested. They diverge exactly
>   at tables. Conflating them broke backspace between two paragraphs in one cell, which is how the
>   distinction was found.
> - **`BlockView.ParagraphAt(int)`** is the seam that lets both be true at once. `RichTextView`'s
>   `Block`, `BlockStart` and `BlockOffset` all resolve through it, so no editing command has to
>   learn what a cell is; `TableBlockView` recurses, so a nested table works for free.
>
> Layout details worth carrying forward:
>
> - **Row heights settle in two passes.** Single-row cells fix their row first; only then do
>   spanning cells push, and they push the **last** row they cover, by the shortfall. Charging a
>   spanning cell's full height to its first row is the classic way to get one enormous row.
> - **Stated column widths are a proportion, not a measurement.** A pasted table's widths were
>   written for someone else's window, so they are scaled to the space available; unstated columns
>   get the mean of the stated ones; none stated is an even split. That is not what a browser does
>   — browsers size to content — but it is predictable and never collapses a column. Content-based
>   sizing needs a min/max width pass the block views do not expose.
> - **A cell that states its own background beats the header tint.** The document was explicit; the
>   tint (`TableHeaderBrush`) is only the control saying what a header row usually looks like.
> - **The schema reports, it does not enforce.** `new TableCellNode([])` is constructible and
>   reaches the view, so "the schema guarantees it" is never a safe assumption about view input.
>   Assuming it cost a real `IndexOutOfRangeException`.
>
> Two known gaps at the time: `Reconcile` has no in-place update path for a table, so any
> edit inside one rebuilds the whole table view. Correct, but unmeasured. Rectangular **cell
> selection** was the other, and is closed by P8 below.
>
> **Vertical caret movement had to become geometric.** It stepped to the adjacent *drawn* block,
> which once a table was one block skipped every table whole. Stepping over `Leaves` instead fixed
> that and introduced a subtler bug: document order is not visual order, so Down out of a top-left
> cell went *right*, into the cell beside it. `AdjacentParagraph` now takes the nearest row of
> paragraphs past the caret's line and, within that row, the one landing closest to the goal
> column — and the goal column is kept in the control's coordinates rather than the paragraph's,
> or it would slide back to each cell's left edge on the way through.
>
> That walk is linear in the document, so `Leaves` is cached against the document it was built
> from — sound because a document is immutable, so reference equality is the right key and there is
> no invalidation hook to forget. It is worth it: measured on 2000 paragraphs, the walk was
> **0.577 ms of a 0.582 ms** keypress. Cached, a Down press costs **0.406 ms** against **0.459 ms**
> for a Right press, so vertical movement is no dearer than horizontal and the residual is
> pre-existing per-keypress cost rather than anything tables introduced.

> **As built (P7, accessibility).** A `DocumentPresenter` now creates a
> **`DocumentPresenterAutomationPeer`**, so both the editor and the viewer appear to a screen
> reader as `AutomationControlType.Document` carrying the document's plain text, with
> `IValueProvider.IsReadOnly` telling the two apart. Verified against a running demo: the
> accessibility tree previously showed the editor and the viewer as untitled controls with no
> content; it now reads out the whole document, tables included.
>
> **The hard limit is worth stating rather than working around: Avalonia 12 has no UIA text
> pattern.** `Avalonia.Automation.Provider` offers value, invoke, scroll, selection,
> expand/collapse, range and toggle, and nothing for text — `TextBox` is in the same position and
> answers it the same way, with `IValueProvider` alone. So **caret position, selection extent and
> per-run formatting cannot be announced.** A reader can read the document and be told when it
> changes; it cannot follow the caret through it. Closing that means implementing `ITextProvider`
> upstream in Avalonia, not here.
>
> Two decisions inside that constraint:
>
> - **The editor reports `Document`, not `Edit`.** `Edit` tells an assistive technology to expect a
>   field it can navigate with the text pattern — exactly the promise this peer cannot keep.
> - **`SetValue` is honoured on the editor**, replacing the document with plain text in one undo.
>   Destructive, and nonetheless the contract: it is how every UI-automation framework fills a
>   field, and an editor that refused it could not be driven by one. The viewer throws
>   `ElementNotEnabledException`.
>
> **`DocumentText.Of` in `VellumText.Core` is now the single plain-text projection**, shared by the peer
> and by the clipboard's plain-text flavour. Unifying them fixed a defect nobody had noticed: copying
> a selection containing an image put a raw `U+FFFC` on the clipboard instead of the image's alt
> text. An embed contributes its `PlainTextFallback`; blocks are separated by one newline and
> nothing else, because a list marker is drawn by the view and is not in the model.

> **As built (P7, find and replace).** `DocumentSearch` in `VellumText.Core` walks the tree and returns
> `ImmutableArray<SearchMatch>`; `RichTextView` owns the open search, paints the matches, and
> `RichTextEditor` exposes `Find`/`FindNext`/`FindPrevious`/`ReplaceCurrent`/`ReplaceAll` plus
> observable `MatchCount` and `CurrentMatchNumber`. Four decisions are load-bearing and each was
> forced by something measured rather than chosen on taste:
>
> - **Search reads `paragraph.Content.Text`, not `DocumentText.Of`.** The raw text has exactly one
>   code unit per document position, so an offset into it *is* a position. The plain-text projection
>   expands an embed into its alt text and puts every later position out by the difference.
> - **Comparison is ordinal.** Under ICU a culture-sensitive `IndexOf` can return a match that is not
>   its own length: `"co\u00ADoperate".IndexOf("cooperate", CurrentCulture)` is `0` — a soft hyphen
>   has no collation weight — while ordinal is `-1`. Matches are handed straight to a replacement, so
>   a match whose length is not the query's length corrupts the document. (The familiar German ß/ss
>   example does *not* discriminate here; this one was measured.)
> - **The current match is given up on any document change.** Re-finding it by value works for an edit
>   *after* it and silently fails for one *before* it, which is worse than not working because it looks
>   right in testing. Mapping through the transaction is the alternative and is not worth the coupling;
>   `ReplaceCurrent` and `ReplaceAll` step explicitly instead.
> - **The current highlight is painted over the selection, the others under it.** Stepping to a match
>   selects it, so the two always coincide. Painting the current one underneath leaves the selection
>   tint mixed in and the result reads as neither — found by looking at the pixels, not by reasoning
>   about them.
>
> Re-search is guarded on document identity, so moving the caret — the commonest state change, and the
> one stepping between matches goes through — costs a reference comparison.

> **As built (P7, image selection and resize).** Clicking a picture selects it and draws four corner
> handles; dragging one resizes it, preserving aspect. `RichTextView.Images.cs` owns the gesture,
> `BlockView.GetImageRect` answers where the picture is, and `RichTextEditor` needs nothing new.
> Four decisions were forced by measurement:
>
> - **"An image is selected" is derived from the selection's *range*, not from a new selection kind.**
>   A block image is a leaf, so `NodeSelection.At` answers for it; an inline image lives inside a
>   paragraph, where `ResolvedPos.NodeAfter` is null because the position `IsInText`, so a node
>   selection cannot reach it. `SelectImage` produces a `NodeSelection` for the one and a one-long
>   `TextSelection` for the other, and `SelectedImage` just asks whether the range is one position
>   long with an image at its start. Delete already removes either without knowing an image was
>   involved.
> - **`GetImageRect` is a separate question from `GetSelectionRects`.** The selection rectangle for an
>   inline image is the *line* box, which is taller than the picture by the descent of the text beside
>   it — measured, a 40pt image on a line reports a 43pt selection box. Handles placed on that hang
>   off the bottom. The horizontal extent comes from `GetTextBounds`, which is right across a bidi
>   boundary; the vertical extent comes from the run, positioned at the line's baseline less the run's.
> - **A forced line height is absolute in Avalonia, not a minimum.** A paragraph whose
>   `LineHeightScale` produced 19.2pt reported `Height` 19.2 with `Baseline` 40 for a line holding a
>   40pt image — a box that cannot exist — and the picture was drawn over the block below it. This was
>   a live rendering defect, found by asking where to put a handle. `ParagraphView` now gives the
>   leading up when its tallest embed does not fit, which grows only the lines that need it; a small
>   embed changes nothing.
> - **The drag applies nothing until the pointer is released.** A transaction per pointer move would
>   put a hundred steps on the undo stack for one gesture and reflow the document under the pointer
>   while the user is still aiming. A press and release with no movement is a click, not a resize, and
>   produces no transaction at all.
>
> The gesture is checked before the caret on press, because a handle straddles the corner and sits over
> the text beside the picture; Shift is left to the text path so a selection can still be extended
> across an image. Corner drags are clamped rather than mirrored — dragging past the anchor stops at
> `MinimumImageSide` rather than growing again — and both sides are floored there because `ImageEmbed`
> refuses a non-positive dimension.

> **As built (P7, touch selection handles).** A touch tap puts a round handle under each end of the
> selection — or a single one under the caret — and dragging a handle moves that end while the other
> is pinned. `RichTextView.Touch.cs` owns it; the gesture is picked out in `OnPointerPressed` by
> `e.Pointer.Type == PointerType.Touch`, ahead of the image and caret paths, and any non-touch press,
> key or focus loss takes the handles away again. Three things are load-bearing:
>
> - **The offset from the grabbed point to the caret it belongs to is captured on press and added to
>   every move.** A handle deliberately hangs a whole line below the text it points at, so that a
>   finger does not cover what it is aiming at — which means hit-testing the raw touch point puts the
>   end of the selection on the *next* line. Without the offset the selection jumps down the moment it
>   is touched.
> - **The ends come from the caret rectangle at `Selection.From` / `Selection.To`, not from
>   `GetSelectionRects`.** Across a bidi boundary the logical start of a selection is drawn on the
>   *right*, and only the caret rectangle knows that; the selection rectangles are in visual order and
>   would put both handles on the wrong ends.
> - **The fixed end is re-pinned as the anchor on press.** Dragging the start past the end has to flip
>   the selection, not collapse it, and `MoveTo(…, extend: true)` extends from `Selection.Anchor` —
>   which after a leftward drag-select is already the end the finger is holding.
>
> A handle is grabbed from `TouchHandleTargetRadius` (22) away rather than from the 7pt circle it is
> drawn as, and the *nearest* of the two wins, because a fingertip is far larger than the target and
> the two handles of a short selection overlap. A touch away from any handle falls through to the
> normal caret placement.
>
> Avalonia's headless harness has **no touch input** — `HeadlessWindowExtensions` offers mouse,
> keyboard and drag-drop only. The tests construct `Pointer`, `PointerPressedEventArgs` and the rest
> directly (all have public constructors) and `RaiseEvent` them, which exercises the real routing
> rather than calling the handlers.

> **As built (P7, the perf pass).** Measured rather than guessed, on this machine, median of 30–40
> reps after warmup. Typing one character costs:
>
> | Document | Per keystroke |
> |---|---|
> | 50 paragraphs | 0.56 ms |
> | 500 paragraphs | 1.36 ms |
> | 2 000 paragraphs | 2.22 ms |
> | one 400-word paragraph | 7–12 ms |
> | 5×3 table | 1.11 ms → **0.29 ms** |
> | 20×5 table | 6.34 ms → **0.51 ms** |
> | 50×5 table | 18.47 ms → **0.87 ms** |
>
> **One structural defect, now fixed.** A table was the only block `Reconcile` could not update in
> place, so every keystroke inside one rebuilt the view of every cell — a fresh `TextLayout` each.
> That is the only thing in the document whose per-keystroke cost scaled with its own size, and at
> 250 cells it was past a 16.7 ms frame for one typed character. `TableBlockView.Update` now keeps
> every cell whose node the edit did not touch, which the persistent tree makes a reference
> comparison. Roughly **21× at 250 cells**, and the remaining cost is the walk rather than text
> shaping.
>
> **Three things measured as fine and deliberately left alone:**
>
> - `DocumentBlocks.Flatten` is 0.12 ms at 2 000 paragraphs — 6% of the keystroke, not worth
>   making incremental.
> - `Reconcile` touches every block on every edit to reset list markers, but `ParagraphView.SetLead`
>   early-outs on an unchanged depth and marker, so that loop is a comparison per block.
> - Scrolling a 2 000-paragraph document by a viewport is ~4–5 ms, inside a frame.
>
> **One known limit, not fixed.** A single very long paragraph costs 7–12 ms per keystroke, because
> A1 established that Avalonia 12.1.1 cannot reshape a paragraph incrementally and the fallback is a
> wholesale rebuild. It is bounded by paragraph length rather than document length, so it does not
> get worse as a document grows, and virtualization keeps it off screen entirely for paragraphs that
> are not visible. Fixing it needs upstream support for partial invalidation, not a change here.

### 4.7 The text source

```csharp
internal sealed class InlineTextSource : ITextSource
{
    private readonly InlineContent _content;
    private readonly ITextStyleResolver _styles;   // MarkSet -> TextRunProperties, memoized
    private readonly PreeditOverlay? _preedit;     // IME, view-only

    public TextRun? GetTextRun(int textSourceIndex)
    {
        if (textSourceIndex >= EffectiveLength) return new TextEndOfParagraph();
        if (TryGetEmbedAt(textSourceIndex, out var embed)) return new EmbedRun(embed);   // : DrawableTextRun
        var span = SpanContaining(textSourceIndex);
        return new TextCharacters(
            _content.Text.AsMemory(textSourceIndex, span.End - textSourceIndex),
            _styles.Resolve(span.Value));
    }
}

internal sealed class EmbedRun : DrawableTextRun
{
    public override TextRunProperties Properties { get; }         // REQUIRED — see note below
    public override Size Size { get; }
    public override double Baseline { get; }
    public override int Length => 1;                              // matches the \uFFFC placeholder
    public override void Draw(DrawingContext ctx, Point origin) { /* image / inline control */ }
}
```

Two measured details from Increment 0 ([findings §1](increment-0-findings.md)):

- **`Properties` must be overridden.** `TextRun.Properties` is virtual and defaults to `null`. A run that leaves it null **formats successfully** and only throws when drawn — `ArgumentOutOfRangeException (Parameter 'baselineAlignment')` from `TextLineImpl.GetBaselineOffset`. The failure therefore surfaces at paint time, far from its cause.
- **`Draw`'s `origin` is the top-left of the run's box**, with `Baseline` measured downward from it. Use `BaselineAlignment.Baseline`; `Top`/`TextTop` are displaced by Avalonia's baseline formula (`-run.Baseline + line.Height - line.Extent + run.Height / 2`), which is the implementation's behaviour rather than a defect.

Because `Length` is 1 and the model reserves exactly one `\uFFFC`, positions from `TextLayout.HitTestPoint` map to model positions by a single additive block offset. No index translation table.

### 4.8 IME

```csharp
internal sealed class RichTextInputMethodClient : TextInputMethodClient
{
    public override Visual TextViewVisual => _view;
    public override bool SupportsPreedit => true;
    public override bool SupportsSurroundingText => true;
    public override string SurroundingText => CurrentParagraphPlainText();
    public override Rect CursorRectangle => _view.GetCaretRectInVisual();
    public override TextSelection Selection { get => …; set => …; }   // paragraph-local offsets

    public override void SetPreeditText(string? text, int? cursorPos)
    {
        // View-layer overlay ONLY. Never a Transaction.
    }
}
```

**Preedit must not enter the document model or the undo stack.** It is composition state, not content. It lives in `PreeditOverlay`, is injected by `InlineTextSource` as extra runs (styled with an underline decoration), and is committed as one ordinary `Typing` transaction when the IME finalizes. Getting this wrong is the classic rich-text bug: undo replaying half-composed Japanese.

Note `SurroundingText`/`Selection` are paragraph-local — the document is far too large to hand a screen-reader-sized string to the IME, and platform IMEs only want local context.

### 4.9 Clipboard

```csharp
internal static class RichTextClipboardFormats
{
    private static DataFormat<byte[]> Platform(string win, string mac, string other)
        => DataFormat.CreateBytesPlatformFormat(
               OperatingSystem.IsWindows() ? win :
               OperatingSystem.IsMacOS()   ? mac : other);

    public static readonly DataFormat<byte[]> Rtf  = Platform("Rich Text Format", "public.rtf",  "text/rtf");
    public static readonly DataFormat<byte[]> Html = Platform("HTML Format",      "public.html", "text/html");

    // Lossless internal copy/paste — survives what HTML/RTF cannot express.
    public static readonly DataFormat<byte[]> NativeSlice =
        DataFormat.CreateBytesApplicationFormat("VellumText-slice+json");
}
```

**Copy** writes, in priority order: `NativeSlice`, `Html`, `Rtf`, `DataFormat.Text`.
**Paste** reads in the same order and stops at the first it understands — so in-app paste is lossless, cross-app paste degrades gracefully.

Windows `HTML Format` requires the CF_HTML preamble (`Version:0.9`, `StartHTML`/`EndHTML`/`StartFragment`/`EndFragment` **byte** offsets, UTF-8). Byte offsets, not character offsets — a classic source of silent corruption with non-ASCII content.

#### As built (P2)

Two things changed against the sketch above, both forced by decision 3 in §12.1:

- **`VellumText.Avalonia` does not reference the interop packages**, so it cannot name an HTML or RTF exporter. Formats are **registered** instead — `view.Formats.Add(HtmlFormat.Instance)` — through `IDocumentFormat`, which `IDocumentImporter` and `IDocumentExporter` both extend. An application that only wants an editor does not acquire an HTML parser and an RTF reader to get one. With nothing registered, copy and paste still move plain text.
- **`NativeSlice` is not built.** It exists to carry what HTML cannot, and HTML currently carries everything the model can hold. It is worth adding when that stops being true, and not before.

#### Registration is the only seam, so options travel through it

`HtmlFormat` and `RtfFormat` were constructor-less singletons. That made `HtmlImportOptions`
unreachable from the path the bullet above tells a host to use: the control holds an
`IDocumentImporter` and calls `Import`, so `HtmlImporter.Import(html, options)` — a static method on
a type in a package the control cannot name — is not a seam an application using the control can
reach at all. `AllowRemoteImages` is a privacy control and `BaseUri` decides whether a relative
image URL resolves or is dropped, and neither did anything for anyone using the editor.

`With(options)` returns a configured instance and leaves the singleton alone:

```csharp
view.Formats.Add(HtmlFormat.Instance.With(new HtmlImportOptions { BaseUri = source }));
```

An instance rather than mutable state on the singleton, because two views in one application can
legitimately trust different sources, and a shared mutable `Instance` would make the last
registration win for everything.

#### The format contract is bytes, with strings on top

The contract was string-only until it was noticed that this *structurally* excludes DOCX and ODT — both are ZIP containers, both are on the roadmap in §10, and a format that cannot be expressed is worse than one that is merely unimplemented. Streams are the primitive now:

```csharp
public interface IDocumentFormat
{
    string Format { get; }
    string MediaType { get; }
    Encoding? TextEncoding { get; }   // null ⇒ this format is bytes, not text
}

public interface IDocumentImporter : IDocumentFormat
{
    ImportResult Import(Stream source);            // every format implements this
    ImportResult Import(string text) => …;         // default: encode, then defer
}

public interface IDocumentExporter : IDocumentFormat
{
    void Export(Stream destination, DocumentNode doc);   // every format implements this
    string Export(DocumentNode doc) => …;                // default: write, then decode
}
```

Four decisions in that shape:

- **Streams are required and strings are derived**, not the other way round. A DOCX importer must not be made to write a nonsense `Import(string)` to satisfy the interface, whereas a text format's `Export(Stream, doc)` is one line over its existing writer. The string members stay because HTML and RTF genuinely are text and forcing a caller holding a string to wrap it in a `MemoryStream` would be pure ceremony.
- **The string members are default interface members**, so exactly one member of each pair is mandatory and the text formats override both, each calling its parser directly. The hazard is writing `Import(Stream)` in terms of `Import(string)` without declaring the latter — that recurses forever, and it is called out on both members.
- **`TextEncoding` is required, not defaulted.** It is not derivable from `MediaType`: `text/rtf` is Latin1 by construction while `text/html` on the clipboard is UTF-8 by definition, and a wrong default is silent mojibake rather than a compile error. It is also the honest place for a format to say it has no text form at all.
- **A binary format's string members fail loudly and asymmetrically.** `Import(string)` returns an empty result with a `Malformed` diagnostic, because an importer that throws is what `ImportResult` exists to avoid; `Export(DocumentNode)` throws `NotSupportedException`, because there is no honest string and `""` would be a document silently lost on the way to a file.

`DocumentPayload.ReadAllText`/`WriteAllText` are the two-line bridge every text format's stream members use. They are public because the formats that need them are separate assemblies that cannot see each other, and so is any format a host writes. `WriteAllText` emits no BOM — `CF_HTML`'s byte offsets are computed over a payload without one and an RTF reader rejects a document preceded by `EF BB BF` — and `ReadAllText` leaves the stream open, because the caller owns it.

The clipboard carries bytes end to end as a result: `RichTextClipboard.Encode` asks the exporter to write into a `MemoryStream` and `Decode` hands the payload straight back as one, so a registered binary format survives copy and paste unchanged. `ClipboardFlavor` no longer carries an `Encoding` — `IDocumentFormat.TextEncoding` is the single source of truth, and a flavour minted with a hard-coded UTF-8 would be a lie for a format that is not text.

> **Corrected 2026-08-18 — one implementation, not three.** `RichTextView` and `RichTextViewer` each built their own clipboard payload beside this helper's, and the copies drifted until the difference was user-visible (§4.9, plain text). All three are now one call: `RichTextClipboard.Build` and `WriteAsync` take the plain text flavour from the caller, because a control already knows what it shows selected and a rectangle of table cells is copied as a rebuilt document rather than a cut of the original.
>
> Three `FlavorFor(...) is not { } flavour` guards went with it. `FlavorFor` stopped being nullable when it learned to mint an application-specific flavour for an unregistered format, so those branches had become unreachable. The behaviour they guarded is deliberate and was left alone — a format with no platform convention still gets a flavour, which is what makes registering one work at all.

Native formats are read and written as **bytes**, never as strings. Asking a clipboard backend for RTF as a string lets it reinterpret those bytes as UTF-16, which yields convincing mojibake that then parses to nothing. `CF_HTML` is UTF-8 by definition; RTF is seven-bit by construction and is carried as Latin1, which cannot substitute.

**The envelope is Windows-only, and for a while it was not.** `HtmlFormat.Export` wrapped every payload in `CF_HTML` regardless of platform, so a copy on Linux or macOS put `Version:0.9` and a block of byte offsets under `text/html` and `public.html` — targets that are defined to carry bare markup. A receiving application would have rendered the preamble as content. The format *name* was already chosen per platform by the `Platform` helper above; only the payload was not, and this paragraph's own sentence about `HTML Format` being a Windows requirement was the specification the code had drifted from.

It survived because **nothing could see it**. `ClipboardHtml.ExtractFragment` returns non-`CF_HTML` input unchanged, so import accepts both shapes on every platform and a round trip succeeds either way — no round-trip test can distinguish them, and every clipboard test is a round trip. Only an assertion about the payload itself can, and the branch was on a platform that no developer here runs. The fix is one conditional; the test asserts both branches so CI's three-OS matrix exercises the one the author's machine never takes.

**What a round trip actually costs**, measured rather than assumed:

| | HTML | RTF |
|---|---|---|
| Text, escapes, astral characters, bidi, ZWJ clusters | exact | exact |
| Marks, colour, highlight, font, size, links | exact | exact |
| Headings, alignment, indent, lists incl. `start`, tables | exact | exact |
| Collapsible whitespace | preserved, via `white-space:pre-wrap` on the paragraphs that need it | doubled spaces return with the second as U+00A0; a tab is lost; a space at a paragraph edge is lost |
| Quote, code block, header cells | exact | flattened to indent, body and bold |

The RTF losses are in the **reader** — RTF itself keeps all of it, and a word processor opening the file sees it — so the exporter's tests assert on the bytes emitted, not only on what survives a round trip.

#### Telling the application what an import cost

The table above is the point: **an import is lossy and the losses are known item by item.** Every importer already returns an `ImportResult` carrying `ImportDiagnostic`s — a `<blink>` dropped, a colour that could not be represented, an image over the store's limit, a table flattened — and paste and drop both computed that list and then took `.Doc` and threw the rest away. The content changed on the way in and no application could know, let alone say so.

`RichTextView.ContentImported` carries them out:

```csharp
public event EventHandler<ContentImportedEventArgs>? ContentImported;

public sealed class ContentImportedEventArgs : EventArgs
{
    public ImmutableArray<ImportDiagnostic> Diagnostics { get; }
    public bool AnythingDropped { get; }   // something removed, not merely downgraded
}
```

Three decisions in it:

- **A notification, not a veto.** The content is in the document by the time it is raised; there is nothing to cancel. Interception *before* the fact is a separate piece of work and is deliberately not this — a cancellable event here would be a lie, because the undo entry already exists.

  > **Corrected 2026-08-18.** That separate piece of work is now done, and the decision above stands unchanged: `ContentImported` is still a notification and still uncancellable. What was added is a *second* event that comes first.
  >
  > ```csharp
  > public event EventHandler<ContentImportingEventArgs>? ContentImporting;
  >
  > public sealed class ContentImportingEventArgs : CancelEventArgs
  > {
  >     public DocumentNode Content { get; }
  >     public ImmutableArray<ImportDiagnostic> Diagnostics { get; }
  >     public bool AnythingDropped { get; }
  > }
  > ```
  >
  > It carries the exact document that would be inserted, so a host sees what it is refusing rather than a description of it. Four things about it are load-bearing:
  >
  > - **It is raised only for content that would otherwise land.** A read-only control, a rectangle selection, an empty clip — all of those already refuse the paste, and asking a host to arbitrate one that was never going to happen is how a policy handler ends up counting rejections that did not occur. `WouldPaste` and `WouldPasteSlice` answer that question before the event is raised.
  > - **A refusal is a paste that did not occur.** No edit, no undo entry, no `ContentImported`, no diagnostic. The alternative — reporting a refusal through the diagnostics channel — was rejected: `ImportDiagnostic` describes what an *importer* could not represent, and a host's own policy is not that.
  > - **A handler that throws costs one paste.** On the keyboard, menu and drop paths the existing broad handler swallows it, exactly as it does for a throwing exporter. `PasteImport` called directly rethrows, because a programmatic import has a caller who can deal with it. The document is never left half-changed either way: the gate runs before the transaction is built.
  > - **Two seams, both behind the same gate.** `PasteImport` and `PasteImportedText`. The plain-text path is separate for the reason given below, and gating only the rich one would let a host's policy be bypassed by pasting from Notepad — which is §12's "the fourth call site was missed" in advance.
  >
  > Images are deliberately outside it. `IImageStore.Save` returning `null` is already inspect-and-refuse, and `ImageRefused` already reports the outcome; a third convention for the same decision would be worse than either.
- **Raised for a clean import too**, with an empty list. A host that puts up "2 things were changed" after one paste needs the next, clean paste to take that notice down, and it can only do that if the clean paste tells it something.
- **One seam, not three.** Paste, drop and a host's own file import all go through `PasteImport(ImportResult)`, which pastes and then raises. Wiring the event at each of the three call sites is exactly how this defect happens again the next time a fourth is added; the public `PasteImport` is also the programmatic import path an application needs to open a file with the same reporting the clipboard gets.

Plain text is the one path that does not go through it. `PasteText` normalizes line endings and strips the reserved U+FFFC placeholder, and routing it through `DocumentNode.FromParagraphs` instead would throw on that placeholder into a broad `catch` — a bug already fixed once, where a whole clip pasted nothing. It reports the same way with an empty list, having lost nothing worth naming.

> **Corrected 2026-08-18.** Still true that plain text has its own seam, and no longer true that it has its own *normalization*. The two paths had drifted: `RichTextClipboard`'s plain-text fallback did route through `DocumentNode.FromParagraphs` and did throw on U+FFFC into a broad `catch`, so the bug described above as "already fixed once" was fixed only in `RichTextView` — dropping text carrying U+FFFC inserted nothing while pasting the same text worked. The two also disagreed about what a line break is, the helper normalizing through `string.ReplaceLineEndings` and the view only CRLF and CR, so one clip became seven paragraphs dropped and three pasted.
>
> `RichTextClipboard.Lines` is now the single answer to "what paragraphs does this text become", used by the helper's two read overloads and by the view's paste path. The seam is still separate — `PasteImportedText` rather than `PasteImport` — because the two build different slices: plain text always pastes into an open slice, where `PasteImport` closes the slice for an empty host block so that text pasted into an empty heading stays a heading.

It follows `ImageRefused` (§4.6) rather than inventing a second convention: a host that does not subscribe gets exactly the old behavior.

---

## 5. Alternatives considered

Only materially distinct options; scored 1–5 (5 best).

### 5.1 Document model

| | Correctness | Op risk | Maintainability | Migration cost | Reversibility |
|---|---|---|---|---|---|
| **A1** WPF-style `FlowDocument`/`InlineCollection` tree | 2 | 2 | 2 | 5 | 2 |
| **A2** Flat buffer + interval attributes (Quill Delta) | 3 | 4 | 4 | 3 | 3 |
| **A3 ✅** Typed block tree + flat inline runs + steps | 5 | 4 | 5 | 2 | 4 |

**A3 wins.** A1 has no positions and no invertible ops — undo becomes snapshotting, and every structural feature is a special case. A2 is genuinely attractive and is what Quill ships, but a purely linear model represents tables and nested lists only by convention; every table operation degenerates into fragile marker arithmetic, and AC5 is a v1 requirement. A3's cost is a real schema/position layer up front — that cost is paid once, in Increment 1, headlessly, with unit tests.

### 5.2 Layout strategy

| | Correctness | Op risk | Maintainability | Migration | Reversibility |
|---|---|---|---|---|---|
| **B1** One `TextLayout` for the whole document | 2 | 1 | 3 | 4 | 3 |
| **B2 ✅** One `TextLayout` per paragraph in a virtualizing host | 5 | 4 | 4 | 3 | 4 |
| **B3** A `TextBlock`/control per paragraph | 3 | 2 | 3 | 4 | 3 |

**B2 wins.** B1 reshapes the entire document on every keystroke and cannot express tables at all. B3 puts thousands of styled `AvaloniaObject`s in the visual tree — measurable memory and layout cost, and caret/selection control is surrendered to controls that were not built for it. B2 also composes: a table cell is just another block host, recursively.

### 5.3 Styling attribution

| | Notes |
|---|---|
| **C1** `textStyleOverrides: IReadOnlyList<ValueSpan<TextRunProperties>>` | Zero custom code. Character formatting only — **no embeds**. |
| **C2 ✅** Custom `ITextSource` emitting `TextCharacters` + `DrawableTextRun` | Required for AC4 (inline images). |

**C2, with C1 as the Increment-0 spike.** C1 proves the rendering path in an afternoon and is a valid fallback if A2 (embed-in-source) fails validation.

### 5.4 Document interop

| Format | Choice | License | Rationale |
|---|---|---|---|
| DOCX | `DocumentFormat.OpenXml` 3.5.1 | **MIT** | Microsoft/.NET Foundation, active (rel. 2026-03-18), pure managed OOXML — no Office/COM, works on Linux/macOS. Low-level, so it sits behind our own adapter. |
| HTML | `AngleSharp` 1.7.1 | **MIT** | HTML5-spec parsing with browser-grade error recovery — essential for the malformed fragments Word and web apps put on the clipboard. |
| Markdown | `Markdig` 1.3.2 | **BSD-2** | AST + custom renderers + trivia round-trip. |
| RTF | `RtfPipe` 2.0.7 for import, hand-rolled writer for export | **MIT** | Superseded the original "a general parser is not worth it" call once the clipboard-manager use case made RTF import mandatory (§12.3). RtfPipe converts RTF to HTML, which then goes through the same AngleSharp path as any other paste, so there is exactly one place deciding what a paste may contain. Stale (last release 2021) but correct on the Word and Outlook output that matters. |

**Known RTF import limitation.** RtfPipe treats black as the absence of a colour and emits no
CSS for it, so text coloured *explicitly* black is indistinguishable from unstyled text by the
time anything of ours sees it, and it follows the theme. Recovering the distinction would mean
reading `\colortbl` and `\cf` ourselves — the hand-written RTF reader this section exists to
avoid — so the behaviour is pinned by a test rather than worked around. `\highlight` and every
non-black `\cf` colour do survive. See `Explicit_black_arrives_as_no_colour_at_all`.

**The HTML stage is internal, and the public API says so.** `RtfImporter.Import` took an
`HtmlImportOptions`, which put HTML's type in the RTF package's public surface: a consumer who
wanted only RTF had to reference the HTML package to configure it, and the choice above — that RTF
is read by way of HTML — was frozen where a later rewrite of the RTF reader could not have changed
it. `RtfImportOptions` is the RTF package's own type, mapped to HTML's internally. It is
deliberately smaller: `MaxDepth` is absent because the HTML it would apply to is generated here
rather than authored, so there is no reason for a host to loosen a stack-overflow bound on markup it
did not write. The project reference stays; only the leak is closed.

> **Corrected 2026-08-18.** This paragraph also listed `PreserveIdentifiers` among the options
> `RtfImportOptions` deliberately omits. That option has since been deleted from
> `HtmlImportOptions` too — it was declared, defaulted and documented, and no code path ever read
> it, so setting it did nothing in either direction and a host had no way to find that out. There
> is nothing left to omit.

**Rejected:** `NPOI` — Apache-2.0 source but 2.8.0 binaries impose a maintenance-fee EULA on revenue-generating users; a legal hazard for a "free" library. `DocX`/Xceed — Xceed Community License, non-commercial. `OpenXML-Office` — AGPL-3.0/commercial. `ShapeCrawler` — PPTX only.

### 5.5 Build on `RichTextKit` instead?

`toptensoftware/RichTextKit` is **Apache-2.0** (GitHub shows `NOASSERTION`; `license.txt` is Apache-2.0), active (commits through 2026-07), ~425 stars, and includes an `Editor/TextDocument` subsystem with caret and selection support.

**Rejected as a foundation, valuable as prior art.** It layers on SkiaSharp directly, which means bypassing Avalonia's renderer — losing the compositor, non-Skia backends, Avalonia's font manager, and theme/DPI integration, while duplicating shaping work Avalonia already does. Its editor subsystem is worth reading closely when implementing caret and selection.

---

## 6. End-to-end traces

### 6.1 Typing `x` with bold armed

1. `RichTextView.OnTextInput("x")`.
2. Guard: `IsReadOnly` → swallow.
3. Marks = `state.StoredMarks ?? MarksAt(selection.From)`.
4. `tr = state.Tr.ReplaceSelectionWith(text: "x", marks)` → `[ReplaceStep(from,to,slice)]`, `Kind = Typing`, `SelectionAfter = TextSelection(from+1)`.
5. `History.Push(tr)` — coalesce into previous entry iff previous is `Typing` ∧ Δt < 500 ms ∧ contiguous ∧ no selection jump.
6. `state = state.Apply(tr)`; raise `DocumentChanged`.
7. `RichTextView` reads `tr.Mapping` → affected block index *i*; `ParagraphView[i]` drops its `TextLayout` and rebuilds it (**not** `InvalidateFrom` — see the A1 correction in §4.6); `InvalidateMeasure` on that view only.
8. Measure rebuilds only paragraph *i*'s `TextLayout`; `BlockHeightIndex` updates if height changed; scroll offset adjusted if the change is above the viewport.
9. Caret rect recomputed via `TextLayout.HitTestTextPosition`; blink timer reset (`_caretBlink = true`); `CursorRectangleChanged` raised for the IME.
10. Render: selection rects → block content → caret.

### 6.2 Paste from Word

1. `TryGetDataAsync` → `IAsyncDataTransfer`.
2. `NativeSlice` present? → deserialize `Slice`, apply, **done** (lossless).
3. Else `Html` present? → strip CF_HTML preamble using its byte offsets → `AngleSharp` parse → allow-list importer walks the DOM into `Slice`. Unknown elements collapse to their inline content; `<script>`, event attributes, `<iframe>`, `<object>` dropped unconditionally; remote image URLs **not** fetched unless `AllowRemoteImages` is opted in.
4. Else `Rtf`? → subset parser (if enabled) else fall through.
5. Else `Text` → paragraphs split on `\r?\n`, current marks applied.
6. One `Transaction` with `Kind = Paste` — a single undo step regardless of size.
7. Schema validation: the slice is coerced to be valid in its insertion context (e.g. a table pasted inside a table cell is flattened to its text content rather than rejected).

### 6.3 Merging table cells

1. Selection must be `CellSelection`; otherwise command `CanExecute` is false.
2. Take the cells the selection covers. **No raggedness check** — see the correction below.
3. `TableStructureStep`: target cell gets `RowSpan`/`ColSpan`; absorbed cells' block content is appended to the target; absorbed cells removed.
4. `Kind = Structure` → never coalesced with neighbours in history.
5. `TableView` re-measures; column widths preserved; selection becomes a `TextSelection` at the end of the merged cell.

> **Step 2 corrected 2026-08-18.** It used to read "compute the covering rectangle; reject with
> a status message if it is ragged (partially-overlapping existing spans)". That sentence was
> written before `CellSelection` existed, and P8 built the selection so that the situation it
> guards against cannot arise: `TableGrid.Rectangle` grows the rectangle until it is *closed*
> under every cell it touches, so a merged cell hanging over the edge widens the selection
> instead of making it ragged. The cells it returns therefore tile their bounding box exactly.
>
> Rejecting is not merely unnecessary now, it would be wrong: the view draws its selection
> highlight from the same `Cells(doc)` call, so the grown rectangle is what the reader can see
> selected. A merge that refused it would be refusing exactly what is on screen.
>
> The check to keep is therefore in `TableEdits`, and it is a different one: **fewer than two
> cells is not a merge.** That is the only way the command declines.

---

## 7. Validation, failure behavior, security

| Concern | Rule |
|---|---|
| Schema validity | `Step.Apply` returns `StepResult.Failed` rather than producing an invalid document. A failed step is **skipped and recorded in `Transaction.Failures`**; the remaining steps still apply. Every step that does apply produces a schema-valid tree, so the document is never invalid — but a transaction is **best-effort, not atomic**, and a host that needs all-or-nothing must check `Failures` before committing. See "Why not atomic" below. |
| Import robustness | Importers return `ImportResult(Document, IReadOnlyList<Diagnostic>)`. Never throw into the UI. Unsupported constructs downgrade to the nearest supported node; each downgrade is a diagnostic. |
| HTML paste | Strict allow-list of elements/attributes/CSS properties. Script, event handlers, embedded objects, and `javascript:`/`data:` URLs removed. Remote images off by default. |
| Image size | Configurable cap (default 16 MB decoded) — reject with a diagnostic rather than OOM. |
| Huge paste | Cap (default 10 MB) with a confirm hook. |
| Position mapping | All externally-held positions must be routed through `Mapping`; a debug-build assertion catches stale positions. |
| Undo integrity | Property test: for any random transaction sequence, `undo*` returns to the exact initial document; `redo*` returns to the exact final document. |
| IME | Preedit never reaches the model or history. |
| Read-only | Enforced at the single `RichTextEditor.Dispatch(Transaction)` chokepoint, not per-command. |
| External side effects | An irreversible side effect an edit needs — writing bytes to `IImageStore` — is arranged **after** the edit is known to be acceptable, never before. See "Ordering an edit against a side effect" below. |

### Why a transaction is not atomic

An earlier draft of the table above promised that "the editor drops the transaction and logs;
it never applies a partially-valid tree". The implementation never did that, and the code was
right: a command that composes several steps should be able to try one and carry on. Lifting a
list item out of a list, for example, emits steps that are individually optional depending on
what surrounds the item, and a strict transaction would force every command to pre-compute
exactly which of its steps will succeed — which is the work the step is doing.

So the guarantee is narrower than "atomic" and worth stating exactly:

- **The document is never invalid.** Every step that applies produces a schema-valid tree; that
  part of the original promise holds unconditionally.
- **A transaction may apply partially.** A failed step is skipped, recorded in
  `Transaction.Failures`, and later steps still apply against the document as it then stands.
- **One transaction is still one undo entry**, whether it applied fully or partially.

The consequence for a host is that `Failures` is not diagnostic noise: it is the only signal
that an operation did less than it was asked to. A host that needs all-or-nothing should build
the transaction, inspect `Failures`, and discard it rather than calling `Apply` — the check is
cheap because steps are pre-applied as they are added.

**This is a deliberate decision, taken 2026-08-17**, when a review found the document and the
implementation disagreeing. Best-effort was kept and the document corrected, rather than the
reverse.

### Ordering an edit against a side effect

The row above exists because `InsertImage` had the order backwards. It wrote the bytes to
`IImageStore` and *then* pasted the embed that names them, so every refusal — a read-only view, a
rectangle of cells, a position the schema will not take an embed at — left the store holding a
file nothing references. `IImageStore.Save` is the whole interface; there is no way to take one
back, and a host implementing it against a directory or a blob container accumulates those
forever.

**An edit here fails by returning `false`, not by throwing**, which is why this survived: the
`try`/`catch` reflex does not catch it and neither did the tests, which asserted only that the
document was unchanged. Nothing asserted about the store, because the store was not the thing
under test.

The fix is the ordering, and it is available because **building a transaction is not applying
one**. `Apply` is the only thing that installs state, so a caller can build the exact transaction
it means to apply, ask whether it changed anything, and only then arrange the side effect:

```csharp
if (!WouldPaste(ImageDocument("probe:"))) return false;   // nothing written
if (ImageStore.Save(bytes, mediaType) is not { } source) { /* refused */ }
return PasteDocument(ImageDocument(source));
```

The probe carries a placeholder source because nothing about the paste depends on what a source
says — only that it is not blank — and nothing can move between probe and paste, since none of
this path awaits. The visible consequence is that an edit the *document* refuses no longer raises
`ImageRefused`: the store refused nothing there, and had it been asked it would now be holding
the orphan.

---

## 8. Verification scenarios

Headless (`Avalonia.Headless` + xUnit), CI on Windows/Linux/macOS:

1. **Undo round-trip property test** — random op sequences (FsCheck), documents compared structurally.
2. **Position mapping property test** — a position tracked through random edits equals the position recomputed from a re-derived document.
3. **Grapheme integrity** — arrow keys across "👨‍👩‍👧‍👦", "é" (combining), and Devanagari clusters never split a cluster. **Backspace is different and intentionally so:** `GetBackspaceCaretCharacterHit` peels one *component* off a ZWJ or combining sequence, matching Word, browsers and most native editors. The test asserts that component-peel behaviour positively — not whole-cluster deletion — plus monotonic progress and that no operation ever produces invalid UTF-16. Backspace must additionally **absorb a trailing ZWJ** into the same keystroke, since the raw API leaves 👨‍👩‍👧‍ (dangling ZWJ) after one press. Grapheme assertions compare **equality** against pinned Unicode `GraphemeBreakTest` data, *not* subset membership against `System.Globalization.StringInfo`, which predates UAX #29 GB9c and disagrees with Avalonia on Indic conjuncts. (Measured in Increment 0 — [findings §4](increment-0-findings.md).)
4. **Bidi caret & selection** — mixed Hebrew/English paragraph: visual caret order, and `GetTextBounds` producing multiple disjoint rects for a logically contiguous selection.
5. **IME** — scripted preedit set/replace/commit/cancel; assert document unchanged during composition, one undo entry after commit.
6. **Clipboard round-trip** — copy → paste in-app is byte-identical; HTML/RTF/DOCX exports validated (OOXML schema validation via the SDK; RTF parsed by a reference parser in tests).
7. **Manual interop matrix** — paste into and from Word (Win/Mac), Google Docs, LibreOffice, Chrome/Firefox, Slack. Tracked as a checklist per release; RTF fidelity in Google Docs is explicitly *expected-import, not guaranteed-fidelity*.
8. **Perf** — 50k-paragraph document: first render < 500 ms, keystroke-to-frame < 16 ms, scroll 60 fps. Benchmarked with BenchmarkDotNet + a headless render loop.

   **Measured 2026-08-17, corrected 2026-08-18, re-measured 2026-08-19 after the resolve index, and again 2026-08-19 after the edit pivot — all four now PASS.** `bench/VellumText.Bench` is the harness (`dotnet run -c Release --project bench/VellumText.Bench -- ac12`), a headless Skia render loop rather than BenchmarkDotNet, because what these numbers need is a shape across doubling sizes rather than a distribution at one size.

   | | budget | before the index | after the index | after the pivot | |
   |---|---|---|---|---|---|
   | first render | 500 ms | 20.85 ms | 19.7–20.0 ms | **20.1–26.2 ms** | pass |
   | keystroke, caret at the start | 16 ms | 9.81 ms | 10.0–12.4 ms | **7.0–10.5 ms** | pass |
   | keystroke, caret at the end | 16 ms | 24.55 ms | 16.22 ms | **10.38 ms** | pass |
   | caret move | 16 ms | 1.05 ms | 1.04 ms | **1.05–1.17 ms** | pass |

   The keystroke-at-the-end figure is a median of nine consecutive runs of the same build,
   `-- ac12` alone in its own process, which is the treatment the previous entry in this table
   got and the only way to compare them. Nine runs of `c35be99` on the same machine at the same
   hour gave 15.34–17.59 ms, median **15.99**, four of the nine over budget; nine runs of the
   pivot gave 9.84–18.33 ms, median **10.38**, one of the nine over budget. That one run is not
   dismissed but it is not the edit either — every line of that run was elevated together, first
   render at 26.17 ms against a 20.13 ms best and a caret move that allocates 1,768 bytes
   whatever happens. This is a shared machine and the tail is the machine.

   So the honest claim is a median 35% under budget where it used to sit on top of it, not
   an edit that can never miss a frame on a loaded box. The deterministic half of the measurement
   is the stronger evidence and it is below.

   The keystroke lines are the point of the table. The first was the only one measured until
   2026-08-18, and it types at position 1 — the cheapest position in a document, because
   resolving a position walks the parent's children to find it and position 1 stops at the first
   one. Typing at the end walked all 50,000. A benchmark that only ever typed at the start
   flattered the result by 2.5x and reported a pass where there was a failure.

   This did not pass before that date: a keystroke was **27.59 ms**, and a criterion nothing measures is a sentence rather than a check. Four causes, all found by measurement and none by a failing test — validation re-walking the whole document after every step and building diagnostic strings it discarded, the block list being re-flattened on every state change including a caret move, and two linear lookups over sorted data.

   **The position scan went first.** `Node.ChildIndexAt` scans at most 32 children and, past that,
   builds a prefix-sum array of cumulative child ends and binary searches it — the same shape as
   `BlockHeightIndex` minus the Fenwick update path, which children never need because a node
   with different children is a different node. Isolated (`-- resolve`), 64 lookups at the end of
   a 16,000-block document went 20.956 ms to 0.147 ms; the same 64 lookups at the *start* are
   unchanged and still flat at 0.010 ms and 17,448 bytes. Keeping that flat is why the index
   waits for a scan to prove expensive rather than building on first touch: indexing on the first
   lookup was measured at 0.140 ms and 81 KB for those start lookups, linear in document size,
   which is this defect again at the cheap end. What is left of it is one O(n) build per
   *document version*.

   **What cost the remaining 16 ms was `ReplaceStep`, not the lookup.** Splitting a keystroke at
   the end of a 50,000-paragraph document into stages gave: caret move 2.09 ms, `InsertText`
   12.90 ms of which 9.49 was the model, measure and arrange 1.12 ms. The model half was the
   cut-and-splice: `CutBefore(start, 0)`, `CutAfter(end, 0)` and two `Splice` calls, each of
   which rebuilt the *root's* child array. Three rebuilds of a 50,000-element array to arrive at
   an array differing from the original in one place, because typing happens inside one
   paragraph and touches one child of the document.

   **A pivot depth replaces that with one spine rebuild.** `ReplaceStep.PivotDepth` finds the
   deepest ancestor of the range whose child list survives the edit unchanged in length — the
   shallower of `SharedDepth - 1` (above that the range's two ends are in different children, so
   closing it up removes everything between them) and the depth the slice's own content arrives
   at (a slice of one node replaces a child, a slice of two grows the list). At or above that
   depth every level is its old children with one child replaced, so the cutting and splicing is
   done inside the one child and `TreeSurgery.ReplaceChild` walks the spine with
   `Children.SetItem`. When the pivot is -1 — Enter at the top level, a paste of several blocks —
   nothing is claimed and the old whole-document route runs unchanged. `TreeSurgery.Merge`, the
   seam decision, is shared by both routes rather than copied, because whether a seam joins is
   what decides whether a step applies at all.

   Measured with `-- model`, which isolates the edit with no view and where allocation is
   deterministic, at 16,000 paragraphs:

   | | before | after | |
   |---|---|---|---|
   | insert at the start | 774,672 B / 2.007 ms | **261,672 B / 0.715 ms** | 48.4 → 16.4 B per block |
   | insert at the end | 1,160,848 B / 2.947 ms | **263,872 B / 0.720 ms** | 72.6 → 16.5 B per block |
   | delete at the end | 1,160,848 B / 2.862 ms | **263,872 B / 0.732 ms** | 72.6 → 16.5 B per block |
   | join two blocks | 1,158,832 B / 2.909 ms | 774,744 B / 2.651 ms | 72.4 → 48.4 B per block |

   The first three rows now agree with each other, which is the result: an edit costs the same
   wherever it happens and whether it inserts or deletes, and the position-dependent costs are
   gone. Backspace was worth measuring separately rather than assuming — it is the same
   replacement with an empty slice, but "the same code path" is a claim about the code and the
   table is evidence about the behaviour.

   **The fourth row is the case the pivot cannot help, and it is correct that it cannot.**
   Joining two blocks — backspace at the start of a line — replaces two of the document's
   children with one, so no ancestor's child list survives unchanged, `PivotDepth` returns -1
   and the whole-document route runs. It still improved by a third, because `MoveToImmutable`
   applies to every rebuild, but it stays at three times the cost of typing and extrapolates to
   roughly 8 ms at 50,000 blocks: inside the budget, and one keypress rather than a held key.
   A "two children become one" route would close it and was not written, on the same reasoning
   as `WithChildReplaced` below — a third route through the primitive every edit goes through,
   to speed up a keystroke that already passes.

   Every row is still linear in document size and always will be: the document's child array is
   one array and replacing one element of it copies the rest. What changed is the constant. Two
   arrays per block remain: the one `SetItem` returns and the one the node's constructor keeps.

   **Not done, and why.** Giving each node type a `WithChildReplaced` override would drop those
   two arrays to one, but the model edit is now ~800 KB of a 4.4 MB keystroke at 50,000 blocks,
   so halving it buys under 10% of the keystroke for six new overrides on the type every edit
   goes through. Incrementally maintaining `DocumentNode`'s content size instead of re-summing
   it per rebuild was rejected for the same reason — it is an O(n) scan that allocates nothing,
   and it is now paid once per keystroke rather than three times.

   Equivalence is tested rather than argued: `ReplaceStepEquivalenceTests` re-implements the old
   cut-and-splice from the public model API and compares it against the pivot over every range of
   thirteen document shapes crossed with seven slices, plus every range cut out and put back, plus
   a second edit on top of the first. That test was mutation-checked — a pivot one level too deep,
   a pivot that ignores the slice's length, a spine walk starting one level too high, and a seam
   merged at the wrong depth each make it fail, in 13 to 95 cases.
9. **Table nav** — Tab/Shift-Tab across cells and spans; arrow keys crossing cell and table boundaries. **Covered 2026-08-18** by `RichTextViewTableCommandTests` for Tab and `RichTextViewBlockNavigationTests` for the arrows.
10. **Trimming/AOT** — `PublishTrimmed` and NativeAOT smoke test of the demo app; the Open XML SDK path is the likeliest casualty and may need a trimming-safe boundary or opt-in package.
11. **Theme** — light/dark variant switch updates default foreground/selection/caret without touching explicit per-run colors.
12. **Find bar** — `DocumentSearch.Find` is O(document) and the find bar calls it once per keystroke.

    **Measured 2026-08-18 and left alone. This is a null result, recorded as one.** A review
    finding called this a performance defect: typing a five-letter query into a large document
    searches it five times. The description is accurate and the severity is not, and the
    difference only shows up in numbers. `dotnet run -c Release --project bench/VellumText.Bench -- find`.

    | shape, 16,000 paragraphs | ms | bytes | per doubling |
    |---|---|---|---|
    | one search, a word in every paragraph | 1.18 | 390,520 | 2.00× time, 2.00× bytes |
    | one search, a word in no paragraph | 0.69 | 216 | 1.98× time, 1.00× bytes |
    | typing `enoug`, 5 keystrokes, model only | 6.54 | 3,122,920 | 2.02× time, 2.00× bytes |
    | typing `enoug`, 5 keystrokes, through the view | 6.97 | 3,123,560 | 2.02× time, 2.00× bytes |

    Linear, exactly as the finding says. **The verdict has to be taken at 50,000 paragraphs**,
    because that is the size AC12 states its 16 ms budget at and extrapolating from 16,000 is how
    this table has been wrong before:

    | one keystroke at 50,000 paragraphs | matches | ms | bytes | against 16 ms |
    |---|---|---|---|---|
    | first — `e` | 200,000 | 7.21 | 5,794,904 | pass |
    | last — `enoug` | 50,000 | 3.54 | 1,449,128 | pass |

    So the worst single keystroke a find bar can take — a one-character query matching 200,000
    places in a 50,000-paragraph document — costs **7.21 ms against a 16 ms frame**, and it is the
    only keystroke that does: every later one narrows. The find bar is inside budget at the size
    the budget is stated at, with the same harness and machine every other number in this section
    comes from.

    **Two things a fix would have to face, on top of a payoff that is already inside budget.**
    The first keystroke is the most expensive one and no incremental scheme can skip it — it is
    the one with no previous result to narrow. And narrowing the *match list* is unsound, because
    matches are non-overlapping: in `"aaab"`, `"aa"` matches at 0 and `"aab"` matches at 1, so the
    longer query's matches are not a subset of the shorter query's. A sound version has to keep
    every *overlapping* occurrence as a candidate list, which for a one-character query is larger
    than the match list it replaces, and has to be thrown away on every edit, undo and paste
    anyway. That is real complexity bought against a measurement that says there is nothing to
    buy.

    The scenario stays in the harness. It is the evidence, and it is what would catch this
    changing — a re-find on every keystroke sitting inside a frame is a property of the current
    constant, not a guarantee.
13. **Vacuous-test audit** — the suite is searched, on purpose, for tests that cannot fail.

    **Done 2026-08-20 over all 4,397 tests. Five found, five proven, five fixed.** The reason to
    do it was that the five vacuous tests found before this were all found by accident, which says
    nothing about how many there are and everything about how hard anyone had looked.

    A finding counts only if breaking the production code the test names leaves the whole suite
    green. Nothing here is an opinion about a weak-looking assertion; each row below was produced
    by a mutant that survived, and each fix was confirmed by re-running the same mutant and
    watching the fixed test go red.

    | test | the mutant it did not notice |
    |---|---|
    | `HtmlSecurityTests.A_span_of_a_thousand_columns_is_not_a_table` | The 1,000-column clamp removed. Its only assertion was `Assert.NotNull(result.Doc)`, and `ImportResult.Doc` is non-nullable and throws in its own constructor — true before the importer ran. |
    | `RichTextViewImageInsertTests.ADropIsRefusedWhenTheViewIsReadOnlyOrOptedOut` | `CanAcceptDrop() => true`. It never raised a drop and never set `AcceptsDrop`; it called `InsertImage` on a read-only view, which is the check inside `Apply` and the test directly above it. A view that had opted out of drops would have accepted them. |
    | `HostileValueTests.A_vast_base64_payload_is_rejected_without_being_decoded` | The pre-decode length bound removed. The payload is valid base64, so decoding it and measuring the result reaches the same "too large" verdict by exactly the route the bound exists to avoid. |
    | `BitmapEmbedRendererTests.APngClaimingMorePixelsThanAllowedIsRefusedFromItsHeader` | The IHDR dimension check removed. `Accept` re-reads the pixel count after decoding and returns the same fallback size, so nothing downstream can tell which one refused it. |
    | `HtmlSecurityTests.A_data_image_that_is_not_base64_is_refused` | The `;base64` requirement removed. Degenerate input: the percent-encoded payload is also invalid base64, so it is refused either way and the requirement itself was never reached. |

    Two of the five could not be fixed by assertion alone, because the property is not visible in
    the result. The size bound is now checked by allocation — the same document dropped by two
    routes that differ in nothing but which check refuses it, and the megabytes between them. The
    PNG header check is asked directly, which is what `IsOversizedPng` is internal for.

    One more, separately: `ClipboardHtmlTests.AssertOffsetsAreTrue` compared a byte range against
    the identical expression over the identical bytes — `Assert.Equal(x, x)`, no production change
    can fail it. It is listed apart from the five because the assertions beside it already pin the
    same offsets against the fragment markers, so it was a false signal rather than an uncovered
    defect. It now takes the caller's own fragment.

    **The other half of the result, which is the more useful half.** These shapes were looked for
    across the whole suite and found already sound: `Assert.Equal` on two `ImmutableArray<T>`
    (reference comparison — zero unfixed instances, and the hazard is commented at the three sites
    that hit it); the overflow-guard tests in `MappingTests`, which already carry an explicit
    "neither range runs off the end on its own" anti-degeneracy note; round-trip tests without an
    independent anchor (three round-trip sites, all anchored on a literal); and the stored-marks,
    JSON version and link-sanitisation guards, each of which has a mutant that a test catches.
    Several surviving mutants were examined and deliberately **not** counted: `File.Exists` on an
    `http://` source, `Convert.FromBase64String` inside a broad catch, and the negative-start
    clause in `StepMap.Check` are redundant defence-in-depth on paths that are already closed, not
    tests that cannot fail.
14. **Drop pipeline** — `RichTextViewDropTests` covers the drop path at the same depth as paste,
    against a real `DragDrop.DropEvent` rather than a direct call to the insert methods.

    Drop shares nearly all of paste's risk surface and had two defects in a week — an in-flight
    drop landing in whichever document was open when its file finished reading, and images reaching
    the store before the edit was known to succeed. Both are ordering faults that only exist
    because a drop reads files asynchronously, so neither is reachable through `InsertImage` alone,
    which is what the existing tests called.

    Every test here was watched fail first. The mutants:

    | property | the mutant that proves it |
    |---|---|
    | A multi-file drop inserts every image it can read | The per-file origin left stale after an insert. |
    | A rejected file does not end the drop | The stale origin, and the per-file `catch` made to rethrow. |
    | A drop stops when the document is swapped underneath it — mid-drop, before it starts, and when the replacement holds identical text | The per-file `StillAt(origin)` guard. |
    | A read-only or opted-out view never reaches the store and reports nothing | `CanAcceptDrop() => true`. |
    | A store refusal is reported rather than silently dropping the image | `ImageRefused` not raised. |
    | Going read-only mid-flight leaves nothing in the store | `ImageStore.Save` moved above `WouldPaste`. |
    | Sanitised-away content brings neither text nor link addresses across | `script` removed from `NonContentElements`. |
    | A payload with nothing left after sanitising still reports the loss | The removal reported as `Downgraded` rather than `Dropped`. |

    Writing them found a third defect, in `InsertImageFiles`: the swap guard was checked against an
    origin taken before the loop, so the *first* insert made every later file look like a document
    swap and a drop of a folder of pictures silently inserted one of them. The origin is now
    re-taken after each successful insert. It survived because the only multi-file coverage called
    `InsertImage` per file directly, which is the one path where nothing takes the guard.

    The positive control is load-bearing here and not decoration. `OnDrop` is `async void`, so
    raising the event only starts it; every negative assertion in this file would hold against a
    drop that never ran at all. `AMultiFileDropInsertsEveryImageItCanRead` is what says the
    dispatcher pump is real.

---

## 9. Risks

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| **R1** | **No `ITextProvider` in Avalonia (C1).** Screen readers get plain text via `IValueProvider` at best — no ranges, no formatting, no caret events. A rich text editor with weak a11y is a real limitation, and no amount of work in *our* repo fixes it. | ~~High~~ **Medium — being lifted upstream** | Ship `IValueProvider` + `AutomationProperties` in v1 (done, P7). **[2026-08-16]** The upstream gap is closing without us: [#21988](https://github.com/AvaloniaUI/Avalonia/pull/21988) is open and adds platform-agnostic `ITextProvider`/`ITextRangeProvider` plus a `TextBox` peer and Win32 UIA glue. So the mitigation is no longer "propose it" but **watch it and implement the contracts on `DocumentPresenterAutomationPeer` once they land** — a rich text editor is the strongest case for that API, and VellumText can be an early consumer rather than the petitioner. Document the limitation honestly in the README until then. |
| **R2** | Table layout is entirely ours (C2) — column sizing, spans, cell content reflow, page-width interaction. | High | Sequence it last (Increment 6), after the block host has proven itself recursive. Constrain v1 to fixed/proportional column widths; no auto-fit-to-content. |
| **R3** | `TextRunCache` incremental invalidation may not behave as assumed (A1). | ~~Medium~~ **Low — realised and resolved** | Increment 0 measured it: `InvalidateFrom(k>0)` is stale, so paragraph layouts are rebuilt wholesale on edit (measured 0.35–0.6 ms at 800 chars, comfortably inside a frame budget). The cache is retained for width-only relayout, where it is correct and ≈3.5× faster. |
| **R4** | Clipboard behavior varies across X11 compositors and Wayland (A3). | Medium | Capability-test at runtime; always include `DataFormat.Text`. Integration-test GNOME/KDE on both X11 and Wayland. |
| **R5** | Scope creep — the AC list is a commercial product's feature list. | High | Increment gating (§10). Increments 0–5 are a genuinely useful editor on their own; 6–8 are the long tail. |
| **R6** | Trimming/NativeAOT regressions from Open XML SDK. | Low | DOCX lives in a separate package; core and the demo stay trimming-clean. |
| **R7** | Avalonia 12 is recent; `DataFormat` and text APIs may still shift. | Low | Pin to 12.1.x; keep interop behind our own thin abstraction. |

---

## 10. Implementation sequence

Each increment is independently useful and independently shippable. No compatibility scaffolding between them.

| # | Increment | Proves / delivers | Rough size |
|---|---|---|---|
| **0** | ~~**Spike (throwaway).**~~ **✅ Complete — see [`increment-0-findings.md`](increment-0-findings.md).** Read-only styled paragraph via `TextLayout` + `textStyleOverrides`; second spike with a custom `ITextSource` + one `DrawableTextRun` image; measure `TextRunCache.InvalidateFrom`. | A2 and the B2/C2 premise **validated**; A1 **partly invalidated** (stale for edits, ≈3.5× for width-only relayout). C1 confirmed as a ceiling. Spike code deleted; findings and evidence retained under `docs/`. | *actual: ~1 day* |
| **1** | **Core model, headless.** Nodes, positions, `Step`/`Transaction`/`Mapping`, schema validation, history. Full unit + property tests. No UI. | The foundation everything else rests on, verified without a window. | 1–2 weeks |
| **2** | **Editable single paragraph.** `RichTextView` with one `ParagraphView`: caret, mouse/keyboard selection, typing, Backspace/Delete, undo/redo, IME. | The hard interactive core. If this is right, the rest is largely additive. | 1–2 weeks |
| **3** | **Multi-block + virtualization.** Block host, `BlockHeightIndex`, scrolling, cross-block selection, Enter/split/merge, headings, alignment, indent, lists, blockquote, rule. | AC2, AC12. **First genuinely usable editor.** | 1–2 weeks |
| **4** | **Marks & embeds.** Full character formatting, stored marks, links, inline images via `EmbedRun`, `NodeSelection`, resize handles, optional `RichTextToolBar`, Fluent/Simple themes. | AC1, AC3, AC4, AC10, AC13. **Plausible v0.1 public release.** | 1–2 weeks |
| **5** | **Clipboard & lightweight interop.** `DataFormat` wiring, `NativeSlice`, HTML import/export (AngleSharp) + sanitizer, Markdown (Markdig), drag-and-drop. | AC6, AC7 (partial), AC8 (partial). | 1–2 weeks |
| **6** | **Tables.** `TableNode`/`TableView`, `CellSelection`, structure commands, column resize, cell backgrounds, tab navigation. | AC5. The big one. | 2–4 weeks |
| **7** | **Office interop.** RTF writer (subset), DOCX export and import via Open XML SDK, interop test matrix. | AC7, AC8 complete. | 2–3 weeks |
| **8** | **Polish.** `IValueProvider` a11y + upstream `ITextProvider` proposal, find/replace, spellcheck hook, context menu, touch/mobile handles, perf pass, docs, samples. | v1.0. | 2–3 weeks |

**Realistic totals:** Increments 0–4 (a genuinely good basic rich text editor) ≈ **5–8 focused weeks**. Full v1.0 including tables and DOCX ≈ **4–6 months** of sustained part-time work.

### 10.1 Where the increments actually went

The sequence above survived contact with the code, but not in order. Recorded because the
remaining plan (§10.3) only makes sense against what is genuinely done.

| # | Planned | Actual |
|---|---|---|
| 0 | Spike | ✅ Complete and retired. A2 validated, A1 partly invalidated, C1 confirmed as a ceiling. |
| 1 | Core model | ✅ Complete. Both §8 property tests pass. Tables, lists, links, embeds and every mark in the schema. |
| 2 | Editable single paragraph | ✅ Complete. Caret, selection, typing, undo, IME. |
| 3 | Multi-block + virtualization | **Mostly.** Block host, split/merge, headings, alignment, indent and the height index are in. `ILogicalScrollable` and list *commands* are not. |
| 5 | Clipboard & interop | **Pulled forward, and widened.** HTML *and* RTF import both shipped, on AngleSharp and RtfPipe, because the driving use case is a clipboard manager. Export is not started. |
| 4 | Marks & embeds | **Partly, and late.** The model carried every mark from Increment 1, but the control could not reach any of them until the parity pass below. |

Pulling Increment 5 in front of 4 was the right call for the use case and the wrong call for
coherence: for several increments VellumText could *import* bold, coloured, hyperlinked text and
then offer the user no way to produce or change any of it.

### 10.2 Parity gap against the commercial control

Avalonia sells a closed-source `RichTextEditor` in Accelerate. Its **feature list** is public
(§11: feature sets are not protected; expression is) and is the obvious yardstick for "is this
worth using". Comparing it against what VellumText actually had:

| Capability | Their control | VellumText's model | VellumText's control |
|---|---|---|---|
| Character formatting (17+ controls) | ✅ | ✅ every mark | ❌ → ✅ **closed** |
| Headings, alignment, indent | ✅ | ✅ | ✅ |
| Lists | ✅ | ✅ | ❌ → ✅ **closed** (P3) |
| Tables, merged cells | ✅ non-destructive | ✅ full schema + `CellSelection` | ⚠️ → ✅ real grid (P6); cross-cell selection (P8) |
| Inline & block images | ✅ | ✅ | ✅ renders, selects and resizes by dragging a corner |
| Hyperlinks | ✅ | ✅ | ✅ **closed** |
| Toolbar (`EditorToolbar`) | ✅ | — | ❌ → ✅ **closed** (P4) |
| Selection flyout (`EditorSelectionFlyout`) | ✅ | — | ⚠️ right-click context menu only; no floating selection flyout |
| Read-only viewer (`FlowDocumentScrollViewer`) | ✅ | — | ❌ → ✅ **closed** (P5) `RichTextViewer` |
| Serializers (RTF/DOCX/XAML, separate packages) | ✅ export + import | — | ⚠️ HTML + RTF both ways (P2); no DOCX/XAML |
| Light & dark theme | ✅ | — | ❌ → ✅ **closed** (P4) light + dark variants |
| `IsReadOnly`, `UndoLimit`, `AcceptsReturn`/`Tab` | ✅ | — | ❌ |
| Spellcheck | ✅ | — | ❌ no hook |

> **Verified against the tree at P7 (2026-08-16), not from memory.** This table had gone stale: P2–P6
> closed seven of its rows while it still read `❌`. Each row above was re-checked against the code —
> `RichTextView.Lists.Commands.cs`, `TableBlockView`, `RichTextToolbar`, `RichTextViewer`,
> `Html`/`RtfExporter`, `Themes/VellumText.axaml` — and the four that remain open were confirmed absent by
> search. `IsReadOnly` exists **only** as `IValueProvider.IsReadOnly` on the automation peer, derived
> from `SupportsTextInput`; it is not a settable control property. Note the read-only *flag* is partly
> moot by the decision below that a viewer is a distinct control.

> **Correction (2026-08-18, read-only affordances).** Reported from live use, and worth recording
> because the fix is a rule rather than a patch. A clipboard manager rendering previews as
> `RichTextEditor { IsReadOnly = true }` was offered the full floating toolbar over any selection,
> Cut and Paste on the context menu, and the entire table submenu — every one of which the editor
> then refused. `IsReadOnly` was enforced correctly at `Apply`; nothing consulted it when deciding
> what to *offer*. (This also supersedes the note above that *"it is not a settable control
> property"* — it is one on both `RichTextView` and `RichTextEditor` now.)
>
> **Refusing an action and not advertising it are separate obligations, and only the first was
> met.** An editor that renders a Bold button and then declines to embolden has already misled the
> user by the time the command runs, so the tests for this assert affordances — the bar is not
> visible, the entry is not there — and not that the command returns false.
>
> Effective bar visibility is now `IsSelectionToolbarEnabled && !IsReadOnly`. The flag keeps its
> own authority so a host can still suppress the bar on an editable control, but `IsReadOnly` is
> sufficient alone: a consumer who has said edits are refused should not need a second flag to
> stop the control advertising them.
>
> **Hiding and greying answer different questions, and conflating them is how this regresses.**
> `BuildTableItem`'s existing rule — entries stay enabled when they cannot apply, because a menu
> whose entries flicker reads as broken — is about state that tracks the *caret*, which moves
> constantly. Read-only tracks the *control's configuration*, which changes only when the host
> decides, so hiding on it cannot flicker. The existing rule stands; read-only is simply the other
> case.
>
> Two halves a single fix would have missed. `IsReadOnlyProperty` routed to `PushOptions` and not
> to `UpdateSelectionToolbar`, so flipping it at runtime left a visible bar on screen — the
> reported repro sets the flag at construction and would never have caught it. And `CutAsync`
> copies before it deletes while only the delete consulted `IsReadOnly`, so a read-only Cut left
> the document alone and still wrote the clipboard: half an operation, with nothing to tell the
> user which half ran.
>
> **Hiding a menu entry does not disarm its accelerator, and that is the durable lesson.** On a
> read-only pane Ctrl+X still reached `CutAsync` with the entry gone from the menu, so the gate
> belongs at the choke point — `CutAsync` itself, refusing before it copies — which covers the
> entry, the accelerator and any programmatic caller at once. Gating call sites is how the fourth
> call site gets missed.
>
> Auditing the rest of the gesture table for side effects that survive a refused edit found Cut is
> the only clipboard one: there is no `Key.Insert` case at all so Ctrl+Insert is unbound, and
> `Key.Delete` carries no modifier guard so Shift+Delete falls into plain delete. Undo and redo
> already consult `IsReadOnly` in `CanUndo`/`CanRedo`; `ContentImported` and `ImageRefused` are
> suppressed because both paths edit before raising. That is a fact about today's bindings rather
> than a property of the design, so a tripwire test pins it — with Ctrl+C retained as a control,
> because a sweep asserting only that nothing wrote would pass against a fixture with no clipboard.
>
> Two non-clipboard leaks came out of the same audit. Formatting with an empty selection arms
> `StoredMarks` directly and never builds a transaction, so `Apply` never saw it and Ctrl+B left
> bold armed on a read-only view for an insertion that can never happen; gated in `SetMarks`, the
> choke point every formatting shortcut shares. And Ctrl+K raised `LinkRequested`, asking the host
> to put a link dialog on screen for an edit `ApplyLink` then refuses — the same advertising bug as
> the toolbar, but costing the user a dialog and a typed URL before the refusal.

> **Which control a preview pane wants (2026-08-18).** The consumer above shipped `RichTextViewer`
> first, found it exposed `Document` and nothing else, and switched to a read-only editor. That
> raises whether the viewer earns its place, and the answer is that it does — but for a narrower
> case than the guide implied, so the guide now says which.
>
> The viewer is for a document rendered as a *cell*: a list row, a search result, a hover card,
> many on screen and each glanced at. The 19.7 KiB and 0.050 ms measured above are per instance,
> and across a virtualised list that is the whole argument. The editor is for a preview a user
> *works* in — one on screen, wanting a context menu, keyboard selection across blocks, and the
> forwarded options.
>
> The field report is nonetheless evidence the split was mis-sold rather than wrong: the consumer
> reached for the viewer because "read-only" named it, not because their case was cell-shaped.
> Adding selection and copy closes the gap that made it unusable; the guide now points a preview
> pane at the editor instead of leaving the reader to infer it.

> **Correction (2026-08-18, hyperlinks and viewer selection).** Two rows above were wrong.

>
> *"Hyperlinks | ✅ **closed**"* described the **model and the importers**, not the control. The
> model had `LinkMark` and both importers produced links, but `RichTextView` offered no way to
> create, edit, remove or activate one — a link was a blue underline that did nothing. That is now
> closed for real: Ctrl+K raises `LinkRequested` (carrying the link under the selection, if any) so
> the host can show its own dialog and call `ApplyLink`; Ctrl+Shift+K removes; Ctrl+click raises
> `LinkActivated`; and hovering a link shows a hand cursor and a tooltip with the href.
>
> **The library deliberately does not open the URL.** `LinkActivated` follows the shape of the
> existing `ContentImported` and `ImageRefused` events precisely so that the decision stays with
> the host. A control that silently launches a browser on click is an attack surface, not a
> feature — the document may have been pasted from anywhere, and in the driving consumer (a
> clipboard manager) it certainly was.
>
> *"Read-only viewer … ✅ **closed** (P5)"* remains true, and now includes selection and copy; see
> the correction in §4.6.
>
> Also closed in the same pass: double-click selects a word, triple-click a paragraph,
> drag-selecting past an edge auto-scrolls, Shift+Enter inserts a soft break, and Ctrl+Shift+V
> pastes as plain text.
>
> **Word and paragraph selection use `WordBreakEnumerator` (UAX #29), not `char.IsLetterOrDigit`.**
> Avalonia 12.1.1 exposes it publicly, and the difference is not academic: the naive form splits a
> four-person ZWJ family emoji into four selections and cuts combining marks off their base — the
> same class of mid-cluster error Increment 0 established the caret must never make. Mutation-
> testing this is what proved the tests: swapping `TextBoundaries.WordAt` for the naive predicate
> turned five tests red, the emoji one among them.
>
> **`Ctrl+Arrow` was deliberately left on its existing non-UAX approximation.** UAX #29 places a
> boundary at every punctuation mark, so migrating it would stop `Ctrl+Right` inside `foo.bar()`
> four times. That is a behaviour change dressed as a correctness fix, and word *selection* — where
> the user has pointed at a specific cluster — is a different question from word *traversal*.

**The finding that matters:** the document model was never the gap. It is competitive already —
it has tables with row and column spans, nested lists, links, embeds and every mark, all
property-tested. The gap was that `RichTextView` exposed no way to *reach* any of it. A host
could make a paragraph a heading but could not make one word bold.

Two structural things are worth copying from their design rather than inventing:

- **One serializer interface, one package per format.** They have `IDocumentSerializer` with
  Rtf/Docx/Xaml in separate packages. VellumText's importers are already separate assemblies
  (`VellumText.Interop.Html`, `VellumText.Interop.Rtf`); exporters should join them behind a single
  interface rather than growing as loose static classes.
- **A read-only viewer is a distinct control, not a flag.** For a clipboard manager showing a
  history of pasted items, a viewer with no caret, no input and no history is the common case
  and is much cheaper than an editor with `IsReadOnly = true`.

### 10.3 Revised remaining sequence

| # | Work | Why it is where it is | State |
|---|---|---|---|
| **P1** | **Character formatting commands.** `SetMarks` and everything on top of it; stored marks for a collapsed caret; Ctrl+B/I/U and friends. | The single widest gap, and the cheapest to close — every step it needs already existed. Nothing else in this list is worth doing while the control cannot make a word bold. | ✅ **done** |
| **P2** | **Exporters.** `IDocumentExporter`, then HTML and RTF, in the existing interop packages, and the richer clipboard flavours on top of them. | A clipboard manager has to put rich text *back* on the clipboard. Import-only is half a feature, and §4.9's richer clipboard formats depend on it. | ✅ **done** |
| **P3** | **List commands + `ILogicalScrollable`.** The two deferred pieces of Increment 3. | Lists render but cannot be created, which is the same class of gap P1 just closed. | ✅ **done** |
| **P4** | **A default theme and a toolbar.** Light/dark resources, `RichTextEditor` template, an opinionated toolbar control, selection flyout. | Only worth building once there are commands to put on it — which is now. Resolves OD2. | ✅ done |
| **P5** | **Read-only viewer.** A control that renders a document with no caret, input or history. | Directly serves the clipboard-history case, and is a small fraction of the editor. | ✅ done |
| **P6** | **Real table rendering.** A block view that hosts child views side by side, replacing today's flattening. | The model is ready and pasted tables already survive round-trip; only the *view* degrades. Deliberately after the cheaper wins. | ✅ done |
| **P7** | **The rest of Increment 8.** a11y, find/replace, image resize handles, touch handles, perf pass. | Unchanged. | ✅ **done** — a11y, find/replace, image resize, touch handles, perf pass |

**Sequencing principle, stated because it was violated once already:** close the gap between
what the model can represent and what the control can do, before widening what the model can
represent. VellumText spent several increments able to import formatting it could not produce.

### 10.4 Open after P7

P1–P7 are complete. Verified against the tree on 2026-08-16 by search, not recollection — the
§10.2 table above had drifted badly, so these were each confirmed present or absent in code:

| # | Work | Evidence it is open | Size |
|---|---|---|---|
| **P8** | ~~**Cross-cell (rectangular) table selection.**~~ **Done.** See "As built" below. | — | — |
| **P9** | **Packaging.** Metadata and release automation are **done** — see "As built" below. ~~**Blocked on package identity**~~ **Resolved 2026-08-17 by renaming to `VellumText.*`.** | The old name collided: `Vellum.Core` 0.1.0 and 0.2.0 exist on nuget.org, published by the envelope-encryption library at `github.com/OkaySire/vellum`, which also holds `Vellum.Vault`, `Vellum.Static`, `Vellum.InMemory` and `Vellum.EntityFrameworkCore`. Plain `Vellum` 1.3.1 is taken too. | Done |
| **P10** | ~~**The four control properties.**~~ **Done.** See "As built" below. | — | — |
| **P11** | ~~**Selection flyout.**~~ **Was already done** — this row was wrong. Built during P4; see `SelectionToolbarTests.cs` (14 tests) and `RichTextEditor.cs`. | — | — |
| — | **DOCX / XAML serializers**, **spellcheck hook** | No such files. Both are new packages/features rather than gap-closing. | **Deferred to v2.0** (decided 2026-08-16) |

**DOCX is deferred but no longer excluded.** It was, structurally, until 2026-08-18: the format contract dealt only in strings, and no amount of later effort could have made a ZIP container fit through it without a `Convert.ToBase64String` fiction. §4.9's stream members are what make the deferral a scheduling decision again rather than a shape the API cannot hold — which mattered enough to fix before 1.0 froze it, because adding a required member to a shipped public interface is the one change that cannot be made afterwards.

**Scope of v1, decided 2026-08-16.** P8–P11 are all required for v1.0. DOCX/XAML serialization and
the spellcheck hook are explicitly **out** — they widen what the library can represent rather than
closing the gap between the model and the control, which is the sequencing principle below.

**P8, P10 and P11 are complete; P9 is complete except for a decision.** Two of the four rows
turned out to be describing a repository that no longer existed — P11 was built during P4, and
most of P9 was already in `Directory.Build.props`. That is the second time this table has drifted,
and in both directions: it has claimed work was done that was not, and open that was not. **Check
the tree, not the row.**

**The one thing that stood between this library and a release was its name, and it is now settled.**
The project was called `Vellum` until 2026-08-17, and `Vellum.Core` is already on nuget.org — not
by accident of squatting, but as the core of an actively published envelope encryption library, at
0.2.0 as of 2026-07-03. That project owns a whole `Vellum.*` family, so `Vellum.Avalonia` being
free was not much comfort: shipping into someone else's namespace would have confused both sets of
users, and an ID cannot be changed after the first publish. Renamed throughout to `VellumText`.
Nothing else blocks v1.

> **Naming decision: settled** (2026-08-17). Renamed to **`VellumText`** throughout — packages,
> assemblies, namespaces, directories and the solution. The old `Vellum.*` prefix was not merely
> squatted: `Vellum.Core` is an actively published envelope-encryption library that ships the
> *same version numbers* this project does, so a version pin was no defence and the symptom was a
> missing-type compile error long after the bad restore.
>
> A `PackageId`-only rename was tested and works — a package id may differ from the assembly it
> contains, verified by consuming one — but was rejected: a package named one thing containing a
> namespace named another is a papercut on every consumer for ever, and the full rename is a
> mechanical change with 4,036 tests as its oracle.
>
> Alternatives surveyed, all free and unreserved at the time: `Scrivenly`, `Inkwell`,
> `Palimpsest`, `Codex`, `Marginalia`. `Foliant` was taken; `Quill` was rejected because a
> well-known JavaScript editor owns the name and `Quill.Delta` on NuGet is its data format.
> **`Avalonia.*` is prefix-reserved**, so `Avalonia.RichText` would be rejected at push time even
> though the ID is free — worth knowing before anyone proposes it again.
>
> **Nothing may be published to NuGet without the maintainer's explicit confirmation** (standing
> instruction). Three independent things still prevent it: the release workflow fires only on a
> `v*` tag and none exists, `publish` sits behind the `nuget` environment, and
> `NUGET_API_KEY` is unset.


> **P8 as built — a rectangle is not a range.**
>
> `CellSelection.From..To` only *bounds* the selected cells. Selecting the left column of a
> two-column table gives a range running straight through the right column, so any command that
> treats `From..To` as the affected range reformats or deletes cells the user never selected — and
> `DeleteRange` over it takes whole cells out of their rows rather than emptying them. Every
> command must therefore ask `Cells(doc)` or refuse outright. This was not theoretical: it is the
> bug the first working version shipped, caught by an existing test.
>
> **Agreed semantics** (Word's, chosen by the user over two alternatives):
>
> | Command | On a rectangle |
> |---|---|
> | Delete, Backspace, cut | Empties each selected cell, keeping the table and one empty paragraph per cell |
> | Bold, italic, colour, any mark | Applied to every selected cell independently |
> | Copy | Yields a table of just those cells, each merged cell taken once |
> | Typing, paste, split, lists, indent, IME | Refused |
>
> **Two defects this uncovered**, both pre-existing and both fixed as their own commits: shared
> marks were read from the top-level blocks only, so a paragraph in a cell was invisible and a
> half-bold selection reported bold; and a cell handed its blocks straight to the view factory,
> so pressing the bullet button with the caret in a table threw at measure time.
>
> **Verification.** 30 mutants of the new code, 18 killed. The 12 survivors are recorded rather
> than hidden: three are genuinely equivalent, three are mutually redundant guards that the suite
> kills as a set (verified by removing all three at once, which fails), and six are explicit
> guards protected incidentally by a schema refusal or a null block — kept deliberately, because
> the alternative is a library that relies on accidental refusals.
>
> **The one non-obvious rule:** a rectangle covering a *whole* table has bounds the schema
> refuses to replace, so destructive commands appear safe there whether or not they check
> anything. Refusal tests must use a rectangle **within a single row**, whose bounds are
> replaceable. Making that change alone took the mutation score from 10/28 to 12/28.

> **P10 as built — one choke point, and defaults that were measured.**
>
> `IsReadOnly` is enforced in exactly **one** place, `RichTextView.Apply`, plus `Replay` for
> undo/redo. `Apply` is the only route by which a transaction becomes state, so one check covers
> typing, paste, drop, delete, IME, every toolbar command, image resize, and anything added later.
> A flag tested at each of the twenty-odd commands is a flag the twenty-first will miss, silently.
> Setting `State` directly still works — that is how a host loads a document into a read-only view.
> The caret stays visible, matching `TextBox`. `CanUndo`/`CanRedo` gate on it too, so a bound
> button greys out rather than appearing live and doing nothing.
>
> **Defaults were read out of Avalonia 12.1.1 by reflection, not assumed.** `TextBox` is
> `IsReadOnly=false`, `AcceptsReturn=false`, `AcceptsTab=false`, `UndoLimit=10`. VellumText matches
> only the first and diverges deliberately on the other three: a document editor that rejects
> Enter is absurd, Tab indenting is what already shipped, and a finite default `UndoLimit` would
> start silently discarding history that callers keep today. Each divergence preserves existing
> behaviour rather than changing it underneath users.
>
> **`History` and `UndoLimit` are two names for one value**, reconciled last-writer-wins at both
> the view and the shell. Assigning a `History` adopts its policy's `Limit` into the property via
> `SetCurrentValue`, so a binding still owns it; setting the property rewrites the `History` in
> place. `OnUndoLimitChanged` returns early when the limit already matches — without it, every
> `History` assignment would hand the caller back a rebuilt copy of what it just passed in.
>
> `HistoryPolicy.Limit` (`Unlimited = -1`) trims in `Record`; `WithPolicy` trims **immediately**,
> both stacks, because a limit that only takes effect on the next edit lies about the state it was
> applied to. `ImmutableStack` cannot drop its bottom, so the trim rebuilds. Measured in Release,
> median of 7 × 200 records over a 400-char paragraph: 1.6 µs untrimmed, 3.8 µs at limit 10,
> 8.3 µs at 100, 38.7 µs at 1000 — negligible against milliseconds of layout.
>
> **Verification.** 51 tests; 27 mutants, 27 killed, none failing to compile. Two traps worth
> recording: asserting `e.Handled` for Tab proves nothing, because the event bubbles to the window
> where keyboard navigation handles it *after* the view declines — assert that focus moves to a
> neighbouring control instead. And a test helper that repeats one character at one position makes
> the edits commute, which let a reversed-order trim survive undetected; distinct characters at
> ascending positions are required.

> **P9 as built — the row was describing a repository that no longer existed.**
>
> "Nothing is publishable today" was false. `Directory.Build.props` already carried descriptions,
> tags, `PackageLicenseExpression`, the packed README, `snupkg` symbols, source link and
> determinism, and `IsPackable=false` for everything outside `src/`. **Checked by packing and then
> consuming**, not by reading: a scratch project built against the produced `.nupkg` files loads
> the theme by `avares://VellumText.Avalonia/Themes/VellumText.axaml`, templates `RichTextEditor`, forwards
> the P10 properties into the view, and imports RTF. The theme travels inside the assembly as
> `!AvaloniaResources`, so no extra content files are needed. `PackageId` is left implicit because
> the assembly names are already the package names.
>
> **What was genuinely missing was a release story.** `release.yml` triggers on a `v*` tag and
> splits in two. `verify` refuses to continue if the tag disagrees with `VersionPrefix` — two
> places hold the version, so two places can disagree, and a NuGet version can never be replaced
> or withdrawn — then requires a `CHANGELOG` section for it, and builds, tests and packs.
> `publish` sits behind a `nuget` environment so a required reviewer gates it, and pushes the
> artifacts `verify` produced rather than a second build, so what ships is what was tested.
>
> The version reaches `grep` and `awk` through the environment rather than by `${{ }}`
> interpolation: a tag is attacker-supplied on a public repository, so interpolating it into a
> shell script is an injection, and its dots would be regex wildcards as well. Both shell steps
> were run locally against a synthetic changelog before being committed.
>
> `GeneratePackageOnBuild` is deliberately **off**: it would move packing onto the inner loop to
> catch mistakes the existing CI `pack` job already catches on every push.
>
> **What is not done: the package IDs.** The publish job asks nuget.org whether each version
> already exists and stops if it does, rather than passing `--skip-duplicate` — that would accept
> an existing version silently while the GitHub release was built from the *current* artifacts,
> leaving the two disagreeing about what a version contains. Run against the live index when this
> was written the check blocked the first release, because the then-current `Vellum.Core` 0.1.0 was
> taken by an unrelated package. So the automation was finished and correct, and was doing its job
> by refusing to release. **The 2026-08-17 rename to `VellumText.*` resolved it**; no further
> workflow work is needed.

**Not fixable here.** One paragraph of ~400 words still costs 7–12 ms per keystroke. This is A1's
documented consequence — Avalonia 12.1.1 cannot reshape a paragraph incrementally, so the fallback
is a wholesale rebuild. It is bounded by *paragraph* length rather than document length and is
virtualized off screen. Closing it needs upstream partial invalidation, not a change here.

### 10.4.1 After P11: what an integration found

P1–P11 closed the gap against the commercial control. What followed was driven by a *consumer* —
a clipboard manager migrating off a WebView — and is recorded here because almost none of it was
visible from inside the project. **Four of these were defects in code that had passing tests.**

| Work | Why it existed |
|---|---|
| `RichTextClipboard` made public | The format descriptors, and the rule that a native format is read as **bytes and never as a string**, were `internal`. The API is now shaped so the mojibake bug cannot be written through it — there is no overload anywhere taking a payload as a `string`. |
| `VellumText.Interop.Json` | HTML and RTF are *interchange* and both lose something. An application that **stores** documents needed one that loses nothing, and a schema version so a file from a newer build is refused rather than silently partly read. |
| `BitmapEmbedRenderer` | VellumText could lay an image out and never draw one — the only renderer in the box was the placeholder. **`data:` only by default**, because a pasted document is untrusted input and every source it names is an address that content chose. || Image paste and drop, `IImageStore` | `DragDrop` appeared nowhere in the assembly, and a pasted screenshot arrived as whatever text flavour rode alongside it. |
| `DocumentPreview`, `DocumentTextMap`, `Slice.OfLines` | A list of documents needs grapheme-safe truncation; code working in text offsets needs a way back to the model. `DocumentText.Of` **cannot** serve the latter — it expands an embed to its alt text, so it is not invertible. |
| `docs/guide.md` | The repository had an architecture document and no usage document. |

**The defects, all found by someone else asking a question rather than by the suite:**

- **Loading a different document left the previous document's undo stack live.** Not a wrong
  result — a throw: `Range [3, 4) is not inside the document`. An application that arrows through
  a list reaches it by pressing one key, and in an archive the corruption is persisted.
- **Every presenter built its own image cache**, so a two-hundred-row history held two hundred
  caches and decoded each image once per row. Invisible until someone's documents have images.
- **The image cache leaked at its limit**, returning a bitmap it had not stored, which nothing
  owned and nothing disposed — in native memory, so not even managed pressure.
- **`H1`/`Quote`/`Code` could not be turned off**, because `ToggleButton`s were bound to a command
  that only ever set.

> **Why the image default is a design property rather than a setting.** The motivating case, from
> the clipboard manager that drove this work: *previewing an HTML clip fetches whatever the
> document names — copy a marketing email, click it in your history, and its tracking pixels fire,
> so the sender learns when you looked at your own clipboard, from a tool whose entire job is
> holding things you copied.*
>
> The guarantee holds because **there is no setting that turns network fetching on.**
> `AllowLocalSources` turns on *local* resolution only; network sources are refused at every
> setting. A knob that could be flipped would eventually be flipped by someone who just needed one
> image, and the guarantee would then be gone for every document. If a path is ever found that
> reaches the network, that is a defect and not a configuration question.

**The lesson worth keeping** is not any one of those. It is that a library's defaults are wrong at
the scale its consumers run it at, not at the scale its tests do — and that the questions which
exposed all four came from outside the project. §10.4's "check the tree, not the row" has a
companion: *check it against a consumer*.

### 10.4.2 Table structure editing

**Built 2026-08-18.** Insert and delete rows and columns, merge a rectangle of cells, split a
merged one, and Tab between cells. The last thing that made VellumText a rich text editor rather
than a rich text *viewer* with formatting: tables rendered, selected and round-tripped, and could
not be changed.

Three things were load-bearing and none of them were about tables.

**A `StepMap` had to describe several ranges.** See the note in §4.3 — the short version is that
the one-range answer is sound arithmetic and collapses every position inside the table onto its
edge.

**A table edit has to be one step**, and that is a consequence of the schema rather than a choice.
`DocumentSchema` requires a table's cells to tile its grid exactly and every step must leave a
document the schema accepts, so a column done a cell at a time stands, between steps, in exactly
the document the schema exists to refuse. Verified before anything was built, by writing those
replacements and watching them fail.

**The step carries both tables, not just the new one.** `GetMap` has no document to consult, so
the alternative is a caller-supplied map beside a caller-supplied table: two descriptions of one
edit, free to disagree, with nothing able to notice. Carrying the pair makes the map derived, and
costs two references — the old table is the one already in the document.

Two things this uncovered:

- **The schema was wrong about empty rows.** Merging a whole rectangle leaves one cell covering
  several rows and the rows under it carrying nothing, which is what Word produces and what HTML
  expresses. `RequireNonEmpty` called that broken while the occupancy check beside it already
  answered the question properly. The rule moved into `ValidateTable` where coverage is known; a
  row reached on its own still has to carry a cell, because it has no table to ask.
- **§6.3's ragged-merge rejection cannot happen.** See the correction there.

And one thing that only appeared because of the first: **the HTML importer dropped every empty
`<tr>`**, so a merged table exported and read back came home as plain paragraphs. That code was
correct for as long as no document could contain an empty row, which is to say it was correct
until this increment. An empty row cannot be judged on its own — it is a merge when the rows above
span into it and a stray tag from a word processor when nothing does — so the importer now tries
the shape the author wrote and drops the empty rows only if it does not stand.

**The lesson is about the tests, not the fix.** Three interop tests were written for exactly this,
one per format, and all three asserted that what came back was schema-valid. A document of one
paragraph is schema-valid. They passed, in green, while the table was being thrown away, and the
defect surfaced only because the round trip was probed for what it actually returned. §8's rule
about round-trip tests proving the writer and reader agree with each other has a companion:
**a test that asserts an invariant the failure also satisfies is not a test.**

**Deliberately not built**, and left for whoever picks this up: column resize by dragging, table
styles and themes, and anything drag-and-drop. `TableEdits` has no notion of a table style and
`ColumnWidths` is already the model for a resize, so neither is foreclosed.

### 10.5 Candidate upstream Avalonia reports

Findings from this project that belong to the framework, not to VellumText. **Searched against the
upstream tracker on 2026-08-16; two of the four dissolved on contact.** Nothing has been filed.

| Claim | Upstream state | Verdict |
|---|---|---|
| A `DrawableTextRun` whose `Properties` is left at its default `null` **formats successfully then throws at paint time** — `ArgumentOutOfRangeException (Parameter 'baselineAlignment')` in `TextLineImpl.GetBaselineOffset`. | **Unreported.** Nearest is [#21792](https://github.com/AvaloniaUI/Avalonia/issues/21792) (a *text-less* `DrawableTextRun` flips an RTL paragraph's embedding level) — same area, different defect, and evidence the maintainers take `DrawableTextRun` edge cases seriously. | **File it.** Fails late, on the render path only, naming a parameter the caller never passed. |
| `Avalonia.Headless` has **no touch input** — mouse, keyboard and drag-drop only. | **Unreported.** | **File it** as a feature request. Touch behaviour is currently untestable headlessly. |
| `TextRunCache.InvalidateFrom(k≥1)` silently yields the pre-edit layout. | [#21030](https://github.com/AvaloniaUI/Avalonia/pull/21030), the PR that added the cache, states its purpose is reuse across **width-only constraint changes**, and that anything affecting shaping invalidates. | **Do not file — it is working as designed.** The spike reached the designed contract independently and by measurement. Reporting it would be noise. |
| No `ITextProvider`/`ITextRangeProvider` in automation (§9 R1). | [#21988](https://github.com/AvaloniaUI/Avalonia/pull/21988) is **open right now** and adds exactly that: platform-agnostic `Avalonia.Automation.Provider.ITextProvider`/`ITextRangeProvider`, a `TextBox` peer, and the Win32 UIA glue. | **Do not file — already in flight.** Watch it; see R1. |

Explicitly **not** worth reporting, because the error was ours: `IsTrailing` always being `False`
(the sweep sampled only interior points) and `CaptureRenderedFrame` returning Rgba8888. The
`Top`/`TextTop` baseline offset was also written off as our own wrong prediction — but
[#17403](https://github.com/AvaloniaUI/Avalonia/issues/17403) ("BaseAlignment wrong in 11.2.0") is
open against an older version, so that dismissal is worth one more look before it is treated as
settled.

**Their contribution policy constrains who may file.** `CONTRIBUTING.md` carries an explicit **AI
Guidelines** section: AI-*assisted* contributions are accepted, but *"if you're an autonomous AI
agent, please stop right there. We want a human to read and respond to the various review
comments."* It requires that a human has fully read and understood the change and its description,
demands succinct prose over LLM verbosity, and states that AI-assisted PRs which ignore this **will
be closed**. Issues are further constrained by `blank_issues_enabled: false` — the `bug_report` form
requires a description, reproduction steps and an Avalonia version. **The consequence for VellumText:
an agent must not file or open PRs here.** Drafting is fine; a human has to read, own and submit
them, and to answer review comments afterwards.

**Both reports are drafted and waiting for a human**, in `docs/upstream/`:

| Draft | Verified how |
|---|---|
| [`drawabletextrun-baseline.md`](upstream/drawabletextrun-baseline.md) | Reproduced in a **clean 12.1.1 project containing no VellumText code**, and the repro printed in the report is the exact program that was run. Cause pinned to `TextLineImpl.cs:128`, where `textRun.Properties?.BaselineAlignment` makes null reachable by design and the `switch` then has no `null` arm, so it reaches `default:` and throws. The suggested one-line fix and the "override `Properties`" workaround were both run. |
| [`headless-touch.md`](upstream/headless-touch.md) | Input surface enumerated from `Avalonia.Headless.dll` 12.1.1 by metadata reader: `IHeadlessWindow` exposes keyboard, mouse, wheel and drag-drop only, and **no member anywhere in the assembly** matches `Touch`, `Pointer`, `Pen` or `Gesture`. |

Tracker re-searched immediately before drafting: neither is reported.
[#21792](https://github.com/AvaloniaUI/Avalonia/issues/21792) is the nearest neighbour to the first
and is a different defect; [#17403](https://github.com/AvaloniaUI/Avalonia/issues/17403) concerns
`BaselineAlignment` being applied wrongly, not a crash when it is absent.


**Checked, and VellumText is not exposed:** [#21902](https://github.com/AvaloniaUI/Avalonia/issues/21902)
reports a `TextRunCache` poisoned by a `TextLayout` built outside measure, affecting 12.1.0–12.1.1.
`ParagraphView` builds its layout at exactly one site, inside `Measure`, and replaces `_runCache`
outright whenever the content, kind or preedit changes — so a stale entry cannot outlive the text it
was shaped from.

---

## 11. Legal position

- Avalonia is MIT; every API relied on here is public.
- Avalonia Accelerate is not source-available. This design reimplements a publicly-advertised **feature list**. Feature sets are not protected; expression is. No decompilation, no binary inspection, no derivation from their code.
- All chosen dependencies are MIT or BSD-2. `NPOI`, `DocX`/Xceed, `NRtfTree` (GPL-3.0), and `OpenXML-Office` (AGPL-3.0) are excluded on licensing grounds.
- `RichTextKit` (Apache-2.0) may be *read* as prior art; if any code is adapted, attribution and Apache-2.0 notice requirements apply and must be tracked in `THIRD-PARTY-NOTICES`.

---

## 12. Decision log & open questions

### 12.1 Decided

| Decision | Outcome | Date |
|---|---|---|
| **Project name** | **VellumText.** Deliberately not `Avalonia.*`, so it never reads as first-party. Packages: `VellumText.Core`, `VellumText.Avalonia`, `VellumText.Interop.*`. | 2026-08-11 |
| **Tables (AC5) in v1.0?** | **Yes — firm.** Tables are a hard requirement, not negotiable scope. This makes R2 the project's defining risk and Increment 6 the critical path. See §12.3. | 2026-08-11 |
| **License** | MIT. | 2026-08-11 |
| **OD1 — Avalonia version** | **Avalonia 12 only** for v1. No 11.3 LTS multi-targeting. It can be added later behind a build constant if demand appears; carrying it from day one taxes every increment. | 2026-08-11 |
| **Target framework** | **`net10.0`**, single-target. Avalonia 12.1.1 ships `net10.0` assemblies, so nothing is given up on the UI side. Consumers must be on .NET 10 — accepted deliberately for a new library rather than carrying a `net8.0` floor. | 2026-08-11 |
| **`VellumText.Core` dependencies** | **None at all** — not even `Avalonia.Base`. The core owns `Rgba` and `ValueSpan<T>` in place of the two Avalonia primitives it would otherwise borrow; `VellumText.Avalonia` owns the conversions. See §4.1. | 2026-08-11 |
| **RTF import (AC8)** | **Required, not optional.** The driving use case is a clipboard manager, and RTF is what Windows applications put on the clipboard. Shipped in `VellumText.Interop.Rtf` on RtfPipe. | user decision |
| **Hand-rolled parsers** | **Replaced by libraries.** HTML goes through AngleSharp + HtmlSanitizer, RTF through RtfPipe. Both formats are minefields where a hand-rolled parser is a permanent source of subtle wrongness. | user decision |
| **OD2 — toolbar?** | **Yes, ship one**, as **P4** in §10.3, on top of the commands from P1. The commercial control ships `EditorToolbar` and a selection flyout, and an editor surface with no default toolbar is not something a user can evaluate. The commands stay independently usable, so a host that wants its own toolbar is not forced through ours. | this pass |
| **A `Selection` member for "is this a rectangle"?** | **No — the type test stays, and closing the hierarchy is why.** 23 sites ask it, but they are 11 refusals with 11 different reasons, 7 dispatches that need a transaction and the presenter's geometry, and 4 reads of a cell index meaningless on the other two shapes. Once the constructor became `private protected` the type test is exhaustive by construction, which is a stronger guarantee than any virtual `bool` could give. `Selection.Map` remains the only member that varies by shape. See §4.5. | 2026-08-18 |
| **Two-way `Document` binding?** | **No — one way, out of the control.** A view model can observe `Document` and `Selection` and bind them `OneWayToSource`; assigning a document in is not offered. Reconciling an external assignment against in-flight edits and the undo history is a design with real hazards — what an outstanding composition means, whether the history survives, what a stale assignment from a slow view model does to text typed since — and no consumer has asked for it. Loading stays `State`. See §1.1 AC13. | 2026-08-18 |
| **Should a host be able to veto imported content?** | **Yes, as a second event rather than by making `ContentImported` cancellable.** `ContentImporting` carries the exact document that would be inserted and a `Cancel` flag, is raised only for content that would otherwise land, and treats a refusal as a paste that did not happen. `ContentImported` stays what it is — a report of what an import cost, raised when the undo entry already exists. See §4.9. | 2026-08-18 |
| **Declarative XAML configuration of `Formats`?** | **No, and the reason is mechanical.** `HtmlFormat` and `RtfFormat` are constructor-less: `Instance` and `With(options)` are the only ways to obtain one, which is what makes the paranoid defaults the default. XAML can only construct types with a public parameterless constructor, so a `<Formats>` collection in markup would require undoing that. Registering in code is one line and does not trade a security default for markup. | 2026-08-18 |

### 12.2 Still open

1. **OD3** — Is DOCX in v1.0, or deferred to v1.1?
2. Should this be published under a personal GitHub, or pitched to the Avalonia community (e.g. as an `Avalonia.Labs` candidate) for contributors from day one?

### 12.3 Consequences of committing to tables

Tables being firm changes three things in this design, and they should be treated as binding on Increments 1–3, long before any table code is written:

- **The model must be a tree (§5.1 A3), not a linear buffer.** Any temptation to simplify `VellumText.Core` to a flat Delta-style model in early increments is now foreclosed. `TableNode`/`TableRowNode`/`TableCellNode` must exist in the schema from Increment 1, even while unreachable from the UI, so positions and steps are exercised against nested structure from day one.
- **`BlockView` must be genuinely recursive from Increment 3.** A table cell is a block host containing blocks. If the block host in Increment 3 assumes a flat top-level list of blocks, Increment 6 becomes a rewrite. Validate recursion early by nesting a blockquote inside a list item.
- **`CellSelection` must be in the `Selection` hierarchy from Increment 1.** Retrofitting a third selection kind through every command is the expensive path.

**Revised risk posture:** R2 (table layout is entirely ours, §9) is no longer deferrable and cannot be scoped out under schedule pressure. The realistic v1.0 estimate stands at **4–6 months** part-time, with Increment 6 as the single largest block of work. Constrain v1 to fixed/proportional column widths and no auto-fit-to-content — that constraint is what keeps tables at weeks rather than months.

