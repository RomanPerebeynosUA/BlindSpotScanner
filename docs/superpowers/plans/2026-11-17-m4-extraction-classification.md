# M4 Endpoint Extraction and Test Classification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** P3 extracts every ASP.NET Core endpoint (attribute routing and minimal APIs) from the
production files with Roslyn. P4 classifies test files with the profile's rules. The fixture
mini-repo `tests/fixtures/bookstore/` yields the expected endpoints and integration tests.

**Architecture:** Roslyn lives in Infrastructure, behind two ports defined in Application:
`IEndpointExtractor` (C# source → controllers with endpoints) and `ITestSyntaxReader` (C# source →
attributes, base types and test method names). The phases `ExtractPhase` and `ClassifyPhase` are
plain Application services that read blobs through `IBlobReader` and apply the rules. The
classification rules are new typed `string[]` columns on `RepoProfile` (user's choice, D18).

**Tech Stack:** .NET 10, Microsoft.CodeAnalysis.CSharp 5.12.0 (syntax API only, no compilation),
Microsoft.Extensions.FileSystemGlobbing, EF Core 10 + Npgsql (`text[]` columns), xUnit +
FluentAssertions, Testcontainers.

**Spec:** `BlindSpotScanner-Blueprint/docs/05-scan-pipeline.md` (P3, P4, Language support),
`docs/11-implementation-plan.md` (M4 AC), `docs/03-backend-dotnet.md` (`IEndpointExtractor`),
`profiles/_template.md` (rule kinds), `prompts/classify-tests.md` (kinds), `docs/DECISIONS.md`.

## Global Constraints

- Clean Architecture: Roslyn types never appear in Domain or Application.
- `TreatWarningsAsErrors`, Nullable on; `dotnet format --verify-no-changes` clean.
- Principle 1: nothing is dropped silently. An endpoint the extractor can't resolve becomes an
  unverified reason naming the file, never a missing row.
- Principle 3: the denominator comes from production files only (P2's `ControllerFiles`).
- Git paths are case-sensitive (`StringComparison.Ordinal`); output lists ordered ordinally.
- No LLM in M4. Files the rules can't classify are returned as `Ambiguous` for P4's LLM step (M5).
- Tests: `Method_Scenario_ExpectedResult`, `// Arrange` `// Act` `// Assert`, one behavior, no branching.

## Review Focus

1. Commented-out code (`// [HttpDelete("{id}")]`, `// [Trait("Category","Integration")]`) must
   not create endpoints or integration signals. Roslyn puts comments in trivia; the tests pin it
   (Task 2 `Extract_CommentedOutAttribute_IsIgnored`, Task 5 `Read_CommentedOutAttribute_IsIgnored`).
2. A route given as a constant or interpolated string can't be read syntactically. It becomes a
   warning with the file and method, not a silently missing endpoint (Task 2, Task 3).
3. Absolute action templates (`"/health"`, `"~/ping"`) replace the controller prefix (Task 2).
4. An abstract base controller contributes no endpoints of its own; a controller-looking class
   with no attribute-routed actions is reported, not counted as zero endpoints (Task 2).
5. A helper in an integration folder (`ApiFactory.cs`, no test methods) is not an integration
   test file (Task 6 `Classify_NoTestMethods_IsOther`).

---

### Task 1: Profile classification rules and minimal-API files in the denominator

**Files:**
- Modify: `src/BlindSpotScanner.Domain/Entities/RepoProfile.cs`
- Modify: `src/BlindSpotScanner.Application/Scanning/EnumeratePhase.cs`
- Create: migration `AddProfileClassificationRules` under `src/BlindSpotScanner.Infrastructure/Persistence/Migrations/`
- Modify: `docs/DECISIONS.md` (D18)
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Persistence/EntityMappingTests.cs`, `tests/BlindSpotScanner.Application.Tests/Scanning/EnumeratePhaseTests.cs`

**Interfaces:**
- Produces on `RepoProfile` (all `public string[] X { get; init; } = [];`, not `required`, so
  existing construction sites compile): `MinimalApiGlobs`, `IntegrationGlobs` (folders and
  naming, for example `tests/*.IntegrationTests/**`, `**/*IntegrationTests.cs`),
  `IntegrationAttributes` (for example `Trait("Category","Integration")`), `IntegrationBaseTypes`
  (for example `WebApplicationFactory`, `IClassFixture<ApiFactory>`), `NotIntegrationGlobs`
  (for example `**/*Fixture.cs`).
- `RepositoryInventory.ControllerFiles` now holds matches of `ControllerGlobs ∪ MinimalApiGlobs`
  minus test files (name unchanged; doc 05 calls them "controllers/route files").

- [ ] **Step 1: Write the failing tests**
  - `EntityMappingTests.RepoProfile_ClassificationRules_RoundTrip`: save a profile with one value
    in each of the five arrays, read it back in a new context, `BeEquivalentTo` the original.
  - `EnumeratePhaseTests.Enumerate_MinimalApiGlobs_JoinDenominator`: Bookstore plus
    `src/Api/Endpoints/HealthEndpoints.cs`, profile with `MinimalApiGlobs = ["**/Endpoints/*.cs"]`
    → `ControllerFiles` paths equal (strict order) Books, Orders, `src/Api/Endpoints/HealthEndpoints.cs`
    sorted ordinally.
- [ ] **Step 2: Run them; expect compile failure, then FAIL** (`dotnet test --filter "FullyQualifiedName~RepoProfile_ClassificationRules|FullyQualifiedName~MinimalApiGlobs"`).
- [ ] **Step 3: Add the five properties; in `EnumerateAsync` match `[.. profile.ControllerGlobs, .. profile.MinimalApiGlobs]`.**
- [ ] **Step 4: `dotnet tool restore`, then `dotnet ef migrations add AddProfileClassificationRules --project src/BlindSpotScanner.Infrastructure --output-dir Persistence/Migrations`.**
  Expected: five `text[]` columns on `repo_profiles`, `nullable: false`. If EF emits no default
  for existing rows, set `defaultValue: new string[0]` in the migration (ledger ruling).
- [ ] **Step 5: Run the two tests; expect PASS. Run `dotnet test`; expect all green.**
- [ ] **Step 6: Add D18 to `docs/DECISIONS.md`:** rules are typed columns (user's choice); meaning
  of each array; attribute entries are written without brackets; base-type entries without `<`
  match any generic arity.
- [ ] **Step 7: Commit** `feat(domain): add profile classification rules and minimal API globs`

### Task 2: Roslyn extractor for attribute-routed controllers

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/IEndpointExtractor.cs`
- Create: `src/BlindSpotScanner.Infrastructure/Extraction/AspNetCoreEndpointExtractor.cs`
- Modify: `src/BlindSpotScanner.Infrastructure/BlindSpotScanner.Infrastructure.csproj` (add `Microsoft.CodeAnalysis.CSharp` 5.12.0)
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/AspNetCoreControllerExtractionTests.cs`

**Interfaces:**
- Produces (Application, namespace `BlindSpotScanner.Application.Scanning`):
  ```csharp
  public interface IEndpointExtractor { ExtractionResult Extract(string path, string source); }
  public sealed record ExtractionResult(IReadOnlyList<ExtractedController> Controllers, IReadOnlyList<string> Warnings);
  public sealed record ExtractedController(string Controller, string SourcePath, string? RoutePrefix, IReadOnlyList<ExtractedEndpoint> Endpoints);
  public sealed record ExtractedEndpoint(EndpointMethod Method, string Route);
  ```
- `AspNetCoreEndpointExtractor : IEndpointExtractor` (public sealed, no constructor args).

Rules (from ASP.NET Core attribute routing):
- A controller is a non-abstract class that has `[ApiController]`, or derives from `Controller` /
  `ControllerBase`, or whose name ends in `Controller`. `Controller` = class name.
- Prefix = the class's `[Route("…")]` literal, `[controller]` → class name minus `Controller`,
  leading `/` trimmed. `RoutePrefix` is that string, or null when absent.
- Each public, non-static method without `[NonAction]` yields one endpoint per `Http{Get,Post,Put,Patch,Delete,Head,Options}`
  attribute. Template = the attribute's first argument, else the method's `[Route]`, else "".
  `[action]` → method name. A template starting with `/` or `~/` replaces the prefix; otherwise
  route = prefix + "/" + template, trimmed of `/`, with no doubled `/`.
- Attribute names match with or without the `Attribute` suffix and with any namespace qualifier.
- A template argument that is not a string literal → warning
  `"{path}: route of {Class}.{Method} is not a string literal; endpoint not extracted"`.
- A controller with zero endpoints → warning `"{path}: {Class} has no attribute-routed actions"`,
  and it is not in `Controllers`.

- [ ] **Step 1: Write the failing tests** (each calls `new AspNetCoreEndpointExtractor().Extract("src/Api/Controllers/X.cs", source)` with an inline raw-string source):
  - `Extract_RoutePrefixAndVerbs_YieldsEndpoints`: `[ApiController][Route("api/books")] class BooksController : ControllerBase` with `[HttpGet]`, `[HttpGet("{id}")]`, `[HttpPost]` → one controller `BooksController`, prefix `api/books`, endpoints (Get `api/books`), (Get `api/books/{id}`), (Post `api/books`).
  - `Extract_ControllerToken_UsesClassNameWithoutSuffix`: `[Route("api/[controller]")] class OrdersController` + `[HttpPost("{id}/cancel")]` → route `api/Orders/{id}/cancel`.
  - `Extract_AbsoluteTemplate_ReplacesPrefix`: `[HttpGet("/health")]` and `[HttpGet("~/ping")]` under prefix `api/x` → routes `health`, `ping`.
  - `Extract_MethodRouteAttribute_IsTemplate`: `[HttpGet][Route("latest")]` → `api/books/latest`.
  - `Extract_TwoVerbAttributes_YieldTwoEndpoints`: `[HttpPut("{id}")][HttpPatch("{id}")]` → Put and Patch.
  - `Extract_CommentedOutAttribute_IsIgnored`: `// [HttpDelete("{id}")]` above a public method with no other attribute; one other `[HttpGet]` action → endpoints are only the Get.
  - `Extract_NonActionAndPrivate_AreSkipped`: `[NonAction][HttpGet("a")] public`, `[HttpGet("b")] private` → no endpoints from them.
  - `Extract_ConstantRoute_IsWarnedNotDropped`: `[HttpGet(Routes.Latest)]` → `Warnings` equal `"src/Api/Controllers/X.cs: route of BooksController.Latest is not a string literal; endpoint not extracted"`.
  - `Extract_AbstractBaseController_YieldsNothing`: `abstract class ApiControllerBase : ControllerBase` with `[HttpGet]` → no controllers, no warnings.
  - `Extract_ControllerWithoutRoutedActions_IsWarned`: `class LegacyController : Controller { public IActionResult Index() … }` → warning `"src/Api/Controllers/X.cs: LegacyController has no attribute-routed actions"`.
  - `Extract_QualifiedAttributeNames_AreRecognized`: `[Microsoft.AspNetCore.Mvc.HttpGetAttribute("x")]` → route `api/books/x`.
- [ ] **Step 2: Run them; expect compile failure then FAIL** (`dotnet test tests/BlindSpotScanner.Infrastructure.Tests --filter "FullyQualifiedName~AspNetCoreControllerExtractionTests"`).
- [ ] **Step 3: Add the package and the port; implement with `CSharpSyntaxTree.ParseText` and `DescendantNodes().OfType<ClassDeclarationSyntax>()`.** If restore reports a Roslyn version conflict with `Microsoft.EntityFrameworkCore.Design`, pin per the resulting error and ledger the ruling.
- [ ] **Step 4: Run them; expect PASS.**
- [ ] **Step 5: Commit** `feat(infra): extract attribute-routed endpoints with Roslyn`

### Task 3: Minimal API extraction

**Files:**
- Modify: `src/BlindSpotScanner.Infrastructure/Extraction/AspNetCoreEndpointExtractor.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/MinimalApiExtractionTests.cs`

**Interfaces:**
- Consumes: Task 2's records.
- Minimal APIs in a file form one `ExtractedController` with `Controller` = file name without
  extension, `RoutePrefix` = null (ruling: reports need a controller row; the file is the unit).

Rules:
- An invocation `receiver.Map{Get,Post,Put,Patch,Delete}("template", …)` is an endpoint.
- Group prefix: walk the receiver. A `MapGroup("p")` invocation adds `p`; any other invocation
  (`WithTags`, `RequireAuthorization`, …) is skipped to its own receiver; an identifier bound in
  the same file to a local whose initializer is such a chain is followed. Prefixes join with `/`.
- Non-literal template or group prefix → warning `"{path}: route of {MethodName} call at line {n} is not a string literal; endpoint not extracted"` (1-based line).

- [ ] **Step 1: Write the failing tests:**
  - `Extract_MapVerbs_YieldEndpoints`: `app.MapGet("/health", …); app.MapPost("api/ping", …);` in `src/Api/Endpoints/HealthEndpoints.cs` → controller `HealthEndpoints`, endpoints (Get `health`), (Post `api/ping`).
  - `Extract_MapGroupVariable_PrefixesRoutes`: `var orders = app.MapGroup("api/orders").WithTags("Orders"); orders.MapPost("/{id}/cancel", …);` → Post `api/orders/{id}/cancel`.
  - `Extract_NestedMapGroup_JoinsPrefixes`: `app.MapGroup("api").MapGroup("v1").MapGet("books", …)` → Get `api/v1/books`.
  - `Extract_InterpolatedMinimalRoute_IsWarned`: `app.MapGet($"api/{Version}/x", …)` on line 3 → warning `"src/Api/Endpoints/HealthEndpoints.cs: route of MapGet call at line 3 is not a string literal; endpoint not extracted"`.
  - `Extract_ControllersAndMinimalApisInOneFile_BothExtracted`: one controller class plus a `MapGet` → two controllers (class name, file name).
- [ ] **Step 2: Run them; expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run Task 2 and Task 3 tests; expect PASS.**
- [ ] **Step 5: Commit** `feat(infra): extract minimal API endpoints and route groups`

### Task 4: P3 ExtractPhase

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/ExtractPhase.cs`, `EndpointInventory.cs`
- Create: `tests/BlindSpotScanner.Application.Tests/Scanning/FakeBlobReader.cs`, `FakeEndpointExtractor.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Scanning/ExtractPhaseTests.cs`

**Interfaces:**
- Consumes: `IBlobReader.ReadAsync(owner, name, blobSha, ct)`, `IEndpointExtractor`, `RepositoryInventory.ControllerFiles`.
- Produces:
  ```csharp
  public sealed class ExtractPhase(IBlobReader blobs, IEndpointExtractor extractor)
  { public Task<EndpointInventory> ExtractAsync(RunContext run, RepositoryInventory inventory, CancellationToken ct); }
  public sealed record EndpointInventory(IReadOnlyList<ExtractedController> Controllers, IReadOnlyList<string> UnverifiedReasons)
  { public int EndpointCount => Controllers.Sum(c => c.Endpoints.Count); }
  ```
- `UnverifiedReasons` = P2's reasons followed by extractor warnings, in file order.
- Controllers ordered by `SourcePath` then `Controller`, ordinal.

- [ ] **Step 1: Write the failing tests** (fakes: `FakeBlobReader` maps sha → text and counts reads; `FakeEndpointExtractor` maps path → `ExtractionResult`):
  - `Extract_EachControllerFile_ReadOnceAtPinnedSha`: two files → `FakeBlobReader.Reads` equals their two blob SHAs.
  - `Extract_Controllers_AreOrderedByPath`: files given as Orders then Books → controllers Books, Orders.
  - `Extract_ExtractorWarnings_BecomeUnverifiedReasons`: inventory reason `"submodule at 'libs/x' was not enumerated"`, extractor warning `"w1"` → `UnverifiedReasons` equal those two in that order.
  - `Extract_EndpointCount_SumsAllControllers`: 3 + 2 endpoints → `EndpointCount` 5.
- [ ] **Step 2: Run; expect FAIL.**
- [ ] **Step 3: Implement (sequential reads; no parallelism yet).**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(app): add P3 extract phase`

### Task 5: Roslyn test syntax reader

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/ITestSyntaxReader.cs`
- Create: `src/BlindSpotScanner.Infrastructure/Extraction/CSharpTestSyntaxReader.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/CSharpTestSyntaxReaderTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface ITestSyntaxReader { TestFileFacts Read(string source); }
  /// Attributes and BaseTypes are whitespace-free syntax text, attributes without brackets.
  public sealed record TestFileFacts(IReadOnlyList<string> Attributes, IReadOnlyList<string> BaseTypes, IReadOnlyList<string> TestMethods);
  ```
- `Attributes`: every attribute on a class or method, `Name(args)` with whitespace removed and an
  `Attribute` suffix dropped from the name (`[TraitAttribute("Category", "Integration")]` →
  `Trait("Category","Integration")`). Distinct, ordinal order.
- `BaseTypes`: base list entries of every class, whitespace removed (`IClassFixture<ApiFactory>`).
- `TestMethods`: names of methods carrying `Fact`, `Theory`, `Test`, `TestCase`, `TestMethod` or
  `DataTestMethod`, as `Class.Method`, in source order.

- [ ] **Step 1: Write the failing tests:**
  - `Read_ClassAndMethodAttributes_AreNormalized`: class `[Trait( "Category" , "Integration" )]`, method `[Fact]` → `Attributes` equal `["Fact", "Trait(\"Category\",\"Integration\")"]`.
  - `Read_BaseTypes_IncludeGenericArguments`: `class BooksApiTests : IClassFixture<WebApplicationFactory<Program>>` → `BaseTypes` equal `["IClassFixture<WebApplicationFactory<Program>>"]`.
  - `Read_TestMethods_NamedByClass`: `[Fact] A`, `[Theory] B`, helper `C` → `["BooksApiTests.A", "BooksApiTests.B"]`.
  - `Read_CommentedOutAttribute_IsIgnored`: `// [Trait("Category","Integration")]` → `Attributes` don't contain it.
- [ ] **Step 2: Run; expect compile failure then FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(infra): read test attributes, base types and methods with Roslyn`

### Task 6: P4 ClassifyPhase (rules only)

**Files:**
- Create: `src/BlindSpotScanner.Application/Scanning/ClassifyPhase.cs`, `TestClassification.cs`
- Create: `tests/BlindSpotScanner.Application.Tests/Scanning/FakeTestSyntaxReader.cs`
- Test: `tests/BlindSpotScanner.Application.Tests/Scanning/ClassifyPhaseTests.cs`

**Interfaces:**
- Consumes: `IBlobReader`, `ITestSyntaxReader`, `RepositoryInventory.TestFiles`, Task 1's profile arrays.
- Produces:
  ```csharp
  public enum TestFileKind { Integration, Other, Ambiguous }
  public sealed record ClassifiedTestFile(string Path, string BlobSha, TestFileKind Kind, string Reason, IReadOnlyList<string> TestMethods);
  public sealed record TestClassification(IReadOnlyList<ClassifiedTestFile> Files)
  { public IReadOnlyList<ClassifiedTestFile> IntegrationTests => …Kind == Integration; public IReadOnlyList<ClassifiedTestFile> Ambiguous => …Kind == Ambiguous; }
  public sealed class ClassifyPhase(IBlobReader blobs, ITestSyntaxReader reader)
  { public Task<TestClassification> ClassifyAsync(RunContext run, RepositoryInventory inventory, RepoProfile profile, CancellationToken ct); }
  ```
- Rules, first match wins, reasons exactly:
  1. path matches `NotIntegrationGlobs` → Other, `"matches not-integration glob '{glob}'"`
  2. no test methods → Other, `"no test methods"`
  3. path matches `IntegrationGlobs` → Integration, `"matches integration glob '{glob}'"`
  4. an attribute equals an `IntegrationAttributes` entry (entry brackets and whitespace removed;
     an entry without `(` matches by name) → Integration, `"has attribute [{entry}]"`
  5. a base type equals an `IntegrationBaseTypes` entry (an entry without `<` matches the base
     type's name before `<`) → Integration, `"derives from {entry}"`
  6. otherwise → Ambiguous, `"no profile rule matched"`
- `{glob}` / `{entry}` is the first matching profile entry in profile order. Files keep P2's order.

- [ ] **Step 1: Write the failing tests** (one file each through fakes, profile with one entry per array):
  - `Classify_NotIntegrationGlob_IsOtherEvenInIntegrationFolder`
  - `Classify_NoTestMethods_IsOther`
  - `Classify_IntegrationFolderGlob_IsIntegration` (reason `"matches integration glob 'tests/*.IntegrationTests/**'"`)
  - `Classify_IntegrationAttribute_IsIntegration` (profile entry `[Trait("Category", "Integration")]` with brackets and a space; reason `"has attribute [Trait(\"Category\",\"Integration\")]"`)
  - `Classify_AttributeNameOnlyEntry_MatchesAnyArguments` (entry `Collection`, attribute `Collection("Database")`)
  - `Classify_BaseTypeWithoutGenerics_MatchesGenericBase` (entry `WebApplicationFactory`, base `WebApplicationFactory<Program>`)
  - `Classify_NoRuleMatched_IsAmbiguous`
  - `Classify_IntegrationTests_CarryTestMethods` (methods from the reader are on the result)
- [ ] **Step 2: Run; expect compile failure then FAIL.**
- [ ] **Step 3: Implement (`Matcher(StringComparison.Ordinal)` per glob for the first-match name).**
- [ ] **Step 4: Run; expect PASS.**
- [ ] **Step 5: Commit** `feat(app): add P4 rule-based test classification`

### Task 7: Fixture mini-repo and M4 acceptance test

**Files:**
- Create: `tests/fixtures/bookstore/` (files below; plain `.cs` text, never compiled)
- Create: `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/FixtureRepository.cs` (an `IGitHubClient` + `IBlobReader` over a directory: tree from the file list, blob SHA = `"blob:" + path`)
- Create: `tests/BlindSpotScanner.Infrastructure.Tests/Extraction/BookstoreFixtureTests.cs`
- Modify: `tests/BlindSpotScanner.Infrastructure.Tests/BlindSpotScanner.Infrastructure.Tests.csproj` (copy `..\fixtures\bookstore\**` to output under `fixtures/bookstore`)
- Modify: `docs/DECISIONS.md` (D19), `CLAUDE.md` layout block (add `tests/fixtures/`)

Fixture files (contents mirror `examples/sample-report.json`):
- `src/Bookstore.Api/Controllers/BooksController.cs`: `[Route("api/books")]`; GET, GET `{id}`, POST.
- `src/Bookstore.Api/Controllers/OrdersController.cs`: `[Route("api/orders")]`; POST, POST `{id}/cancel`.
- `src/Bookstore.Api/Endpoints/HealthEndpoints.cs`: `app.MapGet("/health", …)`.
- `tests/Bookstore.IntegrationTests/BooksApiTests.cs`: `IClassFixture<ApiFactory>`, `[Fact]` `List_returns_seeded_books`, `Get_by_id_returns_ok`, `Create_persists_book`.
- `tests/Bookstore.IntegrationTests/OrdersApiTests.cs`: `[Fact]` `Place_order_persists_lines_and_total`.
- `tests/Bookstore.IntegrationTests/ApiFactory.cs`: `WebApplicationFactory<Program>` subclass, no tests.
- `tests/Bookstore.IntegrationTests/DbFixture.cs`: matches `**/*Fixture.cs`.
- `tests/Bookstore.UnitTests/PriceTests.cs`: `[Fact]`, no signals.
- `tests/Bookstore.UnitTests/StockQueryTests.cs`: `[Trait("Category","Integration")]` + `[Fact]`.

Profile: `ControllerGlobs ["src/**/Controllers/**/*Controller.cs"]`, `MinimalApiGlobs ["src/**/Endpoints/**/*.cs"]`,
`TestGlobs ["tests/**/*.cs"]`, `IntegrationGlobs ["tests/*.IntegrationTests/**"]`,
`IntegrationAttributes ["Trait(\"Category\",\"Integration\")"]`, `IntegrationBaseTypes ["WebApplicationFactory"]`,
`NotIntegrationGlobs ["**/*Fixture.cs"]`.

- [ ] **Step 1: Write the fixture files and the failing tests** (pipeline: `EnumeratePhase` → `ExtractPhase` → `ClassifyPhase` with the real Roslyn classes over `FixtureRepository`):
  - `Bookstore_Extract_YieldsExpectedEndpoints`: `(Controller, Method, Route)` triples equal
    (`BooksController`, Get, `api/books`), (`BooksController`, Get, `api/books/{id}`), (`BooksController`, Post, `api/books`),
    (`OrdersController`, Post, `api/orders`), (`OrdersController`, Post, `api/orders/{id}/cancel`),
    (`HealthEndpoints`, Get, `health`); `UnverifiedReasons` empty.
  - `Bookstore_Classify_YieldsExpectedIntegrationTests`: `IntegrationTests` paths equal
    `tests/Bookstore.IntegrationTests/BooksApiTests.cs`, `tests/Bookstore.IntegrationTests/OrdersApiTests.cs`,
    `tests/Bookstore.UnitTests/StockQueryTests.cs`; `Ambiguous` paths equal `tests/Bookstore.UnitTests/PriceTests.cs`.
- [ ] **Step 2: Run; expect FAIL only for a real defect (earlier tasks already exist) — if both pass first time, temporarily break one fixture route to watch the test fail, then restore it.**
- [ ] **Step 3: Add D19:** extractor scope (attribute routing, minimal APIs with `MapGroup`;
  conventional routing, `[Area]`, inherited `[Route]` and `MapMethods` are not supported and
  surface as warnings or no endpoints), minimal-API controller name = file name, non-literal
  routes become unverified reasons.
- [ ] **Step 4: `dotnet test` and `dotnet format --verify-no-changes`; expect all green, no changes.**
- [ ] **Step 5: Commit** `test: add bookstore fixture repo and M4 acceptance tests`
