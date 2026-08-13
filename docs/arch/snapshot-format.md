# Snapshot format

**Status:** Normative for `schemaVersion: 1`.

A snapshot is the agent-facing surface of an MCP server, captured as a file you
commit to your repository. It lives at `.detent/snapshot.json` by default.

## 1. The load-bearing constraint

**The primary UX of this file is reading it in a pull request diff.** Optimise
for git, not for machines. Every design decision below follows from that.

## 2. Shape

```jsonc
{
  "schemaVersion": 1,
  "server": {
    "name": "orifarm-brand-mcp",
    "version": "2.3.1",
    "protocolRevision": "2026-07-28"
  },
  "capabilities": { "tools": { "listChanged": true }, "resources": {} },
  "tools": [
    {
      "name": "search_products",
      "description": "Search the product catalogue for a given market.",
      "descriptionSha256": "9f2c...",
      "inputSchema": { /* canonicalised */ },
      "outputSchema": { /* canonicalised */ },
      "annotations": { "readOnlyHint": true, "openWorldHint": false }
    }
  ],
  "resources": [],
  "prompts": [],
  "digest": "sha256:4b81..."
}
```

## 3. Rules

**No timestamps, and no `capturedAt` field at all.** If the snapshot changes on
every run, nobody commits it, and the whole product fails. Capture metadata goes
in a sidecar or the run log, never in the artefact. This rule has no exceptions;
a field that varies run to run is a defect regardless of how useful it is.

**Deterministic serialisation.** Sorted keys, two-space indent, LF endings, no
trailing whitespace, trailing newline at EOF. Numbers in canonical form. Arrays
of named things (tools, resources, prompts) sorted by name; arrays whose order is
semantic (`enum`, `anyOf` branches) preserved as-is but compared order-insensitively
by the engine.

**Full description text stored, plus its SHA-256.** The hash makes diffing fast.
The text makes the PR review meaningful, which is the entire point. Storing only
the hash would make a description change show up as an unreadable hex delta.

**The two are normalised differently, and that is load-bearing.** `description`
holds the text as sent, NFC-normalised, with line structure intact so a reviewer
can read it. `descriptionSha256` covers the *comparison* form: NFC, every run of
whitespace collapsed to one space, trimmed.

The engine compares the hash, never the stored text. So re-wrapping a paragraph
changes what a reviewer sees in the pull request and produces no finding, while
changing a single word produces `MCPC304`. Hashing the stored text instead would
make every reflow a `behavioural` finding, and `behavioural` findings that mean
nothing are how a gate loses its audience.

**`digest` covers the canonical form of everything above it.** One-line integrity
check. It is computed over the serialised bytes of the document with the `digest`
field itself absent, not over an in-memory object graph.

**`schemaVersion` is an integer and bumps on any incompatible change.** A reader
encountering a version it does not know exits `2` (usage error) with a message
naming the version it found, never a partial parse.

## 4. Canonicalisation

Applied at capture time so the committed file is already canonical, and again in
the engine so a hand-edited file cannot skew a diff. `canonicalise` is idempotent
and this is verified by a property test.

- Resolve `$ref` and inline `$defs`.
- Normalise the three nullability spellings to one representation.
- Normalise numeric literals (`1` and `1.0` converge).
- Normalise text to NFC. Collapsing whitespace runs and trimming applies to the
  comparison form behind `descriptionSha256` only, never to the stored text.
- Sort object keys, ordinally. Ordering must not depend on the host locale any
  more than line endings depend on the host OS.
- Sort tools, resources, and prompts by name, so a server that paginates or
  reorders its listing does not read as a diff. Arrays whose order is semantic
  (`enum` values, `anyOf` branches) keep their order; the engine compares those
  order-insensitively instead.
- Preserve the distinction between an empty schema and an absent one, and
  between an absent annotation and a false one.

See `diff-rules.md` §9 for why each of these exists.

Requires globalization to be enabled. Under `InvariantGlobalization` the NFC step
is a silent no-op rather than an error; see `docs/adr/0006-globalization.md`.

## 5. Redaction

Known token shapes are redacted from all snapshot output before writing. A
snapshot is a committed file, so a leaked credential in one is a published
credential. A pre-commit check asserts snapshots contain no high-entropy strings;
see `security-model.md`.

## 6. Determinism test

`detent capture` against a fixed fixture, ten consecutive runs, byte equality
asserted across all ten. This is the Phase 1 exit criterion and is a permanent
test, not a one-off check.
