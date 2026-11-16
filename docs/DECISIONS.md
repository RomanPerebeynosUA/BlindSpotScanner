# Decisions

Decisions that override or fill gaps in `BlindSpotScanner-Blueprint/`. Newest last.

## D1 — Versions: .NET 10 + Angular 22 (2026-11-16)
The blueprint says .NET 9 + Angular 19. We use **.NET 10** (LTS) and **Angular 22**, which are
installed and current. Every blueprint feature exists in these versions.

## D2 — One root CLAUDE.md wins (2026-11-16)
The blueprint's `CLAUDE.md` claims precedence. Instead, the root `CLAUDE.md` wins: it keeps the
project dev rules (TDD, no speculative complexity, naming, git) and adds the blueprint's
product principles. The blueprint's `CLAUDE.md` is reference only.

## D3 — Full Clean Architecture, plain services (2026-11-16)
We follow blueprint doc 03: Domain, Application, Infrastructure, Api and a separate Worker
(4 containers: db, api, worker, web). Projects are named `BlindSpotScanner.*`. Use plain
application services, not MediatR (the blueprint allows either).

## D4 — Docker Desktop (2026-11-16)
We install Docker Desktop (WSL2) for PostgreSQL, Docker Compose and Testcontainers, as the
blueprint requires.

## D5 — Name: BlindSpotScanner everywhere (2026-11-16)
It replaces "Integration Coverage Analyzer" and `Coverage.*` in project names, the UI title,
the PR footer and docs.

## D6 — Credentials when needed (2026-11-16)
Build with fakes. Get a Claude API key (billed separately from a Claude Pro plan) at M5 for the
opt-in live test, and a free Jira Cloud site at M9.

## D8 — FluentAssertions 7.x (2026-11-16)
FluentAssertions v8+ needs a paid license for commercial use. We pin the last Apache-2.0 major
(7.x) at an exact version (7.2.2), so the CLAUDE.md test stack stays as written.

## D7 — Angular 2025 file naming (2026-11-16)
Angular 22 generates `name.ts/.html/.scss/.spec.ts` (no `.component` suffix) by default. We keep
that style. CLAUDE.md's frontend rule was updated to match.

## D9 — Full doc 03 domain model in M1 (2026-11-16)
M1 builds all doc 03 entities and tables (Repository, RepoProfile, ScanRun, ScanStep, Finding,
RemediationAction, file_cache, llm_cache) plus the scan_runs headline columns. The job-queue
lease columns (M6) and connection settings (M9) are not in doc 03 and arrive with their milestones.

## D10 — "Byte-equivalent round-trip" means canonical bytes (2026-11-16)
PostgreSQL jsonb reorders keys and drops whitespace, so the source file can't come back
byte-identical. Equivalence is: canonical serialize → store → read → deserialize → serialize gives
identical bytes, and the canonical JSON is semantically equal (`JsonNode.DeepEquals`) to the
source file. `ReportJsonSerializer` (Domain) is the only owner of the canonical options.
`delta` is always written (`null` on a first scan), as in `examples/sample-report-previous.json`.
Report timestamps must carry an explicit offset and are always written as UTC (`…Z`), so the
canonical bytes never depend on the machine's time zone.

## D11 — JsonSchema.Net validates reports (2026-11-16)
The report schema is draft 2020-12 (`$defs`, `const`, type arrays), which NJsonSchema doesn't fully
support. `ReportSchemaValidator` (Infrastructure) uses JsonSchema.Net with format assertion on, so
`uuid`, `date-time` and `uri` are enforced. The schema is embedded; a test keeps it equal to
`BlindSpotScanner-Blueprint/contracts/`.

## D12 — Infrastructure test project (2026-11-16)
`tests/BlindSpotScanner.Infrastructure.Tests` holds Infrastructure unit tests and the Testcontainers
persistence tests. `Api.IntegrationTests` stays for HTTP-level tests.

## D13 — Summary derivation choices the spec leaves open (2026-11-16)
`ReportAssembler` (Application) derives the summary from the detail:
- `falsePositiveCount`, `skippedTestCount` and `uncoveredRealSeams` count findings of kind
  `FalsePositive`, `SkippedTest` and `UncoveredSeam`.
- Rounding is `MidpointRounding.AwayFromZero` (score to 0.1, assessable % to whole numbers).
- Texts not fixed by doc 06/07: a null score labels "Not scorable"; a non-`real-seam` mapping
  appends " — not comparable to real-seam scores ({mapping})"; more than 25% unverified appends
  " (low confidence: many endpoints unverified)" to the Overall bullet; a null score in
  "Since last scan" prints "N/A".
- `examples/sample-report-previous.json` had `summary.uncoveredRealSeams: 1` with no
  `UncoveredSeam` finding (the current report's delta lists that seam as new). It was corrected
  to `0`, so both examples assemble exactly.

## D14 — Endpoint identity keeps route constraints (2026-11-16)
Doc 08 normalizes `{param}` *names* to `{}`. Route constraints are kept (`{id:int}` → `{:int}`), so
`GET items/{id:int}` and `GET items/{slug}` (both legal ASP.NET routes) stay distinct endpoints.
If the same identity still occurs twice in one report (a route declared in two controllers),
occurrences are paired in order rather than failing the comparison.
