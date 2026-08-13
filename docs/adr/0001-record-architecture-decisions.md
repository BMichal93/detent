# 1. Record architecture decisions

**Status:** Accepted
**Date:** 2026-08-13

## Context

This project treats dependency count as a security property rather than an
implementation detail, and it commits to a normative rule set that code must
follow rather than define. Both need a written record of why each choice was
made, because both will be questioned later by someone (possibly the author)
who no longer remembers the reasoning.

Much of the implementation work is agent-driven. An agent that cannot see why a
constraint exists will route around it in good faith.

## Decision

Architecture decisions are recorded as numbered Markdown files under `docs/adr/`,
in the order they are made. An ADR is immutable once accepted: it is superseded
by a later ADR, never edited in place, except to add the superseding link.

**Every NuGet package reference must be justified by an ADR.** This is enforced
by review, and `Directory.Packages.props` carries a comment pointing here.

Status values: Proposed, Accepted, Superseded by ADR-NNNN.

## Consequences

Adding a dependency costs a document, deliberately. The friction is the point.

The `docs/arch/` directory holds normative specifications that change as the
product evolves; `docs/adr/` holds point-in-time decisions that do not. A rule
change edits `diff-rules.md`. A decision change writes a new ADR.
