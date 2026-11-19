# Production seams from code — design

Date: 2026-11-19 · Status: approved; implemented as recorded in D34

> Refined during planning (see D34): detection is a method on the existing `IEndpointExtractor`
> (no new port); seam names are kebab-case keys and the provider is not detected; the report gains
> `inventory.productionSeams`; a startup file that "can't be read" is not a case (P3 reads without a
> size limit), so only a missing startup file makes seams unverified.

## Intent

The quality-owner review of the evaluation prompts found the report's weakest claim: the
"uncovered real seam" finding (usually `topRisks[0]`) and dimension #1 (20% of the score) rest on
a seam list the model infers for itself. `GradeQualityPhase` always sends `productionSeams: []`
(DECISIONS, P6 paragraph), so a risk like "the payment provider is never called for real" is not
grounded in production code. That contradicts the spirit of principle 3 (denominators come from
production code).

Goal: detect the seams from the repository's startup code, send them to P6, and accept an
`UncoveredSeam` finding only for a seam that code detected. Claim only what was read; anything
not read is `unverified`.

## Goals

- Seams (real dependencies) are detected deterministically from production startup files at the
  pinned commit.
- P6 receives the detected list and may report `UncoveredSeam` only for a listed seam (enforced in
  code, not just in the prompt).
- When seams could not be detected, the report says so (unverified reason, `uncoveredRealSeams`
  omitted) instead of reporting 0 or an inferred seam.

## Non-goals

- Deciding in code whether a test crosses a seam for real. That stays the model's judgement in P6.
- Reading all production files (option A, rejected for GitHub rate-limit cost).
- A curated seam list in the profile (option C).
- Severity rules, per-dimension anchors, score-delta noise band, other prompt edits from the review.
  Each is a separate change.

## Anti-goals

- A seam reported because the model guessed it.
- `uncoveredRealSeams: 0` shown when nothing was read; that's the "none vs unverified" mistake.

## Approaches considered

| | Approach | Trade-off |
|---|---|---|
| A | Read every production `.cs` file and match registrations | Most complete; one blob read per file (2,000 files ≈ 2,000 calls of a 5,000/h limit) |
| **B (chosen)** | Read only startup files: `Program.cs`, `Startup.cs`, `*ServiceCollectionExtensions.cs` | Cheap; where registrations usually live; misses unusual placements, which become unverified |
| C | Human-confirmed seam list in the profile | No code reading; only as strong as whoever confirmed it |

Chosen by the user: **B**.

## Design

### P2: enumerate startup files
`RepositoryInventory` gains `StartupFiles`: blobs matching `**/Program.cs`, `**/Startup.cs` and
`**/*ServiceCollectionExtensions.cs`, after the fixed excludes and outside the profile's test globs.
Fixed patterns, no profile field (no present need for one).

### P3: detect seams (inside the Extract phase, no new pipeline phase)
`ExtractPhase` also reads each startup file and passes it to a new port
`ISeamDetector.Detect(path, source)` (language strategy, like `IEndpointExtractor`), implemented in
`Infrastructure/Extraction` with Roslyn so commented-out code never matches.

Detected registrations (invocations / object creations):

| Kind | Signal | Name |
|---|---|---|
| `Database` | `AddDbContext<T>`, `AddDbContextPool<T>`, `AddDbContextFactory<T>`; provider from `UseNpgsql`/`UseSqlServer`/`UseSqlite`/`UseMySql` in the same call | `PostgreSQL (BookstoreDbContext)`; provider unknown → `Database (T)` |
| `OutboundHttp` | `AddHttpClient<T>` / `AddHttpClient<TInterface, TImpl>` / `AddHttpClient("name")` | `Outbound HTTP (T or name)` |
| `MessageBroker` | `AddMassTransit`, `new ConnectionFactory` (RabbitMQ), `ProducerBuilder`/`ConsumerBuilder` (Kafka), `ServiceBusClient` | `Message broker (MassTransit)` etc. |
| `BlobStorage` | `BlobServiceClient`, `AmazonS3Client`, `AddAWSService<IAmazonS3>` | `Blob storage (Azure)` / `(S3)` |
| `Cache` | `AddStackExchangeRedisCache`, `ConnectionMultiplexer.Connect` | `Redis cache` |

Output: `ProductionSeam(Kind, Name, SourcePath)`, de-duplicated by `Name`, ordinal order.
`EndpointInventory` (or a sibling record on the Extract result) carries `Seams` and
`SeamsVerified`.

`SeamsVerified` is false, with an unverified reason, when:
- no startup file exists: `"seams not detected: no Program.cs, Startup.cs or *ServiceCollectionExtensions.cs found"`;
- a startup file can't be read (denied path or over `Scan:MaxFileBytes`).

### P6: send and enforce
- Input: `productionSeams: [{ kind, name, source }]` and `productionSeamsVerified: bool`.
- Prompt `grade-quality.md` → **v1.1.0**: describe the new shape; "Report an `UncoveredSeam` only
  for a seam in `productionSeams`, with `subject` equal to its `name`. If
  `productionSeamsVerified` is false, report no `UncoveredSeam`." The profile's "Real seams in
  production" line remains context only.
- Code: an `UncoveredSeam` finding whose `subject` is not a sent seam name is dropped with a note
  (same handling as D33's dropped evidence; not re-asked). When unverified, all are dropped.
- Finding evidence keeps citing test-side code (the fake or factory); the production source path
  goes in the finding's `detail` as text. Reason: `Program.cs` with top-level statements declares
  no class or method, so it can't be a `path::Name` citation.

### Assemble
`summary.uncoveredRealSeams` = count of `UncoveredSeam` findings when `SeamsVerified`, otherwise
null (omitted; the schema doesn't require it). The web already treats it as optional.

### Versioning
`PromptSetVersion` 1.0.0 → 1.1.0 (`LlmOptions` default, `appsettings.Example.json`). `RunComparer`
then marks runs across the change as not comparable, which is correct: the seam measurement changed.

### Fixture
`tests/fixtures/bookstore` gains `src/Bookstore.Api/Program.cs` registering
`AddDbContext<BookstoreDbContext>(o => o.UseNpgsql(...))` and `AddHttpClient<PaymentGatewayClient>()`,
so the sample report's payment seam is detectable. Expected seams: `PostgreSQL (BookstoreDbContext)`,
`Outbound HTTP (PaymentGatewayClient)`.

### Decisions log
Replace the "`productionSeams` is sent empty" sentence in `docs/DECISIONS.md` with a new decision
entry recording option B, the startup-file patterns, the detection table, the unverified rule and
the drop rule.

## Tests (TDD, one behavior each)

- `SeamDetector`: each signal row → expected seam; commented-out registration → none; provider
  detection; unknown provider name.
- `EnumeratePhase`: startup files matched; test-glob files excluded.
- `ExtractPhase`: seams collected; no startup file → unverified reason and `SeamsVerified=false`.
- `GradeQualityPhase`: input carries the seams; unknown-subject `UncoveredSeam` dropped with note;
  unverified → all dropped.
- `ReportAssembler`: `uncoveredRealSeams` null when unverified.
- `PromptLibrary`: grade-quality version 1.1.0.
- Existing fixture-based tests (Api integration `FakeGitHubServer`, M6 end-to-end) updated for the
  new `Program.cs` in the tree.

## Left to the builder

- Exact record placement (extend `EndpointInventory` vs a new `ExtractResult`).
- Roslyn matching details (generic name syntax, member access chains).
- Updating sample/fake Claude responses so P6 fakes return subjects that match detected seams.
