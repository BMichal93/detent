# 7. Mutation testing is blocked on .NET 10, tracked rather than worked around

**Status:** Accepted
**Date:** 2026-08-14

## Context

`docs/arch/testing.md` and `CLAUDE.md` state Phase 2's exit criterion as: every
row in `diff-rules.md` has a passing golden case, **and** the mutation score on
`Detent.Core.Diff` is above 85%, via Stryker.NET.

With every row but MCPC402 now implemented, this ADR records what happened when
that second half of the criterion was actually attempted rather than assumed.

## What was tried

`dotnet-stryker` 4.16.0 (the latest stable release; no newer version, preview or
otherwise, exists on NuGet as of this date) was installed and run against
`Detent.Core`, targeting `Detent.Core/Diff/**/*.cs` from within
`tests/Detent.Core.Tests`, in several configurations: default invocation, an
explicit source project path, and an explicit `--target-framework net10.0`.

Every attempt failed identically, early, before any mutant was ever generated:

```
Analyzing 1 test project(s).
Analyzing Detent.Core.Tests.csproj
No analyzer results to log. This indicates an early failure in analysis...
Analysis of project Detent.Core.Tests.csproj succeeded.
Analyzing 0 projects.
No project found, check settings and ensure project file is not corrupted.
Failed to analyze project builds. Stryker cannot continue.
```

Stryker's own project-under-test discovery (via Buildalyzer, which drives a
design-time MSBuild evaluation of the test project to find its
`ProjectReference` items) reports zero referenced projects, despite
`Detent.Core.Tests.csproj` referencing `Detent.Core.csproj` correctly - the same
reference `dotnet build` and `dotnet test` resolve without issue throughout this
project.

## Root cause

This is upstream, not local. `stryker-mutator/stryker-net` issue #3367 is the
same failure - "Analyzing 0 projects" - reported against .NET 9 on Stryker
4.8.1. `Directory.Build.props` in this repository does not set
`UseCommonOutputDirectory`, which is the other documented trigger for the same
symptom, so this is the direct SDK-version case rather than that one.

Stryker's newest stable release (4.16.0) exhibits the identical failure one SDK
generation later, on .NET 10. Buildalyzer's design-time build integration has
not kept pace with the newest SDKs at the point each of them ships, and nothing
in Stryker's CLI surface works around it - this is a project-discovery failure,
before mutation begins, not a mutation-run problem tunable via `stryker-config`.

## Decision

Do not chase a workaround. Downgrading `Detent.Core`'s target framework to
appease Buildalyzer would mean testing different code than what ships, which
defeats the purpose. Vendoring or patching Buildalyzer is disproportionate to
the problem for a project at this stage.

Instead: **the mutation-testing half of the Phase 2 exit criterion is deferred,
explicitly and by name, until Stryker.NET (or its Buildalyzer dependency) adds
working .NET 10 support.** `docs/arch/testing.md` and `CLAUDE.md` are updated to
say so rather than silently continuing to cite a criterion nobody can currently
check.

## What is not deferred

Everything else the golden corpus and property tests already provide stands on
its own: every rule row has a passing example, `diff(x, x)` is empty across the
whole corpus, canonicalisation is idempotent, and the harness has been
adversarially verified twice - once by injecting a false negative into
`DiffEngine` directly, and once by discovering (not assuming) that `MCPC901`
cannot be expressed as a golden case at all, because `SnapshotReader`'s own
depth cap trips first. Mutation testing would raise confidence in coverage
*quality* further still; its absence does not mean the engine is untested.

## Consequences

Re-run this exact check (`dotnet-stryker` against `Detent.Core` from
`tests/Detent.Core.Tests`) whenever a new Stryker.NET version ships, or when
upstream issue #3367 or its .NET 10 equivalent closes. If it succeeds, revert
the deferral language in `testing.md` and `CLAUDE.md` and record the achieved
score. This is a re-check with a clear pass condition, not open-ended
monitoring - it does not need a calendar reminder, just a note in the release
checklist.
