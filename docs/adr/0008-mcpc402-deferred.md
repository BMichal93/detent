# 8. MCPC402 (auth scheme or scopes changed) is deferred, and `detent diff` ships without it

**Status:** Accepted
**Date:** 2026-08-15

## Context

46 of 47 rows in `diff-rules.md` are implemented, each with a passing golden
case. `MCPC402` is the one exception: nothing about authentication is captured
anywhere in the pipeline, so there is no field for a rule to compare. Closing
it means teaching `Detent.Transport` to read `WWW-Authenticate` or an OAuth
protected-resource metadata document - new capture surface with its own threat
model, not an extension of the diff engine.

`DiffEngine`'s own doc comment states the project's guardrail plainly: nothing
may expose this engine to a user until every row has a passing golden case. A
literal reading blocks `detent diff` indefinitely on a capture feature that
does not exist yet.

## Decision

Ship `detent diff` now, with `MCPC402` formally descoped for v0.1 rather than
silently missing. Three things make this the safety-maximising choice rather
than a compromise of it:

1. **The guardrail protects against a different failure than this one.** Its
   purpose is stopping the engine from silently misclassifying data it already
   has - the false negative that manufactures confidence diff-rules.md §0
   warns about. `MCPC402`'s gap is not that: `detent diff` never inspects an
   auth header at all, and says so. A tool that is honest about what it does
   not check does not manufacture false confidence; a tool that is silent
   about the gap does.

2. **The rug-pull protection this project leads with does not depend on
   MCPC402.** `security-model.md` §4 names the concrete attack class -
   annotation downgrades, description mutations, new tools appearing in a
   trusted server - and every finding in that list (MCPC303, MCPC304, MCPC306,
   MCPC307, MCPC309, MCPC310) is implemented, golden-cased, and about to ship.
   Withholding all of that indefinitely to wait on one additional, unrelated
   security signal protects nobody in the meantime.

3. **Rushing MCPC402 to close this gap would itself be the less safe choice.**
   Every other addition to `Detent.Transport` in this project got a careful
   threat-model pass before landing - the address guard, the redirect policy,
   the resource caps. Building WWW-Authenticate parsing or OAuth metadata
   following under pressure to complete a table, rather than with the same
   care, is how a supply-chain integrity tool becomes the vulnerability. See
   `docs/adr/0003-no-official-mcp-sdk.md` for the same reasoning applied to
   the transport layer as a whole.

## What changes as a result

- `docs/arch/diff-rules.md` §7's `MCPC402` row is marked deferred, with a
  pointer to this ADR, rather than presented as an ordinary unimplemented row.
- `SECURITY.md`'s scope section states plainly that auth posture changes are
  not currently detected, so nobody reads the tool's security claims as wider
  than they are.
- `DiffEngine`'s remarks are updated: the "nothing may expose this engine"
  guardrail is satisfied as of this ADR, by deliberate exception for MCPC402
  specifically, not by the row quietly stopping being counted.
- `detent diff` is registered as a CLI command.

## Consequences

A user relying on `detent diff` to catch an auth-scope escalation will not be
warned by this tool. `SECURITY.md` says so. If a real incident or a concrete
user request makes this gap load-bearing, building the capture surface -
deliberately, with its own security-model.md entry - is the correct response,
not a retroactive judgement that this deferral was wrong. The kill-criteria
framing in the project plan applies here too: an unbuilt feature with no
demonstrated need is not a failure.
