# detent

**Contract testing for MCP servers.** Capture the agent-facing surface of an MCP
server, commit it as a reviewable file, and fail your build when that surface
changes in a way that breaks the agents depending on it.

This is not a conformance checker. Conformance asks *does this server follow the
standard*, and the MCP project is building an official suite for that. `detent`
asks a different question: **did this server change under me?**

> [!WARNING]
> Pre-release. Phase 0 of the roadmap below is complete; the capture and diff
> engines are not built yet. Nothing here is usable as a CI gate today.

## Why this exists

A tool that was read-only when you approved it and is destructive today is not a
compatibility problem. It is a supply-chain compromise, and nothing else in your
pipeline will notice.

MCP tool descriptions are instruction text to a model. A server can ship benign
descriptions and annotations at approval time and mutate them later, with no
schema footprint at all. Nothing in the ecosystem currently pins that surface.
Pinning it is what this tool does, so **agent supply-chain integrity** is the
headline use case and compatibility gating is the entry-level one.

## How it will work

```bash
detent capture https://mcp.example.com/mcp -o .detent/snapshot.json   # commit this
detent diff .detent/snapshot.json https://mcp.example.com/mcp          # gate on this
```

Two modes:

1. **Snapshot** - did my own server's contract change since last commit? A
   provider-side regression gate.
2. **Contract** - does the server I depend on still satisfy what I actually use?
   Consumer-driven, Pact-style. This is the differentiator: a removed output
   field nobody reads should not wake you up, and one you do read should fail the
   build. Alert fatigue kills CI gates faster than bugs do.

### Findings are classified, not counted

| Class | Meaning | Default |
|---|---|---|
| `breaking` | A conforming consumer will now fail | fail |
| `security` | Trust or blast radius of a tool changed | fail |
| `behavioural` | Schema-compatible, but agent behaviour will likely change | warn |
| `notice` | Worth knowing, not a compatibility event | warn |
| `additive` | New capability, backwards compatible | pass |
| `cosmetic` | No semantic effect | hidden |
| `unanalysable` | Could not classify with confidence | warn, never silent |

`behavioural` does not exist in ordinary API contract testing, and it is the
reason MCP needs its own tool.

The engine is variance-correct, which most schema differs are not. Input schemas
are contravariant and output schemas are covariant, so the same edit classifies
differently depending on which side of the tool it lands on. Widening what a
server accepts is safe; narrowing is breaking. Producing more is safe; producing
less is breaking. Full rules: [`docs/arch/diff-rules.md`](docs/arch/diff-rules.md).

## Exit codes

Distinguishing these matters more than it looks. A flaky network must not read as
a broken contract, or people start ignoring the gate.

| Code | Meaning |
|---|---|
| 0 | Pass |
| 1 | Policy violation |
| 2 | Usage or configuration error |
| 3 | Target unreachable or transport failure |
| 4 | Internal error |

## Roadmap

| Phase | Scope | Status |
|---|---|---|
| 0 | Foundation, CI, architecture tests, normative docs | done |
| 1 | HTTP transport with SSRF and resource guards, `capture` | next |
| 2 | The diff engine, `diff`, human and JSON output | |
| 3 | Consumer contracts, `verify`, `init` | |
| 4 | NativeAOT release matrix, SARIF, GitHub Action, npm shim | |
| 5 | Dual protocol revisions, deprecation detection, `explain` | |

## Building

Requires the .NET 10 SDK (pinned in `global.json`).

```bash
dotnet build Detent.slnx
dotnet test Detent.slnx
dotnet publish src/Detent.Cli/Detent.Cli.csproj -c Release -r linux-x64
```

Ships as a NativeAOT single binary: no runtime install, 2.7 MB, 14 ms cold start
on win-x64 against budgets of 20 MB and 50 ms.

## Design notes

- [`docs/arch/diff-rules.md`](docs/arch/diff-rules.md) - normative classification rules
- [`docs/arch/snapshot-format.md`](docs/arch/snapshot-format.md) - the committed artefact
- [`docs/arch/security-model.md`](docs/arch/security-model.md) - threat model and controls
- [`docs/arch/testing.md`](docs/arch/testing.md) - golden corpus conventions
- [`docs/adr/`](docs/adr/) - why each dependency and design decision exists

The MCP server is treated as untrusted input throughout. See
[`SECURITY.md`](SECURITY.md).

## Licence

MIT. See [LICENSE](LICENSE).
