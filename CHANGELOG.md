# Changelog

All notable changes are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

VellumText is pre-1.0, so the public API is still allowed to move. Nothing is on
NuGet; packages are published to a local feed, and
[`docs/consuming.md`](docs/consuming.md) explains how to reference them.

## [Unreleased]

### Changed

- **`RtfImporter.Import` takes `RtfImportOptions` rather than `HtmlImportOptions` — this is a
  breaking change.** RTF is read by way of HTML internally, and that had leaked into the RTF
  package's public API: a consumer who wanted only RTF had to reference the HTML package to
  configure it, and an internal choice was frozen where a later rewrite of the RTF reader could not
  have changed it. `RtfImportOptions` carries `AllowRemoteImages`, `AllowDataImages`,
  `MaxDataImageBytes` and `BaseUri`. It deliberately omits `MaxDepth` and `PreserveIdentifiers`,
  which apply to authored HTML rather than the HTML this package generates for itself.

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
