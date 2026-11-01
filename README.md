# VellumText

A free, MIT-licensed **rich text editor control for [Avalonia UI](https://avaloniaui.net)**.

> **VellumText 0.9** provides an Avalonia rich-text editor, a dependency-free document model, and HTML, RTF, Markdown and JSON interchange packages. This is a pre-1.0 release: the public API can still change. See the [user guide](docs/guide.md), [package consumption guide](docs/consuming.md), and [architecture](docs/architecture.md).

Install from the `.nupkg` assets in the
[0.9.0 GitHub release](https://github.com/lurebat/VellumText/releases/tag/v0.9.0),
using a local NuGet feed. This release is not distributed through nuget.org.

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
| `VellumText.Interop.Rtf` | OfficeIMO (MIT) for import; own writer for export | working |
| `VellumText.Interop.Json` | **nothing** — lossless, versioned, for storage | working |
| `VellumText.Interop.Markdown` | Markdig (BSD-2) — CommonMark + GFM pipe tables, both ways | working |
| `VellumText.Interop.Docx` | DocumentFormat.OpenXml (MIT) | v2.0 |

`VellumText.Core` takes no dependency on `Avalonia.Base` either. It owns the two
primitives it would otherwise have borrowed (`Rgba` and `ValueSpan<T>`), and a
build target fails the build if a dependency is ever added.

## Requirements

- **.NET 10**
- **Avalonia 12.1.4**

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
