# Consuming Vellum before it is on NuGet

Vellum is not published. Until it is, the supported way to consume it is a **local package feed** —
a folder of `.nupkg` files that NuGet treats as a source. This gives you versioned, immutable
packages without forking the source, and switching to a real feed later changes one line.

## Publishing

```powershell
dotnet pack -c Release -o G:\packages\vellum-local
```

Five packages plus symbols. The folder is the feed; nothing else is needed.

Bump `VersionPrefix` in `Directory.Build.props` before packing if consumers need to tell two builds
apart — a local feed will happily hold `0.1.0` twice, and NuGet caches by id and version, so
re-packing the *same* version and expecting a consumer to pick it up will not work. Either bump, or
clear the consumer's `NUGET_PACKAGES` cache for that id.

## Consuming

Add this `nuget.config` beside the consuming solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="vellum-local" value="external/vellum-packages" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="vellum-local">
      <package pattern="Vellum.*" />
    </packageSource>
    <!--
      List every feed name your machines might use. A mapping naming a source that is
      disabled, or that does not exist at all, is ignored rather than fatal - so one
      committed file works on a dev box behind a mirror and on a hosted runner using
      nuget.org directly, without anyone editing it per machine.
    -->
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
    <packageSource key="azure-default"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

Use a **relative** path when the packages are committed. An absolute path outside the repository
restores on the machine that set it up and nowhere else, which is the same class of problem as
vendoring the source.

Then reference the packages normally:

```xml
<PackageReference Include="Vellum.Avalonia" Version="0.2.0" />
<PackageReference Include="Vellum.Interop.Html" Version="0.2.0" />
```

### Two things that will bite you

**1. The mapping is mandatory, because the name is taken and the collision is live.** `Vellum.Core`
**0.1.0** already exists and is an unrelated envelope-encryption library — same id, same version.
Verified on a developer machine here: with no local feed configured,

```powershell
dotnet add package Vellum.Core --version 0.1.0
```

restores successfully and yields `DekManager`, `PayloadEncryptor` and
`DefaultRandomBytesProvider` — a crypto library, with a `net10.0` target, so nothing fails at
restore time. You find out at compile time when `DocumentNode` does not exist, and the error names
a type rather than a package.

**Pinning a version does not help, which is the non-obvious part.** The impostor publishes both
`0.1.0` and `0.2.0` — the same coordinates Vellum is built at — so an exact version reference
names a package that exists in two places and says nothing about which one you want. Its own
description reads *"PLACEHOLDER: This package name is reserved… Do not install"*, which is no help
at all once NuGet has already installed it.

Source mapping is what makes resolution deterministic instead of a race between a fast local folder
and a slow remote feed. Do not skip it. See §10.4 of [`architecture.md`](architecture.md) for the
naming decision this is waiting on — until it is taken, `Vellum.*` **must** be pinned.

**2. `packageSourceMapping` is all-or-nothing, but one committed file still works everywhere.**
The moment the element is present, *every* package must match some mapping, including ones you
never thought about, and a package matching none fails with `NU1100` — which helpfully lists the
sources it did *not* consider. Corporate proxies and Azure Artifacts feeds rarely use the name
`nuget.org`: the machine this was verified on restores from `azure-default`, with `nuget.org`
registered but **disabled**, while a hosted CI runner uses `nuget.org` directly.

The fix is *not* to edit the config per machine. **A mapping naming a source that is disabled, or
that does not exist at all, is ignored rather than fatal** — verified here with both a disabled
`nuget.org` and a wholly invented source name in the same file, restoring correctly. So list every
feed name your machines might use and commit one file. To find the names in play:

```powershell
dotnet nuget list source
```

## For CI

A folder on a developer's machine is not visible to a hosted runner. Two options:

- **Commit the `.nupkg` files** into the consuming repository — five files, versioned and immutable,
  and far less invasive than vendoring the source. Point the feed at that folder with a relative
  path.
- **Push to a feed the runner can reach** — Azure Artifacts, GitHub Packages, or any NuGet server.
  `dotnet nuget push` works against a folder feed too, so the publishing step is unchanged.

Vendoring the source is the last resort. It forks a library under active development, and it drifts
faster than people expect: the first consumer to try it was five commits stale within an hour.
