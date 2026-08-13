# AGENTS.md

See [CLAUDE.md](CLAUDE.md). It is the router for this repository and applies to
any coding agent, not only Claude.

Two things worth knowing before you read anything else:

1. **Never edit `tests/golden/**/expected.json` to make a test pass.** Stop and
   ask instead. This is the single highest-risk action in this codebase.
2. **Never add a NuGet package without an ADR** under `docs/adr/`.
