# 3. Hand-rolled MCP client instead of the official C# SDK

**Status:** Accepted
**Date:** 2026-08-13

## Context

Capturing a server's surface needs a small slice of MCP: `initialize` (or the v2
`server/discover`), `tools/list`, `resources/list`, `prompts/list`, and
eventually `tools/call` for behavioural probes. That is a few hundred lines of
JSON-RPC over HTTP.

The official `ModelContextProtocol` C# SDK is on pre-release builds while the
ecosystem absorbs the 2026-07-28 revision.

## Decision

Hand-roll a thin client behind an `IMcpProbe` interface, with one implementation
per protocol revision. Take no dependency on the official SDK.

## Rationale

A hard dependency on a pre-stable SDK that is mid-rewrite means this tool breaks
when that SDK breaks, on someone else's schedule, and inherits its entire
transitive tree.

Three things make the trade favourable rather than merely tolerable:

1. **The slice is genuinely small.** The v2 stateless transport makes it smaller,
   not larger, because there is no session management to implement.
2. **The security controls in `docs/arch/security-model.md` require control of
   the transport.** Response size caps, JSON depth caps, redirect policy, and
   SSRF re-resolution after redirect are not things an SDK exposes as knobs. A
   client we do not own cannot be made to enforce them.
3. **Dependency count is a security property here** (ADR-0001). A supply-chain
   integrity tool with a large transitive tree is an awkward argument to make.

The capture layer is roughly 15% of the code and the engine is
revision-agnostic, so spec churn is contained to the part that is cheap to
rewrite. That containment is the main structural reason `IMcpProbe` exists.

## Consequences

We own protocol correctness for the calls we make, and we track revisions
ourselves rather than getting them for free.

Revisit if the SDK stabilises and the maintenance cost inverts. That would be a
new ADR superseding this one, not an edit here.
