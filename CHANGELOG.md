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
