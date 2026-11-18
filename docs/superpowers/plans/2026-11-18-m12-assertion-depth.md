# M12 Assertion Depth (phase A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every integration test gets an assertion level (L0–L5) derived by code, write tests list
the fields they send but never check, three new per-test findings appear, and rubric dimension 5
("Assertion strength") is computed from the levels instead of written by the LLM.

**Architecture:** A new Roslyn reader (`CSharpAssertionReader`, Infrastructure, behind the
Application port `ITestAssertionReader`) turns one test file into per-test facts. A new
Application step (`AssessAssertionsPhase`) runs inside P6: it reads every integration test file,
builds `TestAssertion` rows, sends only `unknown` tests to the LLM (new `classify-assertions`
prompt), and derives the three findings. `ReportAssembler` computes dimension 5 and
`summary.assertionLevels` from `testAssertions[]`; `ReportValidator` re-derives them;
`CitationVerifier` checks every `testAssertions[].test`. `RunComparer` adds `scoreComparable`. The
web report gets a per-test table and new findings groups.

**Tech Stack:** .NET 10, Roslyn (`Microsoft.CodeAnalysis.CSharp`, already referenced by
Infrastructure), xUnit + FluentAssertions · Angular 22 (signals), Vitest, Playwright.

**Spec:** `docs/proposals/2026-11-18-assertion-depth.md` (phase A) plus the team's answers recorded
as D27 in Task 1. Also `BlindSpotScanner-Blueprint/docs/06-scoring-rubric.md` (dimension 5,
calibration example), `contracts/integration-report.schema.json`, `docs/08-run-comparison.md`, and
`docs/DECISIONS.md` D13, D15, D20, D21.

## Global Constraints

- **Team answers (D27):**
  - dimension 5 is computed by code; the LLM no longer grades it;
  - a read-back through a follow-up GET is **L4**, labelled `readBack: "api"` (a direct DB read is `"db"`);
  - a schema-version change marks only the **quality score and dimension 5** not comparable
    (`delta.scoreComparable: false`); endpoint deltas keep `comparable`;
  - points: `L0 1, L1 2, L2 4, L3 6, L4 8, L5 10`, averaged over tests with a known level;
  - levels cover **every integration test**, not only endpoint evidence;
  - assertion libraries: **xUnit, FluentAssertions, NUnit, Shouldly**;
  - scope: **phase A only** (no `survivingBugs`, `WeakMatcher`, `TautologicalAssertion`,
    `UnverifiedSideEffect`, or fix-PR support for the new kinds).
- **Versions:** report `schemaVersion` `"1.1"` (contract accepts `"1.0"` and `"1.1"`);
  `PromptSetVersion` `"1.1.0"`; `grade-quality` prompt `v1.1.0`; new `classify-assertions` prompt `v1.0.0`.
- **Enums are stored as integers:** append the new `FindingKind` values **after `Risk`**; never reorder.
- **Old reports must still load:** every new property is optional on read (`TestAssertions`,
  `AssertionLevels` nullable; `ScoreComparable` non-required, default `true`).
  `UnmappedMemberHandling.Disallow` stays.
- **Citations:** `testAssertions[].test` is `path::Class.Method` (the reader's `TestMethods` form).
- **Principle 5:** a level the code and the LLM can't decide is `null` with `levelSource: "unknown"`,
  never guessed; dimension 5 is unscorable when no test has a known level.
- **Backend tests:** `Method_Scenario_ExpectedResult`, `// Arrange` `// Act` `// Assert`, one
  behavior each, no branching. Frontend: one component per folder, signals, strict TS.

## Review Focus

1. **A non-C# integration test file** (e.g. a `.ts` file the LLM classified as integration): skipped
   with a note `"{path}: assertion levels not assessed (C# only)"`, not counted, no crash (Task 5).
2. **A test whose assertions live in a helper the reader can't see** (base class, other file,
   `AssertOrderMatches(...)`): `unknown`, excluded from dimension 5, counted in
   `assertionLevels.unknown` (Task 2).
3. **No integration tests, or every test unknown:** dimension 5 unscorable with N/A bullets; no
   division by zero; score label still correct (Task 6).
4. **A schema-1.0 report as `previous`** (no `testAssertions`, stored delta without
   `scoreComparable`): deserializes, compares, and gets `scoreComparable: false` (Task 7).
5. **A payload built elsewhere** (a builder, a field, a method result): `fieldsResolved: false`, empty
   field lists, and no `UncheckedFields` finding (Task 3).

---

### Task 1: Record D27, contract and domain model

**Files:**
- Modify: `docs/DECISIONS.md` (append D27), `docs/proposals/2026-11-18-assertion-depth.md` (status line),
  `BlindSpotScanner-Blueprint/docs/11-implementation-plan.md` (add M12 before "Definition of done")
- Create: `src/BlindSpotScanner.Domain/Reports/TestAssertion.cs`
- Modify: `src/BlindSpotScanner.Domain/Enums/FindingKind.cs`, `src/BlindSpotScanner.Domain/Reports/IntegrationReport.cs`,
  `ReportSummary.cs`, `RunDelta.cs`, `src/BlindSpotScanner.Application/Reports/ReportDetail.cs`
- Modify (identical): `BlindSpotScanner-Blueprint/contracts/integration-report.schema.json` and
  `src/BlindSpotScanner.Infrastructure/Reports/integration-report.schema.json`
- Test: `tests/BlindSpotScanner.Domain.Tests/Reports/ReportJsonSerializerTests.cs`,
  `tests/BlindSpotScanner.Infrastructure.Tests/Reports/ReportSchemaValidatorTests.cs`

**Interfaces:**
- Produces:
  - `enum LevelSource { Code, Llm, Unknown }` → wire `"code" | "llm" | "unknown"`
  - `enum ReadBack { Api, Db }` → wire `"api" | "db"` (follow the existing enums' `[JsonStringEnumMemberName]` pattern)
  - `sealed record TestAssertion { required string Test; [JsonIgnore(Never)] required int? Level; required LevelSource LevelSource; ReadBack? ReadBack; required bool ErrorPath; required IReadOnlyList<string> SentFields; required IReadOnlyList<string> CheckedFields; required IReadOnlyList<string> UncheckedFields; required bool FieldsResolved; }`
  - `sealed record AssertionLevels { required int L0, L1, L2, L3, L4, L5, Unknown; }` (wire `l0`…`l5`, `unknown`)
  - `IntegrationReport.TestAssertions` and `ReportDetail.TestAssertions`: `IReadOnlyList<TestAssertion>?` (not required; `ReportDetail.From` copies it)
  - `ReportSummary.AssertionLevels`: `AssertionLevels?`
  - `RunDelta.ScoreComparable`: `bool { get; init; } = true` (not required)
  - `FindingKind`: append `NoAssertion, UncheckedFields, ErrorWithoutStateCheck` after `Risk`

- [ ] **Step 1: Append D27 to `docs/DECISIONS.md`**

```markdown
## D27 — Assertion depth per test, phase A (2026-11-18)
The team accepted `docs/proposals/2026-11-18-assertion-depth.md` with these answers: dimension 5
is computed by code from per-test assertion levels (points L0 1, L1 2, L2 4, L3 6, L4 8, L5 10,
mean over tests with a known level, rounded half away from zero); a follow-up GET after a write is
L4 with `readBack: "api"`, a direct database read is `"db"`; levels cover every integration test;
the reader recognises xUnit, FluentAssertions, NUnit and Shouldly. A report schema change (1.0 → 1.1)
sets `delta.scoreComparable: false` (score and dimension 5 only; endpoint deltas keep `comparable`).
Phase A narrows the proposal: helpers are followed one level within the same file only (anything
else that looks like an assertion helper makes the test `unknown`); `unknown` tests go to a new
`classify-assertions` prompt in P6 rather than to P5, so the coverage prompt and its cache are
unchanged; `testAssertions[]` has no `endpointIds`; the HTML export and fix PRs don't use the new
data yet. P6's grading prompt (v1.1.0) no longer grades dimension 5, and an answer that still
contains it is ignored, not re-asked. `PromptSetVersion` 1.1.0, report `schemaVersion` 1.1.
```

- [ ] **Step 2: Set the proposal's status line** to `**Status:** Accepted 2026-11-18 as D27; phase A is milestone M12.` and add to doc 11:

```markdown
## M12 — Assertion depth (phase A)
- Per-test assertion levels, fields sent vs. checked, NoAssertion / UncheckedFields /
  ErrorWithoutStateCheck findings, computed dimension 5, `scoreComparable`, report table (D27).
- **AC:** the bookstore fixture yields the levels in D27's plan; the validator fails a tampered
  dimension 5; both example reports assemble exactly.
```

- [ ] **Step 3: Freeze the schema-1.0 examples.** Copy both current example files to
  `tests/fixtures/reports/sample-report-1.0.json` and `sample-report-previous-1.0.json`, and link
  `tests/fixtures/reports/*.json` into the Domain and Application test projects next to the
  existing examples line (`Link="Fixtures\%(Filename)%(Extension)"`). Task 9 rewrites the examples;
  these copies keep testing the old format.

- [ ] **Step 4: Write the failing tests**

```csharp
// ReportJsonSerializerTests
[Fact]
public void Deserialize_ReportWithoutTestAssertions_LoadsWithNulls()
// Arrange: Fixtures/sample-report-1.0.json
// Assert: report.TestAssertions.Should().BeNull(); report.Summary.AssertionLevels.Should().BeNull(); report.Delta!.ScoreComparable.Should().BeTrue();

[Fact]
public void Serialize_TestAssertion_WritesWireNames()
// Arrange: new TestAssertion { Test = "tests/A.cs::A.B", Level = null, LevelSource = LevelSource.Unknown, ReadBack = ReadBack.Api, ErrorPath = false, SentFields = [], CheckedFields = [], UncheckedFields = [], FieldsResolved = true }
// Assert: JSON contains "\"level\":null", "\"levelSource\":\"unknown\"", "\"readBack\":\"api\""

// ReportSchemaValidatorTests
[Fact] public void Validate_SchemaVersion11WithTestAssertions_ReturnsNoErrors()
[Fact] public void Validate_TestAssertionLevelSix_ReturnsError()      // errors.Should().ContainMatch("*/testAssertions/0/level*")
[Fact] public void Validate_NoAssertionFindingKind_ReturnsNoErrors()
```

- [ ] **Step 5: Run them to verify they fail**

Run: `dotnet test tests/BlindSpotScanner.Domain.Tests --filter ReportJsonSerializerTests` and
`dotnet test tests/BlindSpotScanner.Infrastructure.Tests --filter ReportSchemaValidatorTests`
Expected: FAIL (types and schema properties missing).

- [ ] **Step 6: Add the types and update both schema copies identically**

Schema changes: `meta.schemaVersion` → `{ "enum": ["1.0", "1.1"] }`; findings `kind` enum gains the
three kinds; `summary.assertionLevels` (object, required `l0`–`l5`, `unknown`, integers ≥ 0);
top-level optional `testAssertions` array of objects with required `test` (`$ref citation`),
`level` (`["integer","null"]`, 0–5), `levelSource` (enum), `errorPath`, `sentFields`,
`checkedFields`, `uncheckedFields` (string arrays), `fieldsResolved`; optional `readBack` (`"api"|"db"`).

- [ ] **Step 7: Run the tests to verify they pass**, including `ReportSchemaValidatorTests`'s
  "copies are identical" test. Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(domain): add per-test assertion model and record D27"
```

---

### Task 2: Assertion levels in `CSharpAssertionReader`

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/ITestAssertionReader.cs`
- Create: `src/BlindSpotScanner.Infrastructure/Extraction/CSharpAssertionReader.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/CSharpAssertionReaderTests.cs`

**Interfaces:**
- Produces:
  - `interface ITestAssertionReader { IReadOnlyList<TestAssertionFacts> Read(string source); }`
  - `sealed record TestAssertionFacts(string TestMethod, int? Level, ReadBack? ReadBack, bool ErrorPath, IReadOnlyList<string> SentFields, IReadOnlyList<string> CheckedFields, bool FieldsResolved);`
    one per test method, same names (`Class.Method`) and order as `CSharpTestSyntaxReader`'s
    `TestMethods`. Task 2 returns `SentFields = []`, `CheckedFields = []`, `FieldsResolved = true`; Task 3 fills them.

**Rules (the tests pin each one):**

*Assertion calls,* searched in the test body and, one level deep, in methods of the same class it
calls by simple name:
- xUnit / NUnit classic: invocations on `Assert`, `ClassicAssert`, `CollectionAssert`, `StringAssert`.
  The *actual* is the 2nd argument of `Equal`, `NotEqual`, `StrictEqual`, `Same`, `NotSame`,
  `Contains`, `DoesNotContain`, `Matches`, `AreEqual`, `AreNotEqual`, `AreSame`; otherwise the 1st.
  NUnit `Assert.That(actual, constraint)`: actual is the 1st argument.
- FluentAssertions: a chain containing `.Should()`; actual = the receiver of `Should()`; matcher =
  the method called on `Should()`'s result.
- Shouldly: a method named `Should…` other than `Should` (e.g. `ShouldBe`); actual = its receiver.
- `EnsureSuccessStatusCode()`.
- A call to a method not declared in the same class whose name starts with `Assert`, `Verify`,
  `Expect`, `Check` or `Should` makes the whole test **unknown** (`Level = null`).

*Level of one assertion* (first match wins):
1. **L1 status:** actual contains `StatusCode` or `IsSuccessStatusCode`; or `EnsureSuccessStatusCode()`;
   or the FluentAssertions actual is a local assigned from `GetAsync`, `PostAsync`, `PutAsync`,
   `PatchAsync`, `DeleteAsync`, `SendAsync`, `PostAsJsonAsync`, `PutAsJsonAsync`, `PatchAsJsonAsync`.
2. **L5 side effect:** actual contains `LogEntries` or `FindLogEntries`, or the matcher is
   `HaveReceivedACall` / `HaveReceivedNoCalls`.
3. **L2 shape:** the method/matcher is one of `NotNull, Null, Empty, NotEmpty, Single, IsType, IsNotType,
   IsAssignableFrom, Throws, ThrowsAsync, IsNotNull, IsNull, IsEmpty, IsNotEmpty, NotBeNull, BeNull,
   BeEmpty, NotBeEmpty, ContainSingle, HaveCount, BeOfType, BeAssignableTo, Throw, ThrowAsync,
   ShouldNotBeNull, ShouldBeNull, ShouldBeEmpty, ShouldNotBeEmpty, ShouldBeOfType`; or the NUnit
   constraint text starts with `Is.Null`, `Is.Not.Null`, `Is.Empty`, `Is.Not.Empty`, `Has.Count`,
   `Has.Length`; or the actual ends in `.Count`, `.Length`, `.Count()`, `.Any()`, `.LongCount()`,
   `.CountAsync()`, `.AnyAsync()`.
4. **L4 read-back:** the actual, or the initializer of a local it names (one level), contains
   - a `GetAsync`/`GetFromJsonAsync`/`GetStringAsync` call placed after the test's first write
     (`PostAsync`, `PostAsJsonAsync`, `PutAsync`, `PutAsJsonAsync`, `PatchAsync`, `PatchAsJsonAsync`,
     `DeleteAsync`) → `ReadBack.Api`; or
   - a member access on an identifier ending (case-insensitive) in `db` or `context`, or a `Set<…>()`
     call → `ReadBack.Db` (wins over `Api` when both occur in one test).
5. **L3 value:** everything else.

*Test:* level = highest of its assertions; no assertion → L0. `ErrorPath` = some status assertion
names `HttpStatusCode.{BadRequest|Unauthorized|Forbidden|NotFound|MethodNotAllowed|Conflict|Gone|PreconditionFailed|UnsupportedMediaType|UnprocessableEntity|UnprocessableContent|TooManyRequests}`
or an integer literal 400–499, or the matcher is `BeBadRequest`, `BeNotFound`, `BeUnauthorized`,
`BeForbidden`, `BeConflict`, `HaveClientError`.

- [ ] **Step 1: Write the failing tests** (`Read` on inline sources, asserting `Level`, `ReadBack`, `ErrorPath`)

```
Read_EmptyTest_IsL0
Read_StatusCodeOnly_IsL1                       // Assert.Equal(HttpStatusCode.OK, response.StatusCode)
Read_EnsureSuccessStatusCode_IsL1
Read_FluentBeSuccessfulOnResponse_IsL1         // response.Should().BeSuccessful()
Read_LengthOnly_IsL2                            // Assert.Equal(3, books!.Length)
Read_DbCountAsync_IsL2                          // (await db.Orders.CountAsync()).Should().Be(1)   (doc 06 calibration: 4 points)
Read_ResponseValue_IsL3                         // Assert.Equal(39.98m, order.Total)
Read_GetAfterPost_IsL4ViaApi
Read_GetWithoutWrite_IsL3                       // a GET of seeded data is not a read-back
Read_DbReadValue_IsL4ViaDb                      // var saved = await db.Books.SingleAsync(); saved.Title.Should().Be("Dune")
Read_WireMockLogEntries_IsL5
Read_ShouldlyShouldBe_IsL3
Read_NUnitThatIsNotNull_IsL2
Read_SameFileHelperAssertsValue_IsL3            // test calls private void AssertBook(Book b) { Assert.Equal("Dune", b.Title); }
Read_UnresolvedAssertionHelper_IsUnknown        // test calls AssertOrderMatches(order) declared nowhere in the file → Level null
Read_BadRequestStatus_IsErrorPath
Read_OkStatus_IsNotErrorPath
Read_TestMethods_MatchSyntaxReaderOrder         // TestMethod names equal new CSharpTestSyntaxReader().Read(src).TestMethods
Read_BookstoreFixture_LevelsMatchD27            // files from tests/fixtures/bookstore (copied to output as FixtureRepository does):
    // BooksApiTests.Get_by_id_returns_ok → 1; List_returns_seeded_books → 2; Create_persists_book → 4 (Api);
    // Create_rejects_missing_title → 1, ErrorPath; OrdersApiTests.Place_order_persists_lines_and_total → 3;
    // DbFixture.Container_starts → 0
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/BlindSpotScanner.Infrastructure.Tests --filter CSharpAssertionReaderTests`
Expected: FAIL (type not defined).

- [ ] **Step 3: Implement `CSharpAssertionReader.Read(string source)`** with the rules above. Reuse
  `CSharpTestSyntaxReader`'s test-attribute detection (extract the shared helper into `SyntaxNames`
  rather than copying it).

- [ ] **Step 4: Run the tests to verify they pass.** Expected: PASS.

- [ ] **Step 5: Commit** — `feat(infra): classify assertion levels per test`

---

### Task 3: Fields sent vs. checked

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/TestAssertionRules.cs`
- Modify: `src/BlindSpotScanner.Infrastructure/Extraction/CSharpAssertionReader.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/CSharpAssertionReaderTests.cs`,
  `tests/BlindSpotScanner.Application.Tests/Scanning/TestAssertionRulesTests.cs`

**Interfaces:**
- Produces: `static class TestAssertionRules { static IReadOnlyList<string> UncheckedFields(IReadOnlyList<string> sent, IReadOnlyList<string> checkedFields); }`
  A sent path is checked when its last segment (after the last `.`, with `[]` removed) equals a
  checked name, ignoring case. Result keeps `sent` order.

**Rules:**
- *Sent:* the body argument of `PostAsJsonAsync`/`PutAsJsonAsync`/`PatchAsJsonAsync` (2nd argument),
  or `x` in `JsonContent.Create(x)` passed to `PostAsync`/`PutAsync`/`PatchAsync`. If it is an
  anonymous object or an object initializer (directly, or through a local initialised so in the same
  test), collect leaf paths: `title`; nested objects `address.city`; collections
  (`new[] { … }`, collection initializers) `lines[].bookId` (union over elements). Anything else →
  `FieldsResolved = false` and both lists empty. Several writes → union. No write → `[]`, resolved.
- *Checked:* for every non-status assertion, the member names accessed in the actual (`order!.Lines.Count`
  → `Lines`; never `Count`, `Length` or invoked method names), plus the member names of an anonymous
  expectation (`BeEquivalentTo(new { title, price })`). An equivalence assertion whose expectation is
  the same identifier as a write's body marks all of that body's fields checked. Distinct, source order.

- [ ] **Step 1: Write the failing tests**

```
UncheckedFields_LeafMatchesIgnoringCase_IsChecked        // sent ["title"], checked ["Title"] → []
UncheckedFields_NestedLeafMissing_IsUnchecked            // sent ["lines[].bookId","lines[].quantity"], checked ["Lines","Total"] → both
Read_AnonymousPayload_CollectsSentFields                  // new { title = "Dune", price = 9.5m } → ["title","price"]
Read_NestedCollectionPayload_UsesBracketPaths             // bookstore Place_order → ["lines[].bookId","lines[].quantity"]
Read_PayloadFromBuilder_IsUnresolved                      // PostAsJsonAsync("api/books", BookBuilder.Valid()) → FieldsResolved false, [] and []
Read_PayloadThroughLocal_IsResolved                       // var request = new { title = "Dune" }; PostAsJsonAsync(url, request)
Read_CheckedFields_SkipCountAndLength                     // Place_order → CheckedFields ["Lines","Total"]
Read_EquivalentToRequest_ChecksAllSentFields              // created.Should().BeEquivalentTo(request) → CheckedFields include title
Read_BookstoreCreatePersistsBook_ChecksTitle              // Sent ["title"], Checked ["Title"]
```

- [ ] **Step 2: Run them to verify they fail.** Expected: FAIL.
- [ ] **Step 3: Implement** `TestAssertionRules.UncheckedFields` and the field collection in the reader.
- [ ] **Step 4: Run them to verify they pass.** Expected: PASS (Task 2's tests still pass).
- [ ] **Step 5: Commit** — `feat(infra): collect fields sent and checked by write tests`

---

### Task 4: Findings derived from the levels

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/AssertionFindings.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Scanning/AssertionFindingsTests.cs`

**Interfaces:**
- Consumes: `TestAssertion` (Task 1).
- Produces: `static class AssertionFindings { static IReadOnlyList<ReportFinding> From(IReadOnlyList<TestAssertion> tests); }`
  in `tests` order, `Id = string.Empty` (P6 numbers them), `Evidence = [test.Test]`:

| Kind | When | Severity | StableKey | Headline | Detail |
|---|---|---|---|---|---|
| `NoAssertion` | `Level == 0` | High | `no-assertion:{Test}` | `A test checks nothing` | `["{Test} passes whatever the code does."]` |
| `UncheckedFields` | `!ErrorPath && FieldsResolved && UncheckedFields.Count > 0` | Medium | `unchecked-fields:{Test}` | `A test sends fields it never checks` | `["Sends: {SentFields joined ', '}", "Not checked: {UncheckedFields joined ', '}"]` |
| `ErrorWithoutStateCheck` | `ErrorPath && Level is 1 or 2` | Medium | `error-without-state-check:{Test}` | `An error test checks only the status code` | `["Checks the error status but not the error body or that nothing was saved."]` |

- [ ] **Step 1: Write the failing tests** — one per row (`From_LevelZero_ReturnsNoAssertion`,
  `From_UncheckedFields_ReturnsUncheckedFieldsFinding`, `From_ErrorPathLevelOne_ReturnsErrorWithoutStateCheck`)
  plus `From_ErrorPathWithUncheckedFields_ReturnsNoUncheckedFieldsFinding`,
  `From_UnresolvedFields_ReturnsNoUncheckedFieldsFinding`, `From_ErrorPathLevelThree_ReturnsNothing`,
  `From_UnknownLevel_ReturnsNothing`.
- [ ] **Step 2: Run** `dotnet test tests/BlindSpotScanner.Application.Tests --filter AssertionFindingsTests`. Expected: FAIL.
- [ ] **Step 3: Implement `AssertionFindings.From`.**
- [ ] **Step 4: Run again.** Expected: PASS.
- [ ] **Step 5: Commit** — `feat(app): derive assertion findings from test levels`

---

### Task 5: `AssessAssertionsPhase` with the LLM fallback

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/AssessAssertionsPhase.cs`
- Create: `BlindSpotScanner-Blueprint/prompts/classify-assertions.md`,
  `src/BlindSpotScanner.Application/Llm/Schemas/classify-assertions.schema.json`
- Modify: `src/BlindSpotScanner.Application/Llm/ILlmClient.cs` (`Prompts.ClassifyAssertions = "classify-assertions"`)
- Test: `tests/BlindSpotScanner.Application.Tests/Scanning/AssessAssertionsPhaseTests.cs`,
  `tests/BlindSpotScanner.Infrastructure.Tests/Llm/PromptLibraryTests.cs` (add the prompt to the version theory, `1.0.0`)

**Interfaces:**
- Consumes: `ITestAssertionReader` (Task 2), `TestAssertionRules` (Task 3), `ILlmClient`, `IBlobReader`, `LlmContentPolicy`.
- Produces: `sealed class AssessAssertionsPhase(ILlmClient llm, IBlobReader blobs, ITestAssertionReader reader)`
  with `Task<PhaseResult<IReadOnlyList<TestAssertion>>> AssessAsync(RunContext run, TestClassification tests, int maxFileBytes, CancellationToken ct)`.
  Rows in `tests.IntegrationTests` order, then method order; `Test = $"{path}::{facts.TestMethod}"`;
  `LevelSource` = `Code` when `Level` is set, else `Unknown`; `UncheckedFields` from `TestAssertionRules`
  when resolved, else `[]`.

**Prompt `classify-assertions.md`** (first line `# Task: classify assertion levels — v1.0.0`): input
`{ "tests": [ { "test": "path::Class.Method", "content": "…full file…" } ] }`; it restates the L0–L5
ladder from Task 2 and returns `{ "tests": [ { "test": "…", "level": 0-5 | null, "reason": "≤30 words" } ] }`,
`null` when the assertions can't be seen in the input. The phase's `Check` requires every input
`test` exactly once and `level` null or 0–5. Only unknown tests from files that pass
`LlmContentPolicy` and `maxFileBytes` are sent; no call when there are none.

- [ ] **Step 1: Write the failing tests** (fakes: `FakeBlobReader`, a fake `ITestAssertionReader`, a canned `ILlmClient`)

```
AssessAsync_KnownLevels_MakesNoLlmCall
AssessAsync_CitationUsesPathAndQualifiedMethod          // "tests/A.IntegrationTests/BooksTests.cs::BooksTests.Get_works"
AssessAsync_UnresolvedFields_LeavesUncheckedEmpty
AssessAsync_UnknownLevel_LlmLevelIsUsedWithSourceLlm
AssessAsync_LlmReturnsNull_StaysUnknown
AssessAsync_LlmFails_StaysUnknownWithNote               // note "assertion levels for 1 test(s) not classified (…)"
AssessAsync_DeniedPath_NotSentAndStaysUnknown
AssessAsync_NonCSharpIntegrationFile_SkippedWithNote    // Review Focus 1: "{path}: assertion levels not assessed (C# only)"
```

- [ ] **Step 2: Run** `dotnet test tests/BlindSpotScanner.Application.Tests --filter AssessAssertionsPhaseTests`. Expected: FAIL.
- [ ] **Step 3: Implement the phase, the prompt file and its answer schema.**
- [ ] **Step 4: Run again, plus** `dotnet test tests/BlindSpotScanner.Infrastructure.Tests --filter PromptLibraryTests`. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(app): assess assertion levels with an LLM fallback for unknowns`

---

### Task 6: Dimension 5 and `assertionLevels` in the assembler and validator

**Files:**
- Modify: `src/BlindSpotScanner.Application/Reports/ReportAssembler.cs`, `ReportValidator.cs`, `CitationVerifier.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Reports/ReportAssemblerTests.cs`, `ReportValidatorTests.cs`,
  `tests/BlindSpotScanner.Infrastructure.Tests/Reports/CitationVerifierTests.cs`

**Interfaces:**
- Consumes: `TestAssertion`, `AssertionLevels`, `Rubric.Dimensions` (id 5 name and weight), `TestAssertionRules`.
- Behaviour, only when `detail.TestAssertions` is not null (a 1.0 report passes through unchanged):
  - `summary.assertionLevels` = counts by level, `unknown` = rows with `Level == null`.
  - The scorecard row with `Id == 5` is replaced (position kept) by:
    - `Score = (int)Math.Round(mean, 0, MidpointRounding.AwayFromZero)` of the points over known
      rows; `Scorable = true`; `Confidence = High` when no row is unknown, else `Med`;
    - `Evidence` = the 3 weakest known rows (points ascending, then `Test` ordinal);
    - bullets, exactly:
      1. `What is tested: {k} of {n} integration tests check response values or saved state (L3 or higher).`
      2. `What is missing: {w} test(s) check only the status code or shape (L0–L2); {u} could not be classified.`
      3. `Evidence: {evidence joined ", "}`
      4. `Why this score: the mean of the per-test points (L0 1, L1 2, L2 4, L3 6, L4 8, L5 10) over {n} test(s) is {mean:0.0}.`
    - with no known row: `Score = null`, `Scorable = false`, `Confidence = Low`, `Evidence = []` and the
      four `N/A — insufficient input` bullets `GradeQualityPhase.Unscorable` uses (`Evidence: none` third).
  - `n` = known rows, `k` = known rows with level ≥ 3, `w` = known rows with level ≤ 2, `u` = unknown rows.
- Validator: also `Diff(expected.Scorecard, report.Scorecard, "scorecard")`; for each
  `testAssertions[i]`: `test` matches the citation regex; `(LevelSource == Unknown) == (Level is null)`;
  when `FieldsResolved`, `UncheckedFields` equals `TestAssertionRules.UncheckedFields(...)`, otherwise all
  three field lists are empty (violation paths `testAssertions/{i}/…`).
- `CitationVerifier`: every `testAssertions[].test` is a must-be-test citation.

- [ ] **Step 1: Write the failing tests**

```
Assemble_TestAssertions_ComputesDimensionFive            // levels 1,2,4,1,3 → Score 4; bullet 4 ends "over 5 test(s) is 4.4."
Assemble_HalfPointMean_RoundsAwayFromZero                // levels 0,1 → points 1,2 → mean 1.5 → Score 2
Assemble_TestAssertions_CountsAssertionLevels
Assemble_AllUnknown_DimensionFiveUnscorable               // Review Focus 3
Assemble_EmptyTestAssertions_DimensionFiveUnscorable      // Review Focus 3
Assemble_NoTestAssertions_KeepsDimensionFive              // a 1.0 detail
Assemble_EvidenceIsThreeWeakestByPointsThenCitation
Validate_TamperedDimensionFiveScore_ReturnsViolation      // "scorecard/4/score: expected 4, found 7"
Validate_TamperedAssertionLevels_ReturnsViolation
Validate_UncheckedFieldsMismatch_ReturnsViolation
Validate_UnknownSourceWithLevel_ReturnsViolation
VerifyAsync_TestAssertionNamesMissingTest_Fails
```

- [ ] **Step 2: Run** `dotnet test tests/BlindSpotScanner.Application.Tests --filter "ReportAssemblerTests|ReportValidatorTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** in the assembler (a private `DimensionFive(IReadOnlyList<TestAssertion>)`), validator and verifier.
- [ ] **Step 4: Run again, plus** `--filter CitationVerifierTests` in Infrastructure. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(app): compute assertion strength from per-test levels`

---

### Task 7: `scoreComparable` in the comparer

**Files:**
- Modify: `src/BlindSpotScanner.Application/Reports/RunComparer.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Reports/RunComparerTests.cs`

**Interfaces:**
- Produces: `RunDelta.ScoreComparable = IsComparable(previous, current) && previous.Meta.SchemaVersion == current.Meta.SchemaVersion`.
  When the schema versions differ, `Comparability` also gets
  `"assertion strength is measured differently (schema {prev} → {curr}): quality score and dimension 5 changes are not like-for-like"`
  after the existing `"schema version changed"` reason. `Comparable` is unchanged by this rule.

- [ ] **Step 1: Write the failing tests**

```
Compare_SameSchema_ScoreComparable
Compare_DifferentSchema_ScoreNotComparableButEndpointsComparable
Compare_DifferentSchema_AddsAssertionReason
Compare_NotComparable_ScoreNotComparable
Compare_PreviousIsSchema10JsonWithoutNewFields_Compares     // Review Focus 4: Fixtures/sample-report-previous-1.0.json (Task 1)
```

- [ ] **Step 2: Run** `dotnet test tests/BlindSpotScanner.Application.Tests --filter RunComparerTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run again.** Expected: PASS.
- [ ] **Step 5: Commit** — `feat(app): flag score deltas across schema versions`

---

### Task 8: Wire it into P6, the grading prompt and the pipeline

**Files:**
- Modify: `src/BlindSpotScanner.Application/Scanning/GradeQualityPhase.cs`, `ScanPipeline.cs`, `ScanPipelineFactory.cs`
- Modify: `src/BlindSpotScanner.Infrastructure/DependencyInjection.cs`, `Llm/LlmOptions.cs` (`"1.1.0"`),
  `src/BlindSpotScanner.Api/appsettings.Example.json` (`"1.1.0"`)
- Modify: `BlindSpotScanner-Blueprint/prompts/grade-quality.md` (`v1.1.0`), `BlindSpotScanner-Blueprint/docs/06-scoring-rubric.md`
- Modify: `src/BlindSpotScanner.Application/Remediation/JiraTickets.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Scanning/GradeQualityPhaseTests.cs`,
  `tests/BlindSpotScanner.Infrastructure.Tests/Llm/ScanPipelineTests.cs`, `CannedLlmClient.cs`, Jira tests

**Interfaces:**
- Consumes: `AssessAssertionsPhase` (Task 5), `AssertionFindings` (Task 4).
- Changes:
  - `GradeQualityPhase.GradeAsync(…, IReadOnlyList<TestAssertion> assertions, int maxFileBytes, CancellationToken ct)`:
    findings = uncovered endpoints, then `AssertionFindings.From(assertions)`, then the LLM's, numbered `f1…`
    (also on the no-tests and failed-answer paths). `Check` requires ids 1–4, 6, 7 exactly once and
    ignores a dimension 5 entry; `Scorecard(output)` and `Unscorable()` emit an unscorable placeholder
    for id 5 (the assembler replaces it).
  - `ScanPipeline` takes `ITestAssertionReader` after `ITestSyntaxReader`; P6 runs
    `AssessAssertionsPhase` then `GradeQualityPhase`, sums both usages and notes, and puts the rows in
    `ReportDetail.TestAssertions`. `SchemaVersion = "1.1"`. `ScanPipelineFactory` passes the reader;
    DI registers `services.AddSingleton<ITestAssertionReader, CSharpAssertionReader>()`.
    Update every `new ScanPipeline(` / `new ScanPipelineFactory(` in the tests.
  - `grade-quality.md`: "Score dimensions 1–4, 6 and 7. Do not return dimension 5 (Assertion
    strength): code computes it from per-test assertion levels (D27)." Doc 06 gets the same note
    under the rubric table and the points mapping next to the calibration example.
  - `JiraTickets.AcceptanceCriterion`: `NoAssertion` → "The test asserts the response values and the
    persisted state."; `UncheckedFields` → "The test asserts every field it sends, in the response or the
    saved data."; `ErrorWithoutStateCheck` → "The test asserts the error body and that nothing was saved."

- [ ] **Step 1: Write the failing tests**

```
GradeAsync_AnswerWithoutDimensionFive_Accepted
GradeAsync_AnswerWithDimensionFive_IgnoresIt
GradeAsync_AssertionFindings_NumberedAfterUncoveredEndpoints
RunAsync_Bookstore_TestAssertionsMatchD27       // ScanPipelineTests over FixtureRepository.Bookstore + CannedLlmClient:
    // 5 rows (DbFixture.cs is not integration: **/*Fixture.cs); dimension 5 Score 4;
    // findings include unchecked-fields:…OrdersApiTests.cs::OrdersApiTests.Place_order_persists_lines_and_total
    // and error-without-state-check:…BooksApiTests.cs::BooksApiTests.Create_rejects_missing_title;
    // report validates with no violations; meta.schemaVersion "1.1"
AcceptanceCriterion_NewKinds_HaveSpecificText    // Jira, one per kind (theory)
```

- [ ] **Step 2: Run** the affected test classes. Expected: FAIL.
- [ ] **Step 3: Implement the changes**, update `CannedLlmClient` only if a canned answer no longer passes `Check`, and update `PromptLibraryTests` (`grade-quality` → `1.1.0`).
- [ ] **Step 4: Run** `dotnet build` and `dotnet test` (Docker running). Expected: PASS except the example-pinned tests Task 9 updates; list them.
- [ ] **Step 5: Commit** — `feat(app): run assertion assessment in P6 and stop grading dimension 5 with the LLM`

---

### Task 9: Regenerate the examples and the pinned numbers

**Files:**
- Modify: `BlindSpotScanner-Blueprint/examples/sample-report.json`, `sample-report-previous.json`
- Modify: tests pinned to the old numbers. Known ones: `tests/BlindSpotScanner.Application.Tests/Reports/ReportAssemblerTests.cs:152` (`5.1`),
  `tests/BlindSpotScanner.Domain.Tests/Entities/ScanRunTests.cs:29` (`Score = 5.1m`),
  `src/web/src/app/pages/report/report.spec.ts` ("▲ 0.4"), `src/web/e2e/compare.spec.ts:41,65`
  (`▲ 0.4`, `4.7 to 5.1`). Find the rest with `grep -rnE "5\.1|0\.4|Assertion strength"` over `tests/` and `src/web/`.

**Values (from Task 2's fixture levels):**
- Both examples: `meta.schemaVersion` `"1.1"`; `testAssertions` for every integration test in the
  fixture state they describe (the previous run has no `OrdersApiTests`), with sent/checked fields
  from Task 3; `summary.assertionLevels`.
- Previous: rows Get_by_id L1, List L2, Create_persists_book L4 (api), Create_rejects_missing_title L1
  (error path) → points 2,4,8,2 → dimension 5 stays **4**; score stays **4.7**.
- Current: plus Place_order L3 → mean 4.4 → dimension 5 **4** (was 5) → score **4.9**
  (`(1.2 + 0.8 + 0.45 + 1.05 + 0.6 + 0.3) / 0.9`); delta `qualityScore` 4.7 → 4.9 (`+0.2`);
  `delta.scorecard` loses its "Assertion strength" item; `delta.scoreComparable: true`.
- Findings: both examples gain `error-without-state-check:…::BooksApiTests.Create_rejects_missing_title`;
  only the current one gains `unchecked-fields:…::OrdersApiTests.Place_order_persists_lines_and_total`,
  so `delta.findings.new` gains just that key. Ids follow P6's order (uncovered endpoints, then
  assertion findings, then the LLM's). Every bullet and label is whatever `ReportAssembler` produces.

- [ ] **Step 1: Run** the "examples assemble exactly" tests (`ReportAssemblerTests`, `ReportValidatorTests`) after editing the JSON. Expected: they fail with the exact differing paths; fix the JSON until they pass.
- [ ] **Step 2: Update the pinned tests** to the new values (4.9, ▲ 0.2, `4.7 to 4.9`).
- [ ] **Step 3: Run** `dotnet test` and, in `src/web`, `npx ng test --watch=false`. Expected: PASS.
- [ ] **Step 4: Commit** — `test: regenerate example reports for schema 1.1`

---

### Task 10: Web report — per-test table, findings groups, score comparability

**Files:**
- Modify: `src/web/src/app/core/api/report.ts`
- Create: `src/web/src/app/pages/report/test-assertions/test-assertions.ts`, `.html`, `.spec.ts`
- Modify: `src/web/src/app/pages/report/report.html`, `findings/findings.ts`,
  `summary-card/summary-card.ts`, `src/web/src/app/pages/compare/compare.ts` + `.html`
- Test: the specs above; the existing e2e suite (`src/web/e2e/`) must keep passing

**Interfaces:**
- Types: `TestAssertion { test: string; level: number | null; levelSource: 'code' | 'llm' | 'unknown'; readBack?: 'api' | 'db'; errorPath: boolean; sentFields: string[]; checkedFields: string[]; uncheckedFields: string[]; fieldsResolved: boolean }`;
  `IntegrationReport.testAssertions?: TestAssertion[]`; `ReportSummary.assertionLevels?: { l0…l5: number; unknown: number }`;
  `RunDelta.scoreComparable?: boolean`; `FindingKind` gains the three kinds.
- `TestAssertions` component (`selector: 'app-test-assertions'`, inputs `tests: TestAssertion[]`,
  `levels: …`, `meta: ReportMeta`): heading "Assertion depth", a counts line
  `L0 {l0} · L1 {l1} · L2 {l2} · L3 {l3} · L4 {l4} · L5 {l5} · unknown {unknown}`, and a table sorted by
  level ascending with unknown last, then by `test`. Columns: Test (`app-evidence-link`), Level
  (`L1 · status only`; labels L0 `no assertion`, L1 `status only`, L2 `shape only`, L3 `response values`,
  L4 `saved state`, L5 `side effects`, null `unknown`), Read back (`api`/`db`/`—`), Not checked
  (`uncheckedFields` joined `, `, `unresolved` when `!fieldsResolved`, else `—`).
  `report.html` renders it after the scorecard, only when `report.testAssertions` exists.
- Findings: add groups `NoAssertion` "Tests that check nothing", `UncheckedFields` "Unchecked fields",
  `ErrorWithoutStateCheck` "Error tests without state checks".
- The quality-score chip (summary card) and the compare page's `qualityScore` metric and scorecard
  rows use `delta.scoreComparable ?? delta.comparable`.

- [ ] **Step 1: Write the failing specs**

```
'assertion table sorts weakest first, unknown last'
'assertion table shows unchecked fields and unresolved'
'assertion table is hidden for a report without testAssertions'
'findings show the three new groups'
'score chip is flat when scoreComparable is false but endpoint chips stay coloured'
'compare scorecard rows are uncoloured when scoreComparable is false'
```

- [ ] **Step 2: Run** `cd src/web && npx ng test --watch=false`. Expected: FAIL.
- [ ] **Step 3: Implement.** Check that `app-fix-action` shows no button for the new kinds (they aren't fixable in phase A); add a spec if it does.
- [ ] **Step 4: Run** `npx ng test --watch=false`, `npx ng lint`, `npx ng build`, and `npx playwright test`. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(web): show assertion depth per test`

---

## Finish

- [ ] `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` (Docker running); in `src/web`: `npx ng lint`, `npx ng test --watch=false`, `npx ng build`, `npx playwright test`.
- [ ] Rebuild the app (`docker compose up --build -d`) and confirm `/health` and the report page load.
- [ ] Self-review per CLAUDE.md: full diff, stale names, `git status`.
