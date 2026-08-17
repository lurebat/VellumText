# VellumText

A free, MIT-licensed **rich text editor control for [Avalonia UI](https://avaloniaui.net)**.

> **Status: in development, pre-release.** The document model and the editing surface work and are covered by tests; the interop packages are being built out. Nothing is published to NuGet yet. See the [user guide](docs/guide.md) to use it, [`docs/consuming.md`](docs/consuming.md) to reference it before it is published, and [`docs/architecture.md`](docs/architecture.md) for the design and implementation plan.

Avalonia's own [Rich Text Editor](https://avaloniaui.net/rich-text-editor) is a commercial Accelerate component. The open-source alternatives are either a *code* editor ([AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)) or unmaintained. VellumText aims to be a genuinely good, permissively-licensed WYSIWYG editor built on Avalonia's own text stack.

**VellumText is an independent community project. It is not affiliated with or endorsed by Avalonia UI** — hence the name, deliberately not `Avalonia.*`.

## Why this is feasible

Avalonia already ships the hard parts under MIT: HarfBuzz shaping, Unicode line breaking, bidi reordering, font fallback, grapheme-aware caret movement (`TextLine.GetBackspaceCaretCharacterHit`), bidi-aware selection geometry (`TextLine.GetTextBounds`), inline drawable content (`DrawableTextRun`), and IME integration (`TextInputMethodClient`).

VellumText is therefore not a text engine. It is a **document model, a transaction system, and an editing surface** on top of one.

## Planned packages

| Package | Depends on | State |
|---|---|---|
| `VellumText.Core` | **nothing at all** — model, positions, transactions, undo. Headlessly testable. | working |
| `VellumText.Avalonia` | `Avalonia` — the `RichTextEditor` control, block views, caret/selection, IME, themes. | working |
| `VellumText.Interop.Html` | AngleSharp (MIT) — import, export, sanitizing | working |
| `VellumText.Interop.Rtf` | RtfPipe (MIT) — import and export | working |
| `VellumText.Interop.Json` | **nothing** — lossless, versioned, for storage | working |
| `VellumText.Interop.Markdown` | Markdig (BSD-2) | planned |
| `VellumText.Interop.Docx` | DocumentFormat.OpenXml (MIT) | v2.0 |

`VellumText.Core` takes no dependency on `Avalonia.Base` either. It owns the two
primitives it would otherwise have borrowed (`Rgba` and `ValueSpan<T>`), and a
build target fails the build if a dependency is ever added.

## Requirements

- **.NET 10**
- **Avalonia 12.1.1**

Both are single-target on purpose: no multi-targeting, no version shims.

## Building it

```
dotnet build
dotnet test
```

The Avalonia tests run headlessly, so nothing needs a display server.
[`CONTRIBUTING.md`](CONTRIBUTING.md) has the rest.

## Scope

**In v1.0:** character & block formatting, lists, links, inline images, **tables**, undo/redo, clipboard interop with Word/Google Docs/browsers, HTML/Markdown/RTF/DOCX import-export, IME, bidi, light/dark themes, virtualization for large documents.

**Not in scope:** a code editor (use AvaloniaEdit), an HTML rendering engine, byte-fidelity Word round-tripping, collaborative editing (though the transaction model leaves room for it).

## Known limitation

Avalonia exposes no UIA `ITextProvider` / `ITextRangeProvider`, so no Avalonia control — including this one — can give screen readers text ranges, run attributes, or caret-move events. VellumText will ship `IValueProvider` and pursue an upstream contribution. See risk R1 in the architecture doc.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Security issues go through
[private reporting](SECURITY.md), not public issues — VellumText imports untrusted
documents, so it has a real threat model.

## License

MIT — see [LICENSE](LICENSE).
