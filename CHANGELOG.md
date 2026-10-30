# Changelog

All notable changes are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

VellumText is pre-1.0, so the public API is still allowed to move. Nothing is on
NuGet; packages are published to a local feed, and
[`docs/consuming.md`](docs/consuming.md) explains how to reference them.

## [Unreleased]

These changes ship as 0.8.0 (`VersionPrefix` is set; nothing is published until
the release is made). Importing became asynchronous, which breaks every caller
and implementer of an importer — see [Migrating](#migrating) below and
[migrating to 0.8](docs/migration-0.8.md).

### Migrating

Await an import instead of calling it:

```csharp
// 0.7
ImportResult result = HtmlFormat.Instance.Import(html);

// 0.8
ImportResult result = await HtmlFormat.Instance.ImportAsync(html);
```

Implement the asynchronous members in a custom importer — a synchronous parser
wrapped in `Task.FromResult` is fine, because the editor runs every importer on a
background thread regardless:

```csharp
// 0.7
public ImportResult Import(Stream source) => Parse(DocumentPayload.ReadAllText(source, Encoding.UTF8));

// 0.8
public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default) =>
    Parse(await DocumentPayload.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken));
```

Replace `RichTextClipboard.Read(transfer, importers)` with
`await RichTextClipboard.ReadAsync(transfer, importers)`. `ContentImporting` and
`ContentImported` still run on the UI thread, but after the background import,
and other UI work can run in between: a handler must not assume the editor is as
it was when the paste began, and a host that needs the paste to have landed
awaits `PasteAsync`.

### Added

- **`ReplaceTextStep`**, a step that replaces several stretches of paragraph
  text at once, and **`TextReplacement`**, one stretch and its new content.
  The replacements must be in document order, must not overlap or touch, and
  must each lie within one paragraph's text; a step that breaks those rules or
  cuts a surrogate pair fails like any other step rather than throwing. Its map
  has one range per replacement, so positions move exactly as they would
  through one `ReplaceStep` per replacement; its inverse is the same kind of
  step holding the displaced text; and `Map` rebases each range as
  `ReplaceStep` does, dropping ranges whose both ends were deleted. Replace
  All is built on it.
- **`RichTextView.ScrollToEnd()`** shows the true end of the document at the
  bottom of the viewport without moving the caret or the selection, as Ctrl+End
  does with it. The end of the scroll range is not always the end of the
  document: rows nobody has scrolled to are counted at an estimated height, and
  when they each hold a long table the range can end many screens short. The
  editor's scroll bar dragged or clicked to its bottom now asks for the true end
  the same way; a document ending in such a table used to stop thousands of
  pixels short. The mouse wheel, `Offset` and `ScrollViewer.ScrollToEnd()` stay a
  plain scroll to the end of the range.

### Removed from the public API

- `IDocumentImporter.Import(Stream)` and `Import(string)`, and the same members
  on `HtmlFormat`, `MarkdownFormat`, `RtfFormat` and `JsonFormat` — replaced by
  `ImportAsync(…, CancellationToken)`.
- `HtmlImporter.Import` and `ImportClipboard`, `MarkdownImporter.Import` and
  `RtfImporter.Import` — replaced by their `…Async` forms.
- `RichTextClipboard.Read(IDataTransfer, …)` and `Decode` — use `ReadAsync` and
  `DecodeAsync`.
- `DocumentPayload.ReadAllText` — use `ReadAllTextAsync`.
### Security

- **RTF that stalled or hung the old reader is bounded.** 32 bytes of
  `\uc2147483647` took RtfPipe over two seconds and silently dropped the rest
  of the text; one row with 100,000 `\cellx` kept it busy past 30 seconds. The
  new reader (see Changed) handles both in milliseconds, honours the skip count
  as the specification says, and reports it (`uc`).
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
- **The decoded-image cache is bounded by memory, not only by count.** A GIF
  states its canvas size in its header and decodes to it whatever its frames
  hold, so 1 KB of pasted HTML naming 7999×7999 GIFs pinned gigabytes in
  `BitmapEmbedRenderer.Shared`, which is process-wide. The renderer now evicts
  the least recently used images until the rest fit `CacheBudgetBytes` (new,
  default 256 MiB); `CacheLimit` stays as a second cap. An image larger than the
  whole budget is still kept, alone, so it is not decoded on every frame.
- **Image decompression bombs are refused from their header in every common
  format, not just PNG.** GIF, BMP, JPEG and WebP were decoded in full before
  `MaximumPixels` was checked: a 38-byte WebP cost 642 MB, and a 287-byte JPEG
  or a 64-byte BMP 2 GB, through a paste and one render. Their declared
  dimensions are now read from the header and checked first, including ICO and
  WBMP, a GIF's first frame where it lies outside the screen, and a JPEG frame
  header behind stray bytes. An inline image whose dimensions cannot be read
  from its header, such as a TIFF, is refused without being decoded.
- **HTML export can no longer turn an imported font name back into CSS the
  sanitizer removed.** A pasted
  `font-family:"x\" ;background-image:url(http://tracker.invalid/p.png);"` kept
  its CSS escape in the model, and the exporter escaped the quote but not the
  backslash, so a copy carried a live `background-image` — a tracking fetch the
  import had stripped. Import now reads the family the CSS actually states, and
  export writes every family name through one CSS string serializer that
  escapes `\`, `"` and control characters. A host's list of fallbacks
  (`Segoe UI, Arial`) stays a list.

### Fixed

- **A transaction of many steps records them in linear time.** Each step's
  position map was appended to an immutable mapping, which copied every map
  before it: 8,000 steps allocated 768 MB on bookkeeping alone. `Transaction`
  now collects the maps and builds `Mapping` when it is read; what it maps is
  unchanged.
- **Replace All no longer hangs on a large document, and neither does undoing
  it.** Every match was its own step, and each step copied the document's
  block list — or, in one long paragraph, the whole paragraph — so the cost was
  the document times the matches: one match in each of 8,000 paragraphs took
  seconds and allocated 1.6 GB, a 100,000-character paragraph 865 MB, and undo
  the same again. It is now one `ReplaceTextStep` (more only where matches abut
  with a replacement between them; abutting deletions are merged into one) that
  rewrites each paragraph once, and one undo still takes it all back. The
  result, where the caret and every other held position lands, and what undo
  and redo restore are unchanged.
- **A registered exporter that throws during a copy is reported, not silent.**
  The copy already went ahead without that format, but nothing said so, so a
  broken exporter only showed as a paste elsewhere arriving with less
  formatting. `RichTextView`, `RichTextViewer` and `RichTextEditor` raise
  `ExportFailed` (format and exception), the failure is written to
  `System.Diagnostics.Trace`, and Ctrl+C still never throws: plain text and
  every other format still reach the clipboard. `RichTextClipboard.Build` and
  `WriteAsync` take an optional `exportFailed` callback for hosts that write
  the clipboard themselves.
- **The font box shows a family name with a comma as the user knows it.** A
  name such as `ACME, Inc.` is stored quoted (`"ACME, Inc."`) so that it stays
  one family through export, and the box showed those quotes. It now shows
  `ACME, Inc.`, and picking or typing such a name stores the quoted form, the
  same one a paste of that font produces. A name without a comma, or one already
  typed in quotes, is stored as typed. `SelectionFontFamily` and
  `SetFontFamilyCommand` still carry the stored form.
- **After a pick or Enter in the font or size box, typing goes on in the
  document.** Focus stayed in the box, so the next keystroke went into it
  (`36x`). Picking from either list or pressing Enter now hands the keyboard back
  to the editor, as Word and Docs do; Escape does too, without applying what was
  typed, and the box shows the selection's value again.
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
- **Backspace at the start of a heading that is the first block makes it body
  text.** With nothing above to join to it did nothing. A heading further down
  still joins onto the paragraph above.
- **A Markdown code block comes back with exactly its text.** Every fenced or
  indented code block was imported with the newline before its closing fence
  as part of the code, which showed as an empty last line; content that really
  ended in an empty line lost it on export to make up for that. The importer now
  reads the lines without that final line ending and the exporter writes every
  line, so export then import is exact.
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
- **RTF import keeps what RtfPipe lost.** Text coloured explicitly black stays
  black (it followed the theme); merged cells come back merged, both vertical
  and horizontal; a link is no longer painted black and keeps its own
  underline; an empty table cell is empty rather than a non-breaking space; a
  white highlight survives; a list numbered from 0 starts at 0; a cell's
  alignment survives; and a horizontal rule comes back as a rule.
- **RTF export writes a column span as Word does**, as `\clmgf` and `\clmrg`
  cells, instead of one wide `\cellx` that only RtfPipe lined up.
- **Shift+Tab or a list toggle in a list numbered from near `int.MaxValue` no
  longer throws out of the key handler.** A pasted
  `<ol start="2147483646">` (48 bytes) made the next split compute a negative
  start, which `ListNode` refuses by throwing; without a dispatcher exception
  handler the host process ended. Imported starts are now clamped to
  999,999,999 (CommonMark's nine digits) with a `Downgraded` diagnostic, and the
  list commands continue numbering with saturating arithmetic, so a model built
  with a start at the top of the range is safe too.
- **Keys and typing work while a cell rectangle or a block image is
  selected.** Both input handlers returned early when no paragraph was under the
  caret, so a mouse-made rectangle could not be cleared, formatted or copied
  from the keyboard, and a clicked picture could not be deleted, typed over or
  undone. Only Home, End and the arrow keys still need a paragraph; Delete and
  Backspace remove a selected block image, and typing replaces it with a
  paragraph holding the text.
- **Retemplating `RichTextEditor` keeps what the host set up.** Configuring
  the editor in code applies the default template to get a view, so an
  application style that then gave the editor its own chrome made a new view and
  every forwarded setting reverted: registered formats, the image store, the
  renderer and style resolvers, the syntax properties, the `ContentImporting`
  veto (vetoed content landed), the reporting events, and the undo history. The
  shell now carries all of them to the new view, keeps the handlers it was given
  so the discarded view stops raising them, and unbinds the discarded template's
  floating toolbar.
- **`RichTextEditor` raises `DocumentChanged` and `SelectionChanged` after its
  view holds the new state.** They were raised first, so a handler reading the
  view saw the old document, and one that selected all on load wrote the old
  document back over the one the host had just set.
- **Syntax highlighting survives edits.** It was applied only when a block's
  view was first created, so typing one character in a highlighted code block —
  in the document or in a table cell — or replacing a `RichTextViewer`'s
  document with one of the same shape drew the code plain.
- **A cut no longer deletes from a document swapped in while the clipboard was
  busy.** Its guard compared selection offsets only, so a host that loaded a
  different clip with a selection at the same numbers lost text it never copied.
  It now uses the same document-and-selection check a paste does.
- **An IME composition over a selection that crosses paragraphs is drawn where
  the commit lands** — at the start of the selection, in the first paragraph —
  rather than at the start of the paragraph holding the head.
- The vertical-move goal column no longer survives a host loading a different
  document, and `RichTextViewer` repaints when `SelectionBrush` changes.
- **Typing, an IME commit or a find-and-replace carrying U+FFFC no longer
  throws out of the input handler.** The embed placeholder is dropped from such
  text, as paste already did.
- **Heading, alignment and indent commands work in a table cell.** They walked
  the drawn blocks, and a table is one drawn block, so Ctrl+1–6, Ctrl+L/E/R/J,
  the toolbar's buttons and its lit state all did nothing inside a table. They
  now walk the paragraphs, as character formatting already did.
- **Find-and-replace keeps the formatting and link of the text it replaces**,
  taking the marks of the match's first character, as typing over it does. It
  used to write plain text, so a linked word silently lost its address.
- With the caret where two links meet, Ctrl+K's dialog and `ApplyLink` /
  `RemoveLink` now act on the same link; they picked opposite sides, so the
  link shown stayed and its neighbour was rewritten.
- **Automation:** `SetValue` replaces the whole document in one transaction. It
  selected all and pasted, which did nothing on a document that is one table
  (select-all is then a cell rectangle) and left a block image at either edge
  behind. A block image's alt text is now part of `DocumentText` — and so of the
  automation value and the plain-text clipboard flavour — on a line of its own,
  as an inline image's already was. The peer raises the value pattern's
  `IsReadOnly` change when `IsReadOnly` toggles.
- A position or cell rectangle mapped through `TableEdits.InsertRows` or
  `InsertColumns` in a host's own transaction stays in its row and column. The
  step's map matched rows and cells by value, and an inserted empty cell equals
  its empty neighbours, so the caret landed one column left or one row up.
- **A browser's "Copy image" pastes the image.** Chrome puts an `<img>` pointing
  at the network beside the bitmap; the sanitizer removed the remote image and
  the paste inserted nothing while returning `true`. Paste and drop now resolve a
  transfer by one rule: rich formats first, but an image on the transfer wins
  when the rich import lost one to the sanitizer or produced nothing, and plain
  text last. Drop used to prefer the image and build plain text into body
  paragraphs, so the same clip landed differently by gesture.
- **A `ContentImported` handler that throws reaches the host** instead of being
  swallowed while `PasteAsync` returned `false` for a paste that had landed. A
  `ContentImporting` policy that throws on a paste the user started still only
  refuses that paste.
- **Importing styled HTML or RTF costs a parse per distinct style, not per
  element.** RtfPipe writes the same few `style` strings on every element of a
  Word document, and each was parsed while the DOM was built and again by the
  sanitizer: 10,000 paragraphs allocated 850 MB against 35 MB without the
  styles. The DOM is now built without CSS support, each distinct style value is
  sanitized once by the same sanitizer and parsed once, and the import allocates
  about 110 MB.
- **A read-only view, or one with a cell rectangle selected, no longer reads and
  parses the clipboard on Ctrl+V or Ctrl+Shift+V before refusing.** Read-only
  was checked only when the decoded document was about to be applied, so a
  viewer ran every registered importer on untrusted content for a paste that
  could never land (600 KB of HTML: 912 ms against 4 ms). Both paste methods now
  refuse before the first clipboard read.
- **A dropped or pasted image file larger than the image store accepts is no
  longer read whole first.** With the default `DataUriImageStore` (4 MB), a
  64 MB file allocated 192 MB only to be refused. The file is now read only up
  to the store's `MaximumBytes`; past that, the drop stops reading and raises
  `ImageRefused` with the file's size. A custom `IImageStore`, whose limit the
  editor cannot know, still receives the whole file.
- **Undoing a format applied to a large selection is no longer quadratic.**
  Ctrl+A, Ctrl+I, Ctrl+Z put the recorded formatting back by trying every
  recorded span against every paragraph: 272 ms at 8,000 plain paragraphs and
  4 s with every other word bold, growing 3.5× per doubling (minutes at
  50,000). Each paragraph now looks only at the spans that reach into it, found
  by binary search, so the undo is linear like the command it undoes; redo
  shares the fix. A hand-built `RestoreMarksStep` whose spans are out of order
  or overlap is still applied exactly as before.
- **Ctrl+Z after a burst of typing replays one step, not one per keystroke.**
  Typing without a 500 ms pause is still one undo, but each keystroke's inverse
  was kept and replayed separately, every one a rebuild of the document's
  spine: 100 characters took 0.6 s (104 MB) to undo at 50,000 paragraphs and
  400 took 2.3 s, growing with every character. History now folds neighbouring
  text edits within one paragraph into a single step as each keystroke is
  recorded, for typing, Backspace and Delete alike, so the undo costs the same
  however long the burst was. What is grouped is unchanged; a split, a join or
  any other structural step in a burst keeps its own step.
- **A frame painted with a search open no longer walks every match in the
  document.** Each frame — a caret blink, a scroll step, a keystroke — looked at
  every match and, for each, at every realized block: 81 ms a frame for a
  query of "e" at 50,000 paragraphs, 21 ms for one match per paragraph. The
  painter now binary-searches the matches for the ones inside the realized
  blocks, so its cost no longer depends on the document's size, and works out a
  block's position only for the blocks a range actually   reaches.
- **Typing, Enter and Backspace at 50,000 paragraphs no longer allocate
  megabytes a keystroke.** Every change re-flattened the whole document into a
  new block list (3.2 MB), Enter and a join fell back to cutting the whole
  document in two and splicing it back (2.4 MB), a join also listed every leaf
  block to find its neighbour (3.2 MB), and the height index was reallocated
  for each block added or removed (1 MB): 4.2 MB for a keystroke, 6.9 MB for
  Enter and 10.1 MB for a join, with a gen-2 collection every few keys. The
  block list is now patched in place for just the top-level blocks an edit
  replaced; Enter and a join rebuild only the blocks they touch and splice the
  document's block array once; the join finds its neighbour by walking out from
  the caret; the height index shifts in place; and typing sets the document's
  top-level entry without a second copy. A keystroke now allocates about
  0.6 MB at 50,000 paragraphs, and Enter and a join about the same — the one
  copy of the document's block array an immutable edit makes, which stays
  linear in the document by design.
- **Reading a large selection's formatting no longer looks at every
  character.** The toolbar asks what formatting the selection shares on every
  state change, and with everything selected that was a lookup per character:
  143 ms at 50,000 paragraphs, paid again by Ctrl+B. It now walks the
  selection's stretches of formatting from the first block the selection
  reaches, and stops as soon as nothing is shared, so a mixed selection costs a
  few lookups whatever its size. The HTML, RTF and Markdown exporters read
  formatting runs the same way, off the mark spans instead of one lookup per
  character.
- **Typing in a very long formatted paragraph no longer slows with the square
  of its length.** Avalonia's line wrapping copies the remaining run list once
  per line, so laying out one paragraph cost its lines times its runs, and every
  keystroke laid out the whole paragraph again: 117 ms at 50,000 characters
  with every other word bold and 326 ms at 100,000, 2.76× per doubling. A
  paragraph longer than 4,096 characters is now held as consecutive layouts of
  about 2,048 characters each, every one starting and ending at a line boundary,
  and an edit lays out again only from the layout it lands in until the lines
  meet the old ones. Caret, selection, hit testing, input-method composition and
  drawing are exactly those of one layout. Paragraphs containing right-to-left
  text, Arabic digits or explicit direction controls, and justified paragraphs,
  stay one layout, because their lines depend on text arbitrarily far away.
  Text so regular that every line holds the same number of words can still lay
  out the rest of the paragraph after an edit — because every later line moved —
  but now in time linear in what it lays out.
- **A frame of a long table draws only the rows on screen, and a keystroke in
  a table walks only its own row.** Every caret blink drew every cell's text
  whatever the viewport showed — 13.3 ms a frame at 1,000 × 5 — and every
  keystroke walked every cell to find the one that changed — 5.35 ms at
  1,000 × 5 — both growing with the table. The table view now draws the row
  bands that meet the visible area, found by a binary search over the row edges
  (a cell spanning into them from above included), and keeps each row's cells
  with positions relative to the row, so an edit that keeps the grid's shape
  rebuilds the rows it replaced and moves the rest by one pass over their
  starts. Resizing a column no longer rebuilds any cell.
- **A long table lays out only the rows near the viewport.** Making a table's
  view built a view of every block in every cell — 280 MB for a 10,000 × 5
  table — and measuring it measured every cell. Rows are now realized and
  measured in bands as the viewport, the caret, a click, a selection or a column
  drag reaches them, with the rest priced at the mean height of the rows
  measured so far, as the document already does for paragraphs. Laying out the
  top of a 10,000-row table measures 95 cells and allocates 7.8 MB; scrolling
  keeps the row at the top of the viewport in place while the rows above it are
  measured, including rows taller than the estimate, and through inserting or
  deleting a row, which keeps the heights of the rows it did not touch;
  painting a selection of the whole table realizes only the rows on screen.
- **Typing into a cell of a long table no longer re-validates the whole table.**
  Each step re-placed every cell of the edited table to prove it still tiled its
  grid: about 6 MB a keystroke at 10,000 rows. A table already known to tile,
  edited without changing its row count, any cell's spans or the number of
  column widths, is now known to still tile; anything else is validated in full
  as before, and a table that never passed validation is still refused.
- **Placing a table's cells costs the placements, not a set of every slot.**
  `TableGeometry.Place` — used to draw a table, select cells, export RTF and
  Markdown, edit rows and columns, and validate — kept a hash set of every
  occupied (row, column) slot: 5.8 MB for a 10,000 × 5 table. It now keeps, per
  column, the row a span above keeps it busy until (about 0.8 MB, the placements
  themselves). The placements are unchanged; a test compares them with the old
  algorithm on 3,000 random tables, malformed ones included.
- **Inserting or deleting a row or column in a long table, and undoing it, no
  longer collects every node of the table.** Mapping positions through the edit
  first asked whether the two versions shared any node by building a set of all
  of them — 14 MB at 10,000 × 5, on the edit and again on its undo. The cells of
  the first or last row now answer it; the full search remains for tables that
  share nothing there.
- **A row or column command on a long table works its grid out once.** The
  command built the table's grid (every cell's placement plus a dictionary of
  them) for the selection, again inside the edit, and again to find the cell to
  put the caret in, and placed the cells twice more. It now reuses the grid it
  started from — only while it is still the grid of that very table at that
  position — and finds the caret's cell from the new table's placements. With
  the placement and sharing changes above, inserting a row into a 10,000 × 5
  table allocates under a third of what it did (77 MB before).
- **A long table inside a table cell lays out only the rows near the viewport
  too, at any depth.** A table in a cell was laid out whole whenever the row
  holding it was: 135 MB and 1.5 s for the first layout of a 10,000 × 3 table in
  a cell, 115 MB kept, and 26 MB for every resize. Each table now hands the
  tables in its cells the part of its window that falls in them, so a table
  nested one, two or three deep costs what it does at the top of the document
  (5.2 MB and 33 ms, 1.6 MB kept, 54 KB a resize). Rows once laid out stay laid
  out, as at the top level, so scrolling through a whole long table keeps what
  it kept before. What the viewport shows stays where it is while estimates are
  replaced — the text line at the top of the viewport is what is held, through
  scrolling, resizing, edits far away, undo and redo, and composition (including
  a line where composed text runs into the paragraph's own text, which is held
  by that text as the composition grows above it) — and
  text beside a long nested table, or a row's bottom that a nested table
  beside it could move, is laid out exactly before it is shown, which can mean
  laying that table out whole. Scrolling beside a nested table that is already
  laid out whole costs a screenful a page, however long it is. A table whose
  windows do not settle, or whose window cannot be worked out — a coordinate
  that is not a number, or a viewport anchor that names nothing inside it — is
  laid out whole, counted and traced, never left blank. The scroll range stays
  of the order of the document's real height: a row holding a table only partly
  laid out teaches the estimate for the rows nobody measured only what is known
  of that table, not that table's own estimate, which had made the extent grow
  as the product of the row counts along the path (34.6 billion pixels for a
  document 5.8 million tall, after a resize).
- **Ctrl+End reaches the end of a document whose unseen rows hold most of it.**
  The caret was aimed at where the estimates for the rows above put it, and
  with an extent far too small the offset was clamped short of it: the document
  ended 8,389 pixels below the viewport, caret and all. Sending the caret to the
  document's last position now lays the window out from the end up, with the end
  at the bottom of the viewport, before the caret is brought into view.
- **The text at the top of the viewport is held through the spacing around its
  lines, a jump to rows nobody had laid out, and questions about far points.**
  Scrolling a long table moved its text by up to 5.6 DIP more or less than the
  scroll; a resize after jumping into a long table, or after a question about a
  far row moved the viewport, put a different line at the top; text inserted at
  the very start of the paragraph below the top moved it down; and a point
  question exactly on the edge between two rows or blocks laid out the one below
  while answering, moving the text on screen.
- **Whole-word search no longer treats letters outside the basic plane as word
  boundaries.** The characters either side of a match were read one UTF-16 code
  unit at a time, and neither half of a surrogate pair is a letter, so "cat"
  matched as a whole word inside "𝐀cat" or next to a Deseret letter. The
  neighbours are now read as whole characters (`Rune`); an emoji is still a
  boundary and an astral digit is still part of the word.
- **`DocumentPreview.IsLongerThan` stops inside a leading table or list once
  the budget is beaten.** A container was measured by building its whole plain
  text, so asking whether a document opening with a 50,000-row table was longer
  than 80 characters allocated 17 MB and took 15 ms, against the method's own
  promise to cost about the budget. It now counts the container paragraph by
  paragraph, exactly as its plain text would be laid out, and stops there.
- **Markdown export no longer throws, or silently changes text, around
  emphasis.** One styled escaped or astral character — the italic `&` in
  "R&D", a bold 👍 — threw `IndexOutOfRangeException` out of
  `MarkdownFormat.Export` (and on copy the Markdown flavour was silently
  missing). A run's closing delimiter and the next run's opening one could
  touch and merge, so bold "Price" beside italic "$5" came back as bold
  "Price" and the literal text `*$5*`, with no diagnostic. The inline writer
  now judges every delimiter against what is really written beside it,
  including the neighbouring run's delimiters, and writes `**Price**_$5_`.
  Bold text that changes colour no longer closes and reopens its bold.
- **A Markdown paragraph ending in a line break no longer comes back ending in
  a backslash.** The break has no notation there; it is dropped with a
  `Downgraded` diagnostic. A break at the end of a link's text is written just
  after the link instead of being lost.
- **Inline code straight after a literal backtick stays code.** The backtick
  was written as a backslash escape directly before the code span's fence,
  which a reader refuses to open there, so the code came back as plain text
  with two extra backticks; the literal backtick is now written `&#96;`.
- **Exporting a styled run with long whitespace at its edges is linear.** 40,000
  trailing spaces in a bold run took 2.3 s and 3.2 GB of allocation.
- **A numbered list nested in a list item keeps its start number in Markdown.**
  One starting anywhere but 1 was written straight under its item's paragraph,
  which it cannot interrupt, so `- Steps` / `2. second` came back as the one
  paragraph "Steps 2. second". It now gets the blank line a top-level one does.
- **RTF export writes tables from the grid.** A cell spanning two rows left the
  row below one cell short, and its cells were written a column too far left;
  a cell's paragraphs ran together ("Hello" and "World" became "HelloWorld");
  and a list, rule or image in a cell was written outside the table and split
  it in two. Row spans are now written as `\clvmgf`/`\clvmrg` merges, every
  paragraph in a cell is marked `\intbl` and keeps its alignment, and two
  tables written back to back get the empty paragraph RTF needs between them
  instead of running together. A table inside a cell is written as the cell's
  paragraphs, row by row: nested tables are not understood consistently by RTF
  readers.
- **A quote or a backslash in a link no longer breaks the RTF hyperlink, and a
  semicolon no longer cuts a font name short.** A quote ended the HYPERLINK
  field's argument, so the link was dropped — and
  `https://ok.example/" "javascript:alert(1)` became a second argument, which
  RtfPipe-based readers take as the target. A quote in an address is now
  written `%22` and a backslash is escaped for the field. `Weird;Font` was the
  font "Weird" followed by stray text; the semicolon is left out of the name.
- **A block image comes back a block image through HTML and Markdown.** No
  importer ever built one, so every copy or save turned a block image into a
  paragraph holding an inline picture — no block selection, no resize — with
  no diagnostic. HTML export now writes a block image as a `<figure>` holding
  only the image, and HTML import reads such a figure (with its alignment) as a
  block image; a figure with a caption, or inside a link, is unchanged.
  Markdown import reads a paragraph that is nothing but one image as a block
  image, so Markdown export now reports an inline image that is the whole of
  its paragraph (`image-paragraph`), since that one comes back as a block image
  too. RTF is unchanged: its reader cannot tell the two apart.
- **An explicit font size inside a heading no longer grows on every RTF copy
  and paste.** It was multiplied by the heading's scale on the way out and read
  back as the explicit size, so 12 px in a Heading 1 became 24, then 48, then
  96. The heading's scale now applies only to text that asked for no size.
- **RTF lists keep their nesting, their boundaries and their start number.**
  Depth was carried only by the indent and every paragraph repeated an
  old-style `\pn` numbering with no level and no identity, so a nested list
  came back flat and two lists written back to back came back as one. Lists are
  now written with a `\listtable`: each top-level list has its own `\ls`,
  nested lists are its deeper `\ilvl` levels with their own kind and start, and
  the marker is still written as literal `\listtext` for readers that know no
  list table. A heading inside a list item now keeps its `\outlinelevel`.
- **RTF export writes a link's title and an image's alternative text.** Neither
  was in the RTF at all. A title is now the HYPERLINK field's `\o` tooltip, and
  alt text is the picture's `wzDescription` property, which Word reads.
- **A JSON document with deeply nested lists or tables now opens again.** The
  writer saved nesting up to its own limit of 1,000 JSON levels, but the reader
  stopped at the default 64, so 19 nested lists or 11 nested tables saved
  without complaint and came back as an empty document with a `Malformed`
  diagnostic. The reader now accepts 2,048 levels, more than the writer can
  emit; it still parses on its own large-stack thread.
- **A range copied from Excel pastes as a table again, not as one paragraph.**
  Excel's clipboard HTML opens the copied fragment after `<table>` and before
  `<col>`, so the fragment alone is rows and cells with no table around them;
  the parser dropped them as stray tags and a 6×4 range arrived as one line of
  every cell's text. A fragment that opens with table rows is now read inside
  the document it was cut from, keeping only what lies between the fragment
  markers and the elements around them, so the range keeps its rows, columns
  and merged cells. A single copied cell, which Excel marks as just the cell,
  still pastes as its text, as Excel's own RTF of the same copy does.
- **A blank line pasted as `<p><br></p>` is one empty paragraph, not two lines.**
  Browsers, mail clients and RtfPipe (for `\par\par`) all write a blank line as
  a paragraph holding only `<br>`, and the importer kept that break as a newline
  character, so every blank line arrived twice as tall and a Markdown save
  turned it into a stray backslash. A break that ends a block now only ends it:
  `<p>a<br></p>` is "a", `<p><br></p>` is an empty paragraph, and a break in the
  middle of a paragraph is still a line break.
- **One bad character or one absurd margin no longer empties a whole paste.**
  The model refuses text that is not well-formed UTF-16 and formatting that
  starts or ends inside a surrogate pair, and HTML, Markdown and RTF (where
  `\u-10240?` is a lone surrogate) could deliver both; the refusal was caught
  and the entire import came back empty with a `Malformed` diagnostic. The
  importer now repairs these where paragraphs are built: an unpaired surrogate
  becomes U+FFFD and a formatting edge inside a character moves to the start of
  that character, each with a `Downgraded` diagnostic, and the rest of the
  paste survives. Likewise a nested `margin-left:1e300px` computed an indent of
  `int.MaxValue`, which wrapped negative once the enclosing indent was added and
  discarded everything; the level is now capped at 8 before it is added.
- **An inline image the importer accepts is one the editor can draw.** The
  sanitizer, the image renderer and the RTF exporter each parsed `data:` URLs by
  their own rules, so `<img src=" data:image/png;base64,…">` (leading
  whitespace), `…;base64;x=y,…` and `…;base64x,…` were imported without a
  diagnostic and then drawn as a missing-image box, and the RTF exporter wrote
  `image/pngx` as a PNG. All of them, and the data-URL image store, now share
  one reading: `;base64` must end the header and the media type is matched
  exactly. The importer strips the whitespace HTML strips around any URL and
  stores the clean URL, so the first case now imports and draws; the others are
  refused with a `Dropped` diagnostic.
- **A bulleted or numbered list copied from Word pastes as a list.** Word's
  clipboard HTML has no `<ul>` or `<ol>`: each item is a paragraph styled
  `mso-list:l0 level1 lfo1`, with its bullet written out as text inside
  `<![if !supportLists]>`. The importer read those as ordinary paragraphs, so
  the list arrived as "·" paragraphs in the Symbol font. Word list paragraphs
  are now rebuilt into lists before the HTML is sanitized: the written-out
  marker is removed and decides bullets or numbering, the level decides
  nesting, and a different Word list, a change of marker kind or any other
  paragraph in between starts a new list. Word's numbered headings stay
  headings.
- **A Markdown link whose address contains a backslash now says that its
  address changed.** CommonMark reads `[a](/a\b)` as a literal backslash and
  writes it as `%5C`, so the link points at `/a%5Cb`, where an HTML link with the
  same address, kept as written by the HTML and RTF importers, means `/a/b` to a
  browser. The import used to change the address silently; it now reports a
  `Downgraded` diagnostic. The address itself is left as CommonMark reads it,
  because keeping the backslash would mean rendering links outside Markdig or
  restoring an address the sanitizer never checked.

### Changed

- **HtmlSanitizer 9.2.1039 and AngleSharp 1.8.5** (were 9.1.982 and 1.8.3). The
  sanitizer tightens URI-list attributes, `srcdoc`, `cite`/`longdesc`,
  `<style type="">`, `@font-face` and `@import`. AngleSharp 1.8.6, which fixes a
  mutation-XSS in attribute serialisation and stack overflows in `Clone`/`Import`,
  is not yet on the package feed this was built from; VellumText reserialises no
  AngleSharp DOM and calls neither, so it is defence in depth to take when it is.
- **Built on Avalonia 12.1.4** (was 12.1.1), so a host must reference Avalonia
  12.1.4 or later. The patch releases between them fix, among others, an infinite
  loop shaping a text run that produces no glyphs (reachable from pasted text),
  clipboard exceptions in `TextBox`, focus left on detached or removed controls,
  and races in headless session startup and disposal. SkiaSharp (3.119.4) and
  HarfBuzzSharp (8.3.1.3) are unchanged.
- **RTF is read by OfficeIMO instead of RtfPipe** (`OfficeIMO.Rtf` and
  `OfficeIMO.Html.Rtf`, pinned to one exact version). RtfPipe had no release
  since 2021 and stalled on small hostile inputs; the replacement was chosen
  through a scored selection gate. What a host may notice:
  - a picture Word stored only as a shape (`\shppict`) is lost and reported
    (`Dropped`, `shppict`) — a paste from Word still brings it through the HTML
    flavour;
  - a heading inside a list item comes back as text with the heading's size
    and weight, and an image's alt text is not read back;
  - a backslash in an http(s) link comes back as `/`, as a browser reads it;
  - an unpaired surrogate is still replaced with U+FFFD and reported, and a
    surrogate pair split by formatting keeps the character but gains a stray
    U+FFFD (a reader quirk);
  - groups nested deeper than 128 levels are dropped with a diagnostic;
  - the sanitizer reports more removed style properties, because the reader
    writes more CSS.
  The HTML stage still decides what a paste may contain. AngleSharp moves to
  1.8.3 and AngleSharp.Css to 1.1.2, which the new reader requires.
  A font family or size every run of a document states is written once rather
  than on every run (the reader's HTML was about 3.5 times RtfPipe's), with an
  import identical to before.
- **Importing is asynchronous, and only asynchronous.** A paste of a large web
  page or Word document froze whatever waited for it — the UI thread, in the
  editor — for seconds, and a synchronous import member invites exactly that
  wait. Importers still never throw for bad input; cancellation is the only way
  their task faults. Exporters are unchanged.
- **The editor runs every importer on a background thread**, built-in or not,
  and applies the result on the UI thread only if nothing has moved: a newer
  paste or drop, the host loading another document, or a retemplate abandons
  an import still running, and a moved caret or swapped document refuses its
  result. A document a `ContentImporting` handler changes is not pasted into.
- **`RichTextToolbar` disables its commands while the editor it is bound to is
  read-only** (`CanExecute` is false and `CanExecuteChanged` is raised when
  `IsReadOnly` toggles). The rule that commands are always enabled still holds
  for an editable editor, wherever the caret is: disabling half the bar on an
  awkward caret reads as broken, but on a locked document every command is
  refused, so the bar offering them offered nothing.
- **The toolbar's font sizes are in points, as its box, list and command were
  always documented.** The number went to the model, which stores
  device-independent pixels, unconverted: picking 12 gave 9-point text, and a
  20-point heading pasted from Word showed as "26.7". `SetFontSizeCommand` now
  converts points to pixels (12 sets 16) and `SelectionFontSize` reports points
  (that heading reads 20). A host that read `SelectionFontSize` as pixels needs
  to multiply by 4/3.
- **The benchmark now carries the codebase audit's reproductions, and the test
  suite pins their shape without a clock.** `bench/VellumText.Bench` gains the
  `a-*` scenarios each performance finding was confirmed with (`a-undo`,
  `a-undofmt`, `a-findrender`, `a-structure`, `a-stages`, `a-sustained`,
  `a-selectall`, `a-exports`, `a-preview` and the rest), so a fix is
  re-measured against the shape that found the problem. Timings still need a
  quiet machine, so CI runs `ScalingGuardTests` instead: each row measures
  bytes allocated or a work counter at doubling sizes and fails on growth above
  2.5× per doubling, or allocates against a budget at AC12's 50,000 paragraphs.

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
