# M1 Domain, Persistence, Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The full doc 03 domain model persisted in PostgreSQL via EF Core, with the report contract as typed C# records, canonical JSON, schema validation, and a DB trigger that makes a succeeded run's report immutable. `examples/sample-report.json` round-trips through the DB byte-equivalently (canonical form) and passes schema validation.

**Architecture:** Domain holds the entities, enums, the report record graph, and `ReportJsonSerializer`, which is the single owner of the canonical JSON options (BCL `System.Text.Json` only). Infrastructure holds the EF Core `BlindSpotScannerDbContext` (Npgsql, snake_case), migrations (including the trigger), and `ReportSchemaValidator` (JsonSchema.Net plus the embedded schema). A new `BlindSpotScanner.Infrastructure.Tests` project holds the validator unit tests and the Testcontainers persistence tests.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql 10, EFCore.NamingConventions, JsonSchema.Net, Testcontainers.PostgreSql, xUnit 2.9.3, FluentAssertions 7.2.2.

**Spec:** `BlindSpotScanner-Blueprint/docs/11-implementation-plan.md` (M1), `03-backend-dotnet.md` (domain model, persistence), `07-report-model.md`, `08-run-comparison.md` (delta shape), `contracts/integration-report.schema.json`, `examples/*.json`, root `CLAUDE.md`, `docs/DECISIONS.md`.

## Global Constraints

- The user chose these (record them as decisions in the task that needs each one):
  - **D9:** build the full doc 03 model in M1.
  - **D10:** the round-trip is measured in canonical bytes.
  - **D11:** validate with JsonSchema.Net.
- Domain references no projects and no NuGet packages. BCL `System.Text.Json` attributes are allowed.
- JSON is camelCase. Enum wire values are exactly the schema's (`covered`, `real-seam`, `Med`, `UncoveredEndpoint`, `GET`, …).
- Use these canonical options everywhere, and only through `ReportJsonSerializer.Options`:
  - `PropertyNamingPolicy = CamelCase`
  - `DefaultIgnoreCondition = WhenWritingNull`
  - `UnmappedMemberHandling = Disallow`
  - `RespectRequiredConstructorParameters`/`required` members enforced
  - `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (keeps `→` and `—` literal; this is DB storage, not HTML)
  - `WriteIndented = false`
- Fields that are required but nullable must serialize as `null`, never be omitted. Mark them `[JsonIgnore(Condition = JsonIgnoreCondition.Never)]`: `scorecard[].score`, `summary.qualityScore`, `summary.previousRunId`.
- Numbers in the report are `decimal` (this keeps `0.20` as `0.20`). Counts are `int`.
- Report timestamps (`meta.generatedAt`, `delta.baseGeneratedAt`) are `DateTime` (UTC kind), not `DateTimeOffset`. STJ writes a UTC `DateTime` as `…Z`, but it writes a `DateTimeOffset` as `…+00:00`, which breaks the round-trip. Entity columns stay `DateTimeOffset` (`timestamptz`). `RecordReport` converts with `new DateTimeOffset(meta.GeneratedAt, TimeSpan.Zero)`. Ids that the schema marks as `uuid` are `Guid` (written lowercase, as in the samples).
- Column names are snake_case. Tables: `repositories`, `repo_profiles`, `scan_runs`, `scan_steps`, `findings`, `remediation_actions`, `file_cache`, `llm_cache`. Enums are stored as their C# names (strings).
- `scan_runs.report_json` is `jsonb`. The headline columns are `score`, `weight_assessable_pct`, `endpoints_total`, `endpoints_covered`, `endpoints_weak`, `endpoints_none`, `endpoints_unverified`.
- Before using any API whose shape matters, look it up with context7: JsonSchema.Net `Evaluate`/`EvaluationOptions` (its API changed across majors), Npgsql EF `text[]` mapping, Testcontainers.PostgreSql builder, `JsonStringEnumMemberName`. Check that packages exist with `dotnet package search … --exact-match` before adding them.
- Use Serena for code reading and edits where it works. If it still has no C# language server, say so and use the plain tools.
- TDD, `Method_Scenario_ExpectedResult` names, Arrange/Act/Assert comments, one behavior per test (Theory with InlineData is fine). Conventional Commits, one commit per task, on `feature/m1-domain-persistence`. Ask before pushing.

## Review Focus

1. **Bypassing immutability in two steps.** First `UPDATE status` away from `Succeeded`, then edit `report_json`. The trigger must also block any status change out of `Succeeded`. Tested in Task 4.
2. **A report with an unexpected extra field** (LLM drift or a hand edit) must fail to deserialize loudly, not be dropped silently. Tested in Task 1.
3. **A required-but-null field dropped on write** (`"score": null` turning into an absent key) would fail the schema. The round-trip `DeepEquals` test and an explicit `"score":null` assertion cover this in Task 1.
4. **Formats the schema declares but validators skip by default** (`uuid`, `date-time`, `uri`). A bad `runId` must be rejected. Turn on format assertion. Tested in Task 2.
5. **A run that records another run's report** (a `meta.runId` mismatch) must throw, so a run's evidence never belongs to a different run. Tested in Task 3.

---

### Task 1: Report contract records and canonical JSON (Domain)

**Files:**
- Create: `src/BlindSpotScanner.Domain/Enums/` with `CoverageStatus.cs`, `RunStatus.cs`, `FindingKind.cs`, `ActionType.cs`, `Severity.cs`, `Confidence.cs`, `IntegrationMappedTo.cs`, `Verdict.cs`, `EndpointMethod.cs`, `DeltaDirection.cs`
- Create: `src/BlindSpotScanner.Domain/Reports/IntegrationReport.cs` (all the report records, one file per top-level section if it grows past ~150 lines), and `src/BlindSpotScanner.Domain/Reports/ReportJsonSerializer.cs`
- Modify: `tests/BlindSpotScanner.Domain.Tests/BlindSpotScanner.Domain.Tests.csproj`. Link `BlindSpotScanner-Blueprint/examples/*.json` as `Fixtures/%(Filename)%(Extension)` with `CopyToOutputDirectory=PreserveNewest`.
- Test: `tests/BlindSpotScanner.Domain.Tests/Reports/ReportJsonSerializerTests.cs`

**Interfaces:**
- Produces:
  - `public static class ReportJsonSerializer { JsonSerializerOptions Options; string Serialize(IntegrationReport r); IntegrationReport Deserialize(string json); }`
  - `Deserialize` throws `JsonException` on unknown or missing-required members.
  - Records (all `sealed record`, with `required init` for schema-required fields):
    - `IntegrationReport(Meta, Inventory, EndpointCoverage, Scorecard, Findings, TopRisks, Summary, Delta?, Validation)`
    - `ReportMeta`, `ReportRepo`, `ReportInventory`, `ControllerCoverage`, `EndpointResult`, `ScoreDimension`, `ReportFinding`, `TopRisk`, `ReportSummary`, `StatusCounts`, `SummaryBullet`
    - `RunDelta` (with `DeltaMetric`, `DeltaEndpoints` incl. optional `ConfidenceChanged`, `DeltaScorecardItem`, `DeltaFindings`, `DeltaRisks`), `ReportValidation`
  - Collections are `IReadOnlyList<T>`. `inventory.classification` is `IReadOnlyDictionary<string,int>?`.
  - Enums, with wire values from the schema, set through `[JsonStringEnumMemberName]` where the C# name differs:
    - `CoverageStatus {Covered,Weak,Skipped,None,Unverified}`
    - `RunStatus {Queued,Running,Succeeded,Failed,Cancelled}`
    - `FindingKind {UncoveredEndpoint,WeakTest,FalsePositive,SkippedTest,UncoveredSeam,Risk}`
    - `ActionType {JiraTicket,PullRequest}`
    - `Severity {Critical,High,Medium,Low}`
    - `Confidence {High,Med,Low}`
    - `IntegrationMappedTo {RealSeam,E2eMockedData,E2eRealBackend,Contract,Other}`
    - `Verdict {Strong,Moderate,Partial,Weak}`
    - `EndpointMethod {Get,Post,Put,Patch,Delete,Head,Options}` → `GET`…
    - `DeltaDirection {Improved,Regressed,Unchanged}`
  - `summary.integrationMappedTo` is a `string` in the schema. Keep it a `string` (don't tighten the contract).

- [ ] **Step 1: Write the failing tests**
  - `Serialize_RoundTripOfExample_IsSemanticallyEqualToFile`: a `[Theory]` over `sample-report.json` and `sample-report-previous.json`. Deserialize the file, serialize it, and assert `JsonNode.DeepEquals(JsonNode.Parse(file), JsonNode.Parse(canonical)).Should().BeTrue()`.
  - `Serialize_UnscorableDimension_WritesExplicitNullScore`: the canonical form of `sample-report.json` contains `"score":null`.
  - `Serialize_NonAsciiText_IsWrittenLiterally`: the canonical form contains `→` (from the "Since last scan" bullet).
  - `Deserialize_UnknownProperty_Throws`: the sample with `"extra":1` added to `meta` → `JsonException`.
  - `Deserialize_MissingRequiredProperty_Throws`: the sample with `meta.commitSha` removed → `JsonException`.
- [ ] **Step 2: Run `dotnet test tests/BlindSpotScanner.Domain.Tests`.** Expected: a compile failure (the types don't exist). That counts as RED.
- [ ] **Step 3: Implement** the enums, records and `ReportJsonSerializer` per Interfaces and Global Constraints.
- [ ] **Step 4: Run it.** Expected: all pass. Run the full `dotnet test`: green, 0 warnings, and `dotnet format --verify-no-changes` exits 0.
- [ ] **Step 5: Docs.** Add D10 to `docs/DECISIONS.md`: "Byte-equivalent round-trip = canonical bytes. jsonb reorders keys, so equivalence is: canonical serialize → store → read → deserialize → serialize gives identical bytes, and the canonical JSON is semantically equal to the source file."
- [ ] **Step 6: Commit** `feat(domain): add report contract records and canonical JSON`

### Task 2: Schema validation (Infrastructure, JsonSchema.Net)

**Files:**
- Create: `src/BlindSpotScanner.Infrastructure/Reports/integration-report.schema.json` (a copy of the contract, `EmbeddedResource`), `src/BlindSpotScanner.Infrastructure/Reports/ReportSchemaValidator.cs`
- Create: `tests/BlindSpotScanner.Infrastructure.Tests/` (xunit, FluentAssertions 7.2.2, references Infrastructure; it links the examples as fixtures like Task 1 and also the blueprint schema file)
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Reports/ReportSchemaValidatorTests.cs`
- Modify: `docs/DECISIONS.md` (D11, D12), `CLAUDE.md` (layout: add `tests/BlindSpotScanner.Infrastructure.Tests/`)

**Interfaces:**
- Consumes: the fixture JSON files only (no Domain types, so validation stays independent of the DTOs).
- Produces: `public sealed class ReportSchemaValidator { public IReadOnlyList<string> Validate(string reportJson); }`. It returns an empty list when the report is valid. Each error is a string `"<instance location>: <message>"`. The schema loads once from the embedded resource. Format assertion is on (`RequireFormatValidation` or the current major's equivalent).

- [ ] **Step 1: Scaffold** the test project, add it to the slnx, and add the JsonSchema.Net package to Infrastructure (exact latest stable version). Check the `Evaluate` API with context7.
- [ ] **Step 2: Write the failing tests.** Each one mutates `sample-report.json` with `JsonNode` and passes the string in:
  - `Validate_ExampleReports_ReturnsNoErrors` (Theory over both samples)
  - `Validate_WrongSchemaVersion_ReturnsError` (`meta.schemaVersion = "2.0"`, which exercises `const`)
  - `Validate_NonHexCommitSha_ReturnsError` (`"xyz"`, pattern)
  - `Validate_RunIdNotUuid_ReturnsError` (`"not-a-uuid"`, format; Review Focus 4)
  - `Validate_MissingSummary_ReturnsError` (required)
  - `Validate_ValidationMethodNotScript_ReturnsError` (`validation.method = "llm"`, doc 07 check 10)
  - `EmbeddedSchema_MatchesBlueprintContract`: the embedded resource text equals the blueprint file text after normalizing line endings
- [ ] **Step 3: Run `dotnet test tests/BlindSpotScanner.Infrastructure.Tests`.** Expected: a compile failure, which counts as RED.
- [ ] **Step 4: Implement** `ReportSchemaValidator` and embed the schema.
- [ ] **Step 5: Run the full `dotnet test`.** Expected: all green, 0 warnings, format clean.
- [ ] **Step 6: Docs.**
  - D11: "JsonSchema.Net validates reports. The schema is draft 2020-12 (`$defs`, `const`, type arrays), which NJsonSchema doesn't fully support. Format assertion is on."
  - D12: "Add `tests/BlindSpotScanner.Infrastructure.Tests` for Infrastructure unit tests and the Testcontainers persistence tests; Api.IntegrationTests stays for HTTP-level tests."
  - Add the project to the CLAUDE.md layout.
- [ ] **Step 7: Commit** `feat(infra): validate reports against the JSON schema`

### Task 3: Domain entities and ScanRun rules

**Files:**
- Create in `src/BlindSpotScanner.Domain/Entities/`: `Repository.cs`, `RepoProfile.cs`, `ScanRun.cs`, `ScanStep.cs`, `Finding.cs`, `RemediationAction.cs`, `FileCacheEntry.cs`, `LlmCacheEntry.cs`
- Test: `tests/BlindSpotScanner.Domain.Tests/Entities/ScanRunTests.cs`
- Modify: `docs/DECISIONS.md` (D9)

**Interfaces:**
- Consumes: `IntegrationReport`, `ReportJsonSerializer` and `RunStatus` from Task 1.
- Produces (plain classes with EF-friendly constructors; guarded state has `private set`):
  - `Repository { Guid Id; string Owner; string Name; string DefaultBranch; string HtmlUrl; }`
  - `RepoProfile { Guid RepositoryId; int Version; string Markdown; IntegrationMappedTo IntegrationMappedTo; string[] TestGlobs; string[] ControllerGlobs; }`. The key is (RepositoryId, Version), which keeps the version history from doc 04.
  - `ScanRun { Guid Id; Guid RepositoryId; string Branch; string CommitSha; DateTimeOffset? GeneratedAt; RunStatus Status; string ModelId; string PromptSetVersion; int ProfileVersion; decimal CostUsd; Guid? PreviousRunId; string? ReportJson; decimal? Score; decimal? WeightAssessablePct; int? EndpointsTotal, EndpointsCovered, EndpointsWeak, EndpointsNone, EndpointsUnverified; }`. Methods:
    - `void RecordReport(IntegrationReport report)` sets `ReportJson = ReportJsonSerializer.Serialize(report)`, `GeneratedAt` and the headline columns from `report.Summary`. It throws `InvalidOperationException` if `Status == Succeeded` or if `report.Meta.RunId != Id`.
    - `void Succeed()` throws `InvalidOperationException` if `ReportJson is null`, and otherwise sets `Succeeded`.
  - `ScanStep { Guid Id; Guid ScanRunId; string Phase; RunStatus Status; DateTimeOffset StartedAt; DateTimeOffset? CompletedAt; int InputTokens; int OutputTokens; decimal CostUsd; int ReadInFull; int Sampled; string? Notes; }`. Fields come from doc 05 ("status, duration, tokens, readInFull/sampled counts, notes").
  - `Finding { Guid Id; Guid ScanRunId; string ReportFindingId; FindingKind Kind; Severity Severity; string StableKey; string Headline; string? WhyItMatters; string[] Detail; string[] Evidence; }`
  - `RemediationAction { Guid Id; Guid RepositoryId; string StableKey; Guid CreatedFromFindingId; ActionType Type; string ExternalKey; string Url; string Status; DateTimeOffset CreatedAt; string CreatedBy; }`. It is keyed for lookup by (RepositoryId, StableKey), because doc 03 says actions outlive runs and show on the same StableKey in later runs.
  - `FileCacheEntry { string BlobSha; string Content; }` and `LlmCacheEntry { string Key; string ResponseJson; }`

- [ ] **Step 1: Write the failing tests** (build a running `ScanRun` whose `Id` equals the sample's `meta.runId` `22222222-2222-4222-8222-222222222222`):
  - `RecordReport_WhenRunning_SetsHeadlineColumns`: `Score` 5.1, `WeightAssessablePct` 90, endpoints 5/3/1/1/0
  - `RecordReport_WhenRunning_StoresCanonicalJson`: `ReportJson` equals `ReportJsonSerializer.Serialize(report)`
  - `RecordReport_WhenAlreadySucceeded_Throws`
  - `RecordReport_ReportOfAnotherRun_Throws` (Review Focus 5)
  - `Succeed_WithoutReport_Throws`
- [ ] **Step 2: Run them.** Expected: a compile failure, which counts as RED.
- [ ] **Step 3: Implement** the entities per Interfaces. Don't add other behavior. Start, Fail and Cancel come with M6.
- [ ] **Step 4: Run the full `dotnet test`.** Expected: green, 0 warnings, format clean.
- [ ] **Step 5: Docs.** D9: "M1 builds the full doc 03 domain model and tables (user's choice). The job-queue lease columns (M6) and connection settings (M9) aren't in doc 03 and arrive with their milestones."
- [ ] **Step 6: Commit** `feat(domain): add entities and scan run report rules`

### Task 4: EF Core persistence, the immutability trigger, and the M1 AC

**Files:**
- Create: `src/BlindSpotScanner.Infrastructure/Persistence/BlindSpotScannerDbContext.cs`, `Persistence/Configurations/*.cs` (one `IEntityTypeConfiguration` per entity), `Persistence/DesignTimeDbContextFactory.cs`, `Persistence/Migrations/*` (`InitialCreate`)
- Create: `.config/dotnet-tools.json` (the `dotnet-ef` local tool, version matching EF Core)
- Create: `tests/BlindSpotScanner.Infrastructure.Tests/Persistence/PostgresFixture.cs` (`IAsyncLifetime`, a `postgres:16` Testcontainer, runs `Database.MigrateAsync()` once, shared through `[CollectionDefinition("postgres")]`)
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Persistence/ReportRoundTripTests.cs`, `ScanRunImmutabilityTests.cs`, `EntityMappingTests.cs`
- Modify: `CLAUDE.md` Commands (add migrations and the "persistence tests need Docker" note)

**Interfaces:**
- Consumes: the entities from Task 3, `ReportJsonSerializer` from Task 1, and `ReportSchemaValidator` from Task 2.
- Produces:
  - `BlindSpotScannerDbContext(DbContextOptions<BlindSpotScannerDbContext>)` with a `DbSet` per entity, configured with `UseNpgsql(...).UseSnakeCaseNamingConvention()`.
  - Mapping:
    - `report_json` → `jsonb`
    - `string[]` → `text[]`
    - enums → `HasConversion<string>()`
    - FKs: profiles/runs/actions → repositories, steps/findings → scan_runs, actions.created_from_finding_id → findings
    - index on `remediation_actions (repository_id, stable_key)`
  - The migration's `Up` adds, through `migrationBuilder.Sql`, the function `scan_runs_report_immutable()` and a `BEFORE UPDATE` trigger on `scan_runs`. When `OLD.status = 'Succeeded'` and (`NEW.report_json IS DISTINCT FROM OLD.report_json` or `NEW.status IS DISTINCT FROM OLD.status`) it raises `'scan run % is succeeded; its report is immutable'`. `Down` drops both.
  - Don't register the DbContext in Api/Worker DI yet (no consumer until M6/M7).

- [ ] **Step 1: Write the failing tests** (they use the `postgres` collection fixture):
  - `RoundTrip_ExampleReport_IsByteEquivalentInCanonicalForm` (Theory over both samples). Deserialize the file, then create a Repository and a running ScanRun with the matching Id. Call `RecordReport`, `Succeed()`, and save. Load the run in a **new** context. Assert `ReportJsonSerializer.Serialize(ReportJsonSerializer.Deserialize(loaded.ReportJson!))` equals the canonical string from before saving, byte for byte (`Should().Be`).
  - `RoundTrip_ExampleReport_PassesSchemaValidation`: the reloaded canonical JSON gives `ReportSchemaValidator.Validate(...)` with no errors.
  - `Update_ReportOfSucceededRun_IsRejectedByDatabase`: raw `UPDATE scan_runs SET report_json = '{}'::jsonb WHERE id = …` → `PostgresException` whose message contains `immutable`.
  - `Update_StatusOfSucceededRun_IsRejectedByDatabase` (Review Focus 1)
  - `Update_ReportOfRunningRun_IsAllowed`
  - `Save_RepositoryWithProfile_RoundTripsGlobArrays`
  - `Save_RunWithStepAndFinding_RoundTripsChildren`
  - `Save_RemediationAction_IsFoundByRepositoryAndStableKey`
  - `Save_CacheEntries_RoundTrip`
- [ ] **Step 2: Run `dotnet test tests/BlindSpotScanner.Infrastructure.Tests`.** Expected: a compile failure (no DbContext), which counts as RED.
- [ ] **Step 3: Implement** the DbContext, configurations and design-time factory, then generate the migration: `dotnet ef migrations add InitialCreate --project src/BlindSpotScanner.Infrastructure --output-dir Persistence/Migrations`. Add the trigger SQL to `Up`/`Down` by hand.
- [ ] **Step 4: Run the full `dotnet test`** (Docker running). Expected: all green, 0 warnings, format clean (generated migrations may need `dotnet format` once, then verify).
- [ ] **Step 5: Check that the M0 stack still works.** `docker compose up -d --build`, then `curl localhost:5080/health` returns `200`, then `docker compose down`.
- [ ] **Step 6: Docs.** In CLAUDE.md Commands, add a "Migrations" row (`dotnet ef migrations add <Name> --project src/BlindSpotScanner.Infrastructure --output-dir Persistence/Migrations`). Change the Test row's note to "Infrastructure and Api integration tests need Docker running".
- [ ] **Step 7: Commit** `feat(infra): add EF Core persistence with report immutability trigger`
