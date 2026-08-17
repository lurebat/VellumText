# Contributing to VellumText

Thanks for looking. VellumText is a rich text editor control for Avalonia, and it
is early — the model and the editing surface exist, the interop packages are
being built out.

## Getting set up

You need the .NET SDK named in [`global.json`](global.json). Nothing else:

```
git clone https://github.com/AsafMah/VellumText.git
cd VellumText
dotnet build
dotnet test
```

The Avalonia tests run headlessly, so there is no display server to configure
and they work the same on Linux, macOS and Windows.

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
- **Comments explain why**, not what. A comment restating the line under it is
  noise; a comment recording a constraint, a measurement or a trap is the most
  valuable thing in the file.

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
3. Commit, then tag: `git tag vx.y.z && git push origin vx.y.z`.
4. The `verify` job builds, tests and packs. The `publish` job then waits on the
   `nuget` environment, which should have a required reviewer configured — that
   approval is the only real protection against publishing the wrong thing.
5. On approval it pushes to NuGet and opens a GitHub release whose notes are the
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
