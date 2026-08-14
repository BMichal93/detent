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
  buckets it falls into. **Still not wired to the CLI**: server-level rules are
  the only table left unimplemented, and shipping before they land would be the
  false negative the product exists to prevent.

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
