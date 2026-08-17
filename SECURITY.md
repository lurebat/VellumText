# Security policy

## Reporting a vulnerability

Please report security issues privately, through GitHub's
[private vulnerability reporting](https://github.com/lurebat/VellumText/security/advisories/new)
rather than as a public issue.

Include what you can reproduce with: the input, what happened, and what you
expected. A failing test is ideal.

## What counts as a vulnerability here

VellumText is a UI library, not a service, so the threat model is narrower than it
looks — but it is not empty, because **import runs on untrusted input by
definition.** Anything arriving from the clipboard, a drop, or a file came from
somewhere else, and a document is a rich, attacker-shaped thing.

Taken seriously:

- **Script or active content surviving HTML import.** The importer works from
  an allow-list: `<script>`, event handler attributes, `<iframe>`, `<object>`
  and `javascript:` / `data:` URLs are dropped unconditionally. Anything that
  gets an executable construct into a document is a vulnerability.
- **Network access the caller did not ask for.** Remote images are not fetched
  unless the host opts in. An importer that reaches the network by default —
  or that can be made to, including through a redirect — is a vulnerability.
- **Reading outside the document.** A path in a pasted document resolving to
  the local filesystem, or to an internal address, is a vulnerability.
- **Denial of service from a small input.** Import is quadratic or worse on
  some shape of input, or a deeply nested fragment overflows the stack, or a
  malformed file loops forever. Importers are expected to be linear-ish and
  bounded in depth.
- **Crashes reachable from import.** Importers return diagnostics rather than
  throwing. An unhandled exception from a malformed document is a bug and, if
  it is reachable from a paste, a security bug.

Out of scope: fidelity problems. An importer that mangles the formatting of an
exotic document is a bug, and worth reporting as one, but it is not a
vulnerability.

## Supported versions

VellumText is pre-1.0. Fixes go onto `main` and into the next release; there are no
maintained release branches yet.
