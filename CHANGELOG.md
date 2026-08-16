# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Every classification rule change requires a line here, alongside a row in
`docs/arch/diff-rules.md` and a golden case. Three artefacts, one commit.

## [Unreleased]

### Added

- Solution skeleton: `Detent.Core`, `Detent.Transport`, `Detent.Formats`, `Detent.Cli`,
  and two test projects.
- `Severity` and `ExitCode` policy enums.
- `detent version`.
- Normative documentation: `docs/arch/diff-rules.md` (classification rules and
  finding IDs), `snapshot-format.md`, `security-model.md`, `testing.md`.
- ADRs 0001-0005.
- `CLAUDE.md` router and `AGENTS.md` pointer, with agent guardrails.
- Architecture tests enforcing that `Detent.Core` has no network, filesystem,
  clock, randomness, or package dependencies.
- CI: build, test, format check, and a NativeAOT publish matrix across
  linux-x64, win-x64, and osx-arm64 with a binary size budget gate.
- `SECURITY.md` disclosure policy and Dependabot configuration.
- `docs/release-checklist.md`, gating a public release on formal trademark
  clearance among other things.

### Phase 2 (in progress)

- `SnapshotReader`: parses canonical bytes under a depth cap, refuses an unknown
  `schemaVersion` outright rather than parsing part of it, and can verify the
  content digest separately.
- `Finding` model and the golden corpus harness. One directory per rule row,
  discovered by a parameterised theory; cases pin rule, class, and location but
  not message wording. Verified to fail on an injected false negative.
- `SchemaNormaliser`: inlines local `$ref` and `$defs`, collapses the three
  nullability spellings into one, reports recursion and unresolvable references
  as MCPC901/MCPC903 rather than following or swallowing them, and caps depth.
  External references are never fetched; `Detent.Core` has no network, so a
  pointer out of an untrusted snapshot cannot become an SSRF vector.
- `SchemaRules`: the contravariant and covariant rule tables as data, so the two
  sides of a tool cannot accidentally share a classification.
- Input schema rules MCPC101-118, all eighteen rows of diff-rules.md §4, each
  with a golden case written before the implementation.
- Output schema rules MCPC201-209, all nine rows of diff-rules.md §5, golden
  cases written before the implementation. `SchemaRules` fields the output
  table has no row for (constraints, `additionalProperties`, `default`, union
  branches) are typed nullable and `SchemaComparer` skips them, rather than the
  output table inheriting checks that only make sense for what a server
  accepts. MCPC208 (enum value added) always classifies `behavioural`, the
  documented no-contract default; promoting it to `breaking` for a consumer
  declaring `exhaustiveEnums` is Phase 3 work applied on top of this finding,
  not something this table decides on its own.
- Tool-level rules MCPC301-310, all ten rows of diff-rules.md §6, golden cases
  written before the implementation. `ToolComparer` handles description, title,
  and the four safety annotations; the four hints share one transition-checking
  helper parameterised by which direction is the dangerous one, so a hint
  losing its assertion entirely (MCPC310) and a hint flipping to the dangerous
  value (MCPC306/307/309) can never both fire for the same transition, and a
  hint improving or newly appearing correctly fires nothing, since
  diff-rules.md has no row for either.
- `ToolRenameDetector` for MCPC302: matches a removed tool against an added one
  by Jaccard similarity over input schema paths, output schema paths, and
  description tokens, averaged over whichever of those three a pair actually
  has in common, then assigned greedily by descending score so two
  simultaneous renames cannot cross-pair. Threshold is 0.75, set deliberately
  high: a missed rename degrades to the ordinary removal/addition pair, but a
  false one misleads the one part of a report a reviewer reads literally.
  Threshold and metric are documented as implementation detail, per
  diff-rules.md §6, so this can be revisited without a rule change.
- A rename is not a terminal event: the matched pair is also run through
  `ToolComparer` and both schema tables under its new name, so a tool renamed
  and downgraded in the same release still surfaces the downgrade instead of
  the rename hiding it. Pinned by a golden case carrying both MCPC302 and
  MCPC306 in the same diff, plus a negative case confirming two genuinely
  unrelated tools stay a removal/addition pair rather than a false rename.
- `DiffEngine.Diff` restructured around one loop per relationship - matched by
  name, matched by rename, then the remaining unmatched removals and additions
  - so a tool is compared exactly once regardless of which of those three
  buckets it falls into.

### Phase 2 continued: server-level rules

- `Snapshot.Instructions`: the `initialize` result's free-text field, previously
  never captured. Added as a sibling of `capabilities`, matching the shape of
  the result itself rather than nesting it under `server`. Stored verbatim like
  every other server-derived string, normalised for storage and separately for
  comparison, the same split already used for tool descriptions.
- Server-level rules MCPC401, MCPC403-407, six of the table's seven rows,
  golden cases written before the implementation. **MCPC402 (auth scheme or
  scopes changed) has no row**: nothing about authentication is ever captured
  anywhere in the pipeline, so there is nothing for a rule to compare. Adding
  it means teaching `Detent.Transport` to read `WWW-Authenticate` or an OAuth
  protected-resource metadata document - new capture surface with its own
  security shape, not a diff-engine task, and out of scope for this pass. This
  is the last unimplemented row in the whole rule set; `detent diff` stays
  unregistered until it lands or is explicitly descoped.
- A protocol revision change (MCPC404) now short-circuits the entire
  comparison rather than running alongside it: diff-rules.md §7 requires this
  explicitly ("must suppress the wall of false breaking changes that a
  revision bump otherwise produces"), because a revision boundary is a
  re-baseline event, not a compatibility one. Pinned by a golden case that
  changes the protocol revision, removes a tool, and edits a description in
  the same fixture, and asserts the result is the MCPC404 notice alone.
- MCPC405 (deprecated subsystem) is the one rule diff-rules.md documents as
  firing on a single snapshot rather than a transition, which conflicts with
  the diff(x, x)-is-empty invariant for any snapshot that legitimately uses a
  deprecated capability. The invariant test now asserts self-comparison
  produces nothing **except** MCPC405, rather than being weakened or excluding
  fixtures - it still fails on any other unexpected finding. Two pre-existing
  golden fixtures had an incidental, unrelated server-version bump between
  their before and after files; fixed by aligning the versions, since adding
  MCPC406 surfaced them as accidentally touching a second rule.
- MCPC902 (unrecognised or vendor keyword changed), the last remaining rule
  row. Applies identically to input and output schemas - an unmodelled
  keyword carries no known variance, so it lives directly in `SchemaComparer`
  rather than in either `SchemaRules` table. `SchemaNormaliser`'s three
  keyword-category lists moved from `private` to `internal` so `SchemaComparer`
  can union them into its "known keyword" set from one source of truth,
  rather than a second hand-maintained list that could quietly drift out of
  sync with the first.
- Golden cases backfilled for MCPC902 and MCPC903, per the guardrail that
  every rule needs one. **MCPC901 (depth cap) does not get one**: a schema
  deep enough to trip it, once embedded in a real snapshot document, trips
  `SnapshotReader`'s own document-wide depth cap first, since that limit
  counts from the document root rather than the schema root and is stricter
  for anything realistic - confirmed by trying, not assumed. The two caps
  exist for different reasons and MCPC901 is only reachable by constructing a
  schema in memory, bypassing `SnapshotReader` entirely, which is exactly what
  the pre-existing unit test in `SchemaNormaliserTests` already does; that
  test is now documented as MCPC901's authoritative pin instead of a golden
  directory nobody could write correctly.
- The diff(x, x) invariant exception widened from MCPC405 alone to MCPC405,
  MCPC901, and MCPC903: all three report whether a state holds for one
  snapshot, evaluated and deduplicated across both sides by `ReportIssues`
  rather than compared as a transition, so a self-comparison of a schema that
  is already unanalysable correctly reproduces the same finding rather than
  none. This was always true of MCPC901/903 by the original design of
  `ReportIssues`; the golden corpus simply had no fixture exercising either
  one until now.

Every row in diff-rules.md now has an implementation except MCPC402, which
remains blocked on capture surface this pass deliberately did not build. 46
rule rows implemented, 285 tests.

### Phase 2 shipped: `detent diff`

- `GatePolicy` and `PolicyEvaluator` in `Detent.Core.Policy`: findings in, an
  exit code and a fail/warn/pass partition out, by set membership only.
- `Detent.Formats`: `JsonRenderer` (its own DTOs, decoupled from `Finding`'s
  domain model) and `HumanRenderer`, which sanitizes every server-derived
  string before it reaches the returned text - verified by breaking the
  sanitization on purpose and watching four tests catch it.
- `MCPC402` formally deferred rather than silently missing (ADR-0008):
  `diff-rules.md`, `SECURITY.md`, and `DiffEngine`'s own remarks all say so.
  `detent diff` ships without it.
- `detent diff <baseline> <target> [--format human|json] [--fail-on ...]
  [--warn-on ...]`. Target is a live URL or another snapshot file,
  auto-detected. `Detent.Cli.Tests` created (there was none), using
  `InternalsVisibleTo` scoped to the test project - a command's `Create()` is
  its only entry point besides `Main`, unlike `Detent.Core`'s types, which are
  reachable through `DiffEngine.Diff` without it.
- Caught two real bugs writing that test project. `Uri.TryCreate` parses an
  absolute Windows path like `C:\snapshots\after.json` as a valid URI with
  scheme `file`, so the original target-resolution logic would have routed
  every local file target on Windows into the HTTP transport, always failing
  with a confusing scheme error; fixed by requiring the scheme be `http` or
  `https` specifically. And the test harness itself was wrong first: wrapping
  the `diff` `Command` in a fresh `RootCommand` for in-process invocation
  requires passing `"diff"` as the first argument, which none of the tests
  did; fixed by parsing directly against the command.
- Verified end to end against a live server: captured a baseline, diffed it
  against the unchanged server (clean, exit 0), then flipped a tool's
  `readOnlyHint` from `true` to `false` and diffed again - `MCPC306`,
  correctly classified `security`, exit 1, in both output formats. This is
  the rug-pull scenario the project exists to catch.

`detent capture` and `detent diff` together are the core v0.1 loop from the
project plan. Consumer contracts (`verify`, `init`, `.detent/contract.yaml`)
are Phase 3.

## Phase 3: consumer contracts (in progress)

### Contract model and scoping

- `Contract`, `ToolRequirement`, `ToolAssumptions`, `ContractPolicy`, and
  `Suppression` in `Detent.Core.Contracts`: the plain data model behind
  `.detent/contract.yaml`.
- `ContractScope.Apply`: narrows an ordinary diff's findings per
  diff-rules.md §8. A finding on a tool the contract never declares using is
  dropped entirely; on a declared tool, a finding on an input/output property
  absent from `sends`/`reads` is dropped. `MCPC208` is the one exception,
  promoted to `breaking` when the field is listed in `exhaustiveEnums`.
  Server-level findings and tool-level ones with no single attributable
  property pass through unfiltered.
- `ContractScope.CheckAssumptions`: a new rule, `MCPC501` (§12 of
  diff-rules.md, a new ID range distinct from MCPC1-9xx), checking a
  contract's `assumes` against the candidate snapshot directly rather than
  diffing - a tool that never once satisfied an assumed annotation produces
  nothing to diff, and still has to be caught. Always `security`.
- `ContractScope.ApplySuppressions`: expiring `ignore` entries. `today` is
  always an explicit `DateOnly` parameter - `Detent.Core` reads no clock, so
  the caller decides what "today" means. A `security` finding is never
  suppressed even under an active entry.

### YamlDotNet tried and dropped: hand-rolled parsing instead

- YamlDotNet's only AOT-safe path, its Roslyn source generator, does not work
  under the current .NET 10 SDK: it silently produces no generated code
  rather than erroring, traced to the generator DLL being compiled against
  `Microsoft.CodeAnalysis 4.4.0` and failing to load under the SDK's actual
  Roslyn version. Confirmed by comparison against `System.Text.Json`'s
  generator working correctly in the same project, and by loading the
  analyzer DLL directly. See [ADR-0009](docs/adr/0009-hand-rolled-yaml.md).
- Presented as a genuine three-way fork rather than decided alone: hand-roll a
  parser, switch contracts to JSON, or ship YamlDotNet reflection-based and
  accept the AOT risk ADR-0004 already rejected once for the CLI framework.
  Chose to hand-roll.
- `YamlParser` and `ContractYamlReader` now live in `Detent.Core.Contracts`
  rather than `Detent.Formats`: a hand-rolled parser needs no package
  reference, so it fits `Detent.Core`'s zero-dependency rule directly, which
  is architecturally simpler than the original Formats-based design that
  YamlDotNet would have required.
- Supports the subset a contract file actually needs: block mappings and
  sequences (including sequences of mappings), inline `[a, b, c]` lists,
  quoted and unquoted scalars, and `#` comments. Deliberately not general
  YAML - no anchors, aliases, multi-document streams, or block scalars.
- Writing it surfaced the same class of problem one level down: an early
  draft of one internal method used `yield return`, and C# compiles an
  iterator to a state machine that reads `Environment.CurrentManagedThreadId`
  to decide whether its enumerator can be reused - a compiler-inserted
  reference to `System.Environment`, not a hand-written one, and enough to
  fail the architecture test keeping `Detent.Core` off the clock and the
  filesystem. Rewritten to return a materialised list instead. "No dependency
  on X" has to hold against what the compiler adds, not only what a human
  types.
- 21 tests against the parser and reader, including the full contract example
  from the project plan, comments, quoted strings, a URL's `://` not being
  mistaken for the mapping separator, and tabs rejected outright.

367 tests total. `Detent.Core` still has zero package dependencies and no
clock access, confirmed by the architecture tests rather than assumed.

### `detent verify` ships

- `detent verify <baseline> <target> --contract <path> [--format human|json]
  [--fail-on ...] [--warn-on ...]`. Pipeline: diff, then
  `ContractScope.Apply` (narrow and promote), then
  `ContractScope.CheckAssumptions` (add MCPC501 findings), then
  `ContractScope.ApplySuppressions` (drop what an active `ignore` covers),
  then policy evaluation. `--fail-on`/`--warn-on` override the contract's own
  `policy:` block when given; otherwise the contract's policy applies,
  falling back to `GatePolicy.Default` field by field.
- `DateOnly.FromDateTime(DateTime.Now)` is computed in `VerifyCommand` and
  passed into `ApplySuppressions` as `today` - the one place in the whole CLI
  that reads the clock on `Detent.Core`'s behalf, exactly at the I/O boundary
  the architecture rule expects.
- `--fail-on`/`--warn-on` parsing and target/baseline resolution extracted
  from `DiffCommand` into shared `PolicyOptions`/`SnapshotResolution` helpers,
  used by both commands rather than duplicated.
- 27 CLI tests (13 normal, 14 edge), each pipeline stage verified
  adversarially rather than trusted: bypassing `ContractScope.Apply` failed
  exactly the 7 tests that depend on scoping; bypassing
  `CheckAssumptions` failed exactly the 3 that depend on it. Covers the
  plan's exit criterion directly, undeclared tools being dropped entirely,
  `exhaustiveEnums` promotion (and its precedence under `reads` - promotion
  never runs for a field the contract does not read at all), suppressions
  including the security-is-never-suppressed rule, and an assumption
  violated with no underlying change firing on a first-ever run.
- Caught a real concurrency bug while adding the second CLI test class:
  `CliInvoker` redirects `Console.Out`/`Console.Error`, which is process-wide
  mutable state, and xUnit parallelises different test classes by default.
  `DiffCommandTests` and `VerifyCommandTests` running concurrently corrupted
  each other's captured output - the full suite failed while each class
  passed alone. `CliInvoker`'s own remarks had predicted this would happen
  the moment a second Console-touching class existed; fixed with a shared
  `[Collection(nameof(ConsoleTests))]` forcing them sequential. Reproduced
  five consecutive full-suite runs afterward to confirm the fix, not just one.
- Verified end to end against a live server with every scoping rule exercised
  in one run: an unsent input property removed, a read output property
  removed, an entirely undeclared tool removed, and an assumed annotation
  flipped. The unscoped `diff` reported four findings, two of them noise
  (the unsent property, the undeclared tool). `verify` dropped exactly those
  two, kept the two real ones, and added the MCPC501 finding no diff could
  produce - the actual "does the server I depend on still satisfy what I use"
  question this phase exists to answer, working.

394 tests total, all green, CI passing on NativeAOT for all three platforms.

### Decided: `mcplock` relationship, and a trademark re-screen

- `detent` is the successor to `mcplock` (project plan §2, Option A):
  archived, not an active sibling. Stated in the README, per the plan's own
  instruction to write this down on day one; ADR-0005 records the reasoning.
- Re-screened the name on request, deeper than the original 2026-08-13 pass.
  Found `detentapp.com`, a live source-available trading-discipline tool
  already trading under "Detent" - a real software product with the
  identical name, not a same-word collision in an unrelated field like the
  originally-rejected candidates. No trademark registration found for it.
  Not treated as disqualifying - no registration to be blocked by, and
  consumer confusion (the actual legal standard) is unlikely between a
  personal trading-risk tool and a developer CI gate - but it is exactly the
  kind of common-law-use fact a web screen cannot reliably clear and a
  professional search needs to weigh. `docs/release-checklist.md` now names
  it specifically for whoever runs that search. See ADR-0005's addendum.

### Investigated

- Whether Stryker.NET's mutation-testing gate (Phase 2's other exit criterion,
  alongside the golden corpus) is achievable on .NET 10. It is not, as of the
  latest release: `dotnet-stryker` 4.16.0 fails before generating a single
  mutant, in every configuration tried, because Buildalyzer's design-time
  project analysis reports zero referenced projects for a correctly-configured
  test project - a known upstream issue (stryker-mutator/stryker-net #3367 for
  .NET 9) that has not resolved one SDK generation later on .NET 10. ADR-0007
  records the investigation and what would need to be true to unblock it.
  `CLAUDE.md`, `testing.md`, and `release-checklist.md` now say so explicitly
  rather than continuing to cite an unverifiable criterion as if it were live.

### Phase 1

- Snapshot model: `Snapshot`, `ServerIdentity`, `ToolDescriptor`,
  `ToolAnnotations`, `ResourceDescriptor`, `PromptDescriptor`. Annotations are
  nullable throughout so an absent hint stays distinguishable from a false one.
- `CanonicalJson`: deterministic serialisation with ordinally sorted keys,
  two-space indent, LF endings, normalised numeric literals, and a trailing
  newline.
- `TextNormaliser`: separate storage and comparison forms, so a reflowed
  description is visible in a pull request but is not a `behavioural` finding.
- `SnapshotWriter`: canonicalisation, description hashing, and the `sha256:`
  content digest, with the ten-consecutive-captures determinism check as a
  permanent test.
- `Sanitizer`: strips C0/C1 controls, zero-width characters, bidirectional
  overrides (Trojan Source), and line/paragraph separators from server text.
- `AddressGuard`: the SSRF blocklist, including cloud metadata at
  `169.254.169.254` and every IPv6 form that can carry an IPv4 address inside
  it - v4-mapped, 6to4, NAT64, and the deprecated v4-compatible `::/96`.
- `GuardedHttpClient`: address vetting inside the connect callback so there is
  no window for DNS rebinding, hand-followed redirects capped at 3 and refused
  across hosts or down to http, a streamed 10 MB body cap, and `--insecure`
  refused off loopback and refused outright in CI.
- `StreamableHttpProbe` behind `IMcpProbe`: `initialize` plus one listing call
  per advertised capability, JSON or SSE, pagination walked to the end under
  page and item caps, all inside a whole-operation 30 second budget. Capture
  never calls a tool, so it cannot cause a side effect on the server.
- `detent capture <url> [-o path] [--allow-host] [--insecure]`, writing bytes
  directly so the canonical form survives to disk. Exit 3 for transport
  failure, distinct from a policy failure.

### Changed

- The tool is named `detent`, not `mcpc`. A detent holds a mechanism in a chosen
  position until deliberate force moves it, which is what this tool does to a
  server's contract. Screened clean on npm, NuGet, GitHub, and USPTO software
  classes. Finding IDs keep the `MCPC` prefix, which stands for MCP Contract and
  names the subject of analysis rather than the tool. See ADR-0005.
- CLI framework is `System.CommandLine` 2.0.11 rather than
  `Spectre.Console.Cli`. The latter fails AOT compilation (`IL3050`, reflection
  based command binding) and has no stable release; the former reached 2.0 GA.
  See ADR-0004.

## Phase 3 complete: `detent init`

- `ContractScaffolder.FromSnapshot`: builds the most permissive contract that
  still says something - every tool in the snapshot, every top-level
  input/output property in `sends`/`reads`. Schemas are run through
  `SchemaNormaliser` first, so a tool built from `$ref`/`$defs` scaffolds its
  real property names rather than nothing. Never guesses `exhaustiveEnums` or
  `assumes` - both require knowing what the consumer's own code does.
- `ContractYamlWriter`: renders a `Contract` back to the YAML
  `ContractYamlReader` reads, with trailing comments telling the person
  editing it what to narrow and how to add `assumes`/`exhaustiveEnums`/
  `policy`. Both live in `Detent.Core.Contracts`, pure, no I/O.
- `detent init <target> --consumer <name> [-o path]`. Target is a live URL or
  an existing snapshot file, same auto-detection `diff`/`verify` already use.
- Caught a real bug writing the round-trip test: an empty tools list rendered
  as `tools:` followed by a nested `[] # comment` line, which the parser reads
  as a malformed mapping entry rather than an inline empty sequence. Fixed by
  writing `tools: []` inline on the same line when there is nothing to list -
  found by testing the writer against the reader it has to agree with, not by
  reading the writer's own code and assuming it was right.
- 14 tests for the scaffolder and writer (including the round-trip and the
  `$ref` case), 12 CLI tests for `init` verified adversarially - bypassing the
  scaffolding call failed exactly the 4 tests that depend on tool count and
  content.
- Verified end to end against a live server, including the one path the
  in-process test harness structurally cannot observe: `-o -` writes raw
  bytes straight to the OS stdout handle (the same deliberate pattern
  `CaptureCommand` already uses), which `CliInvoker`'s `Console.Out`
  redirection never sees. Confirmed instead with the real published binary,
  piped through `head`. Then closed the full loop: scaffolded a contract live,
  hand-narrowed it the way the generated comments ask, and ran `verify`
  against it - clean.

Phase 3 is done: `detent capture`, `diff`, `verify`, and `init` all work
end to end against real servers, not only in unit tests. 420 tests total, all
green, stable across repeated runs. AOT publish clean at 7.71 MB.

## Phase 4 (in progress): SARIF and Markdown output

- `SarifRenderer`: SARIF 2.1.0. The plan calls this the best effort-to-reach
  ratio in the whole project - native rendering in GitHub code scanning and
  Azure DevOps for about half a day of work. Findings have no file or line
  behind them - `tools/search_products/inputSchema/properties/query` is a
  position in a server's advertised surface, not a source file - so every
  result carries a `logicalLocation` rather than a `physicalLocation`, which
  is what SARIF's logical-location concept is for. The rule catalog lists
  only IDs that actually occur in a given run, not the full ~47-row table,
  so it can never drift out of sync with `docs/arch/diff-rules.md` by
  duplicating it.
- `MarkdownRenderer`: GitHub-flavoured tables, meant for a PR comment or a CI
  job summary. Every cell goes through the same `Sanitizer.Sanitize` the
  human renderer uses, then through Markdown-specific escaping on top -
  `|` would otherwise terminate a table cell early, and an unescaped
  backtick in a path would break out of the fence the path is wrapped in and
  let a server-controlled tool name corrupt the table structure itself, not
  merely render oddly inside its own cell. Verified adversarially: removing
  the escaping failed exactly the 3 tests that depend on it, after first
  catching that my own adversarial edit had silently no-op'd (a Python
  heredoc escaping mismatch left the file unchanged) by checking the file
  actually differed before trusting a suspiciously all-green run.
- `--format` on `diff` and `verify` now accepts `sarif` and `markdown`
  alongside `human`/`json`, dispatched through a shared `OutputFormat` helper
  in `Detent.Cli` rather than duplicated per command.
- `ToolVersion` extracted from `VersionCommand` into a shared helper, since
  the SARIF driver needs the same informational-version lookup `version`
  already had.
- 26 renderer tests plus 2 CLI dispatch smoke tests. Verified against a live
  server for both new formats: an unchanged server produces a structurally
  valid, empty SARIF run; a removed tool produces a correctly-leveled
  (`error`) SARIF result and a correctly-escaped Markdown table row.

440 tests total, all green. AOT publish clean at 7.90 MB, still well inside
the 20 MB budget.
