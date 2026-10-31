# Migrating to VellumText 0.8

0.8.0 makes importing asynchronous, and trims the public API to what 1.0 will freeze. Those are
the two breaking changes in the release. The first touches every caller and implementer of an
importer; the second touches only code that subclassed a control or a step, or reached into
members that were public by accident. See [The 1.0 API trim](#the-10-api-trim).

## Why

A paste of a large web page or a Word document took seconds, and whatever thread waited for it —
usually the UI thread — froze for that long. An interface with a synchronous import member invites
exactly that wait, so there is no longer one. The editor also stops trusting an importer to get off
the UI thread by itself: it always runs every importer, built-in or yours, on a background thread
and applies the result on the UI thread only if the document and selection are still where the
import started.

## What changed

| 0.7 | 0.8 |
|---|---|
| `IDocumentImporter.Import(Stream)` | `IDocumentImporter.ImportAsync(Stream, CancellationToken = default)` |
| `IDocumentImporter.Import(string)` | `IDocumentImporter.ImportAsync(string, CancellationToken)` — the token is required |
| `HtmlFormat`/`RtfFormat`/`MarkdownFormat`/`JsonFormat` `.Import(…)` | `.ImportAsync(…)` |
| `HtmlImporter.Import(html, options)` | `HtmlImporter.ImportAsync(html, options, cancellationToken)` |
| `HtmlImporter.ImportClipboard(payload, options)` | `HtmlImporter.ImportClipboardAsync(payload, options, cancellationToken)` |
| `MarkdownImporter.Import(markdown, options)` | `MarkdownImporter.ImportAsync(markdown, options, cancellationToken)` |
| `RtfImporter.Import(rtf, options)` | `RtfImporter.ImportAsync(rtf, options, cancellationToken)` |
| `RichTextClipboard.Decode(importer, payload)` | `RichTextClipboard.DecodeAsync(importer, payload, cancellationToken)` |
| `RichTextClipboard.Read(IDataTransfer, importers)` | removed — use `ReadAsync(IAsyncDataTransfer, importers)` |
| `DocumentPayload.ReadAllText(stream, encoding)` | `DocumentPayload.ReadAllTextAsync(stream, encoding, cancellationToken)` |

Every import returns a `Task<ImportResult>`. The rules an importer follows are the same — it must
not throw (or fault its task) for any input, and it reports what it gave up in
`ImportResult.Diagnostics` — with one addition: cancellation is the only way the task may fault.

## Calling an importer

Await it:

```csharp
// 0.7
ImportResult result = HtmlFormat.Instance.Import(html);

// 0.8
ImportResult result = await HtmlFormat.Instance.ImportAsync(html, cancellationToken);
```

The string overload takes a token with no default; pass `CancellationToken.None` where there is
nothing to cancel. Without one the call fails to compile with *cannot convert from 'string' to
'System.IO.Stream'*, because the stream overload, whose token is optional, is the only candidate
left. The static `HtmlImporter`, `RtfImporter` and `MarkdownImporter` methods keep their optional
parameters.

Do not block on it with `.Result` or `.GetAwaiter().GetResult()` on a UI thread. That is the freeze
this release removes, and on a thread with a synchronization context it can deadlock.

## Writing an importer

Implement the stream member; implement the string member too if the format is text, each calling
your parser directly (never one in terms of the other's default — that recurses forever):

```csharp
public sealed class MyFormat : IDocumentImporter
{
    public string Format => "my-format";
    public string MediaType => "text/x-my-format";
    public Encoding? TextEncoding => Encoding.UTF8;

    public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var text = await DocumentPayload.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken);

        return Parse(text);
    }

    public Task<ImportResult> ImportAsync(string text, CancellationToken cancellationToken) =>
        Task.FromResult(Parse(text));

    private static ImportResult Parse(string text) => /* … */;
}
```

A synchronous parser wrapped in `Task.FromResult`, as above, is fine: the editor runs it on a
background thread regardless.

## The 1.0 API trim

The [public API review](plans/2026-10-28-public-api-review.md) closed what 1.0 should not promise.
Every package's API is now listed in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` beside
it, and the build fails on any change those files do not record.

**Closed to subclassing.** `RichTextView`, `RichTextEditor` and `RichTextViewer` are `sealed`.
`DocumentPresenter` is still public and abstract, but its constructor is `private protected`. `Step`
is closed the same way, so the steps VellumText ships are the only ones; they all stay public and
sealed. A subclass written against 0.7 will not compile. Wrap the control instead of deriving from
it: everything a subclass could reach through a protected override is available as a property,
event or command on the control. To build an edit, combine the built-in steps through
`Transaction`.

**Made internal.** These were reachable but were plumbing for the controls, the exporters or the
tests:

| Was public | Use instead |
|---|---|
| `RichTextView.GoalX`, `IsCaretVisible`, `SnapCaret`, `ResetCaretBlink` | nothing; the caret is the control's own |
| `RichTextView.Preedit`, `SetPreedit`, the `Preedit` record | `IsComposing`; the input method drives the composition |
| `RichTextView.IsResizingColumn`, `IsResizingImage`, `IsDraggingTouchHandle` | `IsDragging` and `IsMovingSelection`, for waiting until a drag ends |
| `RichTextView.TouchHandles` | `ShowTouchHandles`, `HideTouchHandles`, `AreTouchHandlesVisible` |
| `DocumentPresenter.RealizedBlocks` | nothing; it was diagnostic |
| `DocumentPresenter.SetTextFromAutomation` | `DocumentPresenterAutomationPeer.SetValue`, which is what calls it |
| `BlockSlot`, `ListMarker`, `DocumentBlocks` | the block-index API: `BlockCount`, `BlockIndexAt`, `BlockIndexOf`, `BlockOrigin`, `PositionAt` |
| `TableGeometry.Place`, `CellPlacement` | `TableGrid.Build(tablePos, table)`: `Columns`, `Rows`, `Cells` (each a `TableSlot`) |
| `MarkFieldsExtensions.ToFields`, `ToStyle`, `Includes` | `(MarkFields)style`; `(TextStyle)(fields & MarkFields.AllStyles)`; `(fields & wanted) == wanted` |
| `ClipboardHtml` | `HtmlImporter.ImportClipboardAsync`, `HtmlExporter.ExportForClipboard` |

**No public constructor.** `HtmlExporter`, `RtfExporter`, `MarkdownExporter`, `JsonFormat` and
`JsonHighlighter` are reached through `Instance`, as `HtmlFormat`, `RtfFormat` and `MarkdownFormat`
already were. Replace `new HtmlExporter()` with `HtmlExporter.Instance`.

## What the editor does differently

- **Paste and drop decode off the UI thread**, and the result lands only if nothing has moved:
  a newer paste or drop, the host loading another document, or the editor being retemplated
  abandons an import still running, and a moved caret or swapped document refuses its result.
  `ContentImporting` and `ContentImported` are still raised on the UI thread, but after the
  background import, with other UI work possibly run in between. A handler must not assume the
  editor is as it was when the paste began; a host that needs the paste to have landed awaits
  `PasteAsync`.
- **Paste and drop resolve a transfer the same way.** Rich formats first; but if the transfer also
  carries an image and the rich import lost one to the sanitizer — a browser's "Copy image" — the
  image is inserted. Plain text is inserted the same way by both gestures, so it takes the
  formatting of where it lands.
- **A `ContentImported` handler that throws reaches you.** It used to be swallowed, and the paste
  reported `false` for an edit that had happened. A `ContentImporting` policy that throws during a
  paste still just refuses that paste.

## Hosts that vendor a 0.7 build

A host that vendors VellumText 0.7.0 packages (Clipthrough does, from a local feed) upgrades by
packing 0.8.0 into the same feed, bumping its package references, and then letting the compiler
list the call sites: every one is an `Import`, `ImportClipboard`, `Decode`, `Read` or `ReadAllText`
call from the table above, a string `ImportAsync` call on a `*Format` that does not pass a token, a
class implementing `IDocumentImporter`, or a member from [The 1.0 API trim](#the-10-api-trim). Because NuGet caches by id and version, clear the host's cached 0.8.0 packages if
the feed is re-packed under the same version (see [consuming](consuming.md#packing)).