#!/usr/bin/env bash
# Reads the paths a pull request changed, one per line on stdin, and prints `true` if the
# change is one macOS should build and test, `false` otherwise. ci.yml's `platforms` job uses
# it; needs-macos.test.sh pins it.
set -euo pipefail

changed=$(cat)

# What macOS actually catches: the Avalonia control and its tests, the demo, and the build
# itself, including this script.
pattern='^(src/VellumText\.Avalonia/|tests/VellumText\.Avalonia\.Tests/|samples/|Directory\.Build\.props$|global\.json$|VellumText\.sln$|\.github/workflows/ci\.yml$|\.github/scripts/needs-macos\.sh$)'

# A here-string, not `printf | grep -q`: grep -q exits at the first match, whatever is still
# being written into the pipe then gets SIGPIPE, and under pipefail that makes a match read as a
# miss - which silently dropped macOS from large pull requests.
if grep -Eq "$pattern" <<<"$changed"; then
  echo true
else
  echo false
fi