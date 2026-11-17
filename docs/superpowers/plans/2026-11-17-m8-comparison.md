# M8 Run Comparison Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Users can see how integration testing changed between scans. The report shows delta chips, a
"Changes since <date>" section, a run picker and the comparability banner. A compare page diffs any
two runs of a repository. The repositories table and the repository overview show a score trend
sparkline.

**Architecture:** The delta logic already exists. `RunComparer` (M2) is a pure function, and each
succeeded report stores its delta against the previous run (`ReportAssembler`, re-derived by
`ReportValidator`). M8 adds `RunComparison` (Application) behind `GET /api/runs/compare`, which
returns the stored delta for adjacent runs and computes ad-hoc pairs live. M8 also adds
`GET /api/runs/{runId}/repository` (so the report page can list sibling runs) and a `trend` series
on `RepositorySummary`. The web app gets a hand-drawn SVG `Sparkline`, a shared `DeltaChanges`
list, delta chips and a banner on the report, and a `/compare` page. The UI never computes a delta
(doc 08).

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 + Npgsql, xUnit + FluentAssertions,
Testcontainers · Angular 22 (signals), Vitest, Playwright.

**Spec:** `BlindSpotScanner-Blueprint/docs/08-run-comparison.md` (delta, comparability, ad-hoc
compare, UI). Also `docs/04-frontend-angular.md` (`/compare` route, report header run picker and
"Compare with…"), `ui-prototype/index.html` (`compare` view, delta chips `.delta.up/.down`,
`spark()`, `.banner`/`.banner.ok`), `contracts/openapi.yaml` (`GET /runs/compare?base=&target=`,
400 for different repos), `examples/sample-report-previous.json` + `sample-report.json` (the two
example runs), `docs/11-implementation-plan.md` (M8 AC), and `docs/DECISIONS.md` (D13, D14, D21, D22).

## Global Constraints

- **AC (doc 11):** two example runs show the expected newly covered and regressed items, and the
  non-comparable banner works.
- **The user's choices (recorded as D23 in Task 1):**
  - the sparkline is hand-drawn SVG, with no chart library;
  - the trend shows on both the repositories table and the repository overview;
  - ad-hoc compare always runs old → new (by `generatedAt`), swapping a reversed pair and saying so.
- **Principle 2 / doc 08:** the UI never computes deltas. Every change it shows comes from a stored
  delta or from `GET /api/runs/compare`.
- **Principle 6 / doc 08:** when `comparable` is false the UI shows a warning banner listing
  `comparability[]`, and it does not colour deltas as progress or regression (neutral chips and
  text).
- **Doc 08:**
  - adjacent runs (target's stored `delta.baseRunId` == base) return the stored delta;
  - every other pair is computed live and labelled "ad-hoc comparison";
  - risks match on the exact headline, so a reworded risk is dropped + new.
- **Delta wire format:** the delta JSON is written with `ReportJsonSerializer.Options`, so it is
  byte-for-byte the report's own delta format (`"direction": "improved"`), not the controllers'
  default enum style.
- **Chips (prototype):**
  - `▲` for a positive change, `▼` for a negative one, nothing for zero or null;
  - the score shows one decimal (`▲ 0.4`) and counts show integers (`▼ 1`);
  - class `up` when the direction is `improved`, `down` when `regressed`, `flat` when not comparable.
- **Frontend rules:** one component per folder, signals, HTTP only through services, strict TS.
- **Backend tests:** `Method_Scenario_ExpectedResult`, AAA comments, one behavior each, no branching.

## Review Focus

1. **Comparing a run with itself or with a failed/queued run:** 400 "pick two different runs" for
   the same id; 409 when either run has no report. Never a 500 (Task 1).
2. **A reversed pair:** `base` newer than `target` → swapped to old → new, with `swapped: true`.
   Newly covered stays "newly covered" (Task 1 `CompareAsync_BaseNewerThanTarget_SwapsToOldToNew`).
3. **Runs measured differently** (different `integrationMappedTo`): `comparable: false`, the banner
   lists the reasons, and chips get no up/down colour (Task 1, Task 6, Task 8).
4. **A repository with 0 or 1 succeeded runs:** the sparkline renders nothing or one dot, and the
   run picker disables ◀/▶ (Task 4 `sparkline_onePoint_drawsDotOnly`, Task 6).
5. **Null scores in the trend:** an unscorable run is a gap in the line, not 0 (Task 4
   `sparkline_nullPoint_isAGap`).

---

## Backend

### Task 1: Run comparison service and endpoint

**Files:**
- Create: `src/BlindSpotScanner.Application/Reports/RunComparison.cs`
- Create: `src/BlindSpotScanner.Api/Controllers/CompareController.cs`
- Modify: `src/BlindSpotScanner.Infrastructure/DependencyInjection.cs` (register `RunComparison`)
- Modify: `docs/DECISIONS.md` (append D23)
- Test: `tests/BlindSpotScanner.Application.Tests/Reports/RunComparisonTests.cs` (+ a small fake `IScanRequestStore`)
- Test: `tests/BlindSpotScanner.Api.IntegrationTests/CompareApiTests.cs`
- Test: `ApiFactory.SeedSucceededRunAsync`; link `BlindSpotScanner-Blueprint/examples/*.json` into the Api test project as `Fixtures/`, as the other test projects do

**Interfaces (produces):**
```csharp
public sealed record RunHeadline(Guid RunId, string Branch, string CommitSha, DateTimeOffset GeneratedAt, decimal? Score);
/// <param name="Swapped">The request named the newer run as base; the comparison runs old → new.</param>
/// <param name="AdHoc">True when the pair isn't adjacent, so the delta was computed live (doc 08).</param>
public sealed record Comparison(RunHeadline Base, RunHeadline Target, bool Swapped, bool AdHoc, RunDelta Delta);
public enum CompareStatus { Compared, NotFound, SameRun, NoReport, DifferentRepositories }
public sealed record CompareResult(CompareStatus Status, Comparison? Comparison = null);

public sealed class RunComparison(IScanRequestStore runs)   // reuses FindRunAsync (M6)
{
    Task<CompareResult> CompareAsync(Guid baseRunId, Guid targetRunId, CancellationToken ct);
}
```
Route: `GET api/runs/compare?base={guid}&target={guid}`.
- 200 JSON `{ base, target, swapped, adHoc, delta }`. The headlines are camelCase; `delta` is the
  `RunDelta` serialized with `ReportJsonSerializer.Options` and inserted as a `JsonNode`, so it
  matches the report's delta format.
- 400 ProblemDetails for `SameRun` ("Pick two different runs.") and for `DifferentRepositories`
  ("Runs from different repositories can't be compared.").
- 404 when a run doesn't exist; 409 for `NoReport` ("Only succeeded scans can be compared.").

Algorithm: load both runs → check that both have a report → check that they share a repository →
order the reports by `meta.generatedAt` (ties by run id), setting `Swapped` when the request was
reversed → if the newer report's stored `delta?.BaseRunId` equals the older run's id, return the
stored delta with `AdHoc = false`; otherwise return `RunComparer.Compare(older, newer)` with
`AdHoc = true`.

`SeedSucceededRunAsync(Guid repositoryId, string fixture, Guid runId, Guid? previousRunId)` seeds
an example report under a new run id. It rewrites `meta.runId`, `summary.previousRunId` and
`delta.baseRunId` (when present) so the fixed example ids don't collide between tests and the
stored-delta link stays intact.

- [ ] **Step 1: Write the failing service tests** (the example reports from `Fixtures/`):
  - `CompareAsync_AdjacentExampleRuns_ReturnsStoredDeltaNotAdHoc`: base = previous example, target = current example → `AdHoc == false`, `Delta.Endpoints.NewlyCovered` equals `["POST api/orders"]`, `Delta.Endpoints.Regressed` is empty.
  - `CompareAsync_NonAdjacentPair_ComputesAdHoc`: target's stored delta points elsewhere → `AdHoc == true`, and `Delta` equals `RunComparer.Compare(previous, current)`.
  - `CompareAsync_BaseNewerThanTarget_SwapsToOldToNew`: `Swapped == true`, `Base.RunId` is the older run, and `NewlyCovered` is still `["POST api/orders"]`.
  - `CompareAsync_DifferentMapping_IsNotComparable`: the current report is changed to `e2e-mocked-data` → `Delta.Comparable == false`, and `Comparability` contains `"integrationMappedTo changed"`.
  - `CompareAsync_SameRun_ReturnsSameRun`, `CompareAsync_RunWithoutReport_ReturnsNoReport`, `CompareAsync_DifferentRepositories_ReturnsDifferentRepositories`, `CompareAsync_UnknownRun_ReturnsNotFound`.
- [ ] **Step 2: Run** `dotnet test tests/BlindSpotScanner.Application.Tests --filter RunComparison`. Expected: FAIL (the types are missing).
- [ ] **Step 3: Implement** `RunComparison`.
- [ ] **Step 4: Write the failing API tests:**
  - `Compare_ExampleRuns_ReturnsNewlyCoveredAndNoRegressions`: the JSON `delta.endpoints.newlyCovered` is `["POST api/orders"]`, `regressed` is `[]`, and the first metric's `direction` is `"improved"`.
  - `Compare_SameRun_Returns400`, `Compare_RunsFromDifferentRepositories_Returns400`, `Compare_QueuedRun_Returns409`, `Compare_UnknownRun_Returns404`.
- [ ] **Step 5: Implement** `CompareController` and the DI registration.
- [ ] **Step 6: Append D23** to `docs/DECISIONS.md`. It records:
  - the three user choices;
  - the response shape `{ base, target, swapped, adHoc, delta }`, with the delta in the report's own JSON format;
  - status codes 400/404/409;
  - the meaning of "adjacent": the newer run's stored `delta.baseRunId`;
  - `GET /api/runs/{id}/repository` and `RepositorySummary.trend` (12 points) as additions to the contract.
- [ ] **Step 7: Run** `dotnet test` and `dotnet format --verify-no-changes`. Expected: PASS. **Commit** `feat(api): compare any two runs of a repository`.

### Task 2: Run → repository lookup and the trend series

**Files:**
- Modify: `src/BlindSpotScanner.Api/Controllers/RunsController.cs` (`GET api/runs/{runId}/repository`)
- Modify: `src/BlindSpotScanner.Application/Repositories/RepositoryService.cs` (`RepositorySummary.Trend`, `FindByRunAsync`)
- Modify: `src/BlindSpotScanner.Infrastructure/Repositories/EfRepositoryStore.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Repositories/EfRepositoryStoreTests.cs`
- Test: `tests/BlindSpotScanner.Api.IntegrationTests/RepositoriesApiTests.cs`

**Interfaces (produces):**
```csharp
/// <param name="CoveredShare">Covered / total endpoints; null when the run had no endpoints.</param>
public sealed record TrendPoint(Guid RunId, DateTimeOffset GeneratedAt, decimal? Score, decimal? CoveredShare);
// RepositorySummary gains a trailing parameter: IReadOnlyList<TrendPoint> Trend (default []), the last 12 succeeded runs, oldest first.
// IRepositoryStore gains: Task<Repository?> FindByRunAsync(Guid runId, CancellationToken ct);
// RepositoryService gains: Task<Repository?> FindByRunAsync(Guid runId, CancellationToken ct);
```
`GET api/runs/{runId}/repository` returns 200 `RepositoryResponse` (the M7 record), or 404 when
the run doesn't exist.

- [ ] **Step 1: Write the failing tests:**
  - `ListAsync_ThirteenSucceededRuns_TrendHasLastTwelveOldestFirst`;
  - `ListAsync_NullScoreRun_TrendKeepsNullPoint`;
  - `FindByRunAsync_KnownRun_ReturnsItsRepository`;
  - API: `RunRepository_KnownRun_ReturnsRepository`, `RunRepository_UnknownRun_Returns404`.
- [ ] **Step 2: Run** with `--filter "Trend|FindByRun|RunRepository"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Write the trend as one query: succeeded runs ordered by `GeneratedAt` descending, then `Id` descending, take 12 per repository, reversed. Keep using `Score`, `EndpointsCovered` and `EndpointsTotal` (the M7 headline columns).
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS. **Commit** `feat(api): add run repository lookup and score trend`.

## Frontend

### Task 3: Delta types and API methods

**Files:**
- Modify: `src/web/src/app/core/api/report.ts` (replace `RunDelta = Record<string, unknown>` with the typed delta)
- Modify: `src/web/src/app/core/api/api-types.ts` (`TrendPoint`, `RepositorySummary.trend`, `Comparison`)
- Modify: `src/web/src/app/core/api/runs-api.ts` (+ spec)

**Interfaces (produces):**
```ts
export type DeltaDirection = 'improved' | 'regressed' | 'unchanged';
export interface DeltaMetric { name: string; prev: number | null; curr: number | null; change: number | null; direction: DeltaDirection }
export interface RunDelta {
  baseRunId: string; baseCommitSha: string; baseGeneratedAt: string; comparable: boolean; comparability: string[];
  metrics: DeltaMetric[];
  endpoints: { newlyCovered: string[]; regressed: string[]; added: string[]; removed: string[]; confidenceChanged?: string[] };
  scorecard: { dimension: string; prev: number | null; curr: number | null; change: number | null }[];
  findings: { fixed: string[]; new: string[] };
  risks: { dropped: string[]; new: string[] };
}
export interface TrendPoint { runId: string; generatedAt: string; score: number | null; coveredShare: number | null }
export interface RunHeadline { runId: string; branch: string; commitSha: string; generatedAt: string; score: number | null }
export interface Comparison { base: RunHeadline; target: RunHeadline; swapped: boolean; adHoc: boolean; delta: RunDelta }
// RunsApi gains:
compare(base: string, target: string): Observable<Comparison>;   // GET /api/runs/compare?base=&target=
repository(runId: string): Observable<Repository>;               // GET /api/runs/{runId}/repository
```
- [ ] **Step 1: Write the failing specs:** `compare gets /api/runs/compare with base and target`, `repository gets /api/runs/{id}/repository`.
- [ ] **Step 2:** Run them and watch them fail. Implement. Run `npx ng test --watch=false` and `npx ng lint`. Expected: PASS. **Commit** `feat(web): add delta types and compare API`.

### Task 4: Sparkline component and trends

**Files:**
- Create: `src/web/src/app/shared/sparkline/` (component + spec)
- Modify: `src/web/src/app/pages/repositories/` (Trend column), `src/web/src/app/pages/repo-overview/` (score and covered-share sparklines above the run table)

**Interfaces (produces):**
```ts
// <app-sparkline [values]="(number | null)[]" [max]="10" [label]="'Score trend'" />
export class Sparkline {
  readonly values = input.required<(number | null)[]>();
  readonly max = input(10);        // 1 for shares
  readonly label = input.required<string>();
}
```
Drawing (the prototype's `spark()`):
- the SVG is 120×34, with `x = 6 + i·(108/(n−1))` and `y = 30 − (v/max)·26`;
- the line path is drawn in `var(--accent)`, the area fill in `var(--accent-soft)`, and the last non-null point is a dot;
- a null value ends the current line segment (a gap) and is never drawn as 0;
- `aria-label` is `"{label}: {first} to {last}"` over the non-null values, formatted to 1 decimal (shares as %).

What it renders:
- no SVG when there are no non-null values;
- only the dot when there is one value.

The repositories table's Trend cell shows `trend` scores, or `—` when there are fewer than 2
points. The overview shows "Score" (max 10) and "Covered share" (max 1) sparklines, built from the
succeeded runs in history, oldest first.

- [ ] **Step 1: Write the failing specs:**
  - `sparkline_twoPoints_drawsLineAndLastDot`;
  - `sparkline_onePoint_drawsDotOnly`;
  - `sparkline_noValues_rendersNothing`;
  - `sparkline_nullPoint_isAGap` (two path segments);
  - `sparkline_ariaLabel_namesFirstAndLast`;
  - `repositories trend column shows a sparkline for two or more points`;
  - `overview shows score and covered share trends oldest first`.
- [ ] **Step 2:** Run them and watch them fail. Implement. Run `ng test` and `ng lint`. Expected: PASS. **Commit** `feat(web): add score trend sparklines`.

### Task 5: Delta chips and the shared changes list

**Files:**
- Create: `src/web/src/app/shared/delta-chip/` (component + spec)
- Create: `src/web/src/app/shared/delta-changes/` (component + spec)

**Interfaces (produces):**
```ts
// <app-delta-chip [metric]="DeltaMetric | undefined" [comparable]="boolean" [decimals]="0|1" />
//   renders nothing when the metric is missing or its change is 0/null; otherwise "▲ 0.4" / "▼ 1"
//   with class delta up|down|flat (flat when !comparable or direction 'unchanged').
// <app-delta-changes [delta]="RunDelta" />
//   cols3 panels: Newly covered · Regressed · Added / removed endpoints · Confidence changed · Findings (Fixed / New stableKeys) · Risks (Dropped / New headlines);
//   an empty list prints "None."; panels with nothing at all still render so the user sees "None.".
//   Note under the panels: "Risks match on exact wording. A reworded risk appears as dropped plus new, never as resolved."
```
- [ ] **Step 1: Write the failing specs** (with the delta from `examples/sample-report.json`):
  - `chip improved score shows ▲ 0.4 up`;
  - `chip fewer untested shows ▼ 1 up` (endpoints.none −1, improved);
  - `chip not comparable is flat`;
  - `chip zero change renders nothing`;
  - `changes lists newly covered POST api/orders and Regressed None.`;
  - `changes lists dropped and new risks`.
- [ ] **Step 2:** Run them and watch them fail. Implement. Run `ng test` and `ng lint`. Expected: PASS. **Commit** `feat(web): add delta chips and changes list`.

### Task 6: Report page — chips, banner, changes, run picker

**Files:** Modify `src/web/src/app/pages/report/` (`report.ts/.html/.spec.ts`, `summary-card/`).

Content:
- **KPI chips:** delta chips from `report.delta?.metrics`:
  - the score tile uses `qualityScore` with 1 decimal;
  - the "Endpoints covered", "Weak", "Untested" and "Unverified" tiles use `endpoints.covered`, `endpoints.weak`, `endpoints.none` and `endpoints.unverified`.
- **Banner** (between the header and the KPIs):
  - when `delta && !delta.comparable`: `.banner` with "Not comparable with the previous scan:" plus the reasons joined by "; ";
  - when the delta is comparable but `comparability.length > 0`: a muted `.note` with "Compared with the previous scan despite: …";
  - when there is no delta (the first scan): nothing.
- **"Changes since {baseGeneratedAt | date: 'd MMM y'}":** a panel with `<app-delta-changes>` after the summary card, shown only when there is a delta.
- **Run picker** in the header:
  - load `runs.repository(runId)`, then `repositories.runs(repo.id)`, filtered to `Succeeded` and ordered newest first;
  - ◀ goes to the older run and ▶ to the newer one; each is disabled at its end;
  - a `<select>` with "{date} · {sha7}" options navigates to `/runs/{id}`;
  - "Compare with…" is a link to `/compare?base={delta.baseRunId ?? previous run id}&target={runId}`, disabled when there is no other run.
- **Route reuse:** the page reacts to `runId` changes (an `effect` on the input), because the run picker navigates between runs on the same route.

- [ ] **Step 1: Write the failing specs:**
  - `score tile shows the delta chip ▲ 0.4`;
  - `not comparable shows the banner and flat chips`;
  - `first scan shows no banner and no changes`;
  - `changes section names the base date`;
  - `run picker lists succeeded runs newest first`;
  - `older button navigates to the previous run`;
  - `newest run disables the newer button`;
  - `Compare with links base and target`;
  - `navigating to another run reloads the report`.
- [ ] **Step 2:** Run them and watch them fail. Implement. Run `ng test`, `ng lint` and `ng build`. Expected: PASS. **Commit** `feat(web): show deltas, comparability and run picker on the report`.

### Task 7: Compare page

**Files:** Create `src/web/src/app/pages/compare/` (component + spec); modify `app.routes.ts` (`compare`, query params bound as inputs `base` and `target`).

Content (the prototype's `compare` view):
- **Header:** "Compare runs" and the repository full name (from `runs.repository(target)`). Two `<select>` pickers, Baseline and Target, list that repository's succeeded runs as "{date} · {sha7}". Changing a picker navigates to `/compare?base=…&target=…`.
- **Banner:**
  - `.banner.ok`: "These runs are like-for-like: same endpoint list, same definition of integration, same profile and prompt version." when comparable and `comparability` is empty;
  - `.banner`: "Not comparable:" plus the reasons, when not comparable;
  - `.note`: the reasons, when comparable with reasons.
- **Labels:** "Ad-hoc comparison (not adjacent runs)" when `adHoc`; "Swapped to compare older → newer" when `swapped`.
- **Metrics table:** columns Metric, {base date}, {target date}, Change. The rows come from `delta.metrics`, with names mapped to labels: Quality score, Covered operations, Weak, Untested, Unverified, False positives. Then one row per `delta.scorecard` dimension. The Change cell shows "▲ 0.4 improved", "▼ 1 improved" or "unchanged", with class `up`/`down` only when comparable.
- **Changes:** `<app-delta-changes [delta]>`.
- **Errors:** a 400/409 problem shows in the snackbar (the interceptor) and the page body shows "These runs can't be compared." Missing `base` or `target` shows "Pick two runs to compare."

- [ ] **Step 1: Write the failing specs:**
  - `example runs show newly covered POST api/orders and Regressed None.`;
  - `like-for-like shows the ok banner`;
  - `not comparable shows the warning banner and uncoloured changes`;
  - `ad-hoc label shown when adHoc`;
  - `swapped note shown when swapped`;
  - `changing the target picker navigates with both query params`;
  - `compare error shows cannot be compared`;
  - `missing params asks to pick two runs`.
- [ ] **Step 2:** Run them and watch them fail. Implement. Run `ng test`, `ng lint` and `ng build`. Expected: PASS. **Commit** `feat(web): add compare page`.

### Task 8: Playwright acceptance and final verification

**Files:** Modify `src/web/e2e/mock-api.ts`, create `src/web/e2e/compare.spec.ts`.

The mock adds:
- the previous example run (`11111111-…`) next to the current one (`22222222-…`);
- `GET /api/runs/{id}/repository` → the bookstore repository;
- `GET /api/repositories/{id}/runs` → both runs as Succeeded, with headline columns from the examples;
- `RepositorySummary.trend` → both points;
- `GET /api/runs/compare` → `{ base, target, swapped: false, adHoc: false, delta: <the current example's delta> }`;
- option `{ notComparable: true }`, which returns that delta with `comparable: false` and `comparability: ["integrationMappedTo changed"]`.

Specs:
- `report_compareWith_showsNewlyCoveredAndNoRegressions`: open the current report → click "Compare with…" → the URL is `/compare?base=1111…&target=2222…`. The "Newly covered" panel contains `POST api/orders`, and the "Regressed" panel says `None.`.
- `compare_notComparable_showsWarningBanner`: the banner contains "Not comparable" and "integrationMappedTo changed", and no `.up` or `.down` element exists.
- `report_kpiChips_showScoreDelta`: the score tile contains `▲ 0.4`.
- `report_runPicker_opensPreviousRun`: ◀ → the URL is `/runs/1111…`.
- `repositories_trend_drawsSparkline`: the Trend cell contains an `svg`.

- [ ] **Step 1: Write the specs and the mocks.** Run `npx playwright test`. Expected: PASS. A failure here is a bug in a page; fix it test-first in that page's spec.
- [ ] **Step 2: Full verification:**
  - `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`;
  - in `src/web`: `npx ng lint`, `npx ng test --watch=false`, `npx ng build`, `npx playwright test`;
  - `docker compose up --build`: check `http://localhost:8080/compare?...` loads through nginx, then `docker compose down`.
- [ ] **Step 3: Commit** `test(web): add comparison acceptance tests`.
