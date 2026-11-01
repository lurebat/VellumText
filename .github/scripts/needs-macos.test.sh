#!/usr/bin/env bash
# Regression tests for needs-macos.sh. Run with `bash .github/scripts/needs-macos.test.sh`.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
failures=0

expect() {
  local name=$1 want=$2 got
  got=$(bash "$here/needs-macos.sh")
  if [ "$got" = "$want" ]; then
    echo "ok   $name"
  else
    echo "FAIL $name: expected $want, got $got"
    failures=$((failures + 1))
  fi
}

# Paths that do not need macOS, enough of them to be well past a pipe buffer (64 KiB).
filler() {
  local i
  for ((i = 0; i < $1; i++)); do
    echo "src/VellumText.Core/Generated/SomeLongishFileNameNumber$i.cs"
  done
}

avalonia=src/VellumText.Avalonia/RichTextView.cs

expect 'one Avalonia path' true <<<"$avalonia"
expect 'one Core path' false <<<"src/VellumText.Core/Model/Node.cs"
expect 'the picker itself' true <<<".github/scripts/needs-macos.sh"
expect 'a large change with no Avalonia path' false < <(filler 20000)

# The regression: grep -q stops reading at the first match, so whatever is still being written
# into a pipe after that point gets SIGPIPE, and under pipefail the match reads as a miss. An
# Avalonia path with a megabyte of other paths after it dropped macOS from a large pull request.
expect 'a large change with an Avalonia path late in it' true \
  < <(filler 5000; echo "$avalonia"; filler 20000)
expect 'a large change with an Avalonia path first' true \
  < <(echo "$avalonia"; filler 20000)
expect 'a large change with an Avalonia path last' true \
  < <(filler 20000; echo "$avalonia")

if [ "$failures" -ne 0 ]; then
  echo "$failures failed"
  exit 1
fi
echo 'all passed'