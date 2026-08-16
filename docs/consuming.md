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
    <add key="vellum-local" value="G:\packages\vellum-local" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="vellum-local">
      <package pattern="Vellum.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

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

Source mapping is what makes resolution deterministic instead of a race between a fast local folder
and a slow remote feed. Do not skip it. See §10.4 of [`architecture.md`](architecture.md) for the
naming decision this is waiting on — until it is taken, `Vellum.*` **must** be pinned.

**2. `packageSourceMapping` is all-or-nothing, and your feed is probably not called `nuget.org`.**
The moment the element is present, *every* package must match some mapping, including ones you
never thought about. Corporate proxies and Azure Artifacts feeds rarely use that name — the machine
this was verified on restores from `azure-default`, with `nuget.org` registered but **disabled**.
Mapping `*` to a source that is not enabled fails everything else with `NU1100`, which helpfully
lists the sources it did *not* consider. Find the real name first:

```powershell
dotnet nuget list source
```

and use it for the `*` pattern.

## For CI

A folder on a developer's machine is not visible to a hosted runner. Two options:

- **Commit the `.nupkg` files** into the consuming repository — five files, versioned and immutable,
  and far less invasive than vendoring the source. Point the feed at that folder with a relative
  path.
- **Push to a feed the runner can reach** — Azure Artifacts, GitHub Packages, or any NuGet server.
  `dotnet nuget push` works against a folder feed too, so the publishing step is unchanged.

Vendoring the source is the last resort. It forks a library under active development, and it drifts
faster than people expect: the first consumer to try it was five commits stale within an hour.
