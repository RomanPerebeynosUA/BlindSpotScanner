# 04 — Frontend (Angular 19)

Standalone components, signals for state, Angular Material plus CDK, `@microsoft/signalr`,
ngx-charts or ECharts for the trend sparkline, and `diff2html` for diff preview. Lazy-loaded
feature routes.

## Routes

| Route | Page | Content |
|---|---|---|
| `/` | Repositories | Table of repos: last score, trend sparkline, last scan date, status chip. "Add repository" dialog (URL → validates via the API). |
| `/repos/:id` | Repo overview | Run history (date, SHA, score, covered/total), "Scan now", profile editor tab |
| `/repos/:id/profile` | Profile editor | Markdown editor with preview; "Auto-draft with AI"; version history |
| `/runs/:runId` | **Report** | See the layout below |
| `/runs/:runId/live` | Live scan | Phase stepper (P1…P7) with progress, log lines, Cancel |
| `/compare?base=&target=` | Compare | Two run pickers, comparability banner, KPI delta chips, change lists |
| `/settings` | Settings | GitHub, Jira and Claude connections (test button), model, cost cap, Jira defaults (project, issue type, labels) |

## Report page layout

1. **Header:** repo, branch, short SHA (links to GitHub), generated date, model and prompt
   version, `integrationMappedTo` badge, and a **Run picker** (◀ date ▶) plus "Compare with…".
2. **Summary card:** verdict chip (strong / moderate / partial / weak), plain-language bullets
   (Overall · Coverage · Test quality · Since last scan), and KPI tiles:
   - score `X.X/10` with "Y% of weight assessable"
   - endpoints covered/total
   - weak
   - untested
   - unverified
3. **Top risks:** an accordion. The headline and why-it-matters are shown; the detail bullets and
   evidence are expandable. Each risk has **Create Jira** / **Fix with PR** buttons.
4. **Endpoint coverage:**
   - a table grouped by controller, with status icons ✅ ⚠️ ⏭️ ❌ ❔
   - filters by status
   - search
   - row expansion shows the endpoints, evidence links (GitHub blob at SHA, `#L` anchors when
     known) and notes
   - the row action menu has Jira and PR
5. **Quality scorecard:** 7 rows with dimension, weight, score bar (or N/A) and confidence. The
   expansion shows the 4 justification bullets and the evidence.
6. **Findings:** false positives, skipped tests and uncovered seams, each with actions.
7. **Actions log:** tickets and PRs created from this repo, with their live status.

## Fix-with-PR dialog

- Step 1: "Generating…" (spinner and cost estimate).
- Step 2: a diff viewer of the proposed files, the rationale, and editable branch name, PR
  title and body.
- Step 3: Confirm, which opens the draft PR. Show the link and store it on the finding.

## Jira dialog

Pre-filled summary, description (Markdown → Atlassian Document Format on the server), project,
issue type, priority (mapped from severity), labels (`integration-coverage`), and links back to
the report and evidence. If the finding already has a ticket, show it and offer
"Create anyway" only behind a warning.

## State and services

- `ApiClient` is generated from `contracts/openapi.yaml` (openapi-generator `typescript-angular`).
- `ScanProgressService` wraps the SignalR connection and exposes a `signal<ScanProgress>`.
- `ReportStore` is per-route state with `computed()` filters.
- An HTTP interceptor adds auth and maps ProblemDetails to snackbars.
- Accessibility: status uses an icon plus text, never colour alone. Dark/light theme.
  Responsive down to 360px.
