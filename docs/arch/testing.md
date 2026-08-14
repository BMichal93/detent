# Testing strategy

**Status:** Normative for the golden corpus conventions in §2.

## 1. What must be perfect

The diff classification engine, and nothing else. If the tool says "no breaking
changes" and something breaks, the user is lost forever. A false negative in a CI
gate is worse than no gate at all, because it manufactures confidence.

CLI ergonomics, output formatting, transport support, and docs must be merely
good. They are iterable in public. The engine is not.

| Layer | Approach |
|---|---|
| Diff engine | Golden corpus, one case per rule row, plus property-based tests |
| Mutation testing | Stryker.NET on `Detent.Core.Diff`, gate at 85% - **currently blocked, see ADR-0007** |
| Transport | Fake MCP server (in-process Kestrel) with hostile fixtures |
| Determinism | Capture the same fixture 10x, assert byte equality |
| Architecture | Assert `Detent.Core` has no I/O references |
| End to end | 3-5 real public MCP servers, nightly, non-blocking |
| Performance | BenchmarkDotNet, §7 budgets as CI gates |

## 2. Golden corpus convention

```
tests/golden/mcpc102-input-add-required-property/
├── before.json      # baseline snapshot
├── after.json       # new snapshot
├── expected.json    # findings, sorted, with stable IDs
└── README.md        # one line: which rule this pins
```

One directory per rule row, named `<finding-id>-<slug>`. A single parameterised
xUnit theory discovers all of them. Adding a rule is adding a directory, which is
trivially parallelisable across agents, and the count of directories is a legible
progress metric.

The Phase 2 exit criterion is that every row of every table in `diff-rules.md`
has a passing golden case.

### What expected.json pins

```json
{
  "findings": [
    { "id": "MCPC301", "severity": "breaking", "path": "tools/legacy_export" }
  ]
}
```

Rule, class, and location. **Message wording is deliberately not pinned.** The
classification is the contract and must never change silently; the prose is UX
and is meant to be improved. Pinning wording would make every reworded message a
failing test, and a suite that fails for cosmetic reasons trains people to edit
expectations until it goes green, which is the one habit this corpus exists to
prevent.

`before.json` and `after.json` are hand-written and carry no `digest`. The
harness reads them without verifying one, because their content is the point.

The harness also asserts that each case is well formed, that the corpus is not
empty (an empty corpus makes every theory pass vacuously, which looks identical
to a corpus that works), and that directory names match the rule they claim.

## 3. The rule that matters most

**Never modify a file under `tests/golden/**/expected.json` to make a test pass.**

If the expected output looks wrong, stop and ask a human. Editing the expectation
converts a correctness failure into a silent green build, which is precisely the
failure mode that destroys this product. There is no situation in which changing
an expectation is the right way to fix a red test, unless the rule itself is
being deliberately changed, and then `diff-rules.md` changes in the same commit.

For the same reason: **do not use auto-updating snapshot test libraries** for the
golden corpus. Approval-style tooling that rewrites expectations on demand is
exactly the automation of the mistake above. Expected output changes require
human intent, expressed as an edit a reviewer can see.

## 4. Property-based tests

Examples pin rules; properties pin the shape of the whole engine. From
`diff-rules.md` §11:

- `diff(x, x)` is empty.
- `canonicalise` is idempotent.
- Paired rules are antisymmetric under argument swap.
- Output ordering is stable and independent of input key ordering.

## 5. Hostile transport fixtures

The fake server must be able to produce, on demand: a 100 MB response, a
10,000-deep JSON document, ANSI-laden descriptions, a redirect to
`169.254.169.254`, a cross-host redirect, a redirect loop, a slow-loris trickle,
a response that never terminates, and a valid response containing a
credential-shaped string.

Each maps to a control in `security-model.md` §1. A control without a fixture
that exercises it is an untested claim.

## 6. Test-first rules

Tasks implementing the classification tables (input, output, tool, server rules)
are built test-first: write the golden cases from `diff-rules.md` before any
implementation. This prevents the engine from being shaped by whatever the
implementation happened to do, which is the normal way a differ ends up with
rules nobody chose.
