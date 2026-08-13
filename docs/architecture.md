# A Free Rich Text Editor for Avalonia — Architecture & Design

**Status:** Draft for review
**Date:** 2026-08-11
**Target:** Avalonia 12.1.1 (current stable), .NET 8/9
**Proposed license:** MIT
**Name:** Vellum (packages `Vellum.Core`, `Vellum.Avalonia`, `Vellum.Interop.*`)

> Deliberately not named `Avalonia.*` — this is an independent community project and must not read as first-party.

---

## 1. User-visible outcome

A drop-in Avalonia control:

```xml
<vellum:RichTextEditor Document="{Binding Doc}" />
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

- **A1** — ~~`TextRunCache.InvalidateFrom` gives usable incremental reshaping for single-paragraph edits.~~ **Partly invalidated (Increment 0 — see [`increment-0-findings.md`](increment-0-findings.md) §2).** `InvalidateFrom(k>0)` returns a stale, *pre-edit* layout: invalidation is entry-granular and a paragraph is a single entry keyed 0. The correct usages (`InvalidateFrom(0)` / `Invalidate()`) measure **1.00×** against no cache, so R3's fallback — wholesale paragraph rebuild — is adopted **for edits**. **However**, reusing a persistent cache across a *wrap-width* change with unchanged text is correct and **≈3.5× faster** (median of 7 reps, range 2.9–5.2×), which matters because a resize rewraps every paragraph at once. Vellum therefore keeps a per-paragraph `TextRunCache` and drives it by **lifetime, not offset**: `Invalidate()` on a text edit, reuse untouched on width-only relayout.
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
│ Vellum.Avalonia                   (Avalonia dependency)        │
│   RichTextEditor (TemplatedControl)                            │
│   RichTextView (Control, ILogicalScrollable)  ← block host     │
│   BlockView / ParagraphView / TableView / ImageView            │
│   RichTextInputMethodClient : TextInputMethodClient            │
│   Commands, key bindings, RichTextToolBar (optional)           │
│   Themes/Fluent + Themes/Simple                                │
└───────────────┬────────────────────────────────────────────────┘
                │ depends on
┌───────────────▼────────────────────────────────────────────────┐
│ Vellum.Core                       (zero dependencies)          │
│   Model:  DocumentNode, BlockNode, InlineContent, marks        │
│   State:  EditorState, Selection, Transaction, Step, Mapping   │
│   Ops:    typing, formatting, list/indent, table ops           │
│   History: undo stack over inverted steps                      │
│   Schema: node/mark registry + validation                      │
└───────────────┬────────────────────────────────────────────────┘
                │ used by
┌───────────────▼────────────────────────────────────────────────┐
│ Vellum.Interop.Html                 (AngleSharp, MIT)          │
│ Vellum.Interop.Markdown             (Markdig, BSD-2)           │
│ Vellum.Interop.Rtf                  (no dependency, own code)  │
│ Vellum.Interop.Docx                 (DocumentFormat.OpenXml,   │
│                                      MIT, 3.5.1)               │
└────────────────────────────────────────────────────────────────┘
```

**Why the core has no dependencies at all:** the model, transactions, undo, and all interop are testable headlessly, in a plain `dotnet test`, with no windowing system — and, as decided on 2026-08-11, without even `Avalonia.Base`. That is the difference between a project that survives and one that doesn't. Interop packages are separate so an app that only needs Markdown does not drag in the Open XML SDK.

The cost of a zero-dependency core is two small primitives Avalonia would otherwise have supplied, which `Vellum.Core` therefore owns:

- **`Rgba` colour** — a `readonly record struct` of four bytes, replacing `Avalonia.Media.Color` in `MarkSet`.
- **`ValueSpan<T>`** — `(int Start, int Length, T Value)`, replacing `Avalonia.Utilities.ValueSpan<T>` in `InlineContent.Marks`.

`Vellum.Avalonia` owns the conversions in both directions. This is a deliberate trade: a few dozen lines of duplication in exchange for a core that is consumable from a server, a CLI, a source generator, or a test harness with no UI stack present.

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
    public ImmutableArray<ValueSpan<MarkSet>> Marks { get; }      // sorted, non-overlapping, normalized; Vellum's own ValueSpan
    public ImmutableArray<InlineEmbed> Embeds { get; }            // aligned to placeholder offsets
}

[Flags] public enum TextStyle { None=0, Bold=1, Italic=2, Underline=4, Strikethrough=8, Sub=16, Super=32, Code=64 }

public readonly record struct MarkSet(
    TextStyle Style,
    string? FontFamily,
    double? FontSize,
    Rgba? Foreground,          // Vellum's own colour type — the core takes no Avalonia dependency
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
public sealed record SetBlockAttrStep(int Pos, BlockAttr Attr, object? Value) : Step;
public sealed record TableStructureStep(...) : Step;

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
- **Collaboration is later, not never.** `Step.Map` is the rebase primitive OT needs. We ship no collab code, but we do not design it out.

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
> theme in `Themes/RichTextToolbar.axaml` and a light/dark palette in `Themes/Vellum.axaml`. The
> same control serves twice: `RichTextEditor`'s template puts a second instance in an overlay
> panel above the scroll viewer, and a `vellum-selection` class swaps in a compact nine-button
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
> Two known gaps: there is no rectangular **cell selection** UI (`CellSelection` exists in the model
> and the view does not raise it), and `Reconcile` has no in-place update path for a table, so any
> edit inside one rebuilds the whole table view. Correct, but unmeasured.

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
        DataFormat.CreateBytesApplicationFormat("vellum-slice+json");
}
```

**Copy** writes, in priority order: `NativeSlice`, `Html`, `Rtf`, `DataFormat.Text`.
**Paste** reads in the same order and stops at the first it understands — so in-app paste is lossless, cross-app paste degrades gracefully.

Windows `HTML Format` requires the CF_HTML preamble (`Version:0.9`, `StartHTML`/`EndHTML`/`StartFragment`/`EndFragment` **byte** offsets, UTF-8). Byte offsets, not character offsets — a classic source of silent corruption with non-ASCII content.

#### As built (P2)

Two things changed against the sketch above, both forced by decision 3 in §12.1:

- **`Vellum.Avalonia` does not reference the interop packages**, so it cannot name an HTML or RTF exporter. Formats are **registered** instead — `view.Formats.Add(HtmlFormat.Instance)` — through `IDocumentFormat`, which `IDocumentImporter` and `IDocumentExporter` both extend. An application that only wants an editor does not acquire an HTML parser and an RTF reader to get one. With nothing registered, copy and paste still move plain text.
- **`NativeSlice` is not built.** It exists to carry what HTML cannot, and HTML currently carries everything the model can hold. It is worth adding when that stops being true, and not before.

Native formats are read and written as **bytes**, never as strings. Asking a clipboard backend for RTF as a string lets it reinterpret those bytes as UTF-16, which yields convincing mojibake that then parses to nothing. `CF_HTML` is UTF-8 by definition; RTF is seven-bit by construction and is carried as Latin1, which cannot substitute.

**What a round trip actually costs**, measured rather than assumed:

| | HTML | RTF |
|---|---|---|
| Text, escapes, astral characters, bidi, ZWJ clusters | exact | exact |
| Marks, colour, highlight, font, size, links | exact | exact |
| Headings, alignment, indent, lists incl. `start`, tables | exact | exact |
| Collapsible whitespace | preserved, via `white-space:pre-wrap` on the paragraphs that need it | doubled spaces return with the second as U+00A0; a tab is lost; a space at a paragraph edge is lost |
| Quote, code block, header cells | exact | flattened to indent, body and bold |

The RTF losses are in the **reader** — RTF itself keeps all of it, and a word processor opening the file sees it — so the exporter's tests assert on the bytes emitted, not only on what survives a round trip.

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
2. Compute the covering rectangle; reject with a status message if it is ragged (partially-overlapping existing spans).
3. `TableStructureStep`: target cell gets `RowSpan`/`ColSpan`; absorbed cells' block content is appended to the target; absorbed cells removed.
4. `Kind = Structure` → never coalesced with neighbours in history.
5. `TableView` re-measures; column widths preserved; selection becomes a `TextSelection` at the end of the merged cell.

---

## 7. Validation, failure behavior, security

| Concern | Rule |
|---|---|
| Schema validity | `Step.Apply` returns `StepResult.Failed` rather than producing an invalid document. The editor drops the transaction and logs; it never applies a partially-valid tree. |
| Import robustness | Importers return `ImportResult(Document, IReadOnlyList<Diagnostic>)`. Never throw into the UI. Unsupported constructs downgrade to the nearest supported node; each downgrade is a diagnostic. |
| HTML paste | Strict allow-list of elements/attributes/CSS properties. Script, event handlers, embedded objects, and `javascript:`/`data:` URLs removed. Remote images off by default. |
| Image size | Configurable cap (default 16 MB decoded) — reject with a diagnostic rather than OOM. |
| Huge paste | Cap (default 10 MB) with a confirm hook. |
| Position mapping | All externally-held positions must be routed through `Mapping`; a debug-build assertion catches stale positions. |
| Undo integrity | Property test: for any random transaction sequence, `undo*` returns to the exact initial document; `redo*` returns to the exact final document. |
| IME | Preedit never reaches the model or history. |
| Read-only | Enforced at the single `RichTextEditor.Dispatch(Transaction)` chokepoint, not per-command. |

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
9. **Table nav** — Tab/Shift-Tab across cells and spans; arrow keys crossing cell and table boundaries.
10. **Trimming/AOT** — `PublishTrimmed` and NativeAOT smoke test of the demo app; the Open XML SDK path is the likeliest casualty and may need a trimming-safe boundary or opt-in package.
11. **Theme** — light/dark variant switch updates default foreground/selection/caret without touching explicit per-run colors.

---

## 9. Risks

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| **R1** | **No `ITextProvider` in Avalonia (C1).** Screen readers get plain text via `IValueProvider` at best — no ranges, no formatting, no caret events. A rich text editor with weak a11y is a real limitation, and no amount of work in *our* repo fixes it. | High | Ship `IValueProvider` + `AutomationProperties` in v1. Open an upstream Avalonia issue/PR proposing `ITextProvider`/`ITextRangeProvider`; this is plausibly the highest-value upstream contribution the project could make. Document the limitation honestly in the README. |
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
coherence: for several increments Vellum could *import* bold, coloured, hyperlinked text and
then offer the user no way to produce or change any of it.

### 10.2 Parity gap against the commercial control

Avalonia sells a closed-source `RichTextEditor` in Accelerate. Its **feature list** is public
(§11: feature sets are not protected; expression is) and is the obvious yardstick for "is this
worth using". Comparing it against what Vellum actually had:

| Capability | Their control | Vellum's model | Vellum's control |
|---|---|---|---|
| Character formatting (17+ controls) | ✅ | ✅ every mark | ❌ → ✅ **closed** |
| Headings, alignment, indent | ✅ | ✅ | ✅ |
| Lists | ✅ | ✅ | ⚠️ renders; no commands |
| Tables, merged cells | ✅ non-destructive | ✅ full schema + `CellSelection` | ⚠️ flattened to stacked paragraphs |
| Inline & block images | ✅ | ✅ | ✅ renders; no resize handles |
| Hyperlinks | ✅ | ✅ | ✅ **closed** |
| Toolbar (`EditorToolbar`) | ✅ | — | ❌ |
| Selection flyout (`EditorSelectionFlyout`) | ✅ | — | ❌ |
| Read-only viewer (`FlowDocumentScrollViewer`) | ✅ | — | ❌ |
| Serializers (RTF/DOCX/XAML, separate packages) | ✅ export + import | — | ❌ **import only, no export at all** |
| Light & dark theme | ✅ | — | ❌ no default theme |
| `IsReadOnly`, `UndoLimit`, `AcceptsReturn`/`Tab` | ✅ | — | ❌ |

**The finding that matters:** the document model was never the gap. It is competitive already —
it has tables with row and column spans, nested lists, links, embeds and every mark, all
property-tested. The gap was that `RichTextView` exposed no way to *reach* any of it. A host
could make a paragraph a heading but could not make one word bold.

Two structural things are worth copying from their design rather than inventing:

- **One serializer interface, one package per format.** They have `IDocumentSerializer` with
  Rtf/Docx/Xaml in separate packages. Vellum's importers are already separate assemblies
  (`Vellum.Interop.Html`, `Vellum.Interop.Rtf`); exporters should join them behind a single
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
| **P7** | **The rest of Increment 8.** a11y, find/replace, image resize handles, touch handles, perf pass. | Unchanged. | pending |

**Sequencing principle, stated because it was violated once already:** close the gap between
what the model can represent and what the control can do, before widening what the model can
represent. Vellum spent several increments able to import formatting it could not produce.

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
| **Project name** | **Vellum.** Deliberately not `Avalonia.*`, so it never reads as first-party. Packages: `Vellum.Core`, `Vellum.Avalonia`, `Vellum.Interop.*`. | 2026-08-11 |
| **Tables (AC5) in v1.0?** | **Yes — firm.** Tables are a hard requirement, not negotiable scope. This makes R2 the project's defining risk and Increment 6 the critical path. See §12.3. | 2026-08-11 |
| **License** | MIT. | 2026-08-11 |
| **OD1 — Avalonia version** | **Avalonia 12 only** for v1. No 11.3 LTS multi-targeting. It can be added later behind a build constant if demand appears; carrying it from day one taxes every increment. | 2026-08-11 |
| **Target framework** | **`net10.0`**, single-target. Avalonia 12.1.1 ships `net10.0` assemblies, so nothing is given up on the UI side. Consumers must be on .NET 10 — accepted deliberately for a new library rather than carrying a `net8.0` floor. | 2026-08-11 |
| **`Vellum.Core` dependencies** | **None at all** — not even `Avalonia.Base`. The core owns `Rgba` and `ValueSpan<T>` in place of the two Avalonia primitives it would otherwise borrow; `Vellum.Avalonia` owns the conversions. See §4.1. | 2026-08-11 |
| **RTF import (AC8)** | **Required, not optional.** The driving use case is a clipboard manager, and RTF is what Windows applications put on the clipboard. Shipped in `Vellum.Interop.Rtf` on RtfPipe. | user decision |
| **Hand-rolled parsers** | **Replaced by libraries.** HTML goes through AngleSharp + HtmlSanitizer, RTF through RtfPipe. Both formats are minefields where a hand-rolled parser is a permanent source of subtle wrongness. | user decision |
| **OD2 — toolbar?** | **Yes, ship one**, as **P4** in §10.3, on top of the commands from P1. The commercial control ships `EditorToolbar` and a selection flyout, and an editor surface with no default toolbar is not something a user can evaluate. The commands stay independently usable, so a host that wants its own toolbar is not forced through ours. | this pass |

### 12.2 Still open

1. **OD3** — Is DOCX in v1.0, or deferred to v1.1?
2. Should this be published under a personal GitHub, or pitched to the Avalonia community (e.g. as an `Avalonia.Labs` candidate) for contributors from day one?

### 12.3 Consequences of committing to tables

Tables being firm changes three things in this design, and they should be treated as binding on Increments 1–3, long before any table code is written:

- **The model must be a tree (§5.1 A3), not a linear buffer.** Any temptation to simplify `Vellum.Core` to a flat Delta-style model in early increments is now foreclosed. `TableNode`/`TableRowNode`/`TableCellNode` must exist in the schema from Increment 1, even while unreachable from the UI, so positions and steps are exercised against nested structure from day one.
- **`BlockView` must be genuinely recursive from Increment 3.** A table cell is a block host containing blocks. If the block host in Increment 3 assumes a flat top-level list of blocks, Increment 6 becomes a rewrite. Validate recursion early by nesting a blockquote inside a list item.
- **`CellSelection` must be in the `Selection` hierarchy from Increment 1.** Retrofitting a third selection kind through every command is the expensive path.

**Revised risk posture:** R2 (table layout is entirely ours, §9) is no longer deferrable and cannot be scoped out under schedule pressure. The realistic v1.0 estimate stands at **4–6 months** part-time, with Increment 6 as the single largest block of work. Constrain v1 to fixed/proportional column widths and no auto-fit-to-content — that constraint is what keeps tables at weeks rather than months.

