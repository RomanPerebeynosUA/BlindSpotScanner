# Proposal: assertion depth per test (2026-11-18)

**Status:** Accepted 2026-11-18 as D27; phase A is milestone M12.

## Problem

Today a report says two things about assertions:

- per endpoint, `covered` (values asserted) or `weak` (status/shape only);
- for the whole suite, dimension 5 "Assertion strength": one 1–10 number written by the LLM with
  four sentences of justification.

Neither tells a team **what is wrong with a given test, which bug would slip through, or what to
add**. In `examples/sample-report.json`, dimension 5 is "5 — Mostly value assertions, one
status-only (band 5–6)". That can't be acted on, and two runs can't be compared on it in a way
anyone trusts, because the number is an opinion rather than something code derives.

The only per-test assertion output is the `FalsePositive` finding (status-only happy path). Every
other weakness disappears into "weak" or into the single score.

## Goals

1. Every integration test gets an **assertion level** that code derives and anyone can re-check.
2. For write endpoints, the report lists **fields the test sends but never checks**.
3. Each weakness becomes a **finding about one test**, with a concrete next step.
4. Dimension 5 is **computed from the per-test levels** (principle 2), not authored by the LLM.
5. Findings feed the existing **fix PR flow**, so a gap can turn into a reviewed change in one click.

## Non-goals

- Running tests. Mutation testing (Stryker) or flakiness detection need execution and stay out of
  scope; the scanner still reads code at a pinned commit only.
- Languages other than C#. The level rules are C#-specific in this milestone.
- Changing the coverage legend. `covered`/`weak` per endpoint keep their meaning.

## Proposal

### 1. Assertion level per test (computed in code)

Each integration test gets the **highest** level it reaches:

| Level | The test asserts | Example signal |
|---|---|---|
| L0 | nothing | no assertion call in the test body |
| L1 | status code only | `response.StatusCode`, `EnsureSuccessStatusCode()`, `BeSuccessful()` |
| L2 | shape only | `NotBeNull()`, `.Length`/`.Count`, `BeOfType`, `NotBeEmpty()` |
| L3 | response values | equality on fields of the deserialized body |
| L4 | persisted state, read back | a `DbContext`/repository read, or a follow-up GET of the created resource |
| L5 | side effects | WireMock `LogEntries`/request verification, captured outgoing messages |

For an error-path test, the same ladder applies: status only is L1, a checked error body
(`ProblemDetails` fields) is L3, and "nothing was saved" is L4.

**How:** extend `CSharpTestSyntaxReader` to collect each test method's assertion calls (xUnit
`Assert.*`, FluentAssertions `.Should()`, NUnit `Assert.That`, Shouldly `.Should*`) and classify
what each one reads. A call into a helper that the reader can't resolve within the same file or the
fixtures marks the test `unknown`. An `unknown` test is sent to the LLM (P5) to classify, with
evidence. If the LLM can't decide either, the level stays `unknown` and is reported as such, never
guessed (principle 5).

### 2. Fields sent vs. fields checked (write endpoints)

For POST/PUT/PATCH tests, code collects the request's field names (anonymous objects, object
initializers) and the field names the assertions read. Matching is by name, case-insensitive,
with nested paths (`lines[].bookId`). The difference is the **unchecked fields**:

> `POST api/orders`: sends `lines[].bookId, lines[].quantity`; checks `Lines` (count), `Total`.
> Not checked: `lines[].bookId`, `lines[].quantity`.

When the payload comes from a builder or helper the reader can't resolve, the field lists are left
empty and marked unresolved (no guess).

### 3. Bugs a test would still pass (LLM, phase B)

The P5 coverage prompt also returns, per evidence test, 1–3 realistic bugs the test would not
catch (`survivingBugs`), each ≤20 words, e.g. for `Get_by_id_returns_ok`: "the handler returns a
different book". This is per-item detail, so it is allowed under principle 2.

### 4. New finding kinds

| Finding | Meaning | Source | Phase |
|---|---|---|---|
| `NoAssertion` | The test has no assertion (L0) | code | A |
| `UncheckedFields` | A write test sends fields it never checks | code | A |
| `ErrorWithoutStateCheck` | An error-path test checks the status but not the error body or that nothing was saved | code (level) | A |
| `WeakMatcher` | The only value checks are `NotBeNull`, `Count > 0`, `Contains` on raw JSON | code | B |
| `TautologicalAssertion` | The test checks a value it set up in a fake, or compares the response with itself | code + LLM | B |
| `UnverifiedSideEffect` | The handler writes to the DB, calls out, or publishes, and no test checks that effect | code (production extraction) + LLM | C |

`FalsePositive` stays as it is (an L1 test on a happy path), so existing tickets and stable keys
keep working. Stable keys follow the existing pattern, e.g.
`unchecked-fields:tests/Bookstore.IntegrationTests/OrdersApiTests.cs::Place_order_persists_lines_and_total`.

### 5. Suggested assertions in fix PRs (phase C)

`prompts/generate-fix.md` accepts the new kinds and adds the missing assertions to the existing
test, in the style of its siblings, instead of writing a new test. The preview and the human click
stay as they are (principle 8).

### 6. Dimension 5 computed from the levels

`ReportAssembler` derives dimension 5 from the per-test levels, and `ReportValidator` re-derives it:

```
points    = { L0: 1, L1: 2, L2: 4, L3: 6, L4: 8, L5: 10 }
known     = tests whose level is not unknown
score_5   = round(mean(points[level] over known))          (null, unscorable, if known is empty)
```

The mapping matches doc 06's calibration example: status plus a row count (L2) scores 4, and
asserting the persisted fields (L4) scores 8, i.e. "7+". The LLM's four justification bullets for
dimension 5 are replaced by computed bullets (the level counts and the weakest tests). The other
six dimensions are unchanged.

## Report contract changes

- `integration-report.schema.json`: a new top-level `testAssertions[]`:
  ```json
  { "test": "path::Name", "endpointIds": ["e1"], "level": 3, "levelSource": "code|llm|unknown",
    "sentFields": ["lines[].bookId"], "checkedFields": ["Total"], "uncheckedFields": ["lines[].bookId"],
    "fieldsResolved": true, "survivingBugs": ["…"] }
  ```
  plus `summary.assertionLevels` (`{ "L0": 0, …, "L5": 0, "unknown": 0 }`, computed) and the new
  finding kinds in the `kind` enum.
- `schemaVersion` bump; the OpenAPI contract's report type follows.
- `map-coverage` prompt v1.1.0 (`unknown`-level classification, `survivingBugs`); `grade-quality`
  v1.1.0 no longer returns dimension 5; `generate-fix` v1.1.0 accepts the new kinds.
- `examples/sample-report.json` and `sample-report-previous.json` are regenerated so both still
  assemble exactly.

## How the product principles hold

| Principle | How this proposal keeps it |
|---|---|
| 1 Evidence integrity | Every `testAssertions[].test` and every new finding's evidence is a `path::Test` that `CitationVerifier` re-checks |
| 2 Computed summary | Levels, unchecked fields, `assertionLevels` and dimension 5 are derived in code and re-derived by the validator; the LLM only adds per-item detail (`survivingBugs`, `unknown` levels) |
| 5 N/A, not a guess | Unresolved helpers and payloads stay `unknown`/unresolved; dimension 5 is unscorable when no level is known |
| 6 Labelled measurements | The schema version bump marks runs before and after as measured differently (see open question 3) |
| 9 No secrets | Field names only; values from test payloads are not copied into the report |

## Rollout

| Phase | Scope | LLM change |
|---|---|---|
| A | Levels, fields sent vs. checked, `NoAssertion`, `UncheckedFields`, `ErrorWithoutStateCheck`, computed dimension 5, report and UI table | only `unknown` levels go to the LLM |
| B | `survivingBugs`, `WeakMatcher`, `TautologicalAssertion` | P5 prompt v1.1.0 |
| C | Production side-effect extraction, `UnverifiedSideEffect`, suggested assertions in fix PRs | `generate-fix` v1.1.0 |

Each phase ships on its own, built test-first.

## Acceptance criteria (phase A, on `tests/fixtures/bookstore`)

- Levels:
  - `BooksApiTests.cs::Get_by_id_returns_ok` is **L1**
  - `BooksApiTests.cs::List_returns_seeded_books` is **L2** (length only)
  - `OrdersApiTests.cs::Place_order_persists_lines_and_total` is **L3** (response values, nothing read back)
  - `BooksApiTests.cs::Create_persists_book` is **L4** (read back through `GET api/books/{id}`)
  - `BooksApiTests.cs::Create_rejects_missing_title` is **L1** and raises `ErrorWithoutStateCheck`
  - `DbFixture.cs::Container_starts` is **L0** and raises `NoAssertion`
- `Place_order_persists_lines_and_total` raises `UncheckedFields` listing `lines[].bookId` and
  `lines[].quantity`. `Create_persists_book` raises no `UncheckedFields` (it sends `title` and checks it).
- `ReportAssembler` computes dimension 5 from the levels, and `ReportValidator` fails a report whose
  dimension 5 or `summary.assertionLevels` doesn't match `testAssertions[]`.
- A test whose only assertion is an unresolved helper call is `unknown`, is excluded from
  dimension 5, and is counted in `assertionLevels.unknown`.
- `CitationVerifier` fails the run when a `testAssertions[].test` doesn't exist at the pinned commit.
- The report page shows a per-test table sorted weakest first, with unchecked fields per row.
- Both example reports assemble and validate exactly.

## Alternatives considered

- **A stricter dimension 5 prompt, still LLM-authored.** Cheaper, but the number stays an opinion,
  is not reproducible, and still says nothing per test.
- **Mutation testing (Stryker.NET).** The strongest signal for assertion quality, but it needs to
  build and run the target repository, which the scanner doesn't do. It could come later as an
  optional input from CI.
- **Code coverage reports.** They measure lines executed, not what is asserted, so they don't
  address the problem.

## Risks

- **Heuristics miss custom assertion helpers.** Mitigation: `unknown` instead of a guess, with an LLM
  fallback; the `unknown` share is shown, like "% of weight assessable".
- **Name-based field matching** misses renamed fields (`title` sent, `Name` checked). Mitigation:
  report it as unchecked with the field paths visible, so a reviewer can tell at a glance.
- **Dimension 5 values will shift** for existing repositories when the definition changes, which may
  look like a regression. Mitigation: the comparison banner states the definition changed.

## Open questions

1. Should dimension 5 become fully computed (as proposed), or stay LLM-authored with the computed
   level counts shown next to it?
2. Does a read-back through the API (a follow-up GET) count as L4, or only a direct database read?
3. Comparability: `RunComparer` today lists a schema or prompt-set change only as a reason, without
   marking the runs not comparable. Should a change in assertion-model version make only the
   dimension 5 and score deltas not comparable, or the whole run?
4. Is the points mapping (1, 2, 4, 6, 8, 10) right, or should L5 be required for a 9–10?
5. Should levels cover every integration test, or only tests that are evidence for an endpoint?
6. Which assertion libraries must phase A support: xUnit, FluentAssertions, NUnit, Shouldly, all?
