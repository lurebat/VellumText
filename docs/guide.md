# VellumText user guide

A rich text editor control for [Avalonia](https://avaloniaui.net), and the document model behind
it. This guide covers using it; [`architecture.md`](architecture.md) covers why it is built the way
it is.

- [Packages](#packages)
- [Quick start](#quick-start)
- [The document model](#the-document-model)
  - [Text offsets](#text-offsets)
- [Editing](#editing)
- [The toolbar](#the-toolbar)
- [Which control for a preview pane](#which-control-for-a-preview-pane)
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
| `VellumText.Core` | The document model. Nodes, positions, selections, transactions, history. Headless. | Nothing at all |
| `VellumText.Avalonia` | The editor and viewer controls. | Avalonia, `VellumText.Core` |
| `VellumText.Interop.Html` | HTML import and export, with a sanitizer. | `VellumText.Core` |
| `VellumText.Interop.Rtf` | RTF import and export. | `VellumText.Interop.Html` |
| `VellumText.Interop.Json` | Lossless JSON, for storing documents. | `VellumText.Core` |
| `VellumText.Interop.Markdown` | Markdown (CommonMark plus GFM pipe tables) import and export. | `VellumText.Interop.Html`, Markdig |

`VellumText.Avalonia` deliberately does not reference the interop packages, so an application that
wants a text editor does not acquire an HTML parser to get one. You add the formats you want; see
[import and export](#import-and-export).

Targets .NET 10 and Avalonia 12.1.1.

## Quick start

Add the theme to `App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui" ...>
  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://VellumText.Avalonia/Themes/VellumText.axaml" />
  </Application.Styles>
</Application>
```

Then put an editor in a window:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:v="clr-namespace:VellumText.Avalonia;assembly=VellumText.Avalonia">
  <v:RichTextEditor x:Name="Editor" />
</Window>
```

That is a working editor: typing, selection, undo, bold and italic, headings, lists, tables,
images, find, bidi text and IME all work with no further setup.

`RichTextEditor` is the shell — toolbar, scrolling, chrome. `RichTextView` is the editing surface
inside it, and is where the commands live:

```csharp
Editor.EnsureView().ToggleStyle(TextStyle.Bold);
```

`Editor.View` is null until the control is templated, so it answers "has this been templated yet?"
rather than "give me the view". `EnsureView()` applies the template if that has not happened and
returns a view that is never null — use it for commands, which run in response to a user action.

Setup does not need it. The surfaces a host configures before the control is on screen are on the
shell directly — `Formats`, `ImageStore`, `Embeds`, `TextStyles`, `ParagraphStyles`,
`SyntaxHighlighter`, `SyntaxTheme`, and the `ContentImporting`, `ContentImported` and `ImageRefused`
events — because
those are the ones where being too early is silent: a format registered on a view that does not
exist throws, and an event subscribed on one is simply never heard from.

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

**The node, embed and selection hierarchies are closed.** `Node`, `BlockNode`, `InlineEmbed` and
`Selection` have `private protected` constructors, so those lists are the whole set. That is
deliberate: the exporters, the JSON writer and the block factory all switch on the concrete type and
have nothing to do with one they do not recognise, so a node you defined would compile and then be
dropped or throw the first time it was rendered or saved. The extension points that *are* open are
the renderers and resolvers — `IEmbedRenderer`, `IImageStore`, `ITextStyleResolver`,
`IParagraphStyleResolver`, `ISyntaxTheme` — and `Step`, where every consumer is generic.

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
var view = Editor.EnsureView();

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
lists, Tab and Shift+Tab for indent, list nesting and moving between table cells, Ctrl+Z/Y,
Ctrl+F.

Selection and entry follow the conventions people bring with them: double-click selects a word,
triple-click selects a paragraph, dragging past the top or bottom edge keeps scrolling, Shift+Enter
inserts a soft break inside the paragraph rather than starting a new one, and Ctrl+Shift+V pastes
as plain text.

Lists behave as in a word processor. Enter in a bullet starts the next bullet, carrying whatever
follows the caret with it, and Enter in an empty bullet leaves the list. Tab nests the bullet under
the one above it and Shift+Tab lifts only the bullet the caret is in, out of the list at the top
level. Backspace at the start of a bullet takes the bullet off and keeps the words; a second
Backspace joins them onto the line above.

Code blocks keep their lines in one block, as editors such as Tiptap and Notion do. Enter in a code
block breaks the line rather than starting a new block. To leave it, press Enter three times at the
end: Enter on an empty last line under another empty line leaves the block for an ordinary
paragraph below and takes the empty lines with it, and one Ctrl+Z puts them back. Ctrl+Enter
(Cmd+Enter on macOS) leaves it from anywhere and keeps every line, and ↓ on the last line of a code
block that nothing follows opens a paragraph below it. Each way out puts the new paragraph directly
after the block, inside the bullet or table cell that holds it. Backspace in an empty code block
turns it into an ordinary paragraph. Text pasted or dropped into a code block stays in it, one line
of code per line of text; rich content of more than one paragraph arrives as its plain text, as it
does in Tiptap.

Enter at the end of a heading starts body text at the default alignment, as in a word processor;
in the middle of a heading it gives two headings, and Backspace at the start of a heading that
opens the document makes it body text. Enter in a quote continues the quote, and Enter on an empty
quote line leaves it: the line becomes body text, splitting the quote around it if it is in the
middle. Backspace at the start of a quote takes the quote off and keeps the words; a second
Backspace joins them onto the line above.

Selected text can be moved by dragging it. Press inside the selection, drag past four pixels and
the selection is picked up; a caret follows the pointer to show where it will land, and releasing
drops it there, selected. Releasing before those four pixels is an ordinary click and collapses the
caret where the press landed. The whole move is one undo step. `view.IsMovingSelection` is true
between the pickup and the drop, for a host that wants to suppress its own chrome while content is
in the air. A read-only view never picks anything up, so a drag inside a selection there is an
ordinary drag-select. Dragging out to another application, into another editor, or with a modifier
held to copy rather than move are all out of scope.

A selected picture moves the same way, picked up from its own box: press on it, drag, and it lands
where the drop caret is, splitting the paragraph it was dropped into. A press beside it on the same
line is an ordinary press, and clicking it without dragging leaves it selected.

Word and paragraph selection use Unicode segmentation (UAX #29), so double-clicking a family emoji
or an accented letter selects the whole cluster rather than half of one.

## Links

The model has always had links and both importers produce them; these are the ways to make and
follow one.

```csharp
view.ApplyLink(new LinkMark("https://example.com", "Example"));   // over the selection
view.RemoveLink();                                                // Ctrl+Shift+K
view.SelectionLink;                                               // the link under the selection, or null
```

Ctrl+K raises `LinkRequested` so you can show your own dialog — it carries the existing link when
the caret is inside one, so the same dialog serves create and edit:

```csharp
view.LinkRequested += async (_, e) =>
{
    var href = await AskUser(e.Link?.Href);   // e.Link is null when creating
    if (href is not null)
        view.ApplyLink(new LinkMark(href));
};
```

Ctrl+clicking a link raises `LinkActivated`. **The library never opens the URL itself** — that is
your decision, and a control that silently launches a browser on a document it was handed is a
security problem rather than a convenience. Validate the scheme before you act on it:

```csharp
view.LinkActivated += (_, e) =>
{
    if (Uri.TryCreate(e.Link.Href, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
    {
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
};
```

Hovering a link shows a hand cursor and a tooltip with its target, so it is discoverable without
clicking.

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
than a different toolbar. Its commands never throw and are enabled whenever the editor accepts
edits: one that cannot apply does nothing, because a bar that greys out half its buttons whenever the
caret moves reads as broken. On a read-only editor every command is refused, so every command
reports `CanExecute = false`, and raises `CanExecuteChanged` when `IsReadOnly` is turned back off.

`RichTextToolbar.FontFamilies`, `FontSizes` and `Palette` are exposed for binding your own pickers.
Sizes on the toolbar — `FontSizes`, `SelectionFontSize` and `SetFontSizeCommand` — are in points,
as in a word processor; the document model stores device-independent pixels (96 to the inch), so
picking 12 sets 16 and a 20-point heading pasted from Word shows as 20.

The editor also shows a floating toolbar over a selection; turn it off with
`IsSelectionToolbarEnabled="False"`.

A read-only editor never shows it, whatever that flag says — a control that has been told edits are
refused should not advertise them. The flag keeps its own authority in the other direction, so you
can suppress the bar on an editable control too. The same rule applies to the context menu: Cut,
Paste and the table submenu are hidden when `IsReadOnly` is set, and Copy and Select All remain.

## Binding to a view model

Both controls expose the two halves of their state as bindable properties:

| Member | On | What it is |
| --- | --- | --- |
| `Document` | editor, view | The document as it now is. |
| `Selection` | editor, view | What is selected — a text range or a rectangle of cells. |
| `DocumentChanged` | editor, view | Raised after the document became a different document. |
| `SelectionChanged` | editor, view | Raised after the selection moved or changed shape. |

`Document` and `Selection` are **one-way out**. Their default binding mode is `OneWayToSource`, so
this is all a view model needs to follow along:

```xml
<v:RichTextEditor Document="{Binding Doc}" Selection="{Binding Caret}" />
```

Assigning them is refused with an `ArgumentException` rather than accepted and dropped. To *load* a
document, assign `State` — which announces itself the same way, so a view model that both loads and
observes hears its own load:

```csharp
Editor.State = EditorState.Create(await LoadAsync(path));
```

Two-way document binding is deliberately not offered. Reconciling a document assigned from outside
against edits in flight, and against a history recorded over a document that is no longer there, has
real hazards; refusing the write says so out loud.

**A binding carries changes, not the value that is already there.** Measured against Avalonia
12.1.1: a `OneWayToSource` binding attached to a control that already holds a document writes
nothing until the next edit, so a view model that only bound would hold `null` through a session in
which the user changed nothing. That is what the two events are for — subscribe, then read the
property:

```csharp
Editor.DocumentChanged += (_, _) => vm.IsDirty = true;
Editor.SelectionChanged += (_, _) => vm.WordCount = Count(Editor.Document, Editor.Selection);
```

They are raised from the shell's own state rather than relayed from the view, so a view model may
subscribe in a constructor — before the template exists — and still hear the first change. Neither
fires for a change that did not happen: pressing Ctrl+B at a collapsed caret arms formatting for the
next character and announces nothing, because no reader of the document could tell.

For formatting **commands**, bind `RichTextToolbar` — it exposes an `ICommand` for every action
(`ToggleStyleCommand`, `SetParagraphKindCommand`, `SetAlignCommand`, `ToggleListCommand`,
`SetForegroundCommand`, `SetHighlightCommand`, `SetFontFamilyCommand`, `SetFontSizeCommand`,
`IndentCommand`, `OutdentCommand`, `ClearFormattingCommand`, `SetLinkCommand`, `UndoCommand`,
`RedoCommand`) and none of them throw.

## Which control for a preview pane

`RichTextViewer` for rendering, `RichTextEditor { IsReadOnly = true }` for a preview a user
interacts with. Both are read-only and neither will change a document.

Reach for the **editor** when the preview is a pane the user works with — a right-hand pane, a
detail view, anything where they will want a context menu, or to select across blocks with the
keyboard. It brings the context menu, `EnsureView()` and every forwarded option with it.

Reach for the **viewer** when the document is a *cell*: a list row, a search result, a hover card —
many on screen at once, each glanced at rather than used. That is what it is built for, and the
saving is real: a `RichTextView` costs 19.7 KiB and 0.050 ms more than a bare control before it
lays out a line, which is roughly half the cost of a laid-out clipboard-sized document. Across a
list of two hundred rows that is the difference worth having.

The viewer supports selection and copy, so it is not the "cannot take anything from it" control it
once was. What it does not have is a context menu, an editing surface, or any of the editor's
options — and if you find yourself wanting those, you wanted the editor.

## A read-only viewer

`RichTextViewer` renders a document with no caret, no input and no history — a fraction of the
editor's cost, which matters when a list shows many of them.

```xml
<v:RichTextViewer Document="{Binding Doc}" />
```

Text in it **is** selectable and copyable — drag, double-click a word, triple-click a paragraph,
extend with Shift+arrows, select all with Ctrl+A, copy with Ctrl+C — because "look at this, but you
may not take anything from it" is the wrong behaviour for a reader. It stays genuinely read-only:
there is no edit path in the control at all, so copy is the only thing the clipboard sees.

It takes keyboard focus when clicked but is not a tab stop, so a list of viewers does not become a
long walk through the tab order.

```csharp
viewer.SelectedText();      // what is selected, as plain text
await viewer.CopyAsync();   // same payload the editor writes: document, HTML, RTF and text
viewer.SelectAll();
viewer.ClearSelection();
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

`Formats` is also on `RichTextEditor`, so registration works from a constructor or a binding,
before the control has been templated.

`HtmlFormat.Instance` and `RtfFormat.Instance` import with the paranoid defaults. `With(options)`
returns a configured copy, and registering that copy is how import options reach the importer at
all — the control only ever calls `Import`, so an option that cannot travel through registration
cannot travel:

```csharp
view.Formats.Add(HtmlFormat.Instance.With(new HtmlImportOptions
{
    BaseUri = new Uri("https://example.com/dir/page"),   // relative URLs resolve against this
    AllowRemoteImages = true,                            // off by default: a remote image tracks
}));
```

`With` leaves `Instance` alone, so one configured view does not change what every other one trusts.
`AllowRemoteImages` is the security control here; `BaseUri` is what decides whether a relative image
URL resolves or is dropped.

RTF import takes `RtfImportOptions` — its own type, not the HTML importer's, even though RTF is
read by way of HTML internally. Naming HTML's options here would make a consumer who only wanted
RTF depend on the HTML package to configure it. `MarkdownImportOptions` is its sibling, for the
same reason.

### Markdown

`MarkdownFormat.Instance` reads and writes CommonMark with GitHub-flavoured pipe tables and
`~~strikethrough~~`. Nothing else: every further extension is a dialect, and a dialect read here is
a dialect the library then has to keep reading.

```csharp
view.Formats.Add(MarkdownFormat.Instance);
```

**Raw HTML in the Markdown is removed by default.** CommonMark says to pass it through, which would
make every Markdown document a carrier for every HTML hazard — script, event handlers, `iframe`,
styled overlays — in a format whose whole reputation is for being plain text. Set `AllowRawHtml` to
read it instead; it then goes through the same sanitizer every HTML import goes through, so it is
checked rather than trusted:

```csharp
view.Formats.Add(MarkdownFormat.Instance.With(new MarkdownImportOptions
{
    AllowRawHtml = true,        // off by default: CommonMark passes raw HTML through untouched
    AllowRemoteImages = true,   // off by default: ![](https://…) is a tracking pixel
}));
```

Link destinations go through the same allow-list as HTML — `http`, `https`, `mailto`, `tel` — so
`[click](javascript:alert(1))` keeps its text and loses its destination, and so do `//host/path`
and `\\host\share`, which are relative addresses however much they look absolute.

**Markdown is smaller than the document model**, which no other format here is. Colour, highlight,
font family, font size, underline, subscript, superscript, paragraph alignment, indent, merged
cells and cell backgrounds have no notation at all. `IDocumentExporter` has nowhere to report that,
so the Markdown exporter offers one extra method that does:

```csharp
MarkdownExportResult exported = MarkdownExporter.Instance.ExportWithDiagnostics(doc);

Console.WriteLine(exported.Text);

foreach (var diagnostic in exported.Diagnostics)
{
    Console.WriteLine(diagnostic);   // Downgraded: Markdown cannot highlight text… (highlight)
}
```

`Export` returns exactly the same text; the only difference is that the caller finds out what it
cost. Import reports in the ordinary way, through `ImportResult.Diagnostics`.

Or use them directly:

```csharp
ImportResult result = await HtmlFormat.Instance.ImportAsync(html);

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

Every format also reads and writes **streams**, and that overload — not the string one — is the one
every format implements:

```csharp
using var file = File.OpenRead("document.html");

ImportResult fromFile = await HtmlFormat.Instance.ImportAsync(file);

using var output = File.Create("document.json");

JsonFormat.Instance.Export(output, fromFile.Doc);
```

The stream overloads exist because a format need not be text at all: DOCX and ODT are ZIP
containers, and a contract that only spoke in strings could not express one. A format declares
which it is through `IDocumentFormat.TextEncoding` — an encoding for a text format, `null` for a
binary one. For a binary format `ImportAsync(string)` returns an empty result carrying a `Malformed`
diagnostic and `Export(DocumentNode)` throws `NotSupportedException`, because there is no honest
string to return and `""` would be a document silently lost.

Writing a format of your own means implementing `ImportAsync(Stream, CancellationToken)` and
`Export(Stream, doc)`. If it is text, implement the string overloads too and have each call your
parser directly — `DocumentPayload.ReadAllTextAsync` and `DocumentPayload.WriteAllText` bridge the
two. Never write one overload in terms of the other's default implementation; that recurses forever.

**Import is asynchronous and nothing else** (since 0.8.0). Every importer — `IDocumentImporter`, the
format types and the static `HtmlImporter`, `MarkdownImporter` and `RtfImporter` — returns a
`Task<ImportResult>`, because a large paste takes long enough to freeze whatever waits for it. The
built-in importers parse on a thread of their own and hand back a task at once; the editor goes
further and always runs an importer, built-in or yours, on a background thread, applying the result
on the UI thread only if the document and selection are still where the import started. See
[migrating to 0.8](migration-0.8.md).

**Importers never throw.** They see input nobody wrote for them — a fragment from Word, a file from
1998 — so they produce the best document they can and report, item by item, where they gave ground;
cancellation is the only way the task faults. HTML import also sanitizes: script, event handlers and
dangerous URLs are removed rather than downgraded.

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
    // flavour.Format ("html"), flavour.DataFormat (to read/write)
}

byte[] payload = RichTextClipboard.Encode(HtmlFormat.Instance, doc);
ImportResult read = await RichTextClipboard.DecodeAsync(HtmlFormat.Instance, payload);
```

The payload is bytes the whole way, produced by the exporter itself, so a registered binary format
travels on the clipboard unchanged.

> **Read native clipboard formats as bytes, never as a string.** Asking a clipboard backend for a
> native format as text lets it reinterpret the bytes with whatever encoding it guesses, and its
> guess for RTF produces convincing mojibake that then parses to an empty document — a failure that
> looks like an unsupported payload rather than a decoding bug. `RichTextClipboard` has no method
> anywhere that takes a payload as a `string`, so this mistake cannot be written through it.

`DecodeAsync` runs the importer on a background thread, as the editor's own paste does. A paste and
a drop decide what to insert by the same rule: the richest flavour that imports to anything, unless
the transfer also carries an image and the rich import lost one to the sanitizer (a browser's "Copy
image" is an `<img>` pointing at the network plus the bitmap), and plain text last.

## Inspecting and refusing incoming content

`ContentImporting` runs **before** anything is inserted, and can refuse it:

```csharp
editor.ContentImporting += (_, e) =>
{
    if (DocumentText.Of(e.Content).Length > 100_000 || e.AnythingDropped)
        e.Cancel = true;
};
```

`e.Content` is the document that is about to arrive — what the importer made of the payload, after
every drop and downgrade `e.Diagnostics` describes. Not the bytes on the clipboard: a policy that
vetted those would be vetting something other than what lands.

It covers every way content gets in: paste, paste-as-plain-text, drop, and `PasteImport` called by
the host. Plain text is offered as the paragraphs it will become, with the line breaks already
normalized, so one policy works for both.

- **A refusal is a paste that never happened.** No edit, no undo entry, no `ContentImported`. The
  host is the one refusing, so nothing is reported back to it.
- **It is raised only for content that would otherwise land.** A read-only control, a rectangle
  selection, an empty clip: none of them reach a policy that may be slow.
- **A handler that throws loses the paste, not the document.** Nothing has been applied when it
  runs. On the keyboard, menu and drop paths the exception meets the same handler a failing
  clipboard does; call `PasteImport` yourself and you get your own exception back, because
  swallowing it there would hide your bug from you.
- **Images are not routed through it.** `IImageStore.Save` returning null is already an
  inspect-and-veto for image bytes, and `ImageRefused` already reports it.

`ContentImported` is the other half and stays a notification: it says what an import *cost*, after
it has landed.

```csharp
editor.ContentImported += (_, e) =>
{
    status.Text = e.AnythingDropped ? $"{e.Diagnostics.Length} things changed" : "";
};
```

It is raised for a clean import too, with an empty list — a host that put up "2 things were changed"
after one paste can only take that notice down if the next, clean paste tells it anything at all.

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

### Editing the structure

```csharp
view.InsertRowAbove();
view.InsertRowBelow();
view.DeleteRow();

view.InsertColumnLeft();
view.InsertColumnRight();
view.DeleteColumn();

view.MergeCells();
view.SplitCell();

view.IsInTable;   // whether any of the above has anything to act on
```

Each is one undo step, and each returns `false` when it cannot apply: outside a table, merging
without a rectangle selected, splitting a cell that was never merged, or deleting the last row or
column — refused because removing a table is a different thing from editing one. They are also on
the context menu under **Table**, which appears when the caret is in one.

With a rectangle selected, the row and column commands act on as many rows or columns as it
covers, so selecting three rows and inserting puts three rows in.

**Tab and Shift+Tab move between cells**, and Tab in the last cell adds a row, so a table can be
filled in without reaching for the mouse. Inside a list in a cell, Tab still nests the list — the
list is the more specific thing to be doing, and it would otherwise have no way to nest at all.

Merging keeps every absorbed cell's content, in document order, under the top-left cell's
background and header flag; a cell holding nothing but an empty paragraph contributes nothing, so
merging three empty cells into a full one does not leave three blank lines behind. Splitting
leaves the content where it is, puts empty cells in the freed slots, and keeps the caret exactly
as far into the cell as it was.

Merging a whole rectangle leaves the rows underneath it carrying no cells of their own. That is a
valid table — the merged cell covers them — and it is what both Word and HTML produce for the same
gesture.

To do the same to a document with no control involved, `TableEdits` in `VellumText.Core` returns
the step to put in a transaction, and `TableGrid.CellContaining` finds the cell position it takes:

```csharp
var cell = TableGrid.CellContaining(doc, caret);
var step = TableEdits.InsertRows(doc, cell!.Value, cell.Value, below: true);
```

### Tables inside cells

A cell can hold a table, and every command above then means **the innermost table the caret is
in** — the one you are looking at. Insert a row inside a nested table and the nested table gets it;
delete a row of the outer table and the nested table goes with it.

Two consequences are worth knowing before they surprise you:

- **Tab never leaves a nested table.** In its last cell Tab adds a row to the *nested* table rather
  than stepping out to the outer one, and Shift+Tab in its first cell stays put. This is what Word
  does, and it is the same trade as Tab inside a list in a cell. The arrow keys and the mouse move
  in and out freely.
- **Merging a cell whose content ends in a nested table leaves the caret inside that table**, since
  that is the last place text can sit in the merged cell. The next table command will therefore act
  on the nested table; click in the merged cell's own text first if you meant the outer one.

Tab *into* a cell that holds a table puts the caret in that table's first cell, because there is
nowhere else in the cell for it to go.

### Column widths

**Dragging a column boundary resizes the two columns either side of it.** The pointer turns into a
horizontal resize cursor within four pixels of a boundary, anywhere down the table's height, and a
guide line follows the drag; the document is relaid out once, on release, as a single undo step.
Everything outside those two columns stays where it is, and the table's total width never changes.

A column will not go below **5% of the table**, or a quarter of the two columns' combined width for
a table with so many columns that 5% each is already impossible.

A table nested in a cell has columns of its own, and dragging one of its boundaries resizes that
table, not the one around it. Its boundaries are only grabbable where it is drawn, so the outer
table's boundaries remain reachable everywhere else.

Column widths are **proportions, not pixels**. A table is always laid out to the width it is given,
and `TableNode.ColumnWidths` says how to divide that width up, so `[1, 2]` and `[100, 200]` mean the
same thing. Dragging writes a vector summing to 1. A table with no stated widths divides the space
evenly, and gets a full vector the first time a boundary is dragged.

To set them without a control:

```csharp
var step = TableEdits.SetColumnWidths(doc, tablePos, [0.5, 0.25, 0.25]);
```

It returns `null` when there is no table at that position, when the vector has the wrong number of
entries for the table, or when the widths are the ones the table already has; it throws for a width
that is not a positive finite number. The entries do not have to sum to anything in particular.

Only the JSON format carries column widths. HTML and RTF export a table's structure and content but
not its proportions, so a round-trip through either lays the table out evenly.

### Selecting cells

Dragging across cells makes a **rectangular** selection, which follows Word's rules: delete and cut
empty the selected cells rather than removing them; marks apply to each cell independently; copy
yields a table of just those cells. Typing, paste, and the *block* commands — headings, lists,
indent — are refused, because a rectangle names no single block for them to land on.

A rectangular selection is a `CellSelection` on the state:

```csharp
var rectangle = view.State.Selection as CellSelection;
```

Dragging to a cell that is merged across two columns takes the whole of it, and taking it widens
the rectangle, which may take another — so a rectangle always covers whole cells, and there is no
such thing as a ragged one to merge.

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
| `IsSelectionToolbarEnabled` | `true` | The floating toolbar over a selection. Never shown when `IsReadOnly` |

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
  <SolidColorBrush x:Key="VellumTextToolbarAccent" Color="#3060C0" />
</Application.Resources>
```

The others are `VellumTextToolbarBackground`, `VellumTextToolbarForeground`, `VellumTextToolbarBorderBrush`,
`VellumTextToolbarHover` and `VellumTextToolbarAccentForeground`.

`RichTextEditor` and `RichTextToolbar` are templated controls; replace their `ControlTemplate` to
rearrange them entirely. Block-level presentation is a `IParagraphStyleResolver`
(`view.ParagraphStyles`) and character-level is an `ITextStyleResolver` (`view.TextStyles`); left
alone, both derive from the control's inherited text properties, so a document inside a themed
panel picks up that theme's font.

## Recipe: a clipboard manager

The case VellumText was shaped around. Capture whatever the clipboard holds, show a list of previews,
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
    (await JsonFormat.Instance.ImportAsync(entry.Stored)).Doc,
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
