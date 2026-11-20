# Production Seams from Code Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect real dependencies ("seams") from the repository's startup files, record them in the report, send them to P6, and accept an `UncoveredSeam` finding only for a detected seam.

**Architecture:** P2 lists startup files; P3 (`ExtractPhase`) reads them and asks the existing C# production-code extractor for seams (Roslyn, so comments never match). The seams travel on `EndpointInventory` into P6's input and into `report.inventory.productionSeams`. `ReportAssembler` derives `summary.uncoveredRealSeams` (null when seams weren't detected). P6 drops seam findings whose subject isn't a detected seam name.

**Tech Stack:** .NET 10, C#, Roslyn (`Microsoft.CodeAnalysis.CSharp`, already referenced by Infrastructure), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-11-19-production-seams-design.md` (option B). This plan refines it in three places, recorded in D34 (Task 4): the detector is a method on `IEndpointExtractor` (no new port), seam names are kebab-case keys (so `uncovered-seam:payment-gateway` stays stable), and the report gains `inventory.productionSeams`.

## Global Constraints

- Startup file patterns (fixed): `**/Program.cs`, `**/Startup.cs`, `**/*ServiceCollectionExtensions.cs`; after the fixed excludes; never a file matched by the profile's `TestGlobs`.
- Not detected (no startup file) → unverified reason exactly: `seams not detected: no Program.cs, Startup.cs or *ServiceCollectionExtensions.cs found`, `inventory.productionSeams` omitted, `summary.uncoveredRealSeams` omitted, every LLM `UncoveredSeam` finding dropped.
- Prompt `grade-quality.md` → `v1.1.0`; `PromptSetVersion` → `1.1.0`.
- TDD, `Method_Scenario_ExpectedResult`, `// Arrange // Act // Assert`, one behavior per test, no branching in tests (CLAUDE.md).
- Branch `feature/production-seams`; Conventional Commits; commit only after build + tests for the changed side pass.

## Review Focus

1. A `Program.cs` inside a test project (`tests/**/Program.cs`) must not be read as production startup code → Task 2 test `Enumerate_StartupFileUnderTestGlob_IsExcluded`.
2. A registration inside a comment or string must not create a seam → Task 1 test `DetectSeams_CommentedOutRegistration_ReturnsNone`.
3. Two services registering the same client → one seam → Task 1 test `DetectSeams_SameClientTwice_ReturnsOneSeam`; across files → Task 2 test `Extract_SameSeamInTwoFiles_IsListedOnce`.
4. The model reports a seam under a near-miss subject (`PaymentGatewayClient` instead of `payment-gateway`) → dropped with a note → Task 3 test `Grade_UncoveredSeamWithUnknownSubject_IsDroppedWithNote`.
5. A repo with startup files but no registrations → seams detected as empty (measured 0), not unverified → Task 2 test `Extract_StartupFileWithoutRegistrations_SeamsEmptyNotNull`.

---

### Task 1: Seam detection in the C# extractor

**Files:**
- Create: `src/BlindSpotScanner.Domain/Enums/SeamKind.cs`
- Create: `src/BlindSpotScanner.Application/Scanning/ProductionSeam.cs`
- Modify: `src/BlindSpotScanner.Application/Scanning/IEndpointExtractor.cs` (add method; update summary to "reads one production source file")
- Create: `src/BlindSpotScanner.Infrastructure/Extraction/CSharpSeamDetector.cs` (internal static)
- Modify: `src/BlindSpotScanner.Infrastructure/Extraction/AspNetCoreEndpointExtractor.cs` (delegate `DetectSeams` to `CSharpSeamDetector.Detect`)
- Modify: `tests/BlindSpotScanner.Application.Tests/Scanning/FakeEndpointExtractor.cs` (add optional `seamsByPath` dictionary, default empty)
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/SeamDetectionTests.cs`

**Interfaces:**
- Produces:
  - `public enum SeamKind { Database, OutboundHttp, MessageBroker, BlobStorage, Cache }` in new file `src/BlindSpotScanner.Domain/Enums/SeamKind.cs` (Domain, because the report record in Task 3 uses it; serialized as strings like the other report enums)
  - `public sealed record ProductionSeam(SeamKind Kind, string Name, string SourcePath);`
  - `IReadOnlyList<ProductionSeam> IEndpointExtractor.DetectSeams(string path, string source);` distinct by `(Kind, Name)`, ordered by `Name` ordinal.

Detection table (invocation / object-creation names via Roslyn syntax; `Name` is the key):

| Kind | Syntax | Name |
|---|---|---|
| Database | `AddDbContext<T>`, `AddDbContextPool<T>`, `AddDbContextFactory<T>` | kebab(T minus trailing `Context`): `BookstoreDbContext` → `bookstore-db` |
| OutboundHttp | `AddHttpClient<T>` / `AddHttpClient<TI, TImpl>` (last type arg) | kebab(T minus trailing `Client`): `PaymentGatewayClient` → `payment-gateway` |
| OutboundHttp | `AddHttpClient("name")` string literal | kebab(name) |
| MessageBroker | `AddMassTransit` / `new ConnectionFactory` / `new ProducerBuilder<…>` or `new ConsumerBuilder<…>` / `new ServiceBusClient` | `masstransit` / `rabbitmq` / `kafka` / `azure-service-bus` |
| BlobStorage | `new BlobServiceClient` / `new AmazonS3Client` or `AddAWSService<IAmazonS3>` | `azure-blob-storage` / `s3` |
| Cache | `AddStackExchangeRedisCache` / `ConnectionMultiplexer.Connect` | `redis` |

kebab: split PascalCase on upper-case boundaries (keep acronym runs together: `S3Client` → `s3`), lower-case, join with `-`.

- [ ] **Step 1: Write the failing tests** in `SeamDetectionTests.cs` (helper `Detect(string source) => new AspNetCoreEndpointExtractor().DetectSeams("src/Api/Program.cs", source)`):
  - `DetectSeams_AddDbContext_ReturnsDatabaseSeam`: `builder.Services.AddDbContext<BookstoreDbContext>(o => o.UseNpgsql(cs));` → `[new ProductionSeam(SeamKind.Database, "bookstore-db", "src/Api/Program.cs")]`
  - `DetectSeams_TypedHttpClient_ReturnsOutboundHttpSeam`: `AddHttpClient<PaymentGatewayClient>()` → `(OutboundHttp, "payment-gateway")`
  - `DetectSeams_InterfaceAndImplementationClient_UsesImplementation`: `AddHttpClient<IPayments, StripePaymentsClient>()` → `"stripe-payments"`
  - `DetectSeams_NamedHttpClient_UsesKebabName`: `AddHttpClient("ShippingApi")` → `"shipping-api"`
  - `DetectSeams_MassTransit_ReturnsBrokerSeam` → `(MessageBroker, "masstransit")`
  - `DetectSeams_BlobServiceClient_ReturnsBlobSeam` → `(BlobStorage, "azure-blob-storage")`
  - `DetectSeams_RedisCache_ReturnsCacheSeam` → `(Cache, "redis")`
  - `DetectSeams_CommentedOutRegistration_ReturnsNone`: `// services.AddHttpClient<PaymentGatewayClient>();` → empty
  - `DetectSeams_SameClientTwice_ReturnsOneSeam` → one `payment-gateway`
  - `DetectSeams_NonCSharpPath_ReturnsNone`: path `src/server.ts` → empty (mirror the extractor's existing non-C# behavior)

- [ ] **Step 2: Run them to see them fail**
  Run: `dotnet test tests/BlindSpotScanner.Infrastructure.Tests --filter FullyQualifiedName~SeamDetectionTests`
  Expected: build error, `DetectSeams` not defined.

- [ ] **Step 3: Add `ProductionSeam`/`SeamKind`, the interface method, `CSharpSeamDetector.Detect(string path, string source)`, the delegating method, and the fake's `DetectSeams`** (fake returns `seamsByPath.GetValueOrDefault(path, [])`). Parse with `CSharpSyntaxTree.ParseText`; walk `InvocationExpressionSyntax` (name via `GenericNameSyntax`/`IdentifierNameSyntax`, including member-access `x.AddHttpClient<T>()`) and `ObjectCreationExpressionSyntax`.

- [ ] **Step 4: Run the tests to see them pass**
  Run: `dotnet test tests/BlindSpotScanner.Infrastructure.Tests --filter FullyQualifiedName~SeamDetectionTests` → all pass; then `dotnet build` (warnings are errors).

- [ ] **Step 5: Commit** `feat(scan): detect production seams in startup code`

---

### Task 2: Enumerate startup files and collect seams in P3

**Files:**
- Modify: `src/BlindSpotScanner.Application/Scanning/RepositoryInventory.cs` (add `IReadOnlyList<RepoFile> StartupFiles` as a new positional parameter after `TestFiles`; update every `new RepositoryInventory(` call site, passing `[]` where a test doesn't care)
- Modify: `src/BlindSpotScanner.Application/Scanning/EnumeratePhase.cs`
- Modify: `src/BlindSpotScanner.Application/Scanning/EndpointInventory.cs` (add `IReadOnlyList<ProductionSeam>? Seams`; null = not detected)
- Modify: `src/BlindSpotScanner.Application/Scanning/ExtractPhase.cs`
- Create: `tests/fixtures/bookstore/src/Bookstore.Api/Program.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Scanning/EnumeratePhaseTests.cs`, `ExtractPhaseTests.cs`, `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/BookstoreFixtureTests.cs`

**Interfaces:**
- Consumes: `IEndpointExtractor.DetectSeams`, `ProductionSeam` (Task 1).
- Produces: `RepositoryInventory.StartupFiles`; `EndpointInventory.Seams` (`null` when no startup file; otherwise distinct by `(Kind, Name)` across files, ordered by `Name`); the unverified reason string from Global Constraints appended to `EndpointInventory.UnverifiedReasons` when null.

- [ ] **Step 1: Write the failing tests**
  - `Enumerate_StartupFiles_AreListed`: the existing `Bookstore` array (has `src/Api/Program.cs`) plus `src/Api/Startup.cs`, `src/Api/Infrastructure/PaymentsServiceCollectionExtensions.cs` → `StartupFiles` paths equal those three, ordinal order.
  - `Enumerate_StartupFileUnderTestGlob_IsExcluded`: add `tests/Api.IntegrationTests/Program.cs` → not in `StartupFiles`.
  - `Extract_StartupFiles_SeamsCollected`: fake seams for `src/Api/Program.cs` = `[db]` → `endpoints.Seams` equals `[db]`.
  - `Extract_SameSeamInTwoFiles_IsListedOnce`: two startup files both returning `payment-gateway` → one seam.
  - `Extract_NoStartupFile_SeamsNullAndUnverifiedReason`: → `Seams` null and `UnverifiedReasons` contains the exact Global Constraints string.
  - `Extract_StartupFileWithoutRegistrations_SeamsEmptyNotNull`: → `Seams` is empty, not null; no seam reason.
  - `Bookstore_StartupCode_DetectsDatabaseAndPaymentSeams` (Infrastructure, real extractor over the fixture): seam names equal `"bookstore-db", "payment-gateway"`.

- [ ] **Step 2: Run them to see them fail**
  Run: `dotnet test tests/BlindSpotScanner.Application.Tests --filter "FullyQualifiedName~EnumeratePhaseTests|FullyQualifiedName~ExtractPhaseTests"` → fail (members missing).

- [ ] **Step 3: Implement.** `EnumeratePhase`: match the three patterns over the same candidates, `.Except(testPaths)`. `ExtractPhase`: read each startup file (ordinal order) with the same `blobs.ReadAsync`, call `DetectSeams`, merge. Fixture `Program.cs` (top-level statements, never compiled):
  ```csharp
  var builder = WebApplication.CreateBuilder(args);
  builder.Services.AddControllers();
  builder.Services.AddDbContext<BookstoreDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Bookstore")));
  builder.Services.AddHttpClient<PaymentGatewayClient>();
  var app = builder.Build();
  app.MapControllers();
  app.Run();

  public partial class Program;
  ```

- [ ] **Step 4: Run the tests to see them pass**, then the whole backend suite (`dotnet test`; Docker running). Fix tests that count fixture files or blob reads (`BookstoreFixtureTests`, `ScanPipelineTests`, Api `ScanFlowTests`) only where the new `Program.cs` legitimately changes the expected value.

- [ ] **Step 5: Commit** `feat(scan): read startup files for seams in the extract phase`

---

### Task 3: Seams in P6, the report, and the summary

**Files:**
- Modify: `src/BlindSpotScanner.Domain/Reports/IntegrationReport.cs` (`ReportInventory`: add `IReadOnlyList<ReportSeam>? ProductionSeams`; `public sealed record ReportSeam { required SeamKind Kind; required string Name; required string Source; }`, using Task 1's `SeamKind`)
- Modify: `BlindSpotScanner-Blueprint/contracts/integration-report.schema.json` **and** `src/BlindSpotScanner.Infrastructure/Reports/integration-report.schema.json` (keep identical): `inventory.properties.productionSeams` = array of `{kind: enum[Database,OutboundHttp,MessageBroker,BlobStorage,Cache], name: string, source: string}`, all required; not in `inventory.required`.
- Modify: `BlindSpotScanner-Blueprint/examples/sample-report.json` and `sample-report-previous.json`: add `inventory.productionSeams` = `[{"kind":"Database","name":"bookstore-db","source":"src/Bookstore.Api/Program.cs"},{"kind":"OutboundHttp","name":"payment-gateway","source":"src/Bookstore.Api/Program.cs"}]` (summaries unchanged).
- Modify: `src/BlindSpotScanner.Application/Reports/ReportAssembler.cs:42`
- Modify: `src/BlindSpotScanner.Application/Scanning/ScanPipeline.cs` (pass `endpoints.Seams` to `GradeAsync` and into `Inventory.ProductionSeams`)
- Modify: `src/BlindSpotScanner.Application/Scanning/GradeQualityPhase.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Reports/ReportAssemblerTests.cs`, `Scanning/GradeQualityPhaseTests.cs`

**Interfaces:**
- Consumes: `EndpointInventory.Seams` (Task 2).
- Produces: `GradeQualityPhase.GradeAsync(RunContext run, TestClassification tests, IReadOnlyList<ControllerCoverage> coverage, RepoProfile profile, IReadOnlyList<ProductionSeam>? seams, int maxFileBytes, CancellationToken ct)`. P6 input fields `productionSeams: [{kind, name, source}]` (empty array when null) and `productionSeamsVerified: seams is not null`.

- [ ] **Step 1: Write the failing tests**
  - `Assemble_SeamsNotDetected_UncoveredRealSeamsNull`: current example detail with `Inventory.ProductionSeams = null` → `Summary.UncoveredRealSeams` null.
  - `Assemble_SeamsDetected_CountsUncoveredSeamFindings`: current example detail as loaded → `1` (the existing `Assemble_CurrentExampleDetail_ReproducesWholeReport` must still pass).
  - `Grade_Seams_SentInInput`: seams `[payment-gateway]` → request input JSON has `productionSeams[0].name == "payment-gateway"` and `productionSeamsVerified == true`.
  - `Grade_UncoveredSeamWithUnknownSubject_IsDroppedWithNote`: finding `{"kind":"UncoveredSeam",…,"subject":"PaymentGatewayClient"}` with seams `[payment-gateway]` → no `UncoveredSeam` finding; notes contain `UncoveredSeam 'PaymentGatewayClient' dropped: not a detected seam`.
  - `Grade_UncoveredSeamForDetectedSeam_IsKept`: subject `payment-gateway` → kept, `StableKey == "uncovered-seam:payment-gateway"`.
  - `Grade_SeamsNotDetected_AllUncoveredSeamFindingsDropped`: seams null, subject `payment-gateway` → dropped.
  Existing `Grade(...)` helper gains a `seams` parameter defaulting to `[]`.

- [ ] **Step 2: Run them to see them fail**
  Run: `dotnet test tests/BlindSpotScanner.Application.Tests --filter "FullyQualifiedName~GradeQualityPhaseTests|FullyQualifiedName~ReportAssemblerTests"` → fail.

- [ ] **Step 3: Implement.** Assembler: `UncoveredRealSeams = detail.Inventory.ProductionSeams is null ? null : <existing count>`. Phase: replace `productionSeams = Array.Empty<string>()`; filter LLM findings of kind `UncoveredSeam` whose `subject` is not (ordinal) a seam `Name` before the D33 evidence step, adding the note. Pipeline: map `ProductionSeam` → `ReportSeam` (`Source = SourcePath`).

- [ ] **Step 4: Run the tests to see them pass**, then `dotnet test` (whole backend; schema validator tests cover the contract change).

- [ ] **Step 5: Commit** `feat(scan): ground uncovered-seam findings in detected seams`

---

### Task 4: Prompt v1.1.0, prompt-set version, decisions

**Files:**
- Modify: `BlindSpotScanner-Blueprint/prompts/grade-quality.md`
- Modify: `src/BlindSpotScanner.Infrastructure/Llm/LlmOptions.cs:16`, `src/BlindSpotScanner.Api/appsettings.Example.json:30` (`"1.1.0"`); grep `"1.0.0"` in `src` and `tests` for other pins of the prompt-set version and update those that mean the prompt set.
- Modify: `docs/DECISIONS.md` (new `## D34 — Seams come from startup code (2026-11-19)`; replace the sentence "`productionSeams` is sent empty (the seams are in the profile Markdown)." with "`productionSeams` carries the seams detected from startup code (D34).")
- Modify: `docs/superpowers/specs/2026-11-19-production-seams-design.md` (note the three refinements and that "can't be read" was dropped: P3 reads controller and startup files without a size limit, so only a missing startup file makes seams unverified)
- Test: the existing PromptLibrary tests in `tests/BlindSpotScanner.Infrastructure.Tests` (find with `grep -rn "PromptLibrary" tests`)

**Interfaces:**
- Consumes: P6 input shape (Task 3).

- [ ] **Step 1: Write the failing test** `Version_GradeQuality_Is110`: `new PromptLibrary().Version(Prompts.GradeQuality)` → `"1.1.0"`.

- [ ] **Step 2: Run it to see it fail** (`dotnet test tests/BlindSpotScanner.Infrastructure.Tests --filter FullyQualifiedName~PromptLibrary`) → `"1.0.0"`.

- [ ] **Step 3: Edit the prompt.** Header `— v1.1.0`. Input example: `"productionSeams": [{ "kind": "OutboundHttp", "name": "payment-gateway", "source": "src/Api/Program.cs" }], "productionSeamsVerified": true`. Replace the `uncoveredSeams` bullet with:
  > **uncoveredSeams:** only seams listed in `productionSeams` that no test crosses for real. Set `subject` to the seam's `name` exactly. Put the seam's `source` path in `detail`. If `productionSeamsVerified` is false, report no `UncoveredSeam`. The HTTP pipeline is not listed; judge it in dimension #1 as before.
  
  Add under dimension #1: "Count breadth against `productionSeams` plus the HTTP pipeline." Then bump `PromptSetVersion`, write D34 (option B; patterns; detection table; kebab names; null vs empty; drop rule; `inventory.productionSeams` contract addition; prompt set 1.1.0 makes earlier runs not comparable), fix the DECISIONS sentence, update the spec.

- [ ] **Step 4: Verify.** `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` (Docker running), and in `src/web`: `npx ng test --watch=false`, `npx ng lint` (examples changed; specs read them). All green.

- [ ] **Step 5: Commit** `feat(prompts): grade-quality v1.1.0 grounds seams in code` (include DECISIONS and spec).

---

## Self-review notes

- Spec coverage: P2 startup files (T2), detection (T1), unverified rule (T2/T3), P6 input + enforcement (T3), prompt v1.1.0 (T4), assemble null (T3), versioning (T4), fixture (T2), decisions (T4). Provider detection from the spec's table is dropped (YAGNI; the key is the context name), recorded in D34.
- Types: `SeamKind` lives in Domain (`BlindSpotScanner.Domain.Enums`), created in Task 1, used by `ProductionSeam` (Application) and `ReportSeam` (Domain).
