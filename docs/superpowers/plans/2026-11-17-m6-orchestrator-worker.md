# M6 Citation Verifier, Orchestrator, Worker and Progress Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A scan requested over the API is queued in PostgreSQL, picked up by the worker, run
through P1–P7 with per-phase steps, progress over SignalR, cancel and a cost cap, its citations
verified at the pinned SHA, and its report stored and served. A forged citation fails the run.

**Architecture:** `ScanRun` gains a job lifecycle (queued → running → succeeded/failed/cancelled,
lease, attempts, cancel flag). Application adds `ScanOrchestrator` (runs `ScanPipeline`, records
steps, verifies citations, stores the outcome) behind ports `IScanStore`, `IScanQueue`,
`IScanEvents`, plus `CitationVerifier` and `BudgetedLlmClient`. Infrastructure implements the ports
with EF Core, `FOR UPDATE SKIP LOCKED` and `pg_notify`, and registers everything in one DI
extension. The Worker hosts `ScanWorker`; the Api hosts thin controllers, the `/hubs/scans` hub
and a `LISTEN` relay. Repository/profile endpoints are M7 (user's choice, D21).

**Tech Stack:** .NET 10, ASP.NET Core controllers + SignalR, EF Core 10 + Npgsql (LISTEN/NOTIFY),
BackgroundService, xUnit + FluentAssertions, Testcontainers, WireMock.Net,
Microsoft.AspNetCore.SignalR.Client (tests), FakeTimeProvider.

**Spec:** `BlindSpotScanner-Blueprint/docs/02-architecture.md` (queue, worker, sequence, budgets),
`docs/03-backend-dotnet.md` (services, API surface, hub messages), `docs/05-scan-pipeline.md`
(P7, citation hard rule, budget rule), `docs/10-security-config-ops.md` (config, failure table),
`contracts/openapi.yaml` (normative: `POST /runs/{runId}/cancel` → 202, `GET /runs/{runId}/steps`,
scan body `{ branch?, maxCostUsd? }`), `docs/11-implementation-plan.md` (M6 AC), `docs/DECISIONS.md`.

## Global Constraints

- Config (doc 10): `ConnectionStrings:Postgres`; `Llm:MaxCostUsdPerScan` = 5.0;
  `Scan:MaxConcurrentScans` = 2, `Scan:MaxFileBytes` = 200000; `Scan:LeaseSeconds` = 120,
  `Scan:HeartbeatSeconds` = 30, `Scan:PollSeconds` = 2 (new, D21).
- Failure table (doc 10): budget cap → remaining items `unverified`, run still **Succeeded**, reason
  recorded; unresolved citation → run **Failed** with the citations listed; worker crash → lease
  expires, retried **once**, then **Failed**.
- Principle 7: a stored report is immutable (existing trigger + `RecordReport`); a failed or
  cancelled run stores no report.
- Principle 8: nothing outward-facing in a scan.
- Hub messages exactly: `ScanProgress(runId, phase, pct, message)`, `ScanCompleted(runId, status)`;
  status strings are the `RunStatus` names.
- Secrets: no token or key in logs, exceptions, failure reasons or hub messages.
- Tests: `Method_Scenario_ExpectedResult`, AAA, one behavior, no branching.

## Review Focus

1. **Two workers dequeue at once**: each job is taken by exactly one (Task 5 `Dequeue_TwoWorkersConcurrently_GetDistinctJobs`).
2. **A worker dies mid-scan**: the expired lease is retried once, then the run is Failed (Task 5 `Dequeue_ExpiredLease_RetriedOnceThenFailed`).
3. **Cancel while running**: the scan stops at the next phase boundary, ends `Cancelled` (not `Failed`), stores no report (Task 6 `Run_CancelRequestedDuringScan_EndsCancelled`).
4. **Budget runs out mid-mapping**: later LLM calls are not made, their items are `unverified`, the run Succeeds with the reason recorded (Task 6 `Run_BudgetExhausted_SucceedsWithReason`).
5. **A cited file vanished or a test was invented**: Failed with each bad citation listed; a fixture-method citation outside coverage evidence (`ApiFactory.cs::InitializeAsync`) is accepted (Task 3).

---

### Task 1: ScanRun job lifecycle and columns

**Files:**
- Modify: `src/BlindSpotScanner.Domain/Entities/ScanRun.cs`
- Create: migration `AddScanJobColumns`
- Test: `tests/BlindSpotScanner.Domain.Tests/ScanRunLifecycleTests.cs`, `tests/BlindSpotScanner.Infrastructure.Tests/Persistence/EntityMappingTests.cs`

**Interfaces (produces):**
```csharp
public static ScanRun Enqueue(Guid id, Guid repositoryId, string? requestedBranch, decimal maxCostUsd, int profileVersion, string modelId, string promptSetVersion, DateTimeOffset queuedAt);
public DateTimeOffset? QueuedAt { get; private set; }     // null for runs built with the old constructor
public string? RequestedBranch { get; private set; }
public decimal MaxCostUsd { get; private set; }
public int Attempts { get; private set; }
public DateTimeOffset? LeasedUntil { get; private set; }
public bool CancelRequested { get; private set; }
public string[] FailureReasons { get; private set; } = [];
public void Pin(string branch, string commitSha);          // only while Running; sets Branch/CommitSha
public void Fail(IReadOnlyList<string> reasons);           // not after Succeeded/Cancelled
public void RequestCancel();                               // Queued → Cancelled at once; Running → CancelRequested = true; terminal → no-op
public void Cancel();                                      // Running → Cancelled
```
- `Enqueue` leaves `Branch` = requested or `""` and `CommitSha` = `""` until `Pin`.
- Status/lease/attempts are changed by the queue's SQL (Task 5), not by domain methods.

- [ ] **Step 1: Failing tests:** `Enqueue_NewRun_IsQueuedUnpinned`, `RequestCancel_Queued_CancelsAtOnce`, `Fail_Queued_RecordsReasons`, `Pin_NotRunning_Throws`; mapping `ScanRun_JobColumns_RoundTrip`. (A run only becomes Running through the queue, so `RequestCancel_Running_SetsFlag` and `Pin_Running_SetsBranchAndSha` are in Task 5.)
- [ ] **Step 2: Run; expect compile failure.**
- [ ] **Step 3: Implement; `dotnet ef migrations add AddScanJobColumns` (columns `queued_at`, `requested_branch`, `max_cost_usd`, `attempts`, `leased_until`, `cancel_requested`, `failure_reasons text[]`; index on `(status, queued_at)`).**
- [ ] **Step 4: Run; expect PASS; `dotnet test` green.**
- [ ] **Step 5: Commit** `feat(domain): add scan run job lifecycle`

### Task 2: GitHub file-at-commit read and all method names

**Files:**
- Modify: `src/BlindSpotScanner.Application/GitHub/IGitHubClient.cs` (+ `Task<string?> GetFileTextAsync(string owner, string name, string path, string commitSha, CancellationToken ct)`; null when the file doesn't exist at that commit)
- Modify: `src/BlindSpotScanner.Infrastructure/GitHub/GitHubClient.cs` (contents API `repos/{o}/{n}/contents/{escaped path segments}?ref={sha}` with `Accept: application/vnd.github.raw+json`; 404 → null; other errors as `GetAsync`)
- Modify: `ITestSyntaxReader`'s `TestFileFacts` (+ `IReadOnlyList<string> Methods`: every method as `Class.Method`), `CSharpTestSyntaxReader`
- Modify: fakes `FakeGitHubClient` (Application.Tests), `FixtureRepository` (Infrastructure.Tests)
- Test: `GitHubClientTests`, `CSharpTestSyntaxReaderTests`

- [ ] **Step 1: Failing tests:** `GetFileText_Exists_ReturnsRawText`, `GetFileText_NotFound_ReturnsNull`, `GetFileText_PathWithSpaces_EscapesSegments` (raw URL contains `My%20Tests/A.cs`, slashes kept); `Read_Methods_IncludeNonTestMethods`.
- [ ] **Step 2: Run; expect compile failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(infra): read a file at a commit and list all methods`

### Task 3: CitationVerifier

**Files:**
- Create: `src/BlindSpotScanner.Application/Reports/CitationVerifier.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Reports/CitationVerifierTests.cs` (FixtureRepository + real reader)

**Interfaces:**
```csharp
public sealed record CitationCheck(int Checked, int Resolved, IReadOnlyList<string> Failures);
public sealed class CitationVerifier(IGitHubClient github, ITestSyntaxReader reader)
{ public Task<CitationCheck> VerifyAsync(IntegrationReport report, CancellationToken ct); }
```
- Citations: every `path::Name` in endpoint evidence (covered/weak/skipped), scorecard evidence,
  findings evidence and top-risk evidence, distinct. Each file fetched once at `Meta.CommitSha`.
- Endpoint evidence must name a **test** method (`TestMethods`, by method name or `Class.Method`);
  all other citations must name **any** method (`Methods`) — fixtures like `ApiFactory.cs::InitializeAsync` are evidence too (ruling).
- Failure strings: `"{citation}: file not found at {sha7}"`, `"{citation}: no test named {Name}"`, `"{citation}: no method named {Name}"`.

- [ ] **Step 1: Failing tests** (reports built from `examples/sample-report.json` with paths matching the bookstore fixture): `Verify_AllCitationsExist_NoFailures` (Checked = Resolved), `Verify_InventedTest_IsListed`, `Verify_MissingFile_IsListed`, `Verify_FixtureMethodInScorecard_IsAccepted`, `Verify_HelperMethodAsCoverageEvidence_IsRejected`, `Verify_SameFileCitedTwice_FetchedOnce`.
- [ ] **Step 2: Run; expect compile failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(app): verify every citation at the pinned commit`

### Task 4: BudgetedLlmClient and pipeline hooks

**Files:**
- Create: `src/BlindSpotScanner.Application/Llm/BudgetedLlmClient.cs`
- Create: `src/BlindSpotScanner.Application/Scanning/IScanObserver.cs`
- Modify: `src/BlindSpotScanner.Application/Scanning/ScanPipeline.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Llm/BudgetedLlmClientTests.cs`, `tests/BlindSpotScanner.Infrastructure.Tests/Llm/ScanPipelineTests.cs`

**Interfaces:**
```csharp
public sealed class BudgetedLlmClient(ILlmClient inner, decimal maxCostUsd) : ILlmClient
{ public decimal SpentUsd { get; } public bool Exhausted { get; } }   // Exhausted once SpentUsd >= max; further calls return LlmResult(null, ["LLM budget of ${max} reached"], None, false) without calling inner
public sealed record PhaseReport(string Phase, LlmUsage Usage, IReadOnlyList<string> Notes, int ReadInFull);
public interface IScanObserver
{
    Task PhaseStartedAsync(string phase, CancellationToken ct);      // throws OperationCanceledException to stop the scan
    Task PhaseCompletedAsync(PhaseReport report, CancellationToken ct);
}
// ScanPipeline.RunAsync gains: IntegrationReport? previous, IScanObserver observer; it also returns the RunContext.
public sealed record ScanOutcome(RunContext Run, IntegrationReport Report, IReadOnlyList<string> Violations, LlmUsage Usage);
```
- Phases (names stored on `ScanStep.Phase`): `P1`, `P2`, `P3`, `P4`, `P4b` (LLM classification), `P5`, `P6`, `P7`.
- `ReportAssembler.Assemble(detail, previous)` now receives `previous` (delta + "Since last scan").

- [ ] **Step 1: Failing tests:** `Complete_UnderBudget_CallsInner`, `Complete_BudgetReached_StopsCalling` (inner usage 0.004 each, max 0.006 → 2 calls, 3rd not made, `Exhausted` true); `Pipeline_Observer_SeesEveryPhaseInOrder`, `Pipeline_PreviousReport_ProducesDelta` (previous = this fixture's own first outcome → `Delta.Comparable` true).
- [ ] **Step 2: Run; expect compile failure.**
- [ ] **Step 3: Implement (update existing pipeline tests and the live test for the new signature).**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(app): cap LLM cost per scan and report pipeline phases`

### Task 5: PostgreSQL scan queue

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/IScanQueue.cs`
- Create: `src/BlindSpotScanner.Infrastructure/Scanning/PostgresScanQueue.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Scanning/PostgresScanQueueTests.cs`

**Interfaces:**
```csharp
public interface IScanQueue
{
    Task EnqueueAsync(ScanRun run, CancellationToken ct);
    /// Takes the oldest Queued run, or a Running run whose lease expired with Attempts < 2; sets Running, Attempts+1, LeasedUntil = now + lease.
    Task<Guid?> TryDequeueAsync(TimeSpan lease, CancellationToken ct);
    Task<bool> HeartbeatAsync(Guid runId, TimeSpan lease, CancellationToken ct);   // false if no longer Running
    /// Running runs whose lease expired with Attempts >= 2 → Failed ["worker lease expired twice; the scan was abandoned"]; returns their ids.
    Task<IReadOnlyList<Guid>> FailAbandonedAsync(CancellationToken ct);
}
```
- Dequeue is one SQL statement: `UPDATE scan_runs SET … WHERE id = (SELECT id FROM scan_runs WHERE … ORDER BY queued_at FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING id`; "now" comes from the injected `TimeProvider` (passed as a parameter) so tests control time.

- [ ] **Step 1: Failing tests:** `Dequeue_Queued_TakesOldestAndLeases`, `Dequeue_Empty_ReturnsNull`, `Dequeue_TwoWorkersConcurrently_GetDistinctJobs` (two contexts, `Task.WhenAll`), `Dequeue_ExpiredLease_RetriedOnceThenFailed` (FakeTimeProvider advanced past the lease twice; second expiry → `FailAbandonedAsync` returns it, status Failed), `Heartbeat_ExtendsLease`, `Heartbeat_CancelledRun_ReturnsFalse`, `RequestCancel_RunningRun_SetsFlag`, `Pin_RunningRun_SetsBranchAndSha` (runs made Running by a dequeue, reloaded in a fresh context).
- [ ] **Step 2: Run; expect compile failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(infra): add a SKIP LOCKED scan queue with leases`

### Task 6: ScanOrchestrator and the EF scan store

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/ScanOrchestrator.cs`, `IScanStore.cs`, `IScanEvents.cs`
- Create: `src/BlindSpotScanner.Infrastructure/Scanning/EfScanStore.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Scanning/ScanOrchestratorTests.cs` (postgres + FixtureRepository + CannedLlmClient + recording events; runs made Running with `PostgresScanQueue` from Task 5)

**Interfaces:**
```csharp
public sealed record ScanJob(ScanRun Run, Repository Repository, RepoProfile Profile, IntegrationReport? Previous);
public interface IScanStore
{
    Task<ScanJob?> LoadAsync(Guid runId, CancellationToken ct);           // profile = latest version; previous = latest Succeeded run of the same repo and branch
    Task SaveStepAsync(ScanStep step, CancellationToken ct);
    Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken ct);
    Task SaveAsync(ScanRun run, CancellationToken ct);                     // persists Pin/RecordReport/Succeed/Fail/Cancel
}
public interface IScanEvents
{
    Task ProgressAsync(Guid runId, string phase, int pct, string message, CancellationToken ct);
    Task CompletedAsync(Guid runId, RunStatus status, CancellationToken ct);
}
public sealed class ScanOrchestrator(IScanStore store, IScanEvents events, Func<ILlmClient> llm, ScanPipelineFactory pipelines, CitationVerifier verifier, TimeProvider time)
{ public Task<RunStatus> RunAsync(Guid runId, CancellationToken ct); }
public sealed class ScanPipelineFactory(/* IGitHubClient, IBlobReader, IEndpointExtractor, ITestSyntaxReader, ReportValidator, TimeProvider, ScanPipelineOptions */)
{ public ScanPipeline Create(ILlmClient llm); }
```
Flow: load (missing → return Failed, nothing saved) → wrap `llm()` in `BudgetedLlmClient(run.MaxCostUsd)` →
pipeline with an observer that (start) checks `IsCancelRequestedAsync` and throws
`OperationCanceledException`, publishes `ProgressAsync(phase, pct = index × 100 / 8, "{phase} started")`;
(complete) saves a `ScanStep` (status Succeeded, times from `TimeProvider`, tokens, cost,
`ReadInFull`, notes joined by "\n") → `Pin` after P1 → if `Exhausted`, add the reason
`"LLM budget of ${max} reached; items not yet processed are unverified"` to the report's
unverified reasons (re-assemble) → verify citations; failures → `Fail(["citation: …", …])` →
validator violations → `Fail(["validation: …"])` → else `RecordReport` with `Validation.CitationsChecked/Resolved`
set, `Succeed`. `OperationCanceledException` (cancel flag or `ct`) → `Cancel`. Any other exception →
`Fail([ex is GitHub*Exception ? ex.Message : "scan failed: {ex.GetType().Name}"])`. Always
`CompletedAsync(final status)`.

- [ ] **Step 1: Failing tests:** `Run_Bookstore_SucceedsAndStoresReport` (status Succeeded, `ReportJson` set, 8 steps saved, `CitationsChecked` > 0), `Run_ForgedCitation_FailsWithCitationListed` (canned grade cites `BooksApiTests.cs::Deletes_book`), `Run_CancelRequestedDuringScan_EndsCancelled` (events fake sets the flag on `P3` progress; no report), `Run_BudgetExhausted_SucceedsWithReason` (max 0.006 with canned 0.004/call), `Run_PreviousSucceededRun_ProducesDelta`, `Run_RepositoryGone_FailsWithMessage` (GitHub 404 from a client that throws `GitHubNotFoundException`), `Run_Completes_PublishesCompletedOnce`.
- [ ] **Step 2: Run; expect compile failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(app): orchestrate scans with steps, citations and outcomes`

### Task 7: DI, scan events over NOTIFY, and the worker host

**Files:**
- Create: `src/BlindSpotScanner.Infrastructure/DependencyInjection.cs` (`AddBlindSpotScannerCore(this IServiceCollection, IConfiguration)`)
- Create: `src/BlindSpotScanner.Infrastructure/Scanning/PostgresScanEvents.cs` (`IScanEvents` via `SELECT pg_notify('scan_events', @json)`; JSON `{ kind: "progress"|"completed", runId, phase, pct, message, status }`), `ScanEventListener.cs` (opens a dedicated `NpgsqlConnection`, `LISTEN scan_events`, raises `event Func<ScanEvent, Task>`)
- Create: `src/BlindSpotScanner.Worker/ScanWorker.cs`, `ScanOptions.cs`; Modify `Worker/Program.cs`, `appsettings.json`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Scanning/ScanEventsTests.cs`, `tests/BlindSpotScanner.Infrastructure.Tests/DependencyInjectionTests.cs`

Rules:
- DI registers: DbContext (`ConnectionStrings:Postgres`), `GitHubOptions`/`LlmOptions`/`ScanOptions` from config, `GitHubClient` as a typed `HttpClient` with `GitHubRateLimitHandler` (closes the M3 minor), `IGitHubClient`, `IBlobReader` = `CachingBlobReader`, extractor, reader, `PromptLibrary` (singleton), `ILlmClient` = `CachingLlmClient(ClaudeLlmClient)`, `ReportSchemaValidator`, `ReportValidator`, `CitationVerifier`, `ScanPipelineFactory`, `IScanStore`, `IScanQueue`, `IScanEvents`, `ScanOrchestrator` (scoped), `TimeProvider.System`.
- `ScanWorker`: `Scan:MaxConcurrentScans` loops; each loop: `FailAbandonedAsync` (publish Completed Failed for each), `TryDequeueAsync`, else wait `PollSeconds`; for a job: new DI scope, heartbeat every `HeartbeatSeconds` (stops when `HeartbeatAsync` is false or the cancel flag is set → cancels the scan's CTS), `orchestrator.RunAsync`. Exceptions are logged by type and run id only.

- [ ] **Step 1: Failing tests:** `Notify_Progress_ReachesListener` (publisher + listener on the test container: one `progress` event with the same fields), `AddCore_ResolvesOrchestratorAndQueue` (in-memory config with a connection string; resolve in a scope).
- [ ] **Step 2: Run; expect compile failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(worker): host the scan worker with leases, heartbeat and events`

### Task 8: API endpoints, SignalR hub and the relay

**Files:**
- Create: `src/BlindSpotScanner.Api/Controllers/ScansController.cs`, `RunsController.cs`; `Hubs/ScanHub.cs` (`Subscribe(Guid runId)` → group `run:{runId}`; `Unsubscribe`); `ScanEventRelay.cs` (BackgroundService: listener → `IHubContext<ScanHub>` group sends)
- Modify: `src/BlindSpotScanner.Api/Program.cs` (controllers, SignalR, `AddBlindSpotScannerCore`, relay, migrate on startup), `docker-compose.yml` (api/worker env: `ConnectionStrings__Postgres`, `GitHub__Token`, `ANTHROPIC_API_KEY` passthrough), `.env.example` (`GITHUB_TOKEN=`)
- Test: covered by Task 9

Endpoints (OpenAPI):
- `POST /api/repositories/{repoId}/scans` body `{ branch?, maxCostUsd? }` → 202 `{ runId }`; 404 unknown repo; 409 `{ error: "The repository has no profile yet." }` without a profile; `maxCostUsd` ≤ 0 → 400; default `Llm:MaxCostUsdPerScan`.
- `GET /api/runs/{runId}` → Succeeded: the report JSON (canonical serializer, `application/json`); otherwise `{ runId, status, failureReasons }`; 404 unknown.
- `GET /api/runs/{runId}/steps` → `[{ phase, status, startedAt, completedAt, inputTokens, outputTokens, costUsd, readInFull, notes }]` ordered by `startedAt`.
- `POST /api/runs/{runId}/cancel` → 202 (also for terminal runs, no-op); 404 unknown.

- [ ] **Step 1: Build only (endpoints are exercised end to end in Task 9).** `dotnet build`
- [ ] **Step 2: Commit** `feat(api): add scan and run endpoints and the progress hub`

### Task 9: End-to-end acceptance tests

**Files:**
- Modify: `tests/BlindSpotScanner.Api.IntegrationTests/*.csproj` (Testcontainers.PostgreSql, WireMock.Net, Microsoft.AspNetCore.SignalR.Client, reference Worker; copy `..\fixtures\bookstore\**`; NoWarn NU1608 as in Infrastructure.Tests)
- Create: `ApiFactory.cs` (Testcontainers DB; config overrides: connection string, `GitHub:ApiBaseUrl` and `Llm:BaseUrl` → WireMock, `Scan:PollSeconds` = 0.2 via milliseconds option; adds `ScanWorker` as a hosted service), `FakeGitHubServer.cs` (WireMock serving repo, branch, recursive tree, blobs and raw contents from `tests/fixtures/bookstore`), `FakeClaudeServer.cs` (WireMock `/v1/messages`, answer chosen by body substring: `classify ambiguous` / controller name / `grade integration-test quality`; answers as the M5 canned client, with a switch for a forged citation)
- Create: `ScanFlowTests.cs`
- Modify: `docs/DECISIONS.md` (D21)

- [ ] **Step 1: Failing tests:**
  - `PostScan_WorkerRuns_GetReturnsReport` (seed repository + profile in the DB; POST → 202; poll `GET /api/runs/{id}` ≤ 60 s until `meta` appears; report validates with `ReportSchemaValidator`; `summary.endpoints.total` = 6)
  - `PostScan_ForgedCitation_RunFailed` (status `Failed`, `failureReasons` contains `citation: tests/Bookstore.IntegrationTests/BooksApiTests.cs::Deletes_book: no method named Deletes_book`)
  - `GetSteps_AfterScan_ListsEveryPhase` (8 phases P1…P7 incl. P4b)
  - `Hub_Subscriber_ReceivesProgressAndCompleted` (HubConnection over `Server.CreateHandler()`; ≥1 `ScanProgress`, one `ScanCompleted` with `Succeeded`)
  - `CancelQueuedRun_IsCancelled` (worker disabled for this test via a second factory option; POST scan, POST cancel → `Cancelled`)
  - `PostScan_UnknownRepository_Returns404`, `PostScan_NoProfile_Returns409`
- [ ] **Step 2: Run; expect FAIL (or compile failure).**
- [ ] **Step 3: Fix what the end-to-end run reveals; ledger each fix.**
- [ ] **Step 4: `dotnet test`, `dotnet format --verify-no-changes`, `docker compose build`; expect green.**
- [ ] **Step 5: Add D21:** repository/profile endpoints are M7 (user's choice); cancel is `POST` (OpenAPI over doc 03); the queue lives on `scan_runs` (lease, attempts, cancel flag; retried once); progress via `pg_notify('scan_events')` relayed to `/hubs/scans` groups `run:{id}`; citation rules (test methods for coverage evidence, any method elsewhere); budget behavior; API applies migrations at startup; new `Scan:*` options.
- [ ] **Step 6: Commit** `test: add end-to-end scan flow acceptance tests`
