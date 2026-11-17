# BlindSpotScanner

Web app that audits a GitHub repository's **integration tests**. It produces an evidence-backed
coverage map and a quality score (0–10), turns gaps into Jira tickets or fix PRs, and compares
scans over time.

Stack: Angular 22 frontend · ASP.NET Core (.NET 10, C#) API + Worker · PostgreSQL 16 (EF Core) ·
Docker Compose · Claude API.

## Sources of truth (in this order)
1. **This file.** It wins over everything below.
2. `docs/DECISIONS.md`: decisions that override or fill gaps in the blueprint.
3. `BlindSpotScanner-Blueprint/`: the product spec. Read `docs/` in numeric order. These are
   normative: `contracts/` (report schema, OpenAPI), `prompts/` (the LLM steps), the milestone
   acceptance criteria in `docs/11-implementation-plan.md`. Its `CLAUDE.md` is reference only.

When a requirement is genuinely open, ask instead of assuming, and record the answer in
`docs/DECISIONS.md`.

## Layout
```
src/
  BlindSpotScanner.Domain/          entities, value objects, enums (no dependencies)
  BlindSpotScanner.Application/     use cases, ports, ReportAssembler/Validator, RunComparer
  BlindSpotScanner.Infrastructure/  EF Core, GitHub/Jira/Claude clients, extractors, SecretScrubber
  BlindSpotScanner.Api/             controllers, SignalR hub, auth, DI
  BlindSpotScanner.Worker/          BackgroundService host for scans
  web/                              Angular app (standalone components)
tests/
  BlindSpotScanner.Domain.Tests/
  BlindSpotScanner.Application.Tests/
  BlindSpotScanner.Infrastructure.Tests/   Infrastructure units + Testcontainers persistence (D12)
  BlindSpotScanner.Api.IntegrationTests/   HTTP-level tests
  fixtures/bookstore/                      synthetic mini-repo read as text by the M4 extractors (never compiled)
BlindSpotScanner.slnx
global.json                         pins the .NET SDK (10.0.102, latestFeature)
dotnet-tools.json                   local tools (dotnet-ef); run `dotnet tool restore`
Directory.Build.props               Nullable, ImplicitUsings, TreatWarningsAsErrors for all projects
docker-compose.yml                  db, api, worker, web
.env.example                        copy to .env (gitignored) before docker compose
.github/workflows/ci.yml            backend, frontend and docker build jobs
```
The Api and Worker each have a `Dockerfile` (build context: repo root). `src/web` has its own
`Dockerfile` and `nginx.conf` (serves the SPA, proxies `/api` and `/hubs` to the api).

## Commands
| Task | Backend (repo root) | Frontend (`src/web`) |
|---|---|---|
| Restore/install | `dotnet restore` | `npm ci` |
| Build | `dotnet build` | `npx ng build` |
| Test | `dotnet test` (Infrastructure and Api integration tests need Docker running) | `npx ng test --watch=false` |
| E2E | | `npx playwright test` (first run: `npx playwright install chromium`; API and hub are mocked from `examples/`) |
| Migrations | `dotnet tool restore`, then `dotnet ef migrations add <Name> --project src/BlindSpotScanner.Infrastructure --output-dir Persistence/Migrations` | |
| Run | `dotnet run --project src/BlindSpotScanner.Api` and `--project src/BlindSpotScanner.Worker` | `npx ng serve` |
| Lint/format | `dotnet format --verify-no-changes` | `npx ng lint` |
| Everything | copy `.env.example` to `.env`, then `docker compose up --build` (web: http://localhost:8080, api: http://localhost:5080/health) | |

Run backend and frontend tests before saying a change is done.

## Product principles (non-negotiable, enforce in code)
1. **Evidence integrity.** Every `covered`/`weak` grade cites `path/File.ext::TestName` from a file
   the pipeline actually fetched. `CitationVerifier` re-checks each one at the pinned SHA; an
   unresolved citation fails the run. Never report `none` when the truth is `unverified`.
2. **The summary is computed, never authored.** The LLM returns only per-item detail.
   `ReportAssembler` (plain C#) derives every headline number; `ReportValidator` re-derives them
   and fails on any mismatch.
3. **Denominators come from production code** (parsed controllers/routes), never the test folder.
4. **Counts come from the Git Trees API** (recursive), never code search.
5. **Unscorable means N/A, not a guess.** The score always carries "% of weight assessable".
6. **Measurements are labelled** (`integrationMappedTo`). Runs measured under different
   definitions are flagged as not comparable.
7. **Reports are immutable.** A re-scan creates a new run; deltas are computed by code.
8. **Outward-facing actions need a human click.** Jira tickets and PRs are created only from an
   explicit UI action after a preview, never during a scan.
9. **No secrets in code, logs, prompts or reports.** `SecretScrubber` runs before every LLM call.

All LLM calls go through one `ILlmClient` with structured JSON output validated against a
schema (max 2 retries). Prompts load from `prompts/`, and their version is stored on the run.

## Development rules
- **TDD:** write a failing test first, then the minimal code to pass it, then refactor.
- **No speculative complexity:** no extra abstractions, interfaces, locks, caching or config
  switches without a real, present need. Something the blueprint specifies counts as a need only
  once its milestone is being built.
- Build milestone by milestone (`docs/11-implementation-plan.md`); a milestone is done only when
  its acceptance criteria pass.
- Look up Angular, ASP.NET Core, EF Core and library APIs with context7. They change between
  major versions; don't rely on memory.
- Never invent commands or packages that aren't in the generated project files.

## Backend (C#)
- Nullable reference types and `TreatWarningsAsErrors` on.
- Clean Architecture: `Domain` ← `Application` ← `Infrastructure` ← `Api`/`Worker`.
- Plain application services (no MediatR). FluentValidation, Serilog, OpenTelemetry.
- Controllers stay thin; logic lives in Application services; data access behind ports.
- Async all the way down (`async`/`await`, pass `CancellationToken`); no `.Result` or `.Wait()`.
- Use built-in DI; register services in `Program.cs` (or extension methods once it grows).
- Naming: PascalCase types and members, `_camelCase` private fields, `Async` suffix on async methods.

## Frontend (Angular)
- Standalone components, signals for local state, `inject()` for DI. Angular Material.
- Strict TypeScript; no `any` without a comment explaining why.
- One component per folder (`name.ts/.html/.scss/.spec.ts`, the Angular 2025 style; see D7).
- HTTP calls go through services, never directly from components.
- Visual target: `BlindSpotScanner-Blueprint/ui-prototype/index.html`.

## Tests
- Backend: xUnit + FluentAssertions. Integration tests use Testcontainers (PostgreSQL) and
  WireMock.Net for GitHub, Jira and Claude. Name tests `Method_Scenario_ExpectedResult`.
- Frontend: the Angular default runner, `*.spec.ts` next to the code; Playwright for E2E.
- Use the blueprint's `examples/` (synthetic data) as fixtures.
- Use `// Arrange`, `// Act`, `// Assert` comments.
- One behavior per test; no branching (`if`/loops) inside tests.

## Before every commit (self-review)
1. Re-read the full diff.
2. Grep for stale names after any rename.
3. Run build, lint and tests for whichever side changed.
4. Check `git status` for files that shouldn't be committed.

## Git
- Branches: `feature/<short-name>`, `fix/<short-name>`.
- Commit messages: Conventional Commits, e.g. `feat(api): add order endpoint`, `fix(web): handle empty list`. Imperative, ≤72-char subject.
- Small, focused commits. Commit only when asked; ask before pushing.

## Automated review comments
- Verify each comment against the code before applying it.
- Reply on the thread only for false positives, explaining why.
