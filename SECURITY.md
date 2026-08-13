# Security policy

## Reporting a vulnerability

Please report security issues privately, not as a public GitHub issue.

Use **GitHub private vulnerability reporting** (the "Report a vulnerability"
button under this repository's Security tab). If that is unavailable to you,
email the maintainer at `m.budziszewski93@gmail.com` with `detent security` in the
subject.

Please include: what you found, how to reproduce it, and what an attacker gets.
A proof-of-concept helps but is not required to file.

### Response targets

| Stage | Target |
|---|---|
| Acknowledgement | 3 working days |
| Initial assessment | 10 working days |
| Fix or documented mitigation | 90 days from acknowledgement |

This is a spare-time project, and these are targets rather than contractual
guarantees. If a deadline slips you will be told rather than left waiting.

Coordinated disclosure is the default: we will agree a publication date with
you, credit you in the advisory unless you prefer otherwise, and publish a
GitHub Security Advisory with a CVE where one is warranted.

## Supported versions

Pre-1.0. Only the latest release receives fixes. This will be revised at 1.0.

## Scope

`detent` connects to MCP servers, which it treats as **untrusted input**: a remote
endpoint returning attacker-controllable JSON, attacker-controllable text bound
for a language model, and attacker-controllable text bound for your terminal.

In scope, and taken seriously:

- SSRF, including via redirect and DNS rebinding
- Command injection through the stdio transport
- Resource exhaustion from hostile or broken servers
- ANSI and terminal escape injection through server-supplied text
- Secret leakage into snapshots, logs, or process arguments
- Path traversal through server-supplied names
- TLS validation bypass
- **A missed finding.** A diff that reports "no breaking changes" when a breaking
  change occurred is a security issue, not merely a bug. A false negative in a CI
  gate manufactures confidence, which is worse than having no gate. Report these
  through this policy.

Out of scope:

- Vulnerabilities in the MCP servers you point the tool at. Report those to their
  maintainers; finding their contract changes is what this tool is for.
- Findings that require `--allow-exec`, `--allow-host`, or `--insecure` to have
  been passed deliberately. These flags exist to lower specific defences and say
  so.
- Missing hardening with no demonstrated impact.

The full threat model and the control for each entry above is in
[`docs/arch/security-model.md`](docs/arch/security-model.md).

## Our own supply chain

A supply-chain integrity tool has to be able to make its own argument.

- Direct dependencies are minimal and each one is justified by an ADR under
  [`docs/adr/`](docs/adr/). `Detent.Core`, the engine, has none at all, and an
  architecture test enforces that.
- GitHub Actions are pinned to full commit SHAs, never tags.
- Workflow `GITHUB_TOKEN` permissions are least-privilege per job.
- From the first release: CycloneDX SBOM and signed artefacts with published
  verification instructions.
