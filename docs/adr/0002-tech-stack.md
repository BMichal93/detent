# 2. Tech stack

**Status:** Accepted
**Date:** 2026-08-13

Referenced from `Directory.Packages.props`. Every package version entry there
maps to a row here or to a later ADR.

## Context

The product is a CI gate. That implies three hard requirements: it must start
fast enough that nobody notices it, install without a runtime prerequisite on an
arbitrary build agent, and carry a small enough dependency tree that its own
supply chain is not the weakest link in a supply-chain integrity tool.

## Decision

| Choice | Decision | Rationale | Rejected |
|---|---|---|---|
| Runtime | .NET 10 LTS | Support window through 2028, NativeAOT mature | .NET 9 (STS, shorter runway) |
| Distribution | NativeAOT single file per RID | No runtime install, sub-50ms start | Framework-dependent (needs an SDK on the agent) |
| CLI framework | `System.CommandLine` | See ADR-0004 | `Spectre.Console.Cli` |
| JSON | `System.Text.Json` with source generators | AOT-compatible, in-box, zero added supply-chain surface | Newtonsoft (reflection-heavy, AOT-hostile) |
| MCP client | Hand-rolled thin client | See ADR-0003 | Official `ModelContextProtocol` C# SDK |
| Schema validation | `JsonSchema.Net` (MIT), probe validation only | The diff walks the tree with our own code | Using a validator as a differ, which is the wrong tool |
| YAML | `YamlDotNet` (MIT), contract files only | Contracts are hand-written; JSON is worse to hand-write | JSON contracts |
| Tests | xUnit plus a golden corpus | Deterministic, reviewable | Auto-updating snapshot libraries (see `docs/arch/testing.md` §3) |

Packages are added at the phase that first needs them, not up front.
`JsonSchema.Net` and `YamlDotNet` are Phase 3; neither is referenced yet.

### Performance budget

Enforced in CI from Phase 4 via BenchmarkDotNet. "Swift" is a requirement, so it
gets measured rather than hoped for.

| Metric | Budget | Measured 2026-08-13 |
|---|---|---|
| Cold start to first output | < 50 ms | 14 ms median (win-x64) |
| Capture, 50 tools, localhost | < 300 ms | not yet |
| Diff, 200 tools | < 50 ms | not yet |
| Binary size, linux-x64 | < 20 MB | 2.7 MB (win-x64) |
| Peak RSS | < 60 MB | not yet |

## Consequences

The .NET choice costs the npm distribution channel, which is where most MCP
developers install things. Mitigated in Phase 4 by prebuilt binaries, a GitHub
Action, and a thin npm wrapper that downloads the right binary. This is a real
cost, taken deliberately rather than by accident.

`Detent.Core` takes no package references at all. It is a pure function library and
that is enforced by an architecture test, not by convention.
