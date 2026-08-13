# Diff rules (normative)

**Status:** Normative. This file is the single source of truth for classification.

Code implements this document; tests verify it. No rule exists only in code. When
you change a classification you change this file, the golden case, and the
changelog in the same commit.

If an implementation and this document disagree, the document is right and the
implementation is a bug.

---

## 1. Why variance is the whole game

Input schemas are **contravariant**: the server accepts, the consumer sends.
Output schemas are **covariant**: the server produces, the consumer reads.

- Server **widening** what it accepts is safe. **Narrowing** is breaking.
- Server **producing more** is safe. **Producing less** is breaking.

The same structural edit therefore classifies differently depending on which side
of the tool it lands on. Adding a required property is breaking on an input and
additive on an output. Most naive schema differs treat both sides the same and
are consequently wrong about half the time. Rule tables below are split by
position for exactly this reason, and no rule may be shared between them.

## 2. Severity classes

| Class | Meaning | Default policy |
|---|---|---|
| `breaking` | A conforming consumer will now fail | fail build |
| `security` | Trust or blast radius of a tool changed | fail build |
| `behavioural` | Schema-compatible, but agent behaviour will likely change | warn |
| `notice` | Operator needs to know, not itself a compatibility event | warn |
| `additive` | New capability, backwards compatible | pass |
| `cosmetic` | No semantic effect | pass, hidden by default |
| `unanalysable` | The engine could not classify with confidence | warn, never silent |

`behavioural` is the class that does not exist in ordinary API contract testing,
and it is the reason MCP needs its own tool. Description text is instruction text
to a model, so a description edit is a behaviour change with no schema footprint.

**These class names are ordered arbitrarily.** Policy is expressed as a set
(`fail_on: [breaking, security]`), never as a threshold. Nothing may compare
severities with `<` or `>`. Ranking security against breaking is a judgement the
user makes in their policy file, not one this engine makes for them. See the
remarks on `Detent.Core.Policy.Severity`.

## 3. Finding IDs

Every rule row has a stable ID. IDs are permanent: once published, an ID is never
reused for a different rule and never renumbered. Retired rules are marked
withdrawn here rather than deleted.

`MCPC` stands for **MCP Contract**, not for the tool. The prefix names the thing
being analysed, so it stays fixed if the tool is ever renamed again, and it reads
correctly in a SARIF report alongside rule IDs from other analysers.

| Range | Area |
|---|---|
| `MCPC1xx` | Input schema, contravariant |
| `MCPC2xx` | Output schema, covariant |
| `MCPC3xx` | Tool level |
| `MCPC4xx` | Server level |
| `MCPC9xx` | Analysis limits |

The ID is what `detent explain <finding-id>` resolves, what the golden corpus
directories are named after, and what appears as the SARIF `ruleId`. It is a
published interface.

---

## 4. Input schema rules (contravariant)

Applies to `inputSchema` on a tool, and to any nested subschema reached from it.

| ID | Change | Class |
|---|---|---|
| `MCPC101` | Add optional property | `additive` |
| `MCPC102` | Add required property | `breaking` |
| `MCPC103` | Remove property | `breaking` |
| `MCPC104` | Optional becomes required | `breaking` |
| `MCPC105` | Required becomes optional | `additive` |
| `MCPC106` | Type widened (`string` to `string\|number`) | `additive` |
| `MCPC107` | Type narrowed | `breaking` |
| `MCPC108` | Enum value added | `additive` |
| `MCPC109` | Enum value removed | `breaking` |
| `MCPC110` | Constraint tightened (`minLength` up, `maxLength` down, `pattern` added, `minimum` up) | `breaking` |
| `MCPC111` | Constraint loosened | `additive` |
| `MCPC112` | `additionalProperties` true to false | `breaking` |
| `MCPC113` | `additionalProperties` false to true | `additive` |
| `MCPC114` | `default` added | `additive` |
| `MCPC115` | `default` changed | `behavioural` |
| `MCPC116` | `description` changed on a property | `behavioural` |
| `MCPC117` | Branch added to `anyOf` or `oneOf` | `additive` |
| `MCPC118` | Branch removed from `anyOf` or `oneOf` | `breaking` |

`MCPC103` is breaking rather than cosmetic even though a removed input property
is one the server no longer reads: under `additionalProperties: false` the
consumer's existing call is now rejected outright, and the engine classifies on
the conservative reading. A consumer contract that does not list the property in
`sends` suppresses the finding at verification time; see §8.

## 5. Output schema rules (covariant)

Applies to `outputSchema` on a tool, and to any nested subschema reached from it.

| ID | Change | Class |
|---|---|---|
| `MCPC201` | Add property | `additive` |
| `MCPC202` | Remove property | `breaking` |
| `MCPC203` | Required becomes optional | `breaking` |
| `MCPC204` | Optional becomes required | `additive` |
| `MCPC205` | Type narrowed | `additive` |
| `MCPC206` | Type widened | `breaking` |
| `MCPC207` | Enum value removed | `additive` |
| `MCPC208` | Enum value added | `breaking` if the contract declares `exhaustiveEnums` for the field, else `behavioural` |
| `MCPC209` | `description` changed on a property | `behavioural` |

`MCPC208` is the row that earns trust. A consumer switching exhaustively on an
output enum breaks when a new value appears; a consumer that does not, does not.
Only the contract knows which, so this is the one rule whose class depends on
contract input. With no contract loaded it classifies `behavioural`.

## 6. Tool level rules

| ID | Change | Class |
|---|---|---|
| `MCPC301` | Tool removed | `breaking` |
| `MCPC302` | Tool renamed | `breaking`, reported as a rename |
| `MCPC303` | Tool added | `additive` |
| `MCPC304` | `description` changed | `behavioural` |
| `MCPC305` | `title` changed | `cosmetic` |
| `MCPC306` | `annotations.readOnlyHint` true to false | `security` |
| `MCPC307` | `annotations.destructiveHint` false to true | `security` |
| `MCPC308` | `annotations.idempotentHint` true to false | `behavioural` |
| `MCPC309` | `annotations.openWorldHint` false to true | `security` |
| `MCPC310` | Any annotation removed entirely | `security` |

`MCPC304` is always surfaced and never auto-approved. This is the rug-pull
signal: a server that shipped benign descriptions at approval time and mutates
them later has changed what the model does, with no schema footprint at all.

`MCPC310` is `security` because losing a safety assertion is not neutral. An
absent `readOnlyHint` is not the same claim as `readOnlyHint: true`, and the
engine must not treat a dropped assertion as an unchanged one.

`MCPC302` requires rename detection via schema similarity. When a removal and an
addition in the same diff are above the similarity threshold, emit one `MCPC302`
rather than an `MCPC301` plus `MCPC303` pair. Below the threshold, emit the pair.
The threshold and the similarity metric are implementation detail; the choice
between one finding and two is not, and is pinned by golden cases.

## 7. Server level rules

| ID | Change | Class |
|---|---|---|
| `MCPC401` | Advertised capability removed | `breaking` |
| `MCPC402` | Auth scheme or required scopes changed | `security` |
| `MCPC403` | Server `instructions` changed | `behavioural` |
| `MCPC404` | Protocol revision changed | `notice`, plus a re-baseline prompt |
| `MCPC405` | Deprecated subsystem in use (`roots`, `sampling`, `logging`) | `notice`, with the published earliest-removal date |
| `MCPC406` | Server identity changed (name or version) | `notice` |
| `MCPC407` | Advertised capability added | `additive` |

`MCPC404` must suppress the wall of false breaking changes that a revision bump
otherwise produces. A protocol revision change is a re-baseline event, not a
compatibility event.

`MCPC405` is the only rule that fires on a single snapshot rather than on a pair.
The 2026-07-28 revision deprecated three subsystems with a 12-month minimum
support window, so a large share of published servers carry latent migration
debt. This rule is the reason a stranger runs the tool once.

## 8. Contract-scoped classification

Rules above classify a change in isolation. A loaded consumer contract narrows
the result, and only ever downward:

- A finding on an input property absent from `sends` is dropped.
- A finding on an output property absent from `reads` is dropped.
- `MCPC208` promotes to `breaking` when the field is listed in `exhaustiveEnums`.
- A violated `assumes` entry (for example a tool the consumer auto-invokes losing
  `readOnlyHint`) is a finding regardless of schema compatibility.

Dropping findings nobody reads is the point of consumer-driven contracts. Alert
fatigue kills CI gates faster than bugs do. A contract may never promote a
finding except through `exhaustiveEnums`, and may never suppress a `security`
finding.

## 9. Edge cases

These make naive implementations wrong. Each needs a golden case.

1. **`$ref` and `$defs`** - resolve before comparing, or two structurally
   identical schemas diff as different.
2. **`anyOf` / `oneOf` / `allOf`** - order-insensitive comparison.
3. **Property reordering** - never a diff. Canonical form sorts keys.
4. **Description text** - normalise to NFC, collapse runs of whitespace, then
   compare. A reflowed paragraph is not a behaviour change.
5. **Nullability** - expressible three ways (`type: ["string","null"]`,
   `nullable: true`, `anyOf` with a null branch). Normalise to one internal
   representation, or the same edit classifies three ways.
6. **Numeric formatting** - `1` and `1.0` are the same value.
7. **Recursive schemas** - depth cap, then `MCPC901`, never a stack overflow.
8. **Unknown and vendor keywords** (`x-*`) - `MCPC902`, never silently ignored.
9. **Tool list ordering and pagination** - sort by name, follow cursors fully
   before comparing.
10. **Empty versus absent schema** - distinct. Do not conflate.

| ID | Condition | Class |
|---|---|---|
| `MCPC901` | Depth cap reached during comparison | `unanalysable` |
| `MCPC902` | Unknown or vendor keyword changed | `behavioural` |
| `MCPC903` | `$ref` could not be resolved | `unanalysable` |

## 10. The default posture

**When the engine cannot classify a change with confidence, it emits a finding.**
It never drops the change silently.

A false negative in a CI gate is worse than no gate at all, because it
manufactures confidence. That asymmetry is why `MCPC902` exists at all: a keyword
the engine does not model may carry meaning, and the honest answer is to say so.

This is also the highest-risk failure mode for agent-driven development on this
repo. Making a test pass by editing its expected output converts a correctness
failure into a silent green build. See the guardrails in `CLAUDE.md`.

## 11. Invariants

Properties that hold for every input, verified by property-based tests rather
than by example:

- `diff(x, x)` is empty for all `x`.
- `canonicalise(canonicalise(x))` equals `canonicalise(x)`.
- For paired rules, class is antisymmetric under argument swap: if
  `diff(a, b)` yields `MCPC102` then `diff(b, a)` yields `MCPC103`.
- Diff output ordering is stable and independent of input key ordering.
- No rule inspects wall-clock time, the filesystem, or the network.
