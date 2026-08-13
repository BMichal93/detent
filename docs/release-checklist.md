# Release checklist

Gates that must clear before a public tagged release. Items marked **blocking**
stop the release; the rest are strongly expected but a release note explaining
their absence is acceptable.

Nothing here applies to pre-release builds from `main`.

## Before the first public release (v0.1.0)

### Legal

- [ ] **BLOCKING: formal trademark clearance on the name `detent`.**
      ADR-0005 records a good-faith availability screen across npm, NuGet,
      GitHub, and a USPTO search of software classes 9 and 42. **That screen is
      not legal clearance and must not be treated as such.** Before going live,
      commission a professional search in the jurisdictions that matter (at
      minimum US and EU, classes 9 and 42) and get an opinion in writing.

      This is deliberately the first item on the list. A rename after traction is
      expensive and the cost rises with every user, every install script, and
      every CI config that pins the binary name. The project's own risk register
      classes this as low probability and high cost, which is exactly the profile
      that gets skipped and then hurts.

      Re-run the availability screen at the same time. It was taken 2026-08-13
      and registries change; a mark filed after that date would not appear in it.

- [ ] Claim `detent` on npm and NuGet before announcing, even if the packages are
      placeholders. Both were free on 2026-08-13. Squatting between announcement
      and publication is cheap for an attacker and embarrassing to undo.
- [ ] Confirm `LICENSE` and the `PackageLicenseExpression` agree, and that every
      bundled dependency's licence permits redistribution.

### Security

- [ ] CycloneDX SBOM generated and attached to the GitHub release.
- [ ] Artefacts signed (Sigstore/cosign) with verification instructions in the
      release notes.
- [ ] All GitHub Actions pinned to full commit SHAs, verified at release time
      rather than assumed.
- [ ] CodeQL and OpenSSF Scorecard enabled and green; Scorecard badge in README.
- [ ] `SECURITY.md` contact route tested end to end by actually sending a report.
- [ ] No high-entropy strings in any committed snapshot or fixture.

### Correctness

- [ ] Every rule row in `docs/arch/diff-rules.md` has a passing golden case.
- [ ] Mutation score on the diff engine at or above 85%.
- [ ] Determinism test green: ten consecutive captures byte-identical.
- [ ] Performance budgets in ADR-0002 measured on CI hardware, not just locally.
- [ ] Tested against at least three real public MCP servers.

### Product

- [ ] README states the conformance-testing distinction in the first paragraph.
- [ ] The `mcplock` relationship is decided and stated (successor or sibling).
- [ ] Kill criteria from the project plan reviewed honestly before investing in
      Phase 6.
