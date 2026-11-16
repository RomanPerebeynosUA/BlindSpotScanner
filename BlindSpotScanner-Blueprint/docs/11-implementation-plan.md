# 11 — Implementation plan (for Claude Code)

Build in order. Each milestone ends with **green tests** and its acceptance criteria met. Commit
per milestone.

## M0 — Scaffold
- `dotnet new` solution per doc 03. Angular workspace (`ng new web --standalone --routing --style=scss`).
  Docker Compose with db/api/worker/web. `.env.example`.
- CI workflow: build, test and lint for both.
- **AC:** `docker compose up` serves an empty Angular shell, and `/health` returns 200.

## M1 — Domain, persistence, contracts
- Entities, enums and EF Core mappings; an immutability trigger on `report_json`.
- Copy `contracts/integration-report.schema.json` and generate C# DTOs (or hand-write them and
  validate with NJsonSchema).
- **AC:** a round-trip of `examples/sample-report.json` through the DB is byte-equivalent and
  passes schema validation.

## M2 — Pure core: Assembler, Validator, Comparer (no I/O)
- Implement docs 06, 07 and 08 as pure functions.
- **AC:**
  - golden tests: assembling the detail of `sample-report.json` reproduces its `summary` exactly
  - `RunComparer(sample-report-previous, sample-report)` reproduces the `delta` embedded in `examples/sample-report.json`
  - mutation tests where tampering with any summary number makes the validator fail

## M3 — GitHub access and enumeration
- `GitHubClient` (PAT first, then App), tree walk with truncation handling, blob cache, URL
  parser.
- **AC:** with WireMock fixtures, P1–P2 produce the inventory counts. Rate-limit backoff is
  tested.

## M4 — Endpoint extraction and test classification
- Roslyn extractor for ASP.NET Core (attribute routing and minimal APIs); rule-based classifier
  from the profile.
- **AC:** a fixture mini-repo (create `tests/fixtures/bookstore/`) yields the expected endpoints
  and integration tests.

## M5 — LLM client and judgment phases
- `ILlmClient` with structured output, schema retry, caching, cost tracking and SecretScrubber.
  Prompts are loaded from `prompts/`.
- P4 (ambiguous classification), P5 (mapping) and P6 (grading).
- **AC:**
  - with a fake LLM returning canned JSON, the full pipeline produces a valid report
  - one opt-in live test (env-gated) runs against a small public sample repo

## M6 — Citation verifier, orchestrator, worker, progress
- `ScanOrchestrator`, the DB queue, the worker host, SignalR progress, cancel, and budget caps.
- **AC:**
  - an end-to-end API integration test: POST scan → worker runs with fake GitHub/LLM → report
    stored → GET returns it
  - a forged citation makes the run `Failed`

## M7 — Angular UI: repos, live scan, report
- Pages from doc 04, except compare and remediation.
- **AC:** Playwright tests using the API mocked from examples cover add repo → scan → live
  progress → report with filters and drill-down.

## M8 — Comparison
- The compare endpoint, run picker, delta chips, changes section, trend sparkline and
  comparability banner.
- **AC:** two example runs show the expected newly covered and regressed items, and the
  non-comparable banner works.

## M9 — Jira
- The Jira client, preview/create, dedup by stable key, and status refresh.
- **AC:** WireMock Jira tests assert the exact REST payload (ADF), and dedup prevents a second
  ticket.

## M10 — Fix PR
- Context gathering, the generate-fix prompt, the static guard, diff preview, and the Git Data
  API commit plus draft PR.
- **AC:**
  - WireMock GitHub asserts the sequence branch → blobs → tree → commit → ref → PR (draft)
  - the guard rejects a production-code edit
  - read-only mode hides the button

## M11 — Hardening
- OIDC auth and roles, secret redaction tests, OpenTelemetry, export (JSON plus printable HTML),
  docs.
- **AC:** a security checklist in doc 10 is verified by tests where possible.

## Definition of done (whole product)

- [ ] A public C# sample repo scans end-to-end and produces a validated report with resolved
  citations.
- [ ] A re-scan produces a second run and a correct delta.
- [ ] A Jira ticket and a draft PR can be created from a finding, and both links show on the
  report.
- [ ] No secrets in logs, prompts or reports, verified by tests.
