# CLAUDE.md — rules for generating the Integration Coverage Analyzer

This folder is a **blueprint**, not code. Generate the application from it. Where this file
conflicts with anything else, this file wins. When a requirement is genuinely open, ask the
user instead of assuming, and record the decision in `docs/DECISIONS.md` (create it).

## Confirmed decisions

- Backend: **.NET 9**, ASP.NET Core Web API + a separate **Worker** (BackgroundService) for scans.
- Frontend: **Angular 19** (standalone components, signals, Angular Material).
- Database: **PostgreSQL 16** via EF Core. Docker Compose for local and default deployment.
- Analysis engine: **deterministic .NET code** for enumeration, math, validation and
  comparison, plus the **Claude API** for judgment (classifying tests, mapping tests to
  endpoints, grading quality, drafting fixes). The default model is the latest Claude Sonnet,
  and it is configurable.
- Repo access goes through the **GitHub REST API** (a GitHub App or a PAT). There are no local
  clones during analysis.
- Ticketing: **Jira Cloud REST v3**. PRs: GitHub REST.
- Scope: **integration tests only** for v1.

## Non-negotiable principles (carry these into code, not just docs)

1. **Evidence integrity.** The app never fabricates.
   - Every covered or weak grade cites `path/to/File.ext::TestName`, a file the pipeline
     actually fetched.
   - A dedicated **CitationVerifier** re-fetches each cited file at the scan's pinned commit and
     confirms the test exists. A report with an unresolved citation cannot be published.
   - The app distinguishes `none` (enumerated and confirmed absent) from `unverified` (not read).
     It never reports `none` when `unverified` is the truth.
2. **The summary is computed, never authored.**
   - The LLM produces only per-item detail (per-endpoint grades, per-dimension scores with
     evidence).
   - `ReportAssembler` (plain C#) derives every headline number from that detail.
   - `ReportValidator` re-derives them and fails on any mismatch. The LLM never writes a total.
3. **Denominators come from production code.** The endpoint list comes from parsing
   controllers/routes in production code, never from the test folder.
4. **Counts come from the Git Trees API** (recursive), never from code search, which
   under-returns.
5. **Unscorable means N/A, not a guess.** The quality score is reported as
   `X.X / 10 across scored dimensions (Y% of weight assessable)`.
6. **Measurements are labelled.** Every score carries `integrationMappedTo` (for example
   `real-seam` or `e2e-mocked-data`). Two runs measured under different definitions are flagged
   as not comparable.
7. **Reports are immutable.** A re-scan creates a new run. The delta between runs is computed by
   code and stored, never claimed by the LLM.
8. **Outward-facing actions need a human click.** Jira tickets and PRs are created only from an
   explicit user action in the UI, after a preview. Never auto-create them in a scan.
9. **No secrets in code, logs, prompts or reports.** Tokens live in user secrets, env vars or a
   vault. Strip secrets from any file content before sending it to the LLM (see doc 10).

## Coding conventions

- Clean Architecture: `Domain` ← `Application` ← `Infrastructure` ← `Api`/`Worker`. Domain has no
  dependencies.
- Use MediatR-style command/query handlers, or plain application services. FluentValidation.
  Serilog. OpenTelemetry.
- All LLM calls go through one `ILlmClient` and use **structured output**: JSON validated against
  a schema, with retry on a schema failure (max 2). Prompts are loaded from `prompts/` and their
  version is stored on the run.
- Tests:
  - xUnit + FluentAssertions + Testcontainers (PostgreSQL) for the backend.
  - Jest or Vitest + Playwright for the frontend.
  - Mock GitHub, Jira and Claude through WireMock.Net or fakes. Use `examples/` as fixtures.
  - Ironically, this app's own integration tests should score well on its own rubric.
- Never invent commands or packages that are not in the generated project files.
