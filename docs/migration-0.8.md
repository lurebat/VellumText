# Migrating to VellumText 0.8

0.8.0 makes importing asynchronous. It is the one breaking change in the release; nothing about
exporting, the document model or the editor's own API changes shape.

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
| `IDocumentImporter.Import(string)` | `IDocumentImporter.ImportAsync(string, CancellationToken = default)` |
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
ImportResult result = await HtmlFormat.Instance.ImportAsync(html);
```

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

    public Task<ImportResult> ImportAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(Parse(text));

    private static ImportResult Parse(string text) => /* … */;
}
```

A synchronous parser wrapped in `Task.FromResult`, as above, is fine: the editor runs it on a
background thread regardless.

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
call from the table above, or a class implementing `IDocumentImporter`. Nothing else in the editor's
API changed shape. Because NuGet caches by id and version, clear the host's cached 0.8.0 packages if
the feed is re-packed under the same version (see [consuming](consuming.md#packing)).