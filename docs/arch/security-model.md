# Security model

**Status:** Normative. Read this before touching anything in `Detent.Transport` or
anything that renders server-derived text.

Two halves: the tool must not be the vulnerability, and the tool exists to find a
vulnerability class.

## 1. Threat model

**The MCP server is untrusted input.** It is a remote endpoint that returns
attacker-controllable JSON, attacker-controllable text destined for an LLM, and
attacker-controllable text destined for your terminal. Every control below
follows from taking that sentence literally.

| Threat | Control |
|---|---|
| **SSRF** - target URL from config or a registry entry | Block loopback, link-local (`169.254.0.0/16`, cloud metadata), RFC1918, and multicast by default. `--allow-host` for explicit opt-in. Re-resolve and re-check after every redirect (DNS rebinding). Cap redirects at 3, refuse cross-host redirects. |
| **Command injection** via stdio transport | `ProcessStartInfo.ArgumentList` only, never a shell string. Never `UseShellExecute`. stdio targets require explicit `--allow-exec`. Never derive the command from server-returned data. |
| **Resource exhaustion** - hostile or broken server | Hard caps: response body 10 MB, JSON depth 64 (`JsonReaderOptions.MaxDepth`), tool count 5,000, description length 100 KB, total wall clock 30 s. Streaming reader, never buffer-then-parse. |
| **ANSI / terminal injection** - descriptions printed to console | Strip C0 and C1 control characters and escape sequences from all server-derived strings before rendering. Underrated and genuinely exploitable. |
| **Prompt injection** | Server text is data, never instruction. The simplest control is the one we take: no LLM features in the core. |
| **Secret leakage** | Tokens from environment variables only. Never CLI arguments (visible in `/proc` and CI logs), never contract files. Redact known token shapes from all output and from snapshots. |
| **Path traversal** | Output paths never derived from server-supplied names. Canonicalise and confine writes to the working tree. |
| **TLS downgrade** | Certificate validation always on. `--insecure` exists only for localhost, prints a loud warning, and is refused when a CI environment variable is detected. |

### The taint rule

**Server-derived strings are tainted.** Anything that reaches the console goes
through `Sanitize()` first. This includes tool names, descriptions, titles,
server instructions, error messages quoted back from the server, and any string
interpolated into a finding message.

The tainted set is: everything parsed out of an MCP response. The untainted set
is: literals in our own source, and values the user typed on the command line.
There is no third category.

## 2. Limits are constants, not options

The resource caps in the table above are compiled-in constants with a single
definition site. They are not command-line flags. A user who can raise the JSON
depth cap to survive one awkward server has disarmed the control for every server
thereafter, and will not remember they did it.

## 3. Supply chain and repo hygiene

- Minimal direct dependencies, each justified by an ADR under `docs/adr/`.
  **Dependency count is a security property of this project, not an
  implementation detail.**
- CycloneDX SBOM generated per release, attached to the GitHub release.
- Release artefacts signed (Sigstore/cosign) with published verification steps.
- All GitHub Actions pinned to full commit SHAs, never tags.
- `GITHUB_TOKEN` permissions declared least-privilege per workflow.
- Dependabot, CodeQL, and OpenSSF Scorecard enabled. Scorecard badge in the
  README, because the users of this tool are security-minded by definition.
- `SECURITY.md` with a disclosure policy and a response SLA.

## 4. Security as a feature

The `security` finding class ships from v0.1 and leads the README:

> A tool that was read-only when you approved it and is destructive today is not
> a compatibility problem. It is a supply-chain compromise, and nothing else in
> your pipeline will notice.

Concretely, the rug-pull attack class is: annotation downgrades (`MCPC306`,
`MCPC307`, `MCPC309`, `MCPC310`), description mutations (`MCPC304`), auth scope
expansion (`MCPC402`), and new tools appearing in a server you already trusted
(`MCPC303`). Pinning the contract is the control. Nothing else in the ecosystem
currently pins it.

A consumer contract may suppress findings it does not care about, but it may
never suppress a `security` finding. See `diff-rules.md` §8.
