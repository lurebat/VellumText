# Contributing to VellumText

Thanks for looking. VellumText is a rich text editor control for Avalonia, and it
is early — the model and the editing surface exist, the interop packages are
being built out.

## Getting set up

You need the .NET SDK named in [`global.json`](global.json). Nothing else:

```
git clone https://github.com/lurebat/VellumText.git
cd VellumText
dotnet build
dotnet test
```

The Avalonia tests run headlessly, so there is no display server to configure
and they work the same on Linux, macOS and Windows.

They render real text in each platform's own default font — Segoe UI on
Windows, DejaVu Sans on Linux, the system font on macOS — so a test that
works only on your machine is easy to write. Two rules keep them portable:

- **Never aim at or assert a pixel position written as a number.** Where a
  character sits and how a paragraph wraps depend on the font. Take points
  from the layout (`BlockOrigin`, `GetCaretRect`) and compare geometry
  relatively.
- **Read rendered pixels through `RenderedPixels`.** A `RenderTargetBitmap`
  is BGRA on Windows and Linux but RGBA on macOS; reading it as one order
  swaps red and blue on the other platform.

CI builds and tests on Linux and Windows for every pull request. macOS minutes
cost ten times as much, so macOS runs on pushes to `main`, on releases, on a
manual run of the CI workflow, and on pull requests that touch
`src/VellumText.Avalonia`, its tests, `samples/`, or the build files.
`.github/scripts/needs-macos.sh` holds the exact list, and
`needs-macos.test.sh` beside it is run by CI's `lint` job.

To run one project's tests, or one test:

```
dotnet test tests/VellumText.Avalonia.Tests
dotnet test --filter FullyQualifiedName~AQuoteIsDrawnWithABar
```

## The shape of the repository

| Path | What lives there |
|---|---|
| `src/VellumText.Core` | The document model, positions, steps, transactions, history. **No dependencies at all**, not even `Avalonia.Base` — a build target enforces this. |
| `src/VellumText.Avalonia` | The `RichTextEditor` control, block views, caret and selection, IME. |
| `src/VellumText.Interop.*` | Import and export for one interchange format each. Separate packages so an app that only wants Markdown does not drag in an HTML parser. |
| `tests/` | One test project per source project. |
| `docs/architecture.md` | The design. It is detailed, it is verified against the Avalonia source, and it is the thing to read first. |

## What a change should look like

**Read `docs/architecture.md` before proposing anything structural.** Every API
it cites was checked against the Avalonia 12.1.1 source before it was written.
If you find it is wrong, that is worth knowing — say so in the issue rather
than quietly working around it.

- **Tests are not optional, and passing is not the bar.** A test that cannot
  fail is worse than no test, because it reads as coverage. The habit in this
  repository is to break the code on purpose and check the suite notices.
- **Commits are one piece of work each**, with a message that says *why*. The
  history is meant to be read.
- **Warnings are errors** and every public member is documented. The build
  enforces both, so there is nothing to remember.
- **The public API is recorded, and changing it is deliberate.** Each package
  under `src/` has a `PublicAPI.Shipped.txt` and a `PublicAPI.Unshipped.txt`.
  Adding, removing or changing a public or protected member fails the build
  (RS0016, RS0017) until the file says so. The IDE's code fix for those
  diagnostics writes the line, as does
  `dotnet format analyzers <project> --diagnostics RS0016 --severity info`.
  New lines go in `PublicAPI.Unshipped.txt`, and the diff to that file is the
  API change a reviewer should look at.
- **Comments explain why**, not what. A comment restating the line under it is
  noise; a comment recording a constraint, a measurement or a trap is the most
  valuable thing in the file.

### Rules for code that reads untrusted input

Import runs on whatever was pasted, dropped or opened, so a document is an
attacker-shaped thing (see `SECURITY.md`). The 2026-10 codebase audit traced
most of its crashes and hangs to a few repeated mistakes; these are the rules
that stop each one coming back.

- **Never recurse over a tree the input shaped** — your own walk or a
  dependency's. Walk with an explicit stack, and run the parse on
  `LargeStack` as the built-in importers do. Every importer has a
  `HostileNestingTests` theory that runs **in a child process** and asserts the
  exit code, because a stack overflow kills the test host and can still print
  "Passed!". A new importer, or a new recursive walk, gets a row there.
- **Saturate every number the input supplies before doing arithmetic with
  it.** A list start, a span, a margin or a size is clamped to what the model
  can mean at the point it is read (`BlockSink`, `CssStyles`), and arithmetic
  on it afterwards saturates rather than overflows. Add an `int.MaxValue` /
  `1e300` row for the new attribute to `HostileValueTests`, followed by the
  list and table commands that will later compute with it.
- **Look for the twin before you fix one site.** Paste and drop, `Create` and
  `Update`, the importer and the renderer reading the same `data:` URL — this
  code base has several places that must agree, and a fix applied to one of
  them is a new inconsistency. Prefer one helper (`DataUri.TryParse`,
  `RichTextClipboard.ResolveAsync`) to two copies, and add a parity test that
  feeds both sites the same input. The pull request template asks.
- **Keep a catch-all around the one call whose failure is expected.** CI's
  `lint` job runs the ast-grep rule in `tools/ast-grep/rules/broad-catch.yml`,
  which rejects a `catch (Exception)` around more than one statement in `src/`.
  The rule's note explains how to mark a deliberate last resort. Run it locally
  with `ast-grep scan` (and `ast-grep test` after changing a rule).

## Performance claims

VellumText's design contains measured claims — for instance that reusing a shaped
run cache across a width change is worth roughly 3.5×, and that invalidating
it by offset is not usable at all. Those numbers came from a spike, not from
reasoning, and the same standard applies to new ones. If you change something
for speed, measure it, and put the numbers in the pull request.

## Releasing

Releases are cut from a tag. The version lives in one place, `VersionPrefix` in
`Directory.Build.props`, and `.github/workflows/release.yml` refuses to publish
if the tag disagrees with it — tagging `v0.2.0` while the props file still says
`0.1.0` would otherwise ship `0.1.0` under a `0.2.0` release, and a NuGet
version can never be replaced or withdrawn.

To release:

1. Bump `VersionPrefix` in `Directory.Build.props`.
2. Turn the `## [Unreleased]` heading in `CHANGELOG.md` into `## [x.y.z] - date`
   and start a fresh `Unreleased` above it. The workflow fails if there is no
   section for the version being released, so this is not optional.
3. In every `src/` project, move the lines of `PublicAPI.Unshipped.txt` (all
   but its `#nullable enable` header) into `PublicAPI.Shipped.txt`. From then
   on, removing one of them is a breaking change the analyzer reports.
4. Commit, then tag: `git tag vx.y.z && git push origin vx.y.z`.
5. The `ci` job runs the whole CI workflow — Linux, Windows and macOS — on the
   tagged commit, and the `verify` job builds, tests and packs. The `publish`
   job needs both, so a commit that fails on any platform cannot be published.
   It then waits on the `nuget` environment, which should have a required
   reviewer configured — that approval is the only real protection against
   publishing the wrong thing.
6. On approval it pushes to NuGet and opens a GitHub release whose notes are the
   changelog section for that version.

Publishing needs a `NUGET_API_KEY` secret on the `nuget` environment. The
packages that get pushed are the artifacts the `verify` job built, not a second
build, so what ships is what was tested.

VellumText is pre-1.0, so the public API is still allowed to move; once 1.0 is out,
semantic versioning applies in the usual way.

## Reporting a bug

The most useful report is a failing test. Second most useful is the smallest
document and sequence of keystrokes that shows the problem, plus your OS and
Avalonia version. Rendering bugs are much easier with a screenshot.

## Security

Import runs on untrusted input by definition — anything on the clipboard came
from somewhere else. See [SECURITY.md](SECURITY.md) for how to report a
vulnerability, and please do not open a public issue for one.

## Licence

By contributing you agree that your contribution is licensed under the MIT
licence, the same as the rest of the project.
