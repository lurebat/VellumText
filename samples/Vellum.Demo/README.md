# Vellum demo

A desktop sandbox for the editor control. It exists to be run, not to be reused — if you
want to know whether something works, this is where you find out.

```sh
dotnet run --project samples/Vellum.Demo
```

## What it shows

The document you land on is **produced by the HTML importer**, not assembled in code, so the
import path is exercised before you touch anything. It deliberately contains the text that
breaks editors: a Hebrew word inside an English sentence, a family emoji built from a ZWJ
sequence, and a letter carrying two combining marks.

The toolbar drives the block commands that exist today — paragraph kind, alignment, indent,
undo and redo. Character formatting (bold, italic, colour) has no toolbar because the
control has no command for it yet; it arrives with the marks increment. Formatting that
comes *in* through an import renders correctly, as the sample document shows.

## The import panel

**Paste from clipboard** does what a real paste has to do: it asks for the richest format
first — RTF, then CF_HTML, then plain text — because a copy out of Word or a browser puts
all three on the clipboard at once, and taking the first one that answers gets you the
worst of them.

**What is on it?** lists the formats the clipboard is actually offering, which is rarely
what you would guess.

Whatever the importer had to give up is listed at the bottom of the panel rather than
thrown away silently. An import never fails: it degrades, and says what it lost.

## Two things worth knowing

**Clipboard formats are bytes, not strings.** Windows hands over RTF as a byte sequence.
Asking Avalonia for it as a string reinterprets those bytes as UTF-16, which pairs them up
and turns `{\rtf1\ansi` into CJK. RTF is decoded here as Latin-1 (one byte, one character,
which keeps an RTF file intact) and CF_HTML as UTF-8. This is the first thing to get wrong
and it fails in a way that looks like a parser bug.

**RTF bullets written the legacy way are lost.** `{\pntext\f0 \'B7\tab}` becomes an
indented paragraph rather than a list item, because that is what RtfPipe produces for it.
The list survives when the source uses a list table, which is what current Word writes.
