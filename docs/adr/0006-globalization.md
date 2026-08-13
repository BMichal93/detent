# 6. Globalization stays enabled

**Status:** Accepted
**Date:** 2026-08-13

## Context

The scaffolding set `InvariantGlobalization=true`, the usual choice for a small,
fast CLI. Separately, `docs/arch/snapshot-format.md` §4 and
`docs/arch/diff-rules.md` §9.4 require description text to be normalised to NFC
before comparison, so that a description re-encoded from NFD to NFC does not read
as a behaviour change.

These two decisions are incompatible, and the way they fail is the problem.

Measured on .NET 10, normalising `"café"` (five chars, combining acute):

| `InvariantGlobalization` | Result |
|---|---|
| `true` | returns unchanged, five chars, **no exception** |
| `false` | returns `"café"`, four chars, correct |

Under invariant globalization `String.Normalize` does not throw and does not
signal failure. It silently returns the input. Canonicalisation would claim to
normalise and quietly not do it.

## Decision

`InvariantGlobalization` is `false`. The property carries a comment in
`Directory.Build.props` explaining why it must stay that way.

A unit test normalises a known NFD string and asserts the NFC result. If anyone
sets the flag back, that test fails immediately rather than the failure surfacing
later as an unexplained description finding.

## Rationale

A silent no-op in canonicalisation is the worst available failure mode for this
project. Two descriptions differing only in Unicode encoding would produce a
spurious `behavioural` finding, and `behavioural` is the class the whole product
is built around. Worse, capture on a machine where normalisation works and a
machine where it does not would produce different bytes for the same server,
breaking the determinism guarantee that makes the snapshot committable at all.

The cost is small and was measured rather than assumed, on win-x64 NativeAOT:

| | Invariant | Full ICU | Budget |
|---|---|---|---|
| Binary size | 2.69 MB | 2.93 MB | 20 MB |
| Cold start, median | 14.4 ms | 17.1 ms | 50 ms |

Globalization data is not embedded in the binary; ICU is loaded from the host, so
the size cost is the small amount of enabling code rather than the data itself.

## Consequences

The binary now requires ICU on the host. On Linux that is `libicu`, present on
mainstream distributions but **absent from minimal images** such as Alpine and
some distroless variants, where .NET fails at startup rather than degrading.

The Phase 4 Dockerfile must install ICU explicitly, and the release notes must
say so. This is tracked in `docs/release-checklist.md`.

If a minimal-image build is ever genuinely required, the answer is a separate
publish configuration that sets invariant mode **and** disables description
normalisation with a loud startup warning, not a quiet flip of this flag.
