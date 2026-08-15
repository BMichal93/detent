# 9. Contract files are parsed by a hand-rolled subset parser, not YamlDotNet

**Status:** Accepted
**Date:** 2026-08-15

## Context

ADR-0002 sanctioned YamlDotNet for contract files, reasoning that a
hand-authored config file is worse to write as JSON. Phase 3 is the first
point that dependency was actually needed, and referencing it surfaced the
same category of problem ADR-0004 hit with the original CLI framework choice:
a plan assumption that did not survive contact with the toolchain.

## What was tried

`YamlDotNet` 18.1.0's ordinary `DeserializerBuilder` is reflection-based and
fails `IL3050` under NativeAOT - the exact shape of failure that forced the
System.CommandLine pivot in ADR-0004. YamlDotNet's own answer to this is a
Roslyn source generator, `Vecc.YamlDotNet.Analyzers.StaticGenerator`, which
generates a `StaticContext` at compile time from `[YamlSerializable]`
attributes and is meant to be AOT-safe by construction.

It was added, wired up per its own documentation, and produced no compiler
errors or warnings - and no generated code either. `YamlStaticContext`'s
inherited `GetTypeResolver()` threw `NotImplementedException` at runtime,
meaning the generator had not actually run.

This was confirmed rather than assumed. Forcing emission of every source
generator's output to disk
(`-p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=...`)
showed `System.Text.Json`'s generator producing its usual files in the same
project, and the YamlDotNet analyzer producing nothing at all, with no
diagnostic. Loading the analyzer DLL directly and inspecting it showed why: it
is compiled against `Microsoft.CodeAnalysis, Version=4.4.0.0`, an old, pinned
Roslyn version that fails to load (`FileNotFoundException`) under the Roslyn
the current .NET 10 SDK ships. The generator does not error when it cannot
load - it is silently absent, which is a worse failure mode than a build
error, because nothing signals it happened.

## Decision

Do not use YamlDotNet at all, in either the reflection-based or the
source-generated form. Parse the subset of YAML a `detent` contract file
actually needs with a hand-rolled parser in `Detent.Core.Contracts`:
`YamlParser` produces a small generic tree (`YamlMap`, `YamlList`,
`YamlScalar`), and `ContractYamlReader` walks it into `Contract` by hand, the
same way `SnapshotWriter`/`SnapshotReader` already hand-write the JSON
canonical form rather than delegate it to a generic library.

The supported subset: block mappings, block sequences including sequences of
mappings, inline `[a, b, c]` flow sequences, single- and double-quoted
scalars, and `#` comments. Explicitly out of scope: anchors, aliases,
multi-document streams, tags, and block scalars (`|`, `>`). Nothing in a
contract file needs any of those, and building a general YAML engine to parse
a bounded, self-authored config shape solves a harder problem than the one
this project has.

## Consequences

This was presented as a genuine fork rather than decided unilaterally: hand-roll
a parser, switch contract files to JSON (reusing the AOT-safe
`System.Text.Json` infrastructure already proven in this codebase), or ship
the reflection-based YamlDotNet and accept the AOT risk ADR-0004 already
rejected once. The choice landed on hand-rolling, which is also the only one
of the three that *reduces* the project's dependency count rather than adding
to it - `Detent.Core` still has zero package references, which the existing
architecture test enforces.

Writing the parser surfaced one more instance of the same class of problem,
one level down: `SplitTopLevel`'s first draft used `yield return`, and C#
compiles an iterator method to a state machine that reads
`Environment.CurrentManagedThreadId` to decide whether its enumerator can be
reused - a compiler-inserted reference to `System.Environment`, not one
written by hand, and enough to fail the architecture test that keeps
`Detent.Core` off the filesystem and the clock. Rewritten to return a
materialised `List<string>` instead. The lesson generalises: "no dependency
on X" has to mean no dependency the *compiler* introduces either, not only
the ones a human typed.

Contract files remain YAML, which is what a human editing and re-reviewing
one in a pull request actually wants, and ADR-0002's reasoning for choosing
it over JSON still holds. What changed is how it gets parsed, not what gets
written.
