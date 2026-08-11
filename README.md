# Vellum

A free, MIT-licensed **rich text editor control for [Avalonia UI](https://avaloniaui.net)**.

> **Status: design phase.** No code yet — see [`docs/architecture.md`](docs/architecture.md) for the full architecture and implementation plan.

Avalonia's own [Rich Text Editor](https://avaloniaui.net/rich-text-editor) is a commercial Accelerate component. The open-source alternatives are either a *code* editor ([AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)) or unmaintained. Vellum aims to be a genuinely good, permissively-licensed WYSIWYG editor built on Avalonia's own text stack.

**Vellum is an independent community project. It is not affiliated with or endorsed by Avalonia UI** — hence the name, deliberately not `Avalonia.*`.

## Why this is feasible

Avalonia already ships the hard parts under MIT: HarfBuzz shaping, Unicode line breaking, bidi reordering, font fallback, grapheme-aware caret movement (`TextLine.GetBackspaceCaretCharacterHit`), bidi-aware selection geometry (`TextLine.GetTextBounds`), inline drawable content (`DrawableTextRun`), and IME integration (`TextInputMethodClient`).

Vellum is therefore not a text engine. It is a **document model, a transaction system, and an editing surface** on top of one.

## Planned packages

| Package | Depends on |
|---|---|
| `Vellum.Core` | `Avalonia.Base` only — model, positions, transactions, undo. Headlessly testable. |
| `Vellum.Avalonia` | The `RichTextEditor` control, block views, caret/selection, IME, themes. |
| `Vellum.Interop.Html` | AngleSharp (MIT) |
| `Vellum.Interop.Markdown` | Markdig (BSD-2) |
| `Vellum.Interop.Rtf` | none |
| `Vellum.Interop.Docx` | DocumentFormat.OpenXml (MIT) |

## Scope

**In v1.0:** character & block formatting, lists, links, inline images, **tables**, undo/redo, clipboard interop with Word/Google Docs/browsers, HTML/Markdown/RTF/DOCX import-export, IME, bidi, light/dark themes, virtualization for large documents.

**Not in scope:** a code editor (use AvaloniaEdit), an HTML rendering engine, byte-fidelity Word round-tripping, collaborative editing (though the transaction model leaves room for it).

## Known limitation

Avalonia exposes no UIA `ITextProvider` / `ITextRangeProvider`, so no Avalonia control — including this one — can give screen readers text ranges, run attributes, or caret-move events. Vellum will ship `IValueProvider` and pursue an upstream contribution. See risk R1 in the architecture doc.

## License

MIT — see [LICENSE](LICENSE).
