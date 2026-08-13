# CLAUDE.md

Router. Depth lives in `docs/`. Read the linked file before touching the area it
covers.

## What this is

A CLI that captures the agent-facing surface of an MCP server, stores it as a
reviewable file in your repo, and fails your build when that surface changes in a
way that breaks the agents depending on it.

Two modes: **snapshot** (did my own server's contract change?) and **contract**
(does the server I depend on still satisfy what I actually use?). The second is
the differentiator.

Not in scope: spec conformance testing. Conformance asks *does this server follow
the standard*; this tool asks *did this server change under me*. Different
question, different owner.

## Read before you touch

| Area | Read |
|---|---|
| Anything in `Detent.Core/Diff` | `docs/arch/diff-rules.md` - **normative** |
| Snapshot model, canonicalisation, writers | `docs/arch/snapshot-format.md` |
| `Detent.Transport`, or anything rendering server text | `docs/arch/security-model.md` |
| Tests, golden corpus | `docs/arch/testing.md` |
| Why a dependency or design exists | `docs/adr/` |

## Build

```bash
dotnet build Detent.slnx            # warnings are errors
dotnet test Detent.slnx
dotnet publish src/Detent.Cli/Detent.Cli.csproj -c Release -r win-x64
```

NativeAOT publish on Windows needs `vswhere.exe` on PATH:
`C:\Program Files (x86)\Microsoft Visual Studio\Installer`.

## Guardrails

These are the specific ways this codebase gets damaged. They are not style
preferences.

- **Never modify a file under `tests/golden/**/expected.json` to make a test
  pass.** If the expected output looks wrong, stop and ask. This is the highest
  risk behaviour in this repo: it converts a correctness failure into a silent
  green build, which is exactly the failure mode that destroys the product. The
  only legitimate way an expectation changes is a deliberate rule change, and
  then `docs/arch/diff-rules.md` changes in the same commit.

- **Never add a NuGet package without an ADR.** Dependency count is a security
  property of this project, not an implementation detail. A supply-chain
  integrity tool with a bloated transitive tree cannot make its own argument.

- **`Detent.Core` must not reference `System.Net`, `System.IO`, or `DateTime.Now`.**
  It is a pure function library: two snapshots in, findings out. No network, no
  filesystem, no clock, no randomness. Enforced by `ArchitectureTests`, not by
  convention. If you need I/O in a core code path, the design is wrong.

- **Every new classification rule requires three artefacts in one commit:** a row
  in `docs/arch/diff-rules.md`, a golden case, and a changelog line. No rule
  exists only in code.

- **Server-derived strings are tainted.** Anything reaching the console goes
  through `Sanitize()` first. Tool names, descriptions, titles, server
  instructions, quoted error text. The MCP server is untrusted input.

- **Never compare `Severity` values with `<` or `>`.** Policy is a set
  (`fail_on: [breaking, security]`), never a threshold. The enum's numeric order
  is arbitrary and ranking the classes is the user's judgement, not ours.

- **No `capturedAt` or any run-varying field in a snapshot.** If the file changes
  on every run, nobody commits it and the product fails.

## Conventions

- Comments explain **why**, not what. If a comment restates the code, delete it.
- Hyphens, not em dashes.
- British spelling in prose (`behavioural`, `canonicalise`). Identifiers and file
  formats use the spelling already published: the `behavioural` severity is
  normative, so do not "fix" it to `behavioral`.
- Warnings are errors. A warning is an untriaged defect - fix it or suppress it
  with a justification, never let it accumulate.
- Findings carry stable IDs (`MCPC102`). IDs are permanent and never reused.

## When the plan and the code disagree

`docs/arch/diff-rules.md` is the source of truth for classification. The
implementation is wrong, not the document.

For anything else, the project plan is a plan, not a specification. It was
written before the code and has already been wrong once in a way that mattered:
it selected a CLI framework that cannot be AOT-compiled and described a package
with no stable release as stable (ADR-0004). If a plan assumption does not
survive contact with the toolchain, say so and write an ADR rather than working
around it quietly.

## Before releasing

`docs/release-checklist.md` has the gates. The first is blocking and easy to
forget: **formal trademark clearance on the name.** ADR-0005 is an availability
screen, not legal clearance.

## Status

Phase 0 complete. Phase 1 (capture and snapshot) is next: HTTP transport with the
guards in `security-model.md`, then canonicalisation, then `detent capture`.

Phase 1 is done when `detent capture <url> -o snapshot.json` produces byte-identical
output across ten consecutive runs.
