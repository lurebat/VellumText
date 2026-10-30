# Changelog

All notable changes are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

VellumText is pre-1.0, so the public API is still allowed to move. Nothing is on
NuGet; packages are published to a local feed, and
[`docs/consuming.md`](docs/consuming.md) explains how to reference them.

## [Unreleased]

### Security

- **Importing can no longer crash the host with a stack overflow inside a
  parser.** AngleSharp.Css parses a nested `calc(` by recursion, and 3.3 KB of
  it overflowed an ordinary 1 MiB stack; about 50 KB of nested `<template>` did
  the same in AngleSharp's tree builder, reachable from HTML and from Markdown
  with the default options. A stack overflow cannot be caught, so a paste — or a
  clipboard manager's capture — ended the process. Every built-in importer
  (HTML, Markdown, RTF, JSON) now parses on its own thread with a 256 MiB
  reserved stack, inside the importer, so a host that calls one directly from a
  thread-pool thread is protected as well as the editor. Pinned by tests that
  import in a child process and assert its exit code.
- **8 KB of `[` in Markdown no longer overflows the stack** in the importer's
  own walk of the parsed tree, which recursed once per level and now uses an
  explicit stack. Markdig caps its own nesting at some depths and not others,
  and 8,000 brackets got through.
- **HTML whose parse cost grows faster than its size is bounded before it is
  parsed.** AngleSharp's tree builder is quadratic in element nesting (200 KB of
  nested `<div>` took 7 s) and its tokenizer in attributes per element (one
  element with 40,000 attributes, 349 KB, took 12 s). A linear pre-scan with
  AngleSharp's own tokenizer now drops tags nested deeper than
  `HtmlImportOptions.MaxElementDepth` (default 512) and keeps their text,
  removes a `style` attribute nested deeper than `MaxStyleNesting` (default 32
  brackets; the CSS parser recurses per bracket during parsing), and caps
  attributes at `MaxAttributesPerElement` (default 256). Each reports a
  `Downgraded` diagnostic. Markup within the limits is passed to the parser
  untouched.
- **A few kilobytes of `colspan`/`rowspan` no longer cost seconds and
  gigabytes.** Each span was clamped to 1,000, but the product was not: 1 KB of
  `<td colspan=1000 rowspan=1000>` took 11.5 s and 1.27 GB, because
  `TableGeometry.Place` marked every slot a span named, including rows the
  table does not have. It now marks only the rows that exist (placements are
  unchanged, so the schema still reports a span hanging off the table), and the
  importers budget the slots their tables may cover with `MaxTableSlots`
  (default 100,000 per import) on `HtmlImportOptions`, `MarkdownImportOptions`
  and `RtfImportOptions`. A table over budget becomes paragraphs with a
  `Downgraded` diagnostic.

### Fixed

- **Enter at the end of a heading starts body text.** It started another
  heading, aligned like the first. The new paragraph is body text at the default
  alignment and keeps the heading's indent; Enter in the middle or at the start of
  a heading is unchanged.
- **Enter on an empty quote line leaves the quote.** Every Enter added another
  quote line, so the toolbar was the only way out. The empty line becomes body
  text at the default alignment where it is, splitting the quote around it when
  it is in the middle; Enter on a line with words still continues the quote.
- **Backspace at the start of a quote takes the quote off first.** It joined the
  quoted words straight onto the paragraph above as body text, and a quote that
  was the first block ignored Backspace. The paragraph now becomes body text in
  place, keeping its alignment; the next Backspace joins as before.
- **Enter in a code block adds a line to the same block.** It split the block,
  so every line of code became a code block of its own and the toolbar was the
  only way out of the last one. The lines stay newlines in the one block now, as
  Shift+Enter already made them. Three Enters at the end of the block — Enter on
  an empty last line under another empty line — leave it for a body paragraph
  below and drop the empty lines at its end; Ctrl+Enter (Cmd+Enter on macOS)
  leaves it from anywhere and keeps every line. ↓ on the last line of a code
  block that nothing follows opens a paragraph below it instead of stopping at
  its end. Backspace in an empty code block turns it into an ordinary
  paragraph, and text pasted or dropped into a code block stays in it, where
  each line used to become a paragraph of its own.
- **Enter in a bullet starts the next bullet.** It split the paragraph inside
  the same item, so the new line had no bullet, Tab could not nest it, and
  Shift+Tab took both lines out of the list. Backspace at the start of a bullet
  now takes the bullet off and keeps the words, as in a word processor.
- **Lines grow for text larger than the paragraph's own size.** A paragraph's
  forced line height came from its own font size, and Avalonia keeps a forced
  height even when a run is taller, so a large size picked from the toolbar, or a
  pasted heading with a size of its own, was drawn over the line below.
- **Shift+Tab or a list toggle in a list numbered from near `int.MaxValue` no
  longer throws out of the key handler.** A pasted
  `<ol start="2147483646">` (48 bytes) made the next split compute a negative
  start, which `ListNode` refuses by throwing; without a dispatcher exception
  handler the host process ended. Imported starts are now clamped to
  999,999,999 (CommonMark's nine digits) with a `Downgraded` diagnostic, and the
  list commands continue numbering with saturating arithmetic, so a model built
  with a start at the top of the range is safe too.
- **A read-only view, or one with a cell rectangle selected, no longer reads and
  parses the clipboard on Ctrl+V or Ctrl+Shift+V before refusing.** Read-only
  was checked only when the decoded document was about to be applied, so a
  viewer ran every registered importer on untrusted content for a paste that
  could never land (600 KB of HTML: 912 ms against 4 ms). Both paste methods now
  refuse before the first clipboard read.

## [0.7.0] - 2026-08-20

A sixth package, `VellumText.Interop.Markdown`. **`AC8` is complete for v1 as of
this release** — every format the acceptance criteria name for v1 now imports and
exports. DOCX and XAML remain deferred to v2.0 by an explicit decision rather
than by omission.

No public surface was removed or moved, so upgrading from 0.6.0 is a version bump
plus one optional package reference.

Everything below the Markdown entry itself was found by reviewing that entry
before releasing it: three models over the new package, then a fourth over the
fixes. Two rounds were needed because the first fix to the raw-text leak was
incomplete, which is recorded rather than quietly amended.

### Added

- **A new package, `VellumText.Interop.Markdown`: Markdown import and export.**
  CommonMark plus GitHub-flavoured pipe tables and `~~strikethrough~~`, in both
  directions. Register `MarkdownFormat.Instance` and copy and paste carry
  Markdown; `MarkdownImporter` and `MarkdownExporter` are usable directly. This
  is the last unbuilt clause of `AC8` — Markdown had been named in the
  acceptance criteria from the start, scheduled once, and then neither built nor
  knowingly dropped.

  Paragraphs, headings H1–H6, bold, italic, strikethrough, inline code, code
  blocks, blockquotes, bulleted and numbered lists including nesting and a
  starting number, horizontal rules, links, images and tables all map both ways.

- **`MarkdownExporter.ExportWithDiagnostics`, because Markdown is the first
  format smaller than the document model.** HTML, RTF and JSON can hold
  everything the model can, so `IDocumentExporter` has no diagnostics channel and
  did not need one. Markdown has no notation at all for colour, highlight, font
  family, font size, underline, sub/superscript, paragraph alignment, indent,
  merged cells or cell backgrounds. Rather than discard those in silence — the
  failure `ContentImported` exists to prevent in the other direction — the
  Markdown exporter reports each one. `Export` still returns exactly the same
  text; the extra method just tells the caller what it cost. The shipped
  interface was not widened: adding a required member to a public interface is
  the one change that cannot be made after 1.0.

### Fixed

- **A link preceded by `!` is no longer written as an image, which deleted it.**
  `!` was missing from the export escape set, so a paragraph reading
  `Sale!` followed by a link — an ordinary HTML paste — was written as
  `Sale![click](…)`, and an image's brackets hold alt text rather than content,
  so the link and every word of its text were gone on the way back in. Nothing
  reported the loss, because the Markdown was well formed and so was the
  document it read back as.

- **A list item whose first block is a nested list no longer loses its own
  marker.** The marker for an item is held pending until a line consumes it, and
  a nested list that was the item's *first* block wrote its own marker over the
  pending one before it had ever been flushed. `- - inner` came back out as
  `  - inner`, which reads as a single list with the outer level gone — a
  nesting level destroyed permanently, with no diagnostic. Reachable from
  `<ul><li><ul><li>inner</li></ul></li></ul>` as well as from Markdown.

- **A table row shortened by a cell spanning into it no longer shifts every
  later cell one column left.** Cells were written by position in each row's own
  array and short rows padded at the end, so a table whose first row is
  `A` (spanning two rows), `B`, `C` and whose second row holds `D`, `E`
  wrote `| D | E |  |` instead of `|  | D | E |`: content under the wrong
  headers, which is not a downgrade of the merge but a different table. Rows and
  column alignment are now both read from the grid `TableGeometry.Place` works
  out, the same grid the schema, cell selection and the view already use.

- **Two adjacent sibling lists no longer merge into one on the way back.** Both
  were written with the same marker and a blank line between them, which
  CommonMark reads as a single loose list, so the boundary was gone for good.
  A list can only continue with the marker it started with, so the second of a
  pair now alternates — `-` against `*`, `1.` against `1)`. To a reader
  those are the same bullet and the same number, so nothing else changes.

- **A pipe inside a code span, link destination or link title in a table cell is
  escaped, so the table is readable outside this library.** GFM splits a row on
  its unescaped pipes before parsing anything in it as an inline, so an
  unescaped one there gives GitHub and cmark-gfm three cells with a stray
  backtick in each. Markdig's parser is inline-aware and does not split, which
  is why the library's own round trip was green while what it wrote was
  unreadable everywhere else. The import side undoes the escape inside a code
  span, where Markdig does not, so the two halves still agree.

- **Emphasis is written where it can actually open and close, so styling beside
  punctuation survives.** CommonMark only lets a delimiter run open where it is
  left-flanking and close where it is right-flanking, and the two characters
  touching it decide both. A bold `Ver.` in `Ver.2` was written
  `**Ver.**2`, where the closing `**` sits between a full stop and a digit
  and so is neither: the reader saw four literal asterisks and no styling at
  all. The writer now judges each delimiter against the characters either side
  and moves edge punctuation outside it — `**Ver**.2` — reporting the one
  character that loses its mark. Where the obstacle is another delimiter rather
  than the text, which no edit can separate, the style is dropped with a
  diagnostic instead of being written unreadably.

### Security

- **Raw HTML embedded in Markdown is removed by default.** CommonMark requires
  implementations to pass raw HTML through, which would make every Markdown
  document a carrier for every HTML hazard — script, event handlers, `iframe`,
  styled overlays — in a format whose reputation is for being plain text. Every
  raw block and inline tag is removed before anything reaches the document, and
  each removal is reported. Set `MarkdownImportOptions.AllowRawHtml` to read it
  instead, in which case it goes through the same sanitizer every HTML import
  goes through rather than a second one written for Markdown.

- **An inline `<script>` or `<style>` in Markdown no longer hands its body to
  the document as text.** Removing a raw inline tag keeps the words around it,
  which is right for `<b>bold</b>` and catastrophic for
  `a <script>steal()</script> b`, which imported as the text `a steal() b`. This
  is the defect `c02d649` fixed in the HTML reader in August 2026, reached again
  through Markdown's default path — the path that runs when a host has *not*
  opted into raw HTML. A tag naming an element whose text a browser never shows
  is now left in the parsed tree so that the HTML sanitizer removes the element
  with its subtree, and reports a `Dropped`. The element list is the HTML
  package's own, shared rather than copied, because a second copy of a security
  list is a second copy to forget to extend. The reason the regression was
  invisible: the tests covered `<script>` as a *block*, where it was already
  removed with its subtree by ordinary block handling.

- **…and it now holds when the body is split across the document, which the
  first repair of it did not.** That repair deleted the opening tag's following
  siblings through the matching closing tag, which only works when the tag, the
  body and the closing tag are siblings in one container. Markdig parses each tag
  as an ordinary inline node wherever the parser happens to be, so `**` puts the
  opener inside an emphasis the closer is outside of and a blank line puts them
  in different blocks: `a **<script>evil** body </script> after` still imported
  as `a body after`, and `before <script>hidden`, a blank line,
  `leaked </script> after` as `leaked after`. `script` and `style` are *raw text*
  elements — everything to the matching close is text and not markup — and no
  walk of a Markdown syntax tree can express that, so the tags are handed to the
  one component that already implements it properly. Both `AllowRawHtml`
  settings now agree on every case: an unclosed element takes the rest of the
  document, a stray closing tag is ignored, and a `<script>` written inside a
  code span or a fenced block is still literal text and survives.

- **Markdown link destinations go through the existing scheme allow-list.**
  Import runs by way of the HTML reader, so `[click](javascript:alert(1))` keeps
  its text and loses its destination, and so do `//host/path` and
  `\\host\share`, which `System.Uri` would otherwise read as `file:` addresses —
  an NTLM credential leak on Windows. Reused rather than reimplemented, because
  a second importer with its own idea of which URLs are safe is a second
  importer to get that wrong in.

- **Remote images are still off by default**, in a format where every image is a
  URL the document chose.

## [0.6.0] - 2026-08-19


Two features and four fixes. `AC5` — the table acceptance criterion — is complete
as of this release. No public surface was removed or moved, so upgrading from
0.5.0 is a version bump and nothing else.

### Added

- **Move the selected text by dragging it.** Press inside a selection, drag past
  four pixels and the selection is picked up; a caret follows the pointer to show
  where it will land, and releasing moves it there. Releasing before those four
  pixels is an ordinary click and collapses the caret where the press landed, so
  both gestures a press inside a selection can mean are still available. The
  delete and the insert are **one transaction**: a single Ctrl+Z puts the content
  back where it came from. Dropping inside the range that is being dragged does
  nothing rather than editing the document into itself. Nothing is armed,
  previewed or committed on a read-only view, and a drag whose drop lands after
  the host replaced the document is discarded.
- **Move a selected picture by dragging it.** The same gesture on the existing
  node selection: press on the picture, drag, and it lands where the drop caret
  is, splitting the paragraph it was dropped into — again one undo step. The
  picture is picked up from its own box rather than from anywhere its selection's
  two positions reach, so a press beside it on the same line is still an ordinary
  press, and a click on it without a drag leaves it selected. A drop aimed at a
  picture lands in the text beside it.

- **Column resize by dragging.** Drag a column boundary in a table and the two
  columns either side of it change share; the rest of the table does not move
  and the table's total width does not change. The pointer becomes a horizontal
  resize cursor within four pixels of a boundary, anywhere down the table's
  height, and a guide line follows the drag. The document is relaid out once, on
  release, as **one undo step** — a whole gesture goes back in one Ctrl+Z. A
  table with no stated widths gets a full vector the first time a boundary is
  dragged, and a column will not go below 5% of the table (or a quarter of the
  two columns' combined width, for a table with too many columns for 5% each to
  be possible). Nothing is armed or committed on a read-only view. This
  completes AC5, the last unbuilt clause of the table acceptance criterion.
- **`TableEdits.SetColumnWidths(DocumentNode doc, int tablePos, IEnumerable<double> widths)`.**
  Returns the `TableStructureStep` to put in a transaction, or `null` when there
  is no table at that position, when the vector has the wrong number of entries
  for the table's columns, or when the widths are the ones the table already
  has. Throws for a width that is not a positive finite number, which is a
  caller error rather than a no-op. The entries do not have to sum to anything:
  `TableNode.ColumnWidths` is a set of **proportions**, so `[1, 2]` and
  `[100, 200]` lay out identically. The control writes vectors summing to 1.
- **`RichTextView.IsResizingColumn`**, and the two constants that govern the
  gesture: **`RichTextView.ColumnBoundaryTolerance`** (4 px either side of a
  boundary) and **`RichTextView.MinimumColumnShare`** (0.05). Both are
  `static readonly` rather than `const`, so tuning them in a later release
  reaches a host on upgrade instead of staying baked into whatever it compiled
  against.
- **`RichTextView.IsMovingSelection`**, true while content is held by a drag.

### Fixed

- **A table column narrower than its own cell padding no longer takes the whole
  layout down.** `TableBlockView` passed the cell's inner width straight to
  `ParagraphView.Measure`, which refuses zero, so any document stating widths
  like `[0.995, 0.005]` threw `ArgumentOutOfRangeException` during layout. Text
  now overflows a column that narrow, which is what a column that narrow means.
  Pre-existing, and no gesture required — but reachable only from the JSON
  format or from a host building a `TableNode` directly, since neither HTML nor
  RTF carries widths at all.

- **Tab into a table cell that holds another table put the caret in the wrong
  cell**, and a rectangular selection anchored on such a cell selected every
  cell of the block rather than the column. One cause behind both: the caret
  snapped to the *nearest* text position, and for a cell holding a table the
  nearest one is the end of the cell before it. Tables without nested tables
  were never affected.

- **An `IImageStore` returning a blank source no longer throws out of
  `InsertImage`.** `Save` documents a source or `null` to refuse; a blank is
  neither, and it passed the null check and reached `ImageEmbed`, which rejects
  it in its constructor — so a store answering `""` wrote the image and then
  threw out of a method that returns a bool and documents no throw. A blank is
  now a refusal and raises `ImageRefused`. This stops the exception, not an
  orphaned file: `Save` has to be asked before its answer is known.

### Documentation

- **`TableNode.ColumnWidths` are proportions, not pixels.** The XML doc said
  "device-independent pixels" while the layout scaled them unconditionally to
  the available width. Corrected, along with a claim in `TableBlockView` that
  the HTML and RTF importers produce explicit column widths — neither does.
- **Only the JSON format carries column widths.** HTML and RTF export a table's
  structure and content but not its proportions, in either direction, so a
  round-trip through either lays the table out evenly. Now stated in the guide.

## [0.5.0] - 2026-08-18

Every breaking change here is deliberate. VellumText is pre-1.0 and the point of
this release is that a 1.0 must not freeze an API that a real host cannot use.
Each entry says what moved, why, and what to do about it; **migrating** below is
the short version.

**If you render a read-only document, upgrade for the accessibility fix**, which
is the one defect in this release a consumer cannot work around from outside.

### Migrating

1. Formats you implement yourself: add `Import(Stream)`, `Export(Stream, DocumentNode)` and
   `TextEncoding`. Keep the string bodies. Never implement a stream member by calling its string
   counterpart.
2. `RtfImporter.Import` now takes `RtfImportOptions`; the fields you were passing exist on it under
   the same names, except `MaxDepth` and `PreserveIdentifiers`.
3. `HtmlImportOptions.PreserveIdentifiers` is gone; delete the assignment. It never did anything.
4. `Editor.View!.Formats` and its neighbours become `Editor.Formats`. Where the shell forwards
   nothing, `Editor.EnsureView()` replaces `Editor.View!`.
5. `presenter.Blocks.Count` becomes `presenter.BlockCount`.
6. `Node.Children` is an `ImmutableArray<Node>`. `foreach` and LINQ are unchanged; comparing two
   with `Assert.Equal` is not — that compares the underlying arrays by reference, so call
   `.AsEnumerable()`.
7. If you derived from `Node`, `BlockNode`, `InlineEmbed` or `Selection`, you cannot any more. See
   below for why that is a fix and not a loss.
8. A preview pane that used `RichTextViewer` and wanted selection or copy wants
   `RichTextEditor { IsReadOnly = true }`. `docs/guide.md` said otherwise and was wrong.

### Changed

- **`RtfImporter.Import` takes `RtfImportOptions` rather than `HtmlImportOptions` — this is a
  breaking change.** RTF is read by way of HTML internally, and that had leaked into the RTF
  package's public API: a consumer who wanted only RTF had to reference the HTML package to
  configure it, and an internal choice was frozen where a later rewrite of the RTF reader could not
  have changed it. `RtfImportOptions` carries `AllowRemoteImages`, `AllowDataImages`,
  `MaxDataImageBytes` and `BaseUri`. It deliberately omits `MaxDepth`, which applies to authored
  HTML rather than the HTML this package generates for itself.

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

- **`Node.Children` is an `ImmutableArray<Node>` rather than an `IReadOnlyList<Node>`.** The
  children of a node are already an immutable array in every implementation; the interface return
  boxed the struct on every access, on the hottest walk in the library. Reading code is unchanged.
  One thing to know when testing: `Assert.Equal` on two `ImmutableArray<T>` compares the underlying
  arrays by reference and will fail on two equal arrays, so compare `.AsEnumerable()`.

### Removed from the public API

None of the following was reachable in a way that would have survived contact with a real host; they
are listed because a 1.0 that shipped them would have frozen them.

- **The rendering machinery is internal.** `BlockView`, `ParagraphView`, `RuleView`,
  `BlockImageView`, `TableBlockView`, `InlineTextSource` and `TextAt` are layout objects — a
  `TextLayout`, a run cache, a measured size — whose lifetimes the presenter owns and whose
  invalidation rules nothing outside it can honour. `docs/architecture.md` §4.6 and §4.7 always
  declared them internal; the code did not. `DocumentPresenter.Blocks`, `DocumentPresenter.Slots`
  and `RichTextView.Block` went with them, a property being only as internal as its type.

  **`DocumentPresenter.BlockCount` replaces `Blocks.Count`**, and is the part a host actually
  needed: `BlockOrigin`, `BlockIndexOf`, `BlockIndexAt` and `CaretBlockIndex` all speak in block
  indices, so the range those indices live in is public even though the objects behind them are not.

- **`Node`, `BlockNode`, `InlineEmbed` and `Selection` can no longer be derived from outside the
  library.** Each now has a `private protected` constructor. They were externally derivable and
  consumed by `switch` statements that throw, drop or refuse on a shape they do not recognise —
  `DocumentPresenter`'s block factory, the HTML and RTF exporters, the JSON writer, every command
  that asks whether it has a rectangle of cells. A type you derived would compile and then fail the
  first time it was rendered or saved, which is worse than no extensibility at all because it fails
  at run time in your application instead of at compile time in ours.

  `Step` is deliberately still open: nothing switches on which step it is, and all four obligations
  are on the base, so a step written elsewhere works everywhere one is consumed.

  One hole cannot be closed, and is stated rather than hidden: a record's copy constructor must be
  `public` or `protected` (CS8878), so `InlineEmbed` and `Selection` can still be derived from by
  declaring a copy constructor that chains to the base. It cannot happen by accident, and anything
  that does it lands on a `default:` that throws.

- **`HtmlImportOptions.PreserveIdentifiers` is gone.** It was declared, defaulted, documented and
  never read: no code path in the importer consulted it, so setting it to `true` did not preserve
  an identifier and setting it to `false` did not strip one. A host that set it got the same
  document either way and had no way to find that out. Delete the assignment; nothing else changes.

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

- **`Document` and `Selection` are observable on both controls.** `RichTextView` and
  `RichTextEditor` each gained `Document`, `Selection`, the `DocumentProperty` and
  `SelectionProperty` behind them, and the `DocumentChanged` and `SelectionChanged` events. This is
  the half of `AC13` that was missing: the commands existed and `IsReadOnly` was settable, but a
  view model had no way to hear that the user had typed something.

  **Binding is one-way, out of the control.** The direct properties are read-only and default to
  `OneWayToSource`, so `{Binding Doc, Mode=OneWayToSource}` — or plain `{Binding Doc}` — keeps a
  view model's copy current. Assigning a document from outside is deliberately not offered:
  reconciling an incoming document against in-flight edits and the undo history is a design with
  real hazards and nobody has asked for it. Load a document through `State` as before.

  The events exist alongside the properties because `OneWayToSource` never pushes the value that is
  already there, only subsequent changes, so a binding set up after a document was loaded would
  leave a view model holding `null` until the first keystroke. Subscribe to `DocumentChanged` and
  read `Document` once to prime.

- **`RichTextView.ContentImporting` and `RichTextEditor.ContentImporting`**, raised with a
  `ContentImportingEventArgs` before imported content is inserted, so a host can inspect what is
  about to land and set `Cancel` to refuse it. `ContentImported` reports what an import cost after
  the fact; this is the seam that comes first.

  It carries the exact document that would be inserted, the importer's diagnostics, and whether
  anything was dropped on the way in. It is raised only for content that would otherwise land — a
  read-only control, a rectangle selection and an empty clip never reach it, so a host is not asked
  about a paste that was never going to happen. A refusal is a paste that did not occur: no edit, no
  undo entry, no `ContentImported`, and nothing added to the diagnostics channel. A handler that
  throws costs the paste and nothing else; on the keyboard, menu and drop paths the existing handler
  swallows it, and `PasteImport` called directly rethrows.

  Images are deliberately not covered. `IImageStore.Save` returning `null` is already an
  inspect-and-refuse seam, and `ImageRefused` already reports the outcome.

- **`RichTextClipboard.Build` and `RichTextClipboard.WriteAsync` take an optional `text`.** The
  plain text flavour comes from the caller when one is given, so a control stays the authority on
  what it shows selected. Existing calls compile and behave unchanged.

### Fixed

- **A read-only editor no longer tells a screen reader it is writable.** `RichTextView` answered
  `SupportsTextInput` unconditionally, and the automation peer derives
  `IValueProvider.IsReadOnly` from exactly that — so `RichTextEditor { IsReadOnly = true }`
  announced itself to assistive technology as an editable document. The peer's own
  `ElementNotEnabledException` guard was already correct and simply unreachable, because the value
  it consulted never varied.

  **This is the one defect in this release that a consumer cannot work around.** The peer belongs
  to the templated inner `RichTextView`, so a host holding a `RichTextEditor` has no lever except
  `AccessibilityView = Raw`, which hides the pane from screen readers entirely — worse than being
  misinformed about it. Reported from a shipped application whose preview pane had been affected in
  every release since it adopted VellumText.

  A refused automation write also ran `SelectAll` before declining, so it moved the user's
  selection over the whole document. The document itself was never at risk.

- **A read-only editor no longer composes.** Nothing in the input method path consulted
  `IsReadOnly`, so a locked editor accepted a composition: characters drawn underlined with the
  candidate window open, and the commit refused only at the end, because it arrives as ordinary
  text input. The user watched their own typing appear and vanish. Clearing a composition is still
  allowed whatever the state, so a view locked mid-composition can be tidied up rather than
  stranding underlined text.

- **Text carrying `U+FFFC` is no longer silently dropped.** The object replacement character is
  what the model reserves for embeds, and building a paragraph from text containing one throws.
  `RichTextClipboard`'s plain-text fallback did not know that, so dropping such text — which Word
  writes for objects it cannot otherwise express, and which any text that has been through a rich
  text model can carry — inserted nothing at all, while pasting the same text worked.

- **A drop and a paste of one clip now produce the same paragraphs.** The two paths disagreed about
  what a line break is: the drop normalized through `string.ReplaceLineEndings`, which takes the
  form feed, NEL and the Unicode line and paragraph separators, and the paste normalized only CRLF
  and CR. The same clip became seven paragraphs dropped and three pasted, with raw control
  characters left inside them.

- **A `LinkRequested` example in `docs/guide.md` did not compile**, awaiting inside a non-async
  lambda.

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
