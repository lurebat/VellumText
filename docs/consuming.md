# Consuming VellumText from a local feed

The supported way to consume VellumText is a **local package feed** — a folder of `.nupkg` files
that NuGet treats as a source. This gives you versioned, immutable packages without forking the
source, and switching to a hosted feed later changes one line.

To find out whether a version is on nuget.org rather than assuming either way:

```powershell
Invoke-WebRequest https://api.nuget.org/v3-flatcontainer/vellumtext.core/index.json
```

404 means nothing is published under that id. Publishing is a deliberate, irreversible act — a
version number is consumed permanently, since a package can be unlisted but never withdrawn — so it
happens on request rather than as part of a release, and `.github/workflows/release.yml` is what
does it, driven by a `v<version>` tag and an API key the repository owner holds.

## Packing

```powershell
dotnet pack -c Release -o G:\packages\VellumText-local
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
    <add key="VellumText-local" value="external/VellumText-packages" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="VellumText-local">
      <package pattern="VellumText.*" />
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
<PackageReference Include="VellumText.Avalonia" Version="0.4.1" />
<PackageReference Include="VellumText.Interop.Html" Version="0.4.1" />
```

Pin an exact version rather than a range. Every package here moves together — they share one
`VersionPrefix` and are always published as a set — so a range that resolves them to different
versions is never what you want.

### Two things worth knowing

**1. Map the source anyway, even though the name is now clear.** `VellumText.*` is free and
unreserved on nuget.org, so nothing will impersonate it — but without a mapping, resolution of a
package that exists in a local folder *and* could one day exist on a remote feed is a race between
a fast folder and a slow HTTP source. Mapping makes it a rule instead of a race.

> **Why this section used to be a warning.** The project was called `Vellum` until 2026-08-17, and
> `Vellum.Core` is an actively published envelope-encryption library shipping the *same version
> numbers* — so `dotnet add package Vellum.Core --version 0.1.0` restored successfully and yielded
> `DekManager` and `PayloadEncryptor`, with a `net10.0` target so nothing failed until compile
> time. A version pin was no defence, because the impostor occupied the same coordinates. That is
> what the rename fixed, and it is why the identity check below is still worth running.

**Verify what you actually resolved**, on every upgrade rather than once:

```powershell
# Should print the VellumText assembly, and find no crypto types.
dotnet build && dotnet list package
```

A mapping that silently stops applying fails in exactly one way — the wrong package restores
cleanly and the first symptom is a missing type, long after the restore that caused it.
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

## Where the packages should live

**The feed is where packages come *from*; the repository is where they *live*.** Point a consumer at
the publishing machine's folder and it acquires a dependency on someone else's filesystem — a
stranger coupling than the vendored source this replaces, and one that breaks on a clean clone, on
CI, and whenever the publisher tidies up. Commit the `.nupkg` files into the consuming repository
and use a relative path.

That is not theoretical: deleting the superseded artifacts from the publishing folder here broke
nothing for a consumer who had committed them, and would have broken the build of one who had not.

## For CI

A folder on a developer's machine is not visible to a hosted runner. Two options:

- **Commit the `.nupkg` files** into the consuming repository — five files, versioned and immutable,
  and far less invasive than vendoring the source. Point the feed at that folder with a relative
  path.
- **Push to a feed the runner can reach** — Azure Artifacts, GitHub Packages, or any NuGet server.
  `dotnet nuget push` works against a folder feed too, so the publishing step is unchanged.

Vendoring the source is the last resort. It forks a library under active development, and it drifts
faster than people expect: the first consumer to try it was five commits stale within an hour.
