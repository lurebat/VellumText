# Vellum user guide

A rich text editor control for [Avalonia](https://avaloniaui.net), and the document model behind
it. This guide covers using it; [`architecture.md`](architecture.md) covers why it is built the way
it is.

- [Packages](#packages)
- [Quick start](#quick-start)
- [The document model](#the-document-model)
  - [Text offsets](#text-offsets)
- [Editing](#editing)
- [The toolbar](#the-toolbar)
- [A read-only viewer](#a-read-only-viewer)
- [Import and export](#import-and-export)
- [The clipboard](#the-clipboard)
- [Images](#images)
- [Syntax highlighting](#syntax-highlighting)
- [Find and replace](#find-and-replace)
- [Tables](#tables)
- [Undo](#undo)
- [Options](#options)
- [Theming](#theming)
- [Recipe: a clipboard manager](#recipe-a-clipboard-manager)

## Packages

| Package | What it is | Depends on |
|---|---|---|
| `Vellum.Core` | The document model. Nodes, positions, selections, transactions, history. Headless. | Nothing at all |
| `Vellum.Avalonia` | The editor and viewer controls. | Avalonia, `Vellum.Core` |
| `Vellum.Interop.Html` | HTML import and export, with a sanitizer. | `Vellum.Core` |
| `Vellum.Interop.Rtf` | RTF import and export. | `Vellum.Interop.Html` |
| `Vellum.Interop.Json` | Lossless JSON, for storing documents. | `Vellum.Core` |

`Vellum.Avalonia` deliberately does not reference the interop packages, so an application that
wants a text editor does not acquire an HTML parser to get one. You add the formats you want; see
[import and export](#import-and-export).

Targets .NET 10 and Avalonia 12.1.1.

## Quick start

Add the theme to `App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui" ...>
  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Vellum.Avalonia/Themes/Vellum.axaml" />
  </Application.Styles>
</Application>
```

Then put an editor in a window:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:v="clr-namespace:Vellum.Avalonia;assembly=Vellum.Avalonia">
  <v:RichTextEditor x:Name="Editor" />
</Window>
```

That is a working editor: typing, selection, undo, bold and italic, headings, lists, tables,
images, find, bidi text and IME all work with no further setup.

`RichTextEditor` is the shell — toolbar, scrolling, chrome. `RichTextView` is the editing surface
inside it, and is where the commands live:

```csharp
Editor.View!.ToggleStyle(TextStyle.Bold);
```

`View` is null until the control is templated, so reach it from `Loaded` rather than a constructor.

## The document model

A document is an immutable tree. Nothing is edited in place; every change produces a new tree that
shares everything the edit did not touch.

```csharp
var doc = new DocumentNode([
    new ParagraphNode(InlineContent.FromText("A heading"), ParagraphKind.Heading1),
    new ParagraphNode(InlineContent.Create(
        "Some bold text.",
        [new ValueSpan<MarkSet>(5, 4, new MarkSet { Style = TextStyle.Bold })],
        [])),
]);

Editor.State = EditorState.Create(doc);
```

The block types are `ParagraphNode` (which covers body text, `Heading1`–`Heading6`, `Quote` and
`Code` through its `Kind`), `ListNode` and `ListItemNode`, `TableNode` / `TableRowNode` /
`TableCellNode`, `RuleNode` and `BlockImageNode`.

Character formatting is a `MarkSet` — style flags, font family, size, foreground, highlight, link —
applied over a range by a `ValueSpan<MarkSet>`. Inline images live in `InlineContent.Embeds`, each
standing at one `\uFFFC` placeholder in the text.

Nodes have **value equality**, which is worth knowing: two documents built separately from the same
content compare equal and hash the same.

```csharp
DocumentText.Of(doc);                 // plain text, for search or a label
DocumentPreview.Truncate(doc, 200);   // the first 200 characters, formatting intact
DocumentPreview.IsLongerThan(doc, 200);
```

`Truncate` cuts on grapheme boundaries, so an emoji or a combining mark is kept whole or dropped
whole rather than halved into replacement glyphs.

### Text offsets

`DocumentText.Of` is for *reading* — search, a label, a screen reader. **It cannot be inverted**,
because an embed contributes its alt text: an image with `alt="PHOTO"` is one position and five
characters, and one without alt text is one position and none. A text offset can land inside an
image's alt text, where no document position exists.

For code that works in plain-text offsets and has to get back to the document — a transformation
applied to a selected range, an offset stored before an edit — use `DocumentTextMap`, which
projects one `\uFFFC` per embed so the two stay in step:

```csharp
var map = DocumentTextMap.Of(doc);

var (offset, length) = map.ToOffsets(selection.From, selection.To);
var transformed = MyTransform(map.Text.Substring(offset, length));

var (from, to) = map.ToPositions(offset, length);
transaction.Step(new ReplaceStep(from, to, Slice.OfLines(transformed)));
```

Note the change of convention, which matches what each side expects: text runs are an offset and a
**length**, document ranges are a **pair of positions** — which is what `ReplaceStep` takes.

Going through positions rather than splicing `map.Text` and rebuilding is the point: **formatting
outside the range survives**, because a replace only touches what it spans.

**Use `Slice.OfLines`, not `Slice.OfText`**, whenever the replacement might contain newlines —
pretty-printed JSON, sorted lines, rewrapped prose. `OfText` puts the whole string in one block, so
those newlines become characters nothing will ever render as a break. `OfLines` completes the
projection: a block boundary became `\n` on the way out, and `\n` becomes a block boundary on the
way back in.

The block the range **starts** in keeps its kind, because the first line merges into it rather than
replacing it — so transforming part of a heading leaves a heading, and any further blocks are body
text. Only `\n` divides; a `\r` stays as text, exactly as it does when read back out, so CRLF
survives a round trip. Normalise first if your text uses CRLF without meaning it.

Two cases to decide about rather than discover:

```csharp
map.ContainsEmbeds(from, to);   // check before transforming
```

A transformation takes text and returns text, so an image reaches it as a bare placeholder standing
for something it knows nothing about — and comes back still carrying that placeholder with no embed
attached. `Slice.OfText` refuses such text, correctly but from a long way away. Either skip those
ranges or accept that replacing one discards the image.

The map is a snapshot. Edit the document and build a new one.

## Editing

Every command is a method on `RichTextView`, returns `bool` for "did anything change", and is one
undo step.

```csharp
var view = Editor.View!;

view.ToggleStyle(TextStyle.Bold);            // also Italic, Underline, Strikethrough, Sub, Super, Code
view.SetForeground(new Rgba(200, 0, 0, 255));
view.SetHighlight(null);                     // null clears
view.SetFontFamily("Georgia");
view.SetFontSize(13.5);
view.SetLink(new LinkMark("https://example.com"));
view.ClearFormatting();

view.ToggleParagraphKind(ParagraphKind.Heading1);   // pressing it again returns to body text
view.SetParagraphKind(ParagraphKind.Quote);         // sets without toggling
view.SetAlign(TextAlign.Center);
view.Indent();
view.Outdent();
view.ToggleList(ListKind.Unordered);

view.Undo();
view.Redo();
```

Use `ToggleParagraphKind` behind a button that shows a lit state and `SetParagraphKind` behind a
drop-down, where choosing the current entry again should not clear it.

To ask what the selection currently is — for a toolbar, say — read the state rather than tracking
what was last clicked. A property is only reported when *every* character of the selection agrees,
so a half-bold selection reports not bold rather than picking one:

```csharp
view.SelectionMarks.Style.HasFlag(TextStyle.Bold);
view.ParagraphKindAt;   // null when the selection spans two kinds
view.AlignAt;
view.ListKindAt;
```

The keyboard shortcuts are the usual ones: Ctrl+B/I/U, Ctrl+Shift+X, Ctrl+. and Ctrl+, for
super/subscript, Ctrl+0–6 for paragraph kinds, Ctrl+L/E/R/J for alignment, Ctrl+Shift+7/8 for
lists, Tab and Shift+Tab for indent and list nesting, Ctrl+Z/Y, Ctrl+F.

## The toolbar

`RichTextEditor` includes one. To place your own, bind a `RichTextToolbar` to the editor:

```xml
<StackPanel>
  <v:RichTextToolbar Editor="{Binding #Editor}" />
  <v:RichTextEditor x:Name="Editor" />
</StackPanel>
```

The control holds no buttons of its own — it exposes the state a toolbar shows and the commands it
runs, and leaves the arrangement to the template, so a different toolbar is different markup rather
than a different toolbar. Its commands never throw and are always enabled: one that cannot apply
does nothing, because a bar that greys out half its buttons whenever the caret moves reads as
broken.

`RichTextToolbar.FontFamilies`, `FontSizes` and `Palette` are exposed for binding your own pickers.

The editor also shows a floating toolbar over a selection; turn it off with
`IsSelectionToolbarEnabled="False"`.

## A read-only viewer

`RichTextViewer` renders a document with no caret, no input and no history — a fraction of the
editor's cost, which matters when a list shows many of them.

```xml
<v:RichTextViewer Document="{Binding Doc}" />
```

Measured at roughly **0.6 ms per row** for a short document — not meaningfully worse than a
`TextBlock` — so a virtualised list of them is fine. Two things matter more than the control
choice:

- **Truncate before binding.** The cost is per *paragraph laid out*, so a hundred-page document in
  a two-line row costs a hundred pages. `DocumentPreview.Truncate(doc, 200)` is the fix.
- Every presenter shares `BitmapEmbedRenderer.Shared` by default, so a list of two hundred
  documents decodes each image once between them. Only override `Embeds` per control if you mean
  to.

## Import and export

Register the formats you want, and copy and paste start carrying them:

```csharp
view.Formats.Add(HtmlFormat.Instance);
view.Formats.Add(RtfFormat.Instance);
```

Or use them directly:

```csharp
ImportResult result = HtmlFormat.Instance.Import(html);

if (result.AnythingDropped)
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine(diagnostic);   // severity, message, and the construct it happened to
    }
}

string html = HtmlFormat.Instance.Export(result.Doc);
string json = JsonFormat.Instance.Export(result.Doc);
```

**Importers never throw.** They see input nobody wrote for them — a fragment from Word, a file from
1998 — so they produce the best document they can and report, item by item, where they gave ground.
HTML import also sanitizes: script, event handlers and dangerous URLs are removed rather than
downgraded.

Use **JSON to store** documents and HTML or RTF to **exchange** them. JSON is lossless — a document
written and read back is equal to the one written — and it is the only format that carries a schema
version, so a reader refuses a file from a newer version outright rather than silently dropping the
parts it does not understand.

## The clipboard

`CopyAsync`, `CutAsync` and `PasteAsync` on the view handle the editor's own clipboard use. For an
application that captures or stores clipboard payloads itself, use `RichTextClipboard`:

```csharp
await RichTextClipboard.WriteAsync(clipboard, doc, [HtmlFormat.Instance, RtfFormat.Instance]);

ImportResult? read = await RichTextClipboard.ReadAsync(
    clipboard, [HtmlFormat.Instance, RtfFormat.Instance]);
```

Formats are tried richest first regardless of the order you pass them, because copying from a word
processor puts RTF, HTML and plain text on the clipboard at once and taking whichever answers first
would quietly keep the poorest of the three.

To hold payloads yourself and parse them later:

```csharp
foreach (var flavour in RichTextClipboard.Flavors)
{
    // flavour.Format ("html"), flavour.DataFormat (to read/write), flavour.Encoding
}

byte[]? payload = RichTextClipboard.Encode(HtmlFormat.Instance, doc);
ImportResult? doc = RichTextClipboard.Decode(HtmlFormat.Instance, payload);
```

> **Read native clipboard formats as bytes, never as a string.** Asking a clipboard backend for a
> native format as text lets it reinterpret the bytes with whatever encoding it guesses, and its
> guess for RTF produces convincing mojibake that then parses to an empty document — a failure that
> looks like an unsupported payload rather than a decoding bug. `RichTextClipboard` has no method
> anywhere that takes a payload as a `string`, so this mistake cannot be written through it.

`RichTextClipboard.Read` is the synchronous overload, for a drop's `DragEventArgs.DataTransfer`.

## Images

Images work out of the box. `ImageEmbed.Source` holds a *reference* rather than pixels, and the
default `BitmapEmbedRenderer` resolves it.

**By default it draws only pixels carried inside the document** — `data:` URLs, and nothing else.
A document is untrusted input, since it can arrive by paste from any application, so every source
it names is an address chosen by that content. Fetching `http:` would make the machine issue a
request to an address the document picked; opening `file:` or a path would read a local file the
user never pointed at and put it on screen.

Concretely, from a clipboard manager built on this:

> Previewing an HTML clip fetches whatever the document names. Copy a marketing email, click it in
> your history, and its tracking pixels fire — the sender learns when you looked at your own
> clipboard, from a tool whose entire job is holding things you copied.

**Note the shape of the guarantee, because it is the design and not just the default: there is no
setting that turns network fetching on.** `AllowLocalSources` turns on *local* resolution —
`avares:`, `file:` and paths — for hosts whose documents come from somewhere they trust. Network
sources are refused at every setting. A knob that could be flipped would eventually be flipped by
someone who just needed one image, and the guarantee would be gone for every document afterwards.

```csharp
// For documents from somewhere you trust - your own store, your own assets.
view.Embeds = new BitmapEmbedRenderer { AllowLocalSources = true };
```

Network sources are never resolved, at any setting. Fetch them under your own policy and hand over
the bytes.

Pasted and dropped images are stored through `IImageStore`, which defaults to a
`DataUriImageStore` — self-contained, and the only implementation that needs no cooperation from
the host. Replace it to write elsewhere, and configure the renderer to resolve what it returns:

```csharp
view.ImageStore = new DataUriImageStore(maximumBytes: 1024 * 1024);
view.InsertImage(bytes, "image/png");
```

A store that refuses an image leaves the document unchanged rather than inserting one that cannot
be drawn, and raises `ImageRefused` so you can say so — a pasted screenshot vanishing with no
message is indistinguishable from an editor that ignores Ctrl+V:

```csharp
view.ImageRefused += (_, e) => Toast($"That image is too large ({e.ByteCount / 1024} KB).");
```

Users can select an image by clicking it and resize it by dragging a corner.

## Syntax highlighting

Code blocks can be coloured. Set a highlighter and every `ParagraphKind.Code` block in that view is
tokenized and coloured:

```csharp
view.SyntaxHighlighter = JsonHighlighter.Instance;
view.SyntaxTheme = SyntaxTheme.Light;   // or Dark, the default
```

**Off by default**, because a highlighter is a claim about what language the code is in and nothing
in a document says. A host that knows — because it just pretty-printed the JSON, or because the
user picked a language — sets it.

**Highlighting is presentation and never reaches the document.** It is applied while building the
layout and discarded with it, so exports, equality and the undo stack are untouched, and changing
theme is a redraw rather than an edit. A colour already on the text wins over one the tokenizer
chose.

`JsonHighlighter` ships in the box. To add a language, implement `ISyntaxHighlighter`:

```csharp
public interface ISyntaxHighlighter
{
    string Language { get; }
    ImmutableArray<ValueSpan<TokenKind>> Tokenize(string text);
}
```

It returns *kinds*, not colours — a kind is a fact about the text and a colour is a decision about a
theme, so one tokenizer serves both palettes. It is called **one paragraph at a time**, which is
exact for languages whose tokens cannot span a line (JSON) and an approximation for those where
they can — a C block comment opened on one line and closed on the next will not be understood.
Carrying state between lines is not currently supported.

A highlighter must not throw; one that does costs its own colour and nothing else.

## Find and replace

```csharp
view.Find("needle", new SearchOptions { MatchCase = true, WholeWord = false });
view.FindNext();
view.FindPrevious();
view.ReplaceCurrent("thread");
view.ReplaceAll("thread");
view.ClearFind();
```

The view reports `MatchCount`, `CurrentMatch` (zero-based, `-1` for none) and `Matches`. The editor
exposes `MatchCount` and `CurrentMatchNumber`, which is one-based, for a "3 of 17" label.
`ReplaceAll` is one undo step. `DocumentSearch.Find` does the same over a document with no control
involved.

## Tables

Tables render as real tables, with row and column spans, header cells and cell backgrounds.
Dragging across cells makes a **rectangular** selection, which follows Word's rules: delete and cut
empty the selected cells rather than removing them; marks apply to each cell independently; copy
yields a table of just those cells; typing, paste and structural commands are refused.

A rectangular selection is a `CellSelection` on the state:

```csharp
var rectangle = view.State.Selection as CellSelection;
```

## Undo

History is unbounded by default and coalesces typing into sensible steps.

```csharp
Editor.UndoLimit = 100;   // -1 for unlimited
Editor.CanUndo;
```

`History` and `UndoLimit` are two views of one value and stay in step.

**Loading a different document discards the history automatically.** It has to: the undo stack
describes edits to a document that is no longer there, so replaying it against the new one at best
restores text from somewhere else and at worst throws, because the positions it recorded need not
exist. An application that arrows through a list of documents would otherwise reach that by
pressing one key. Assigning a state that carries the *same* document — which is how selection is
set from outside — keeps the history, and so does editing.

## Options

| Property | Default | Notes |
|---|---|---|
| `IsReadOnly` | `false` | Matches `TextBox`. The caret stays visible; `CanUndo`/`CanRedo` gate on it |
| `AcceptsReturn` | `true` | Diverges from `TextBox`, deliberately — a document editor that rejects Enter is absurd |
| `AcceptsTab` | `true` | Tab indents and nests. Off, it moves focus |
| `UndoLimit` | `-1` | Unlimited |
| `AcceptsDrop` | `true` | On the view. A read-only view refuses regardless |
| `IsSelectionToolbarEnabled` | `true` | The floating toolbar over a selection |

`IsReadOnly` is enforced at one place — the point where a transaction becomes state — so it covers
typing, paste, drop, IME, every toolbar command and anything added later. Setting `State` directly
still works, which is how a host loads a document into a read-only view.

## Theming

The theme ships light and dark resources and follows the application's `RequestedThemeVariant`.
Everything is a normal Avalonia resource, so override what you want:

Colours that belong to the editing surface are properties on the control, so set them where you set
any other:

```xml
<v:RichTextEditor SelectionBrush="#553060C0"
                  CaretBrush="#E0E0E0"
                  FindHighlightBrush="#55C0A020"
                  CurrentFindHighlightBrush="#AAC08020"
                  TableBorderBrush="#404040" />
```

The full set is `SelectionBrush`, `CaretBrush`, `FindHighlightBrush`,
`CurrentFindHighlightBrush`, `TableBorderBrush`, `TableHeaderBrush`, `ImageHandleBrush`,
`ImageHandleBorderBrush` and `TouchHandleBrush`.

The toolbar reads its colours from resources you can override:

```xml
<Application.Resources>
  <SolidColorBrush x:Key="VellumToolbarAccent" Color="#3060C0" />
</Application.Resources>
```

The others are `VellumToolbarBackground`, `VellumToolbarForeground`, `VellumToolbarBorderBrush`,
`VellumToolbarHover` and `VellumToolbarAccentForeground`.

`RichTextEditor` and `RichTextToolbar` are templated controls; replace their `ControlTemplate` to
rearrange them entirely. Block-level presentation is a `IParagraphStyleResolver`
(`view.ParagraphStyles`) and character-level is an `ITextStyleResolver` (`view.TextStyles`); left
alone, both derive from the control's inherited text properties, so a document inside a themed
panel picks up that theme's font.

## Recipe: a clipboard manager

The case Vellum was shaped around. Capture whatever the clipboard holds, show a list of previews,
restore one on demand.

```csharp
// Capture. Keep the original payload as well as the parsed document: if a future version parses
// it better, you still have the bytes.
var result = await RichTextClipboard.ReadAsync(clipboard, [HtmlFormat.Instance, RtfFormat.Instance]);

if (result is not null)
{
    var entry = new Entry
    {
        Stored  = JsonFormat.Instance.Export(result.Doc),   // lossless, versioned
        Preview = DocumentPreview.Truncate(result.Doc, 200),
        Label   = DocumentText.Of(result.Doc),
        Lossy   = result.AnythingDropped,
    };
}

// Show. RichTextViewer in the row template, bound to the truncated preview.

// Restore.
await RichTextClipboard.WriteAsync(
    clipboard,
    JsonFormat.Instance.Import(entry.Stored).Doc,
    [HtmlFormat.Instance, RtfFormat.Instance]);
```

Deduplicating is `Equals`, because nodes have value equality — two captures of the same snippet
compare equal and hash the same, so a `HashSet<DocumentNode>` or a dictionary key works directly.

**Plain text needs its own route.** `RichTextClipboard.ReadAsync`'s text fallback calls
`ReplaceLineEndings("\n")`, which is right for pasting into an editor and wrong for an archive —
CRLF is data. Split it yourself and keep the original bytes as the source of truth:

```csharp
var doc = DocumentNode.FromParagraphs(captured.Split('\n'));
```

That round-trips exactly through `DocumentText.Of`, including CRLF, tabs, blank runs and trailing
newlines.

**Applying a transformation to the selection** goes through `DocumentTextMap` — see
[text offsets](#text-offsets). The short version:

```csharp
var map = DocumentTextMap.Of(view.State.Doc);
var (offset, length) = map.ToOffsets(selection.From, selection.To);

if (map.ContainsEmbeds(selection.From, selection.To))
{
    return;   // or accept that replacing this range drops the image
}

var (from, to) = map.ToPositions(offset, length);
var transaction = view.State.Transaction();

transaction.Step(new ReplaceStep(from, to, Slice.OfLines(Transform(map.Text.Substring(offset, length)))));
view.Apply(transaction);
```
