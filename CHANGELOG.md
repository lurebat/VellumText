# Changelog

All notable changes are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

VellumText is pre-1.0, so the public API is still allowed to move. Nothing is on
NuGet; packages are published to a local feed, and
[`docs/consuming.md`](docs/consuming.md) explains how to reference them.

## [Unreleased]

Every breaking change here is deliberate. VellumText is pre-1.0 with no public
consumers, and the point of this release is that a 1.0 must not freeze an API
that a real host cannot use. Each entry says what moved, why, and what to do
about it; **migrating** below is the short version.

### Migrating

1. Formats you implement yourself: add `Import(Stream)`, `Export(Stream, DocumentNode)` and
   `TextEncoding`. Keep the string bodies. Never implement a stream member by calling its string
   counterpart.
2. `RtfImporter.Import` now takes `RtfImportOptions`; the fields you were passing exist on it under
   the same names, except `MaxDepth` and `PreserveIdentifiers`.
3. `HtmlImportOptions.PreserveIdentifiers` is gone; delete the assignment. It never did anything.
4. `Editor.View!.Formats` and its neighbours become `Editor.Formats`. Where the shell forwards
   nothing, `Editor.EnsureView()` replaces `Editor.View!`.
5. `presenter.Blocks.Count` becomes `presenter.BlockCount`.
6. `Node.Children` is an `ImmutableArray<Node>`. `foreach` and LINQ are unchanged; comparing two
   with `Assert.Equal` is not — that compares the underlying arrays by reference, so call
   `.AsEnumerable()`.
7. If you derived from `Node`, `BlockNode`, `InlineEmbed` or `Selection`, you cannot any more. See
   below for why that is a fix and not a loss.
8. A preview pane that used `RichTextViewer` and wanted selection or copy wants
   `RichTextEditor { IsReadOnly = true }`. `docs/guide.md` said otherwise and was wrong.

### Changed

- **`RtfImporter.Import` takes `RtfImportOptions` rather than `HtmlImportOptions` — this is a
  breaking change.** RTF is read by way of HTML internally, and that had leaked into the RTF
  package's public API: a consumer who wanted only RTF had to reference the HTML package to
  configure it, and an internal choice was frozen where a later rewrite of the RTF reader could not
  have changed it. `RtfImportOptions` carries `AllowRemoteImages`, `AllowDataImages`,
  `MaxDataImageBytes` and `BaseUri`. It deliberately omits `MaxDepth`, which applies to authored
  HTML rather than the HTML this package generates for itself.

- **`RichTextEditor` forwards the whole setup surface, so `Editor.View!` is no longer the
  documented path.** `Formats`, `ImageStore`, `Embeds`, `TextStyles`, `ParagraphStyles`,
  `SyntaxHighlighter`, `SyntaxTheme`, `ContentImported` and `ImageRefused` are now on the shell.
  These are the surfaces a host reaches before the control is templated, which is exactly when
  `View` is null — a format registered on a missing view throws, and an event subscribed on one is
  silently never heard from.

- **`Transaction.DocsBefore` is gone.** It exposed the per-step document snapshots the undo
  bookkeeping keeps and had no caller: `Invert()` reads the field directly. `Transaction.Steps` and
  `Transaction.Failures` were examined at the same time and both stay public — `Steps` is the
  rebase primitive collaboration needs, and `Failures` is what a host checks when it needs a
  transaction to have been all-or-nothing.

- **`IDocumentImporter` and `IDocumentExporter` now read and write streams, and the string
  overloads are derived from them — this is a breaking change for anyone implementing a format.**
  The contract dealt only in strings, which *structurally* excluded DOCX and ODT: both are ZIP
  containers, both are on the roadmap, and no later effort could have made one fit through a
  `string` without a base64 fiction. Streams are now the primitive.

  A format must implement `ImportResult Import(Stream source)` and
  `void Export(Stream destination, DocumentNode doc)`, and must declare
  `Encoding? TextEncoding` — an encoding if it is text, `null` if it is not. The string overloads
  `Import(string)` and `Export(DocumentNode)` remain, as default interface members that encode or
  decode through `TextEncoding`, because HTML and RTF genuinely are text and making a caller wrap a
  string in a `MemoryStream` would be pure ceremony.

  Migrating a format means adding the two stream members and `TextEncoding`; the existing string
  bodies stay exactly as they are. Every omission is a compile error. **Do not implement a stream
  member by calling its string counterpart** unless you also declare that counterpart — the default
  implementation calls back into the stream member and the pair recurses forever.

  For a format with no text form, `Import(string)` returns an empty result carrying a `Malformed`
  diagnostic and `Export(DocumentNode)` throws `NotSupportedException`. There is no honest string to
  return, and `""` would be a document silently lost on the way to a file.

- **`RichTextClipboard.Encode` and `Decode` no longer return `null`.** They never actually did —
  `FlavorFor` mints an application flavour rather than returning `null` — so the nullable return was
  dead, and callers were pushed into a null check that could not fire. Existing code compiles
  unchanged; the annotation is what moved.

- **`ClipboardFlavor` no longer carries an `Encoding`.** `IDocumentFormat.TextEncoding` is now the
  single source of truth, and a flavour minted with a hard-coded UTF-8 would have been a lie for a
  format that is not text. Read the encoding from the format instead.

- **`Node.Children` is an `ImmutableArray<Node>` rather than an `IReadOnlyList<Node>`.** The
  children of a node are already an immutable array in every implementation; the interface return
  boxed the struct on every access, on the hottest walk in the library. Reading code is unchanged.
  One thing to know when testing: `Assert.Equal` on two `ImmutableArray<T>` compares the underlying
  arrays by reference and will fail on two equal arrays, so compare `.AsEnumerable()`.

### Removed from the public API

None of the following was reachable in a way that would have survived contact with a real host; they
are listed because a 1.0 that shipped them would have frozen them.

- **The rendering machinery is internal.** `BlockView`, `ParagraphView`, `RuleView`,
  `BlockImageView`, `TableBlockView`, `InlineTextSource` and `TextAt` are layout objects — a
  `TextLayout`, a run cache, a measured size — whose lifetimes the presenter owns and whose
  invalidation rules nothing outside it can honour. `docs/architecture.md` §4.6 and §4.7 always
  declared them internal; the code did not. `DocumentPresenter.Blocks`, `DocumentPresenter.Slots`
  and `RichTextView.Block` went with them, a property being only as internal as its type.

  **`DocumentPresenter.BlockCount` replaces `Blocks.Count`**, and is the part a host actually
  needed: `BlockOrigin`, `BlockIndexOf`, `BlockIndexAt` and `CaretBlockIndex` all speak in block
  indices, so the range those indices live in is public even though the objects behind them are not.

- **`Node`, `BlockNode`, `InlineEmbed` and `Selection` can no longer be derived from outside the
  library.** Each now has a `private protected` constructor. They were externally derivable and
  consumed by `switch` statements that throw, drop or refuse on a shape they do not recognise —
  `DocumentPresenter`'s block factory, the HTML and RTF exporters, the JSON writer, every command
  that asks whether it has a rectangle of cells. A type you derived would compile and then fail the
  first time it was rendered or saved, which is worse than no extensibility at all because it fails
  at run time in your application instead of at compile time in ours.

  `Step` is deliberately still open: nothing switches on which step it is, and all four obligations
  are on the base, so a step written elsewhere works everywhere one is consumed.

  One hole cannot be closed, and is stated rather than hidden: a record's copy constructor must be
  `public` or `protected` (CS8878), so `InlineEmbed` and `Selection` can still be derived from by
  declaring a copy constructor that chains to the base. It cannot happen by accident, and anything
  that does it lands on a `default:` that throws.

- **`HtmlImportOptions.PreserveIdentifiers` is gone.** It was declared, defaulted, documented and
  never read: no code path in the importer consulted it, so setting it to `true` did not preserve
  an identifier and setting it to `false` did not strip one. A host that set it got the same
  document either way and had no way to find that out. Delete the assignment; nothing else changes.

### Added

- **`HtmlFormat.With(options)` and `RtfFormat.With(options)`**, returning a configured instance and
  leaving `Instance` alone. Both formats were constructor-less singletons, which made
  `HtmlImportOptions` unreachable from the seam hosts are told to use: the control holds an
  `IDocumentImporter` and calls `Import`, so a static `HtmlImporter.Import(html, options)` on a type
  in a package the control cannot name is no use to an application that uses the editor.
  `AllowRemoteImages` is a privacy control and `BaseUri` decides whether a relative image URL
  resolves at all, and neither reached the importer. `Instance` still means the paranoid defaults,
  and `HtmlFormat.Instance.ImportOptions` says so.

- **`RichTextEditor.EnsureView()`**, the honest escape hatch for the editing commands the shell
  does not forward. It applies the template if that has not happened and returns a view that is
  never null. `View` stays nullable and answers the different question — has this been templated
  yet.

- **`DocumentPayload.ReadAllText` and `DocumentPayload.WriteAllText`**, the bridge a text format's
  stream members need. `WriteAllText` writes no byte order mark — `CF_HTML`'s byte offsets are
  computed over a payload without one, and an RTF reader rejects a document preceded by
  `EF BB BF` — and `ReadAllText` honours a mark when one is present and leaves the stream open.

- **Binary formats travel on the clipboard end to end.** `RichTextClipboard` asks the exporter to
  write its own bytes and hands a payload straight back to the importer, so a registered format that
  is not text survives a copy and a paste unchanged.

- **`Document` and `Selection` are observable on both controls.** `RichTextView` and
  `RichTextEditor` each gained `Document`, `Selection`, the `DocumentProperty` and
  `SelectionProperty` behind them, and the `DocumentChanged` and `SelectionChanged` events. This is
  the half of `AC13` that was missing: the commands existed and `IsReadOnly` was settable, but a
  view model had no way to hear that the user had typed something.

  **Binding is one-way, out of the control.** The direct properties are read-only and default to
  `OneWayToSource`, so `{Binding Doc, Mode=OneWayToSource}` — or plain `{Binding Doc}` — keeps a
  view model's copy current. Assigning a document from outside is deliberately not offered:
  reconciling an incoming document against in-flight edits and the undo history is a design with
  real hazards and nobody has asked for it. Load a document through `State` as before.

  The events exist alongside the properties because `OneWayToSource` never pushes the value that is
  already there, only subsequent changes, so a binding set up after a document was loaded would
  leave a view model holding `null` until the first keystroke. Subscribe to `DocumentChanged` and
  read `Document` once to prime.

- **`RichTextView.ContentImporting` and `RichTextEditor.ContentImporting`**, raised with a
  `ContentImportingEventArgs` before imported content is inserted, so a host can inspect what is
  about to land and set `Cancel` to refuse it. `ContentImported` reports what an import cost after
  the fact; this is the seam that comes first.

  It carries the exact document that would be inserted, the importer's diagnostics, and whether
  anything was dropped on the way in. It is raised only for content that would otherwise land — a
  read-only control, a rectangle selection and an empty clip never reach it, so a host is not asked
  about a paste that was never going to happen. A refusal is a paste that did not occur: no edit, no
  undo entry, no `ContentImported`, and nothing added to the diagnostics channel. A handler that
  throws costs the paste and nothing else; on the keyboard, menu and drop paths the existing handler
  swallows it, and `PasteImport` called directly rethrows.

  Images are deliberately not covered. `IImageStore.Save` returning `null` is already an
  inspect-and-refuse seam, and `ImageRefused` already reports the outcome.

- **`RichTextClipboard.Build` and `RichTextClipboard.WriteAsync` take an optional `text`.** The
  plain text flavour comes from the caller when one is given, so a control stays the authority on
  what it shows selected. Existing calls compile and behave unchanged.

### Fixed

- **Text carrying `U+FFFC` is no longer silently dropped.** The object replacement character is
  what the model reserves for embeds, and building a paragraph from text containing one throws.
  `RichTextClipboard`'s plain-text fallback did not know that, so dropping such text — which Word
  writes for objects it cannot otherwise express, and which any text that has been through a rich
  text model can carry — inserted nothing at all, while pasting the same text worked.

- **A drop and a paste of one clip now produce the same paragraphs.** The two paths disagreed about
  what a line break is: the drop normalized through `string.ReplaceLineEndings`, which takes the
  form feed, NEL and the Unicode line and paragraph separators, and the paste normalized only CRLF
  and CR. The same clip became seven paragraphs dropped and three pasted, with raw control
  characters left inside them.

- **A `LinkRequested` example in `docs/guide.md` did not compile**, awaiting inside a non-async
  lambda.

## [0.4.1] - 2026-08-17

### Fixed

- **The `CF_HTML` envelope is no longer written on macOS and Linux.** `HtmlFormat.Export` wrapped
  every payload in the Windows `CF_HTML` preamble regardless of platform, so a copy on macOS or
  Linux put `Version:0.9` and a block of byte offsets under `public.html` and `text/html` — targets
  that carry bare markup — and a receiving application would have rendered the preamble as content.
  Windows is unchanged. Import already accepted both shapes on every platform, which is why no
  round-trip test could see it.

## [0.4.0] - 2026-08-17

### Changed

- **Renamed to `VellumText` throughout — this is a breaking change.** Package ids, assembly names,
  namespaces, directories, the solution and the theme URI all moved from `Vellum.*` to
  `VellumText.*`. Consumers must update `PackageReference` ids, `using` directives, and the
  theme include to `avares://VellumText.Avalonia/Themes/VellumText.axaml`.

  The old name was not available: `Vellum.Core` is an actively published envelope-encryption
  library shipping the *same version numbers*, so a version pin was no defence and the failure
  surfaced as a missing-type compile error long after a clean restore. `VellumText.*` is free and
  unreserved.

  Nothing else changed — no behaviour, no API shape, and the full suite passes unmodified apart
  from its own namespaces.
## [0.3.0] - 2026-08-16

### Fixed

- **Two linear lookups over already-sorted data**, both superlinear in document size and both
  invisible until a document is large. The text formatter scanned the paragraph for the next embed
  on every run request, and `DocumentTextMap` walked its span table on every lookup. A full walk of
  an 8,000-paragraph document went from **49.1 ms to 1.50 ms**. Neither was detectable by a
  correctness test, because every answer was right at every size; only the growth rate showed it.

No API changes. Both fixes are equivalence-tested against the implementations they replace, so
upgrading from 0.2.0 is a drop-in.

## [0.2.0] - 2026-08-16

The first release anyone consumed. 0.1.0 was built but its artifacts were withdrawn before use, so
this is effectively the initial version.

### Added

- **`VellumText.Core`** — the headless document model. An immutable node tree with
  paragraphs, lists, rules, embeds and tables; a flat integer position scheme;
  replace, mark and block-attribute steps that map positions through an edit;
  text, node and cell selections; transactions; and undo history with
  coalescing. No dependencies at all, enforced by a build target.
- **`VellumText.Avalonia`** — the editing surface. A `RichTextEditor` control over
  a block-reconciling view, built on Avalonia's own text stack: bidi-aware
  selection geometry, grapheme-correct caret movement, inline embeds via
  `DrawableTextRun`, and IME composition.
- Multi-block editing: cross-block caret movement and selection, Enter to
  split, Backspace and Delete to join, headings, blockquotes, code blocks,
  alignment, indent, and bulleted and numbered lists.
- **`VellumText.Interop.Html`** and **`VellumText.Interop.Rtf`** — import and export both ways. HTML import
  sanitizes; both report what they could not keep rather than dropping it silently.
- **`VellumText.Interop.Json`** — lossless, schema-versioned JSON, for storing documents rather
  than exchanging them. A document written and read back is equal to the one written, and a file
  from a newer schema is refused rather than partly read.
- **`RichTextClipboard`** — a public clipboard API with no way to read a native format as a string,
  because doing so silently destroys RTF.
- **Images that actually draw.** `BitmapEmbedRenderer` is the default renderer; by design it
  resolves only `data:` URLs unless a host opts in, because a pasted document is untrusted input
  and its sources are addresses that content chose. Network sources are refused at every setting.
- **Pasting and dropping** images, files and rich text. `IImageStore` decides where pixels go, and
  defaults to carrying them inside the document.
- **`DocumentPreview`** — grapheme-safe truncation, for lists of documents.
- **`DocumentTextMap`** — a two-way map between document positions and plain-text offsets, for
  code that transforms a selected range and has to get back to the document.
  `DocumentText.Of` cannot serve this: it expands an embed to its alt text, so it is not
  invertible. `Slice.OfLines` completes the projection in the other direction, turning newlines
  in a replacement back into block boundaries.
- **Syntax highlighting for code blocks.** `ISyntaxHighlighter` emits token kinds, `SyntaxTheme`
  maps them to colours, and the result is applied at layout time and never stored — so exports,
  equality and undo are untouched and a theme change is a redraw. `JsonHighlighter` included.
- **Read-only viewing** with `RichTextViewer`, and the control properties `IsReadOnly`,
  `AcceptsReturn`, `AcceptsTab` and `UndoLimit`.
- A light and dark theme, a toolbar, a floating selection toolbar, find and replace, rectangular
  table-cell selection, and image resize handles.

### Fixed

- **Loading a different document into a view no longer leaves the previous document's undo stack
  live.** Pressing Ctrl+Z after swapping threw `Range [3, 4) is not inside the document` — reachable
  by arrowing through a list of documents and pressing one key.
- Every presenter built its own image cache, so a list of two hundred documents held two hundred
  caches and decoded each image once per row. They now share `BitmapEmbedRenderer.Shared`.
- The image cache leaked at its limit, returning a bitmap it had not stored, which nothing owned
  and nothing disposed.
- `H1`, `Quote` and `Code` could not be turned off, because the toolbar's `ToggleButton`s were bound
  to a command that could only set.
- An image refused by `IImageStore` — too large, wrong type — raised nothing, so a pasted
  screenshot vanished silently. `RichTextView.ImageRefused` now says so.
- The font and size boxes are pickers you can also type into, rather than boxes requiring an exact
  typed name.

### Notes

- Targets **.NET 10** and **Avalonia 12.1.1**, single-target on both. Avalonia 12.1.1 is a hard
  floor: `TextRunCache` does not exist in 12.0.x.
- The design lives in [`docs/architecture.md`](docs/architecture.md), the usage guide in
  [`docs/guide.md`](docs/guide.md), and the measurements that shaped the design in
  [`docs/increment-0-findings.md`](docs/increment-0-findings.md).
