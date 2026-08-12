# Changelog

All notable changes are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

Vellum is pre-1.0 and nothing is released yet, so the public API is still
allowed to move.

## [Unreleased]

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

### Notes

- Targets **.NET 10** and **Avalonia 12.1.1**, single-target on both.
- The design lives in [`docs/architecture.md`](docs/architecture.md), and the
  measurements that shaped it in
  [`docs/increment-0-findings.md`](docs/increment-0-findings.md).
