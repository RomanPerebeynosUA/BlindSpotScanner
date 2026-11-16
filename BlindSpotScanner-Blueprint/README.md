# Integration Coverage Analyzer — Blueprint

The architecture and specification for a web app that **audits a GitHub repository's
integration tests**. Claude Code should use this blueprint to generate the application.

| | |
|---|---|
| **Input** | A GitHub repository URL (plus an optional branch and repo profile) |
| **Output** | A detailed, evidence-backed integration-coverage report with a quality score (0–10) |
| **Actions** | Create a Jira ticket for a gap · open a Pull Request with a generated fix test |
| **History** | Every scan is immutable · any two scans of a repo can be compared (delta) |
| **Stack** | .NET 9 (ASP.NET Core Web API + worker) · Angular 19 · PostgreSQL · Docker Compose · Claude API |

Scope for v1: **integration tests only**. Unit, component and E2E tests are detected only so
they can be *excluded* from the integration numerator. They are not scored.

## How to use this folder (for Claude Code)

1. Read `CLAUDE.md` first. It holds the non-negotiable rules.
2. Read `docs/` in numeric order.
3. Build in the order in `docs/11-implementation-plan.md`. Each milestone has acceptance
   criteria, and a milestone is not done until they pass.
4. The contracts are normative:
   - `contracts/integration-report.schema.json` (the report)
   - `contracts/openapi.yaml` (the HTTP API)
   - `prompts/*.md` (the LLM steps)
5. `examples/` holds **synthetic** data (a fictional "Bookstore API"). Use it for tests and UI
   fixtures.

## Folder map

```
CLAUDE.md                         rules for the generating agent
docs/01-product-vision.md         idea, personas, features, user flows
docs/02-architecture.md           system context, containers, components, sequence diagrams
docs/03-backend-dotnet.md         solution layout, domain model, services, persistence
docs/04-frontend-angular.md       routes, pages, components, state, UX
docs/05-scan-pipeline.md          the analysis pipeline (enumerate → map → grade → assemble → verify)
docs/06-scoring-rubric.md         7-dimension integration quality rubric + coverage legend
docs/07-report-model.md           report structure; summary-is-computed rule
docs/08-run-comparison.md         run store + old-vs-new delta + comparability
docs/09-jira-and-pull-requests.md remediation actions: Jira ticket + fix PR
docs/10-security-config-ops.md    secrets, auth, config, deployment, observability
docs/11-implementation-plan.md    milestones with acceptance criteria
contracts/                        JSON Schema, OpenAPI
prompts/                          versioned LLM prompt templates
profiles/_template.md             per-repo profile ("how integration tests look here")
examples/                         synthetic report + delta
ui-prototype/index.html           clickable UI mockup — the visual target for the Angular app
```
