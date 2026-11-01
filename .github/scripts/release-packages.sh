#!/usr/bin/env bash
set -euo pipefail

# Hosted runners are isolated. Keep Avalonia's post-compiler paths neutral too:
# it does not apply Roslyn's PathMap to its CodeView and XAML document names.
source_root=/tmp/vellumtext-source
artifact_root=/tmp/vellumtext-packages
if [ -e "$source_root" ] || [ -e "$artifact_root" ]; then
  echo "::error::Release build roots already exist; refusing to mix builds."
  exit 1
fi
commit=$(git rev-parse HEAD)
git worktree add --detach "$source_root" "$commit"
mkdir "$artifact_root"
cd "$source_root"
version=$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' Directory.Build.props)
test -n "$version"
jq -n --arg target "https://raw.githubusercontent.com/lurebat/VellumText/$commit/*" \
  --arg source "$source_root/*" '{documents:{"/_/*":$target,($source):$target}}' > "$artifact_root/source-link.json"
properties=(
  "-p:CustomBeforeMicrosoftCommonTargets=$source_root/.github/release-packages.props"
  "-p:PackageSourceRoot=$source_root/"
  "-p:PackageArtifactsRoot=$artifact_root/build/"
  "-p:PackageSourceLinkFile=$artifact_root/source-link.json"
  "-p:RepositoryCommit=$commit"
  "-p:ContinuousIntegrationBuild=true"
)
dotnet build -c Release --artifacts-path "$artifact_root/build" "${properties[@]}"
dotnet test --no-build -c Release --artifacts-path "$artifact_root/build" "${properties[@]}" \
  --logger trx --results-directory "$artifact_root/test-results"
dotnet pack --no-build -c Release --artifacts-path "$artifact_root/build" "${properties[@]}" -o "$artifact_root/packages"
dotnet build tools/PackageInspector/PackageInspector.csproj -c Release --artifacts-path "$artifact_root/inspector"
dotnet "$artifact_root/inspector/bin/PackageInspector/release/PackageInspector.dll" --self-test
dotnet "$artifact_root/inspector/bin/PackageInspector/release/PackageInspector.dll" \
  "$artifact_root" "$version" "$commit" "$source_root" "$artifact_root/build" > "$artifact_root/package-provenance.json"
# Publication downloads these exact archives, not another build.
(cd "$artifact_root" && sha256sum packages/*.nupkg packages/*.snupkg > SHA256SUMS)
