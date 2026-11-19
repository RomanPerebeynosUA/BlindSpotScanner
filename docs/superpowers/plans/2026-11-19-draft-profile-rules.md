# Auto-draft fills the profile rules — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Auto-draft with AI" fills the typed classification rules (not only the Markdown), every
proposed pattern is checked against the repository's real file list, and the editor shows how many
files each pattern matches; the user still reviews and saves (D22).

**Architecture:** A pure `ProfilePatterns` helper (Application) checks pattern syntax and counts
matches over tree paths with the same `Matcher` semantics the scan uses. `ProfileDrafter` asks the
LLM (prompt v1.1.0) for Markdown + rules, re-asks on malformed patterns, re-asks once more for
patterns that match no file, then flags what still matches nothing. A new
`POST …/profile/matches` endpoint recounts edited patterns; profile save rejects malformed ones.
The Angular editor fills every field from the draft and shows the counts.

**Tech Stack:** .NET 10 / C# (Application, Api), Microsoft.Extensions.FileSystemGlobbing, xUnit +
FluentAssertions, WireMock fakes; Angular 22 standalone + signals, Playwright.

**Spec (from the user, 2026-11-19, recorded as D29 in Task 2):**
1. The draft returns `integrationMappedTo` and the seven rule lists (`testGlobs`, `controllerGlobs`,
   `minimalApiGlobs`, `integrationGlobs`, `integrationAttributes`, `integrationBaseTypes`,
   `notIntegrationGlobs`) next to `markdown` and `openQuestions`.
2. Patterns are validated against the repo tree. A malformed pattern (whitespace or a comma) is
   re-asked. A pattern matching 0 files is re-asked once, and if it still matches 0 it is kept
   and flagged as an open question.
3. The editor shows per-pattern match counts right after a draft, and a **Check patterns** button
   recounts after edits (one tree read per click).
4. A draft replaces the Markdown, the mapping and every rule field. Nothing is saved until Save.
5. Prompt set version 1.0.0 → 1.1.0.

## Global Constraints

- Glob fields (counted, syntax-checked): `testGlobs`, `controllerGlobs`, `minimalApiGlobs`,
  `integrationGlobs`, `notIntegrationGlobs`. `integrationAttributes` and `integrationBaseTypes` are
  names, not globs: never counted or syntax-checked.
- Matching is case-sensitive (`new Matcher(StringComparison.Ordinal)`) over tree blob paths that
  are not `EnumeratePhase.IsExcluded`, so counts agree with what a scan enumerates.
- A malformed pattern contains any whitespace character or `,`. Error text, exactly:
  `{field}: '{pattern}' is not one pattern; put one pattern per line with no spaces or commas`.
- Zero-match open question, exactly: `{field}: '{pattern}' matches no file in the repository; check it before saving.`
- Zero-match re-ask input key: `zeroMatchPatterns` (array of `"{field}: {pattern}"` strings).
- `PromptSetVersion` default `"1.1.0"` (`LlmOptions`, `appsettings.Example.json`); prompt header `— v1.1.0`.
- TDD, `Method_Scenario_ExpectedResult` names, `// Arrange // Act // Assert`, no branching in tests.
- No new interfaces or abstractions beyond those named here.
- Commit messages: Conventional Commits, ≤72-char subject, ending with the Co-Authored-By trailer.

## Review Focus

1. **A pattern with a leading `./` or a Windows `\`** (e.g. `.\Todo.Api\**`): it must be reported
   as matching 0 files, not crash. Test in Task 1.
2. **An empty rule list** (`controllerGlobs: []` for a minimal-API-only repo) is valid: no syntax
   error, no zero-match question. Test in Task 2.
3. **The draft's second (zero-match) call fails** (LLM error): keep the first answer and flag its
   zero-match patterns, don't fail the draft. Test in Task 2.
4. **Save with a malformed pattern pasted from elsewhere** (the v4 incident: explanation text in a
   field) returns 400 with the exact syntax error and saves nothing. Test in Task 3.
5. **Check patterns on a read-only (Viewer) session**: counting is a read, so it works for Viewers;
   Save stays hidden. Test in Task 4.

---

### Task 1: `ProfilePatterns` — syntax check and match counts

**Files:**
- Create: `src/BlindSpotScanner.Application/Repositories/ProfilePatterns.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Repositories/ProfilePatternsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ProfileRules(string IntegrationMappedTo, IReadOnlyList<string> TestGlobs, IReadOnlyList<string> ControllerGlobs, IReadOnlyList<string> MinimalApiGlobs, IReadOnlyList<string> IntegrationGlobs, IReadOnlyList<string> IntegrationAttributes, IReadOnlyList<string> IntegrationBaseTypes, IReadOnlyList<string> NotIntegrationGlobs);`
  - `public sealed record PatternMatch(string Field, string Pattern, int Files);` (`Field` is the camelCase JSON name, e.g. `"minimalApiGlobs"`)
  - `public static class ProfilePatterns`
    - `public static IReadOnlyList<string> SyntaxErrors(ProfileRules rules)`
    - `public static IReadOnlyList<PatternMatch> Count(IEnumerable<string> treePaths, ProfileRules rules)` — drops `EnumeratePhase.IsExcluded` paths itself; output order: field order above, then pattern order.

- [ ] **Step 1: Write the failing tests**

```csharp
SyntaxErrors_PatternWithSpace_NamesFieldAndPattern
  // MinimalApiGlobs = ["**/*Api.cs (matches TodoApi.cs)"]
  // → ["minimalApiGlobs: '**/*Api.cs (matches TodoApi.cs)' is not one pattern; put one pattern per line with no spaces or commas"]
SyntaxErrors_TwoPatternsJoinedByComma_IsRejected
  // NotIntegrationGlobs = ["**/A.cs,**/B.cs"] → one error naming notIntegrationGlobs
SyntaxErrors_AttributeWithSpaces_IsNotChecked
  // IntegrationAttributes = ["Trait(\"Category\", \"Integration\")"] → empty
Count_MinimalApiPattern_CountsMatchingTreeFiles
  // paths: Todo.Api/Todos/TodoApi.cs, Todo.Api/Users/UsersApi.cs, Todo.Api.Tests/TodoApiTests.cs
  // MinimalApiGlobs ["**/*Api.cs"] → contains new PatternMatch("minimalApiGlobs", "**/*Api.cs", 2)
Count_ExcludedPaths_AreNotCounted
  // paths include Todo.Api/obj/Gen/FooApi.cs → still 2
Count_BackslashPattern_MatchesNothing   // Review Focus 1
  // TestGlobs [".\\Todo.Api.Tests\\**"] → Files == 0
```

- [ ] **Step 2: Run, expect FAIL (types missing)**

Run: `dotnet test tests/BlindSpotScanner.Application.Tests --filter FullyQualifiedName~ProfilePatternsTests`

- [ ] **Step 3: Implement `ProfileRules`, `PatternMatch`, `ProfilePatterns` in `ProfilePatterns.cs`**

A private `(string Field, IReadOnlyList<string> Patterns)[] Globs(ProfileRules)` lists the five glob
fields in the constraint order; `Count` builds one `Matcher(StringComparison.Ordinal)` per pattern
over the filtered paths (same as `ClassifyPhase.FirstGlob`).

- [ ] **Step 4: Run, expect PASS**

- [ ] **Step 5: Commit** — `feat(app): check and count profile patterns`

---

### Task 2: The draft proposes rules (prompt v1.1.0)

**Files:**
- Modify: `BlindSpotScanner-Blueprint/prompts/draft-profile.md` (v1.1.0: input gains optional `zeroMatchPatterns`; output gains `integrationMappedTo` + the seven lists; rules: one pattern per entry, patterns must match paths in `rootTree`, an empty list is fine)
- Modify: `src/BlindSpotScanner.Application/Llm/Schemas/draft-profile.schema.json` (all 10 properties required; `integrationMappedTo` enum `real-seam | e2e-mocked-data | e2e-real-backend | contract | other`; lists are string arrays)
- Modify: `src/BlindSpotScanner.Application/Repositories/ProfileDrafter.cs`
- Modify: `src/BlindSpotScanner.Infrastructure/Llm/LlmOptions.cs`, `src/BlindSpotScanner.Api/appsettings.Example.json` (`PromptSetVersion` `1.1.0`)
- Modify: `tests/BlindSpotScanner.Api.IntegrationTests/FakeClaudeServer.cs` (`Draft` answer gains rules whose globs match the fake GitHub tree)
- Modify: `docs/DECISIONS.md` (add D29: reverses D22's "the AI draft fills only the Markdown"; D18 stands — rules are still typed columns, never parsed from Markdown)
- Test: `tests/BlindSpotScanner.Application.Tests/Repositories/ProfileDrafterTests.cs`, `tests/BlindSpotScanner.Application.Tests/Llm/LlmSchemasTests.cs`

**Interfaces:**
- Consumes: `ProfileRules`, `PatternMatch`, `ProfilePatterns.SyntaxErrors`, `ProfilePatterns.Count` (Task 1).
- Produces: `public sealed record ProfileDraft(string Markdown, IReadOnlyList<string> OpenQuestions, ProfileRules Rules, IReadOnlyList<PatternMatch> Matches);` — serialized by the API as `markdown, openQuestions, rules{…}, matches[{field, pattern, files}]`.

Flow in `DraftAsync` after the tree is read (`paths` = all non-excluded blob paths, not the 3000-capped list):
1. Call 1 with `Check = answer => markdown-not-empty errors + ProfilePatterns.SyntaxErrors(Rules(answer))`.
2. `zero = Count(paths, rules).Where(m => m.Files == 0)`. If any: call 2 with the same input plus
   `zeroMatchPatterns`; use call 2's answer when it succeeds, else keep call 1's.
3. Every pattern of the kept answer that still matches 0 → zero-match open question (constraint text),
   appended after the model's own questions; the truncated/capped notices stay first.

- [ ] **Step 1: Write the failing tests** (FakeLlmClient answers in order)

```csharp
DraftAsync_Answer_ReturnsRulesAndMatches
  // answer rules TestGlobs ["tests/**/*.cs"] over the existing fake tree → Draft.Rules equals answer; Matches has ("testGlobs","tests/**/*.cs", N>0)
DraftAsync_MalformedPattern_IsReasked
  // Requests.Single().Validate!(answer with "**/*Api.cs (x)") contains the syntax error text
DraftAsync_ZeroMatchPattern_ReasksOnceWithZeroMatchPatterns
  // 1st answer MinimalApiGlobs ["src/**/Endpoints/**/*.cs"] (0 files), 2nd fixes it
  // → 2 requests; 2nd InputJson contains "\"zeroMatchPatterns\":[\"minimalApiGlobs: src/**/Endpoints/**/*.cs\"]"; Draft uses 2nd rules
DraftAsync_StillZeroAfterReask_FlagsOpenQuestion
  // both answers 0 files → OpenQuestions contains "minimalApiGlobs: 'src/**/Endpoints/**/*.cs' matches no file in the repository; check it before saving."
DraftAsync_ReaskFails_KeepsFirstAnswerAndFlags          // Review Focus 3
  // 2nd call returns LlmResult(null, ["x"], …) → Status Drafted, first rules, zero-match question present
DraftAsync_EmptyControllerGlobs_NoQuestionNoReask       // Review Focus 2
  // ControllerGlobs [] and all other globs match → exactly 1 request, no zero-match question
LlmSchemas_DraftProfile_RequiresRules (LlmSchemasTests)
  // schema "required" contains "integrationMappedTo" and "minimalApiGlobs"
```

- [ ] **Step 2: Run, expect FAIL**

Run: `dotnet test tests/BlindSpotScanner.Application.Tests --filter "FullyQualifiedName~ProfileDrafterTests|FullyQualifiedName~LlmSchemasTests"`

- [ ] **Step 3: Implement** — update the prompt and schema, `ProfileDraft`, `DraftAsync` per the flow above (a private `static ProfileRules Rules(JsonElement answer)` reads the answer); bump `PromptSetVersion`; update `FakeClaudeServer.Draft`; write D29.

- [ ] **Step 4: Run all backend tests, expect PASS**

Run: `dotnet test` (Docker running). `ProfilesApiTests.Post_Draft_Returns200WithMarkdownAndQuestions` must still pass with the new fields present.

- [ ] **Step 5: Commit** — `feat(app): let the AI draft propose profile rules`

---

### Task 3: Recount endpoint and save-time syntax check

**Files:**
- Create: `src/BlindSpotScanner.Application/Repositories/ProfilePatternChecker.cs`
- Modify: `src/BlindSpotScanner.Application/Repositories/ProfileService.cs` (`SaveAsync` rejects `ProfilePatterns.SyntaxErrors`)
- Modify: `src/BlindSpotScanner.Api/Controllers/ProfilesController.cs` (new action), DI registration where `ProfileDrafter` is registered
- Modify: `BlindSpotScanner-Blueprint/contracts/openapi.yaml` (add `/repositories/{repoId}/profile/matches`; draft 200 description gains "+ rules + pattern match counts")
- Test: `tests/BlindSpotScanner.Application.Tests/Repositories/ProfilePatternCheckerTests.cs`, `tests/BlindSpotScanner.Application.Tests/Repositories/ProfileServiceTests.cs`, `tests/BlindSpotScanner.Api.IntegrationTests/ProfilesApiTests.cs`

**Interfaces:**
- Consumes: Task 1 types; `IRepositoryStore.FindAsync`, `IGitHubClient.GetBranchHeadShaAsync/GetTreeAsync` (as `ProfileDrafter` uses them).
- Produces:
  - `public enum PatternCheckStatus { Checked, RepositoryNotFound, GitHubFailed }`
  - `public sealed record PatternCheckResult(PatternCheckStatus Status, IReadOnlyList<PatternMatch> Matches, IReadOnlyList<string> Errors);` (`Errors` = syntax errors; malformed patterns are still counted)
  - `public sealed class ProfilePatternChecker(IRepositoryStore repositories, IGitHubClient github)` with `Task<PatternCheckResult> CheckAsync(Guid repositoryId, ProfileRules rules, CancellationToken ct)` — default-branch head, recursive tree.
  - `POST /api/repositories/{repoId}/profile/matches`, body = the save body's rule fields (`SaveProfileRequest` without `markdown`/`baseVersion` is fine to reuse), default (any signed-in role) authorization like the GET; 200 `{ matches: [{field, pattern, files}], errors: [] }`, 404 unknown repo, 502 GitHub.
  - Save: `SaveProfileStatus.Invalid` path returns 400 ValidationProblem with the syntax errors under key `"patterns"`.

- [ ] **Step 1: Write the failing tests**

```csharp
CheckAsync_KnownRepo_CountsAgainstDefaultBranchTree      // matches from a fake tree
CheckAsync_UnknownRepo_RepositoryNotFound
SaveAsync_MalformedPattern_RejectedAndNothingSaved       // Review Focus 4 (ProfileServiceTests)
Post_Matches_Returns200WithCounts                         // ProfilesApiTests, FakeGitHubServer tree
Post_Matches_AsViewer_Returns200                          // Review Focus 5
Put_Profile_MalformedPattern_Returns400WithError          // body has "**/*Api.cs (matches x)"; response contains the exact syntax error; versions unchanged
```

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement** the checker, the save check, the action, DI, OpenAPI.
- [ ] **Step 4: Run `dotnet test` and `dotnet format --verify-no-changes`, expect PASS**
- [ ] **Step 5: Commit** — `feat(api): recount profile patterns and reject malformed ones on save`

---

### Task 4: Editor fills the rules and shows counts

**Files:**
- Modify: `src/web/src/app/core/api/api-types.ts` (`ProfileRules`, `PatternMatch {field, pattern, files}`, `ProfileDraft` gains `rules`, `matches`; `PatternCheck {matches, errors}`)
- Modify: `src/web/src/app/core/api/repositories-api.ts` (`profileMatches(id: string, rules: ProfileRules): Observable<PatternCheck>`)
- Modify: `src/web/src/app/pages/profile-editor/profile-editor.ts/.html/.scss`
- Modify: `src/web/e2e/mock-api.ts` (handle `POST …/profile/draft` and `POST …/profile/matches`)
- Create: `src/web/e2e/profile.spec.ts`
- Test: `src/web/src/app/pages/profile-editor/profile-editor.spec.ts`

**Interfaces:**
- Consumes: the draft and matches JSON from Tasks 2–3.
- Produces (component): `protected readonly matches = signal<PatternMatch[]>([])`, `protected readonly patternErrors = signal<string[]>([])`, `protected checkPatterns(): void`. `draft()` sets markdown, mapping, every list (joined with `\n`), `openQuestions`, `matches`; marks dirty. Editing a list clears that field's counts (they're stale). Under each glob textarea: one line per pattern, `pattern — N files`, a 0 shown with a warning class `zero`. **Check patterns** button (visible also when read-only) calls `profileMatches` with the current lists and sets `matches` + `patternErrors`.

- [ ] **Step 1: Write the failing tests**

```ts
// profile-editor.spec.ts
draft_fillsRuleFieldsAndMapping            // minimalApiGlobs textarea value === '**/*Api.cs'; mapping select === draft value
draft_showsMatchCountPerPattern            // text '**/*Api.cs — 4 files' visible
draft_zeroMatch_markedAsWarning            // the 0-files line has class 'zero'
checkPatterns_sendsCurrentListsAndShowsCounts
checkPatterns_readOnly_stillAvailable       // Review Focus 5: button present for Viewer
// e2e/profile.spec.ts
profile_autoDraft_fillsRulesAndShowsCounts
```

- [ ] **Step 2: Run, expect FAIL** — `npx ng test --watch=false` (in `src/web`)
- [ ] **Step 3: Implement** types, service method, component, template, styles, mocks.
- [ ] **Step 4: Run `npx ng test --watch=false`, `npx ng lint`, `npx ng build`, `npx playwright test`, expect PASS**
- [ ] **Step 5: Commit** — `feat(web): fill profile rules from the AI draft and show match counts`
