# Public API review for 1.0

Date: 2026-10-28. Reviewed at commit `36518f2`, the one that added the baselines (branch `user/asafmahlev/ci-cross-platform-and-public-api`, on top of the audit-fix stack).

This is the input to the 1.0 public-API freeze. It lists what each package exposes, and flags members that look accidental or internal-only, with the evidence. The sections after *Decisions* are the review as it was written, before anything changed. Each flag in them was a question for the owner, not a verdict. The counts are as of `36518f2`.

## Decisions (2026-10-29)

The owner answered every question below. All of it ships in 0.8.0; see `CHANGELOG.md` and `docs/migration-0.8.md`.

1. **Internal:** `RichTextView.GoalX`, `IsCaretVisible` and `SnapCaret`; `DocumentPresenter.RealizedBlocks`; `BlockSlot`, `ListMarker` and `DocumentBlocks`. **Private constructors** on `HtmlExporter`, `RtfExporter`, `MarkdownExporter`, `JsonFormat` and `JsonHighlighter`. **Kept public:** `PlaceholderEmbedRenderer`.
2. **`Step` is closed** with a `private protected` constructor. Every built-in step, including `ReplaceTextStep` and `TextReplacement`, stays public and sealed. This reversed a documented choice: `docs/architecture.md` (§4.5) kept `Step` open because nothing switches on which step it is, and `ClosedHierarchyTests` pinned it open. That review did not surface the conflict. It came up while the decision was being applied, and the owner confirmed closing. The architecture note and the test now record the closure and its reason, which is that custom steps are not a supported 1.0 extension point.
3. **`RichTextView`, `RichTextEditor` and `RichTextViewer` are sealed.** `DocumentPresenter` stays abstract, with a `private protected` constructor.
4. **Control internals trimmed.** Internal: `SetPreedit`, the `Preedit` property and record, `IsResizingColumn`, `IsResizingImage`, `IsDraggingTouchHandle`, `TouchHandles`, `ResetCaretBlink`, and `SetTextFromAutomation`, which is now internal and non-virtual on `RichTextView`. Kept public: `IsComposing`, `IsDragging`, `IsMovingSelection`, `ShowTouchHandles`, `HideTouchHandles`, `AreTouchHandlesVisible`, the block-index geometry API and `DocumentPresenterAutomationPeer`. `IsMovingSelection` was first decided internal. The table below says only `IsDragging` had a documented host scenario, but `docs/guide.md` documents `IsMovingSelection` for a host hiding its chrome while content is dragged, and `IsDragging` is never true during such a move. `GuideCheckTests` caught it, and the owner kept it public.
5. **Tables:** `TableGrid`/`TableSlot` stay public; `TableGeometry`/`CellPlacement` are internal.
6. **Internal:** `MarkFieldsExtensions` and `ClipboardHtml`. **Kept public:** `RichTextClipboard.Flavors`, `FlavorFor` and `ClipboardFlavor`.
7. **`ImportAsync(string, CancellationToken)` requires its token** on `IDocumentImporter` and every implementation; the `Stream` overload keeps its optional token. RS0026 and RS0027 are on again for `src/`. RS0027 also requires the optional overload to have strictly *more* parameters than every other overload. The two overloads tie at two, so the decided shape cannot satisfy it. That rule is suppressed on exactly the five `ImportAsync(Stream, …)` declarations, with the reason at `IDocumentImporter`. The tie cannot make a call ambiguous, because the first parameters differ in type.
8. **Both entry points per format stay**: the `IDocumentFormat` singletons and the static importer and exporter types.
9. **Public positional records stay as they are.**

## How the surface was captured, and how to keep it honest

Every shipping project under `src/` now references `Microsoft.CodeAnalysis.PublicApiAnalyzers` (from `Directory.Build.props`) and carries `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. The analyzer's own code fix generated the files, nullability included. With warnings as errors, any public or protected addition, removal or signature change fails the build (RS0016/RS0017) until those files are edited, so every API change shows up as a diff to a `PublicAPI.*.txt` file.

Nothing has been published to NuGet and there is no release tag, so the whole surface is in `PublicAPI.Unshipped.txt` and `PublicAPI.Shipped.txt` is empty. Releasing 1.0 moves the Unshipped lines into Shipped (CONTRIBUTING.md, *Releasing*). To check a claim in this document against the code as it is now, read the `PublicAPI.Unshipped.txt` beside each project. It is the source of truth, and this document is a snapshot of it.

The counts below are baseline entries: one per type, constructor, method, operator, field, and property or event accessor.

## The surface, per package

### VellumText.Avalonia — 590 entries, 31 types

`BitmapEmbedRenderer` (17), `BlockSlot` (20), `ClipboardFlavor` (14), `DataUriImageStore` (5), `DocumentBlocks` (4), `DocumentPresenter` (63), `DocumentPresenterAutomationPeer` (9), `ExportFailedEventArgs` (4), `IEmbedRenderer` (3), `IImageStore` (2), `IParagraphStyleResolver` (2), `ISyntaxTheme` (2), `ITextStyleResolver` (3), `ListMarker` (14), `ParagraphStyle` (34), `ParagraphStyleResolver` (3), `PlaceholderEmbedRenderer` (4), `Preedit` (14), `RichTextClipboard` (10), `RichTextEditor` (71), `RichTextToolbar` (56), `RichTextView.ContentImportedEventArgs` (4), `RichTextView.ContentImportingEventArgs` (5), `RichTextView.ImageRefusedEventArgs` (4), `RichTextView.LinkActivatedEventArgs` (4), `RichTextView.LinkRequestedEventArgs` (3), `RichTextView` (179), `RichTextViewer` (24), `SyntaxTheme` (5), `TextStyleResolver` (4), `ToolbarConverters` (4)

### VellumText.Core — 944 entries, 73 types

- **Transform**: `AddMarkStep` (23), `Assoc` (3), `BlockAttr` (6), `Mapping` (14), `MapResult` (14), `RemoveMarkStep` (21), `ReplacedRange` (18), `ReplaceStep` (23), `RestoreMarksStep` (21), `SetBlockAttrStep` (21), `Slice` (21), `Step` (16), `StepMap` (18), `StepResult` (13), `TableEdits` (8), `TableStructureStep` (21)
- **Nodes**: `BlockImageNode` (11), `BlockNode` (1), `CellPlacement` (18), `DocumentNode` (13), `DocumentSchema` (4), `ListItemNode` (11), `ListKind` (3), `ListNode` (12), `Node` (10), `ParagraphKind` (10), `ParagraphNode` (19), `RuleNode` (9), `SchemaViolation` (14), `TableCellNode` (15), `TableGeometry` (2), `TableNode` (12), `TableRowNode` (10), `TextAlign` (6)
- **Selection**: `CellSelection` (19), `NodeSelection` (17), `Selection` (22), `TableGrid` (10), `TableSlot` (20), `TextSelection` (14)
- **Interop**: `DiagnosticSeverity` (4), `DocumentPayload` (3), `IDocumentExporter` (3), `IDocumentFormat` (4), `IDocumentImporter` (3), `ImportDiagnostic` (16), `ImportResult` (7)
- **Model**: `DocumentPreview` (3), `DocumentText` (3), `DocumentTextMap` (10), `ImageEmbed` (18), `InlineContent` (28), `InlineEmbed` (12), `ISyntaxHighlighter` (3), `JsonHighlighter` (5), `LinkMark` (11), `MarkFields` (14), `MarkFieldsExtensions` (4), `MarkSet` (27), `ResolvedPos` (19), `TextStyle` (9), `TokenKind` (9)
- **Search**: `DocumentSearch` (4), `SearchMatch` (15), `SearchOptions` (14)
- **State**: `EditorState` (15), `History` (13), `HistoryEvent` (24), `HistoryPolicy` (20), `Transaction` (24), `TransactionKind` (8)
- **Primitives**: `Rgba` (26), `ValueSpan` (19)

### VellumText.Interop.Html — 55 entries, 5 types

`ClipboardHtml` (4), `HtmlExporter` (9), `HtmlFormat` (11), `HtmlImporter` (3), `HtmlImportOptions` (28)

### VellumText.Interop.Json — 11 entries, 1 types

`JsonFormat` (11)

### VellumText.Interop.Markdown — 59 entries, 5 types

`MarkdownExporter` (9), `MarkdownExportResult` (15), `MarkdownFormat` (11), `MarkdownImporter` (2), `MarkdownImportOptions` (22)

### VellumText.Interop.Rtf — 42 entries, 4 types

`RtfExporter` (8), `RtfFormat` (11), `RtfImporter` (3), `RtfImportOptions` (20)

## Candidates that look accidental or internal-only

Ordered roughly by confidence. "Who needs it" names the callers found by searching `src/`, `tests/`, `samples/` and `bench/`. Two facts make "public" unnecessary for many of these. Core grants `InternalsVisibleTo` to `VellumText.Avalonia` and every `Interop.*` project, `Interop.Html` grants it to `Interop.Markdown` and `Interop.Rtf`, and every `src` project except `Interop.Json` grants it to its own tests. So a member used only by sibling packages or by tests could be `internal` without breaking those callers.

### High confidence: exposed for tests or as plumbing

| Member | Package | Why it looks accidental | Who uses it |
|---|---|---|---|
| `RichTextView.GoalX` | Avalonia | Its doc comment says "Exposed for tests". It is the remembered column for a run of Up/Down presses, which is internal caret state. | The view, and tests |
| `RichTextView.IsCaretVisible` | Avalonia | Documented as "for tests and for the blink timer". It reports the blink phase, which the caret timer flips continually and which means nothing to a host. | The view, and tests |
| `DocumentPresenter.RealizedBlocks` | Avalonia | Documented as "For tests and diagnostics: this is the evidence that virtualization is happening at all." | Tests |
| `BlockSlot`, `ListMarker`, `DocumentBlocks` (`Flatten`, `Leaves`) | Avalonia | The view layer's flattened block list. `DocumentPresenter.Slots`, the only member that returns slots, is already `internal`, so a host can get a `BlockSlot` only by calling `DocumentBlocks.Flatten` itself. | Avalonia, tests, and `bench` (which has `InternalsVisibleTo`) |
| Implicit public constructors on the `Instance` singletons: `HtmlExporter`, `RtfExporter`, `MarkdownExporter`, `JsonFormat`, `JsonHighlighter` | Html, Rtf, Markdown, Json, Core | Each type exposes `static Instance` **and** a public parameterless constructor that none of them declare. The C# compiler adds it. `HtmlFormat`, `MarkdownFormat` and `RtfFormat` follow the singleton pattern without one, so this looks like an omission rather than a choice. A private constructor would remove it, but that is a removal, so it is listed here. | None found outside the type |
| `RichTextView.SnapCaret(Rect, double)` | Avalonia | A `public static` pixel-snapping helper on the control, used for the control's own caret drawing. Its remarks are a measurement of `PixelRect.FromRect`, which is implementation rationale rather than a host contract. | The view, and tests |
| `PlaceholderEmbedRenderer` | Avalonia | Its doc comment ties it to "Increment 2", the development milestone, and says it is a stand-in. It is the internal fallback of `BitmapEmbedRenderer` and of paragraph layout. `DocumentPresenter.Embeds` mentions it as an option a host could choose, so this one may be deliberate. | Avalonia internals, and one test |

### Extensibility that may not be intended

| Member | Package | Why it is worth a look |
|---|---|---|
| `Step` (`public abstract record`, with the compiler's implicit `protected` constructors) | Core | A host can derive its own step. The architecture closes the node, embed and selection hierarchies with `private protected` constructors (see `InlineEmbed` and `Selection`) so that closed switches stay exhaustive. `Step` has no such constructor, while all six built-in steps are `sealed`. If custom steps are not a supported extension point, a `private protected` constructor would close it, but that removes the protected constructors. Decide which before 1.0, because opening it later is compatible and closing it is not. |
| Unsealed `RichTextView`, `RichTextEditor`, `RichTextViewer` | Avalonia | Each can be subclassed, so every `protected override` they declare (44 `override`/`virtual`/`abstract` entries in the Avalonia baseline) becomes part of the contract. `DocumentPresenter` is `abstract` and public, so a host can also derive a fourth presenter. |

### Medium confidence: control internals reachable from the public type

| Member | Package | Why it is worth a look |
|---|---|---|
| `RichTextView.Preedit`, `SetPreedit`, `IsComposing`, the `Preedit` record | Avalonia | IME composition state, driven by Avalonia's text-input client. A host calling `SetPreedit` itself would draw a composition that no input method owns. `IsComposing` is plausible for hosts (for example, to suppress shortcuts mid-composition); `SetPreedit` and the record are less so. |
| `RichTextView.IsDragging`, `IsMovingSelection`, `IsResizingColumn`, `IsResizingImage`, `IsDraggingTouchHandle` | Avalonia | Gesture-state flags. `IsDragging` is documented with a host scenario (a toolbar waiting for the drag to end). The others have only a one-line summary. Decide whether the set is a supported contract or should shrink to the documented one. |
| `RichTextView.TouchHandles`, `ShowTouchHandles`, `HideTouchHandles`, `AreTouchHandlesVisible` | Avalonia | The handle geometry (`TouchHandles`: rectangles in control coordinates) is drawing detail. Show/hide may be a deliberate host hook. |
| `RichTextView.BlockStart`, `CaretBlockIndex`; `DocumentPresenter.BlockCount`, `BlockIndexAt`, `BlockIndexOf`, `BlockOrigin`, `PositionAt`, `ImageRect`, `IsOverContent` | Avalonia | A block-index geometry API. `BlockCount`'s remarks say a host needs it, so this looks intentional. But it fixes "block index" as a public concept, which ties a future layout rewrite (the in-flight `ParagraphView`/`TableBlockView` work) to the current flattening. Worth deciding before 1.0 rather than after. |
| `RichTextView.ResetCaretBlink` | Avalonia | Called by the view after every caret move. A host has little reason to call it. |
| `DocumentPresenter.SetTextFromAutomation` (`public virtual`), `DocumentPresenterAutomationPeer` | Avalonia | Accessibility plumbing. Avalonia's own peers are public, so the peer type being public is conventional. `SetTextFromAutomation` being an overridable public entry point is less so. |
| `TableGeometry`/`CellPlacement` **and** `TableGrid`/`TableSlot` | Core | Two public models of a table's grid, one in `Nodes/TableNodes.cs` and one in `Selection/CellSelection.cs`, with overlapping fields (first row, first column, end row, end column). The external callers (Avalonia, Markdown, Rtf) all have `InternalsVisibleTo`. Keep one public model, both, or neither. |
| `MarkFieldsExtensions` (`Includes`, `ToFields`, `ToStyle`) | Core | Bit-twiddling helpers between `MarkFields` and `TextStyle`. Called inside Core, once in Avalonia (which has `InternalsVisibleTo`), and in tests. |
| `ClipboardHtml` (`ExtractFragment`, `IsClipboardHtml`, `Wrap`) | Html | The Windows `CF_HTML` envelope. `HtmlImporter.ImportClipboardAsync` and `HtmlExporter.ExportForClipboard` already handle it, and the type is used only inside the Html package and its tests. |
| `RichTextClipboard.Flavors`, `FlavorFor`, `ClipboardFlavor` | Avalonia | Maps format names to platform clipboard formats. Used by the control and heavily by tests. Public is reasonable if hosts are expected to read or write the clipboard in VellumText's flavours themselves; otherwise it is plumbing. |

### Probably deliberate; noted so the freeze is a decision, not a default

- `ToolbarConverters` is used by `{x:Static}` in the shipped `RichTextToolbar.axaml` and `VellumText.axaml` templates. A host that re-templates the toolbar needs it, so it should stay public, or move into the theme if re-templating is not a supported scenario.
- The transform layer (`Step` and its subclasses, `StepMap`, `Mapping`, `MapResult`, `ReplacedRange`, `Assoc`, `Slice`, `ResolvedPos`) is the ProseMirror-style model the architecture documents, and about 250 baseline entries to commit to.
- `JsonHighlighter` lives in Core, while JSON *documents* live in `Interop.Json`. It highlights JSON inside code blocks, which is a different job, but the name invites the confusion.
- `DocumentPayload` is a helper for third-party format authors, and is reasonable to keep public for that reason.

## Design questions the analyzer raised

- **RS0026/RS0027, optional parameters on overloads.** `IDocumentImporter.ImportAsync(Stream, CancellationToken = default)` and `ImportAsync(string, CancellationToken = default)` both take an optional parameter. So does every implementation (`HtmlFormat`, `JsonFormat`, …). The analyzer's rule is that adding a third such overload later can make existing calls ambiguous. Both rules are switched off for `src/` in `.editorconfig` so that the baseline records the API as it is. Decide for 1.0 whether to keep the shape (and leave the rules off), or make the token required on one overload and turn the rules back on.
- **Two entry points per format.** Each interop package has static `*Importer`/`*Exporter` types *and* an `IDocumentFormat` singleton (`HtmlFormat.Instance`, …) that wraps them. `IDocumentImporter`'s remarks explain why both exist. Freezing both doubles what must stay compatible.
- **Positional records expose `init` accessors and `Deconstruct`.** For example `BlockSlot`, `ListMarker`, `ClipboardFlavor` and `CellPlacement`. Every positional parameter becomes a public property with `init`, a `Deconstruct` overload and `with` support (`<Clone>$`). Adding a positional parameter later is a breaking change, which matters for every public record that survives this review.

## Not covered

- Behavioural compatibility (what members do) is outside the analyzer's reach. The test suites are what pin behaviour.
- Template parts (`PART_*` names), pseudo-classes and resource keys in `Themes/*.axaml` are a contract for anyone re-templating the controls, but they are not C# symbols and the analyzer does not see them.