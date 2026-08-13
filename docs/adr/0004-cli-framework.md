# 4. System.CommandLine as the CLI framework

**Status:** Accepted
**Date:** 2026-08-13

## Context

The project plan selected `Spectre.Console.Cli`, described as stable and MIT, and
flagged `System.CommandLine` with "verify its stable status before committing" on
the grounds that it had spent years in preview and churned its API.

Both halves of that assessment have since inverted, and the scaffolding build
surfaced it immediately:

1. **`Spectre.Console.Cli` fails the AOT requirement.** Building the skeleton CLI
   produced `error IL3050` on the `CommandApp` constructor: the library relies on
   reflection for command discovery and settings binding, and is documented as
   unsupported under trimming and AOT. NativeAOT is not negotiable here, so this
   is not a suppressible diagnostic. Suppressing it would ship a binary whose
   argument parsing fails at runtime under `TrimMode=full`.
2. **`Spectre.Console.Cli` has no stable release.** The highest published version
   is `0.51.1`; the only 1.0 line on NuGet is `1.0.0-alpha.0.16`. The plan's
   "stable" premise was simply wrong.
3. **`System.CommandLine` is now stable.** 2.0.0 shipped GA and the current
   version is `2.0.11`. The plan's open question resolves in its favour.

## Decision

Use `System.CommandLine` 2.0.11 for argument parsing, help, and dispatch.

## Consequences

The AOT toolchain is clean: zero warnings with the trim, AOT, and single-file
analyzers all enabled, and a verified `win-x64` publish at 2.7 MB with a 14 ms
median cold start, against budgets of 20 MB and 50 ms.

We give up Spectre's rich console rendering. This costs little today because
`--format human` is a Phase 2 concern, and the human renderer must route all
server-derived text through `Sanitize()` regardless (see
`docs/arch/security-model.md`). Hand-written rendering against a sanitising
writer is a better fit for that constraint than a general-purpose markup library
that would need auditing for the same property.

If rich rendering is wanted later, `Spectre.Console` core (the rendering library,
not `.Cli`) can be evaluated separately. That would be a new ADR, and would need
to clear both the AOT bar and the taint rule.
