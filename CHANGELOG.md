# Changelog

All notable changes are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

Vellum is pre-1.0, so the public API is still allowed to move. Nothing is on
NuGet; packages are published to a local feed, and
[`docs/consuming.md`](docs/consuming.md) explains how to reference them.

## [Unreleased]

Nothing yet.

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

- **`Vellum.Core`** — the headless document model. An immutable node tree with
  paragraphs, lists, rules, embeds and tables; a flat integer position scheme;
  replace, mark and block-attribute steps that map positions through an edit;
  text, node and cell selections; transactions; and undo history with
  coalescing. No dependencies at all, enforced by a build target.
- **`Vellum.Avalonia`** — the editing surface. A `RichTextEditor` control over
  a block-reconciling view, built on Avalonia's own text stack: bidi-aware
  selection geometry, grapheme-correct caret movement, inline embeds via
  `DrawableTextRun`, and IME composition.
- Multi-block editing: cross-block caret movement and selection, Enter to
  split, Backspace and Delete to join, headings, blockquotes, code blocks,
  alignment, indent, and bulleted and numbered lists.
- **`Vellum.Interop.Html`** and **`Vellum.Interop.Rtf`** — import and export both ways. HTML import
  sanitizes; both report what they could not keep rather than dropping it silently.
- **`Vellum.Interop.Json`** — lossless, schema-versioned JSON, for storing documents rather
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
