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

## D15 — Nothing assessable: honest wording, weak verdict (2026-11-16)
The schema's verdict enum has no "unknown" value, so a report with zero endpoints, or with every
endpoint unverified, keeps `verdict: weak`. Its Overall bullet doesn't claim weakness: it says
"Integration testing could not be assessed: …" (principle 1: never present unverified as a
finding). Scorecard weights must sum to 1 within 0.0001, so weights like thirds can pass.

## D16 — GitHub access: typed HttpClient + PAT (2026-11-16)
The user's choice: a small typed `GitHubClient` (four REST endpoints) instead of Octokit, with a
personal access token (`GitHub:Token`, from user secrets or an env var; never in appsettings).
GitHub App auth arrives in M11. `GitHubRateLimitHandler` waits on `Retry-After` or
`x-ratelimit-reset` (when `x-ratelimit-remaining` is 0) up to `GitHub:MaxRateLimitWait` (60s),
retries at most 3 times, and otherwise fails with the reset time; any other 403 fails at once.
`tests/BlindSpotScanner.Infrastructure.Tests` suppresses NU1608 because WireMock.Net's Handlebars
helpers pin Humanizer 2.14.1 while JsonSchema.Net needs Humanizer.Core 3.x (test project only).
Waits are measured against the response's `Date` header (not the local clock). A 429 without
headers, or a 403 whose body names a "secondary rate limit", waits one minute (GitHub's advice).

## D17 — Enumeration excludes are fixed, not per profile (2026-11-17)
The user's choice: profiles don't carry exclude globs, so the `Exclude` line was dropped from
`profiles/_template.md`. `EnumeratePhase` always excludes `bin`, `obj`, `node_modules`, `vendor`,
`third_party`, `dist`, `wwwroot/lib`, `Generated`/`generated`, `*.Designer.cs` and `*.g.cs`.
Symlinks (mode 120000) are not counted. Blob text is UTF-8 without its BOM; content that isn't
valid UTF-8 decodes as Latin-1.

## D18 — Profile classification rules are typed columns (2026-11-17)
The user's choice: P4's rules live in `string[]` columns on `repo_profiles`, not parsed from the
profile Markdown. `MinimalApiGlobs` (files with minimal API routes; they join `ControllerGlobs` as
the denominator source), `IntegrationGlobs` (folders and naming), `IntegrationAttributes` (written
without brackets, e.g. `Trait("Category","Integration")`; an entry without `(` matches by name),
`IntegrationBaseTypes` (an entry without `<` matches any generic arity) and `NotIntegrationGlobs`
(never integration, even inside an integration folder). All default to empty.

## D19 — Endpoint extraction scope (2026-11-17)
`AspNetCoreEndpointExtractor` reads C# syntax with Roslyn (no compilation). Supported: attribute
routing (every class `[Route]` × every verb template or method `[Route]`, `[controller]`/`[action]`
tokens, absolute `/` and `~/` templates) and minimal APIs (`MapGet`/`MapPost`/`MapPut`/`MapPatch`/
`MapDelete`, with `MapGroup` prefixes through call chains and the nearest in-scope initializer or
assignment). `[action]` honours `[ActionName]` and ASP.NET's trimmed `Async` suffix; `[area]` takes
the class's `[Area]`. Controllers follow ASP.NET's rule: public, not abstract or static, not
`[NonController]`. Not supported: conventional routing, and `Map`/`MapMethods`/`MapFallback`
registrations (reported). These become unverified reasons, so nothing
is dropped silently: a non-literal route; a controller with no attribute-routed actions; a partial
controller or one deriving from a custom base (an inherited or other-file `[Route]` isn't read); an
action routed without an `Http*` verb; a minimal API whose group prefix comes from a parameter or
field this file doesn't resolve; any non-`.cs` file. In P4, non-`.cs` test files and files whose
tests may be inherited from a class base go to Ambiguous (the LLM step), not Other. Derived test
attributes (`[SkippableFact]`, `[IntegrationTheory]`) count as tests. A file's minimal APIs form one
controller row named after the file. Roslyn is pinned to 5.0.0 to match EF Core Design's exact dependency.

## D20 — LLM client and judgment phases (2026-11-17)
The user's choices: the official Anthropic SDK (`Anthropic` NuGet); default model
`claude-sonnet-5-5` (`Llm:Model`, doc 10's "latest Sonnet"); the live test runs only when
`ANTHROPIC_API_KEY` and `BSS_LIVE_REPO_URL` are set. `Llm` defaults: `MaxTokensPerCall` 8000,
`PromptSetVersion` 1.0.0, $2 / $10 per million input/output tokens. Every call: scrubbed input,
structured JSON output, a schema check plus the phase's rule check, and at most 2 re-asks with the
errors; a reply cut off (`max_tokens`) or refused counts as a failed attempt. Server-side refusal
fallback (`fallbacks: "default"`) is on. Accepted answers are cached in `llm_cache` by a hash of
prompt, prompt version, model, schema and input. The scrubber runs on each string value of the
input before it is serialized for the prompt. An API error after the SDK's retries is a failed call
(items become unverified), never a crashed scan. P5 sends a controller the integration tests that
mention any literal segment of its routes (ignoring `api` and version segments; a root route sends
every test). Because that filter is a heuristic, a controller with no candidate tests is `none`
only when the repository has no integration tests at all, otherwise `unverified`.
`allCandidateTestsIncluded` holds only when no test file is still ambiguous or withheld (denied
path, over `Scan:MaxFileBytes`) and P2 reported nothing unverified.
Evidence that doesn't name a test method of a sent file makes the endpoint `unverified`. P6 takes
dimension names and weights from code; without an answer all 7 dimensions are unscorable.
`productionSeams` is sent empty (the seams are in the profile Markdown). A refusal is not re-asked
(it comes after the whole fallback chain declined). Cost sums `usage.iterations` when present (all
attempts, priced at the configured rates). The cache key hashes the full system and task prompt
texts, so an edit without a version bump is a miss. P5 re-asks on missing, duplicate or unknown
endpoint ids and on unresolvable evidence. The scrubber also covers `ASIA` keys, `sk-ant-`/`sk-`,
Slack `xox?-`, Google `AIza`, Azure `AccountKey=`/`SharedAccessKey=`/`SharedAccessSignature=`,
`.WithPassword("…")` and JSON `"password": "…"`. The citation verifier, budgets, queue and DI
wiring are M6.

## D21 — Orchestrator, queue, worker and progress (2026-11-17)
The user's choice: repository and profile endpoints arrive in M7 with the pages that use them; M6
builds the scan/run endpoints only (tests seed repositories in the database). The OpenAPI contract
wins over doc 03: cancel is `POST /api/runs/{runId}/cancel` (202), plus `GET /api/runs/{runId}/steps`.
The queue lives on `scan_runs` (`queued_at`, `attempts`, `leased_until`, `cancel_requested`,
`failure_reasons`, `lease_owner`): `FOR UPDATE SKIP LOCKED` dequeue with a fresh lease token, a
heartbeat (which survives transient errors) extends the lease only for its owner, an expired lease
is retried once, then the run is Failed. `status` and `lease_owner` are EF concurrency tokens: a
save by a worker that lost its lease, or that the reaper or a cancel overtook, is dropped and
nothing is published; a cancel that races a dequeue re-reads the run and becomes a cancel request.
A run measures exactly what it was queued with: its profile version, and a worker whose model or
prompt set differs fails it up front. A cancelled queued run ends at once; a running one
stops at its next phase and ends Cancelled with no report; a host shutdown leaves it Running so the
lease retry picks it up. Progress travels by `pg_notify('scan_events')` and is relayed to SignalR
groups `run:{id}` on `/hubs/scans`. Citations: every `path::Name` in the report (evidence arrays and
scorecard evidence bullets) is re-read at the pinned commit; coverage evidence must name a test
method, other citations any method (fixtures are evidence). The cost cap (`Llm:MaxCostUsdPerScan`,
or the request's `maxCostUsd`) stops further LLM calls, records the reason, and the run still
succeeds; a call is not made when one more call as costly as the costliest so far would cross the
cap, and a run's spend is stored whatever its outcome. Lease times use the database clock. A retry
replaces the earlier attempt's steps; a phase that throws records a Failed step; the run is pinned
to its commit right after P1. An abandoned run whose cancel was requested ends Cancelled.
Progress is reported as each phase starts and ends (0…100). Events are best effort: a lost
`ScanCompleted` is possible, so clients also poll `GET /api/runs/{id}`. Citation paths must be
repository-relative (no root, `.` or `..`). Cache entries are detached once written, so one scan's
context doesn't grow with the repository. The api applies migrations at startup. New options: `Scan:LeaseSeconds` 120,
`Scan:HeartbeatSeconds` 30, `Scan:PollMilliseconds` 2000. `ScanWorker` lives in Infrastructure so
the end-to-end tests host it next to the api; the Worker project only hosts it. Configuration is
read when services resolve, so hosts and test factories can add sources after registration.

## D22 — Repositories, profiles and the web UI (2026-11-17)
The user's choices for M7: adding a repository saves profile v1 from template defaults, and the
profile page also offers "Auto-draft with AI" (`prompts/draft-profile.md`); the profile editor
edits the typed D18 columns and the Markdown; there is no settings page until M9 (connections stay
in configuration); the web app uses hand-written typed API services instead of openapi-generator
(the contract defines almost no response schemas).
- Profile v1 is `profiles/_template.md` with the repository's `owner/name`, `real-seam`, and the
  globs the M6 end-to-end test proved on the bookstore fixture: controllers
  `src/**/Controllers/**/*Controller.cs`, minimal APIs `src/**/Endpoints/**/*.cs`, tests
  `tests/**/*.cs`, integration `tests/*.IntegrationTests/**`, attribute
  `Trait("Category","Integration")`, base type `WebApplicationFactory`, never `**/*Fixture.cs`.
- The branch is the request's `defaultBranch`, else the URL's `/tree/{branch}`, else GitHub's
  default, and it must exist. Duplicates match on GitHub's canonical owner/name (a unique index),
  so URLs that differ by case, `.git` or `/tree/` find the registered repository (409 with its id).
- Contract additions: `RepositorySummary` gains `defaultBranch` and `activeRunId` (the newest
  queued or running run); `GET …/profile?version=n`, `GET …/profile/versions`, and `baseVersion` on
  `PUT …/profile` (409 when another save came first).
- The AI draft fills only the Markdown (D18: the typed columns are not parsed from it) and is never
  saved by itself; the user saves it. The template is appended to the draft prompt at runtime, as
  the prompt says.
- New endpoints return errors as ProblemDetails. Validation stays in the application services (as
  in M6), so FluentValidation is not added yet. No auth header until M11 (OIDC).

## D23 — Run comparison and trends (2026-11-17)
The user's choices for M8: the trend sparkline is a hand-drawn SVG (no chart library); it shows on
the repositories table and on the repository overview; ad-hoc compare always runs old → new.
- `GET /api/runs/compare?base=&target=` orders the two reports by `meta.generatedAt` (then run id)
  and compares older → newer; a reversed request returns `swapped: true`. Runs are adjacent when
  the newer report's stored `delta.baseRunId` is the older run: then the stored delta is returned
  (`adHoc: false`), otherwise `RunComparer` computes it live (`adHoc: true`).
- Response: `{ base, target, swapped, adHoc, delta }`; `base`/`target` are `{ runId, branch,
  commitSha, generatedAt, score }` and `delta` is written with `ReportJsonSerializer.Options`, so
  it reads exactly like a report's stored delta. 400 for the same run twice or runs of different
  repositories, 404 for an unknown run, 409 when either run has no report.
- Contract additions: `GET /api/runs/{runId}/repository` (the run's repository, for the report's
  run picker) and `RepositorySummary.trend` (the last 12 succeeded runs, oldest first: `runId`,
  `generatedAt`, `score`, `coveredShare`; an unscorable run keeps a null score).
- Non-comparable deltas (`comparable: false`) show a warning banner with the reasons and neutral,
  uncoloured chips (principle 6).

## D24 — Jira tickets (2026-11-17)
The user's choices for M9: the Jira connection comes from configuration only (`Jira:BaseUrl`,
`Jira:Email`, `Jira:ApiToken` from user secrets or env vars, `Jira:ProjectKey`, `Jira:IssueType`
default `Task`, `Jira:Labels` default `["integration-coverage"]`); the settings page moves to M11
with auth. "Create Jira" is offered on the Findings section and on untested endpoint rows only
(top risks, scorecard rows and bulk epics wait). Jira is tested with WireMock only; no live test.
- `findingId` in the API is the report's finding id (`f2`). Its `findings` row is created lazily
  when the first action needs it (unique on run + report finding id), so older runs work too.
- Dedup: a stored JiraTicket action for the same repository and `StableKey`, then JQL
  `labels = integration-coverage AND text ~ "\"<key with non-alphanumerics as spaces>\""` (a
  phrase search, first 50 hits). A search hit counts only when its description holds the footer
  `BlindSpotScanner key: <stableKey>` as one plain text node, which the server always appends
  outside the Markdown conversion (and `integration-coverage` is always among the labels), so a
  user's edits can't break dedup. A ticket found only in Jira is stored as an action (adopted).
  "Create anyway" (`force`) skips both checks.
- `CreatedBy` is `anonymous` until M11 auth. Jira not configured → 503 ProblemDetails naming the
  missing settings; a Jira error on create → 502 with Jira's message (never the token).
- Status refresh: `GET /api/repositories/{id}/actions` (a contract addition) re-reads statuses
  checked 10 or more minutes ago; a Jira failure keeps the last known status.
- `App:PublicUrl` (default `http://localhost:8080`) builds the report link in ticket descriptions.
- Concurrency: the finding row is written before Jira is called, and a concurrent insert of the
  same row reuses the winner's row, so a database failure never follows an outward create. Two
  creates for one finding at the same moment (two tabs or users) can still both reach Jira before
  either is stored; that race is accepted until M11 auth (no lock without a present need). Jira
  calls time out after 10 s, and every Jira failure surfaces as a Jira error (502 on create; the
  actions list keeps the cached status).

## D25 — Fix pull requests (2026-11-17)
The user's choices for M10: "Fix with PR" covers the single-test kinds: `UncoveredEndpoint` (a
new test), `WeakTest` / `FalsePositive` (strengthen the existing test) and `SkippedTest` (un-skip
if obvious, otherwise the LLM declines with `files: []`); `UncoveredSeam` and `Risk` stay
Jira-only. When a target file changed on the default branch since the scan, creating the PR is
refused (409 with the paths) and the dialog offers "Regenerate", so a billed call only follows a
click. GitHub is tested with WireMock only; no live test.
- Read-only mode: `Features:PullRequests` (default `true`) and a configured `GitHub:Token` are both
  needed; otherwise the fix endpoints return 403 and the UI hides the button. The token (D16 PAT)
  needs `contents:write` and `pull_requests:write`; a GitHub App waits for M11.
- Writes go through a separate port (`IGitHubPullRequests`), implemented by the same typed
  `GitHubClient`, so the scan's read-only `IGitHubClient` is unchanged.
- The guard allows `.cs` files only, inside the test project folder (the nearest `.csproj` above
  the sibling tests): project files can't change, so no new packages either. The server re-runs
  the guard on whatever the client submits.
- PR status: "Merged", "Closed", "Open", or "Open · checks passing|failing|pending" from the head
  commit's check runs, refreshed like Jira statuses (10 minutes).
- Contract additions: `GET /api/features` (`{ pullRequests, jira }`) for the UI; the fix-preview
  body (`files[{path, action, content, original}]`, rationale, assumptions, riskNotes, branch,
  title, body, costUsd, existing).
- Base commit: a first draft is generated from the scanned commit; "Regenerate" (`fix-preview?regenerate=true`)
  generates from the repository's default branch head. The draft carries that `baseSha`, and
  create compares the target files at `baseSha` with the default branch head, so a regenerated fix
  can be opened. The PR always goes against the default branch head (doc 09 step 5). The action is
  stored as soon as GitHub returns the PR; the `integration-coverage` label is best effort.

## D26 — Sign-in, roles and hardening (2026-11-17)
The user's choices for M11: sign-in is server-side OpenID Connect with an HttpOnly cookie session
(the browser never handles tokens); the settings page is a read-only status page (masked values,
"Test" buttons; values stay in env vars, nothing is stored in the database); the GitHub PAT stays
(a GitHub App is out of v1, superseding D16's M11 note).
- `Auth:Mode`: `Local` (default; every request is `local-admin` with Admin, for development and
  compose) or `Oidc` (`Auth:Authority`, `Auth:ClientId`, `Auth:ClientSecret` from an env var,
  `Auth:RoleClaim` default `roles`, `Auth:DefaultRole` default `Viewer`). The OIDC callback is
  `/api/auth/callback`, so nginx's `/api/` proxy covers it. A user gets the highest known role
  claimed; unknown values never elevate, and no role means the default.
- Policies: Viewer reads everything (the fallback for any endpoint, and the scan hub); Member also
  adds repositories, edits and drafts profiles, acknowledges LLM sharing, scans, cancels, and creates
  Jira tickets and PRs; Admin also gets `/api/settings/*`. API and hub calls get 401/403, never a
  login redirect. `/health` and `/api/auth/*` are anonymous.
- Contract additions: `GET /api/me` (`{ name, roles }`), `GET /api/auth/login?returnUrl=` (local
  paths only), `POST /api/auth/logout`.
- LLM data sharing (doc 10): `POST /api/repositories/{id}/llm-sharing` (Member, idempotent, keeps
  the first acknowledgement and who gave it); `POST …/scans` returns 409 "LLM data sharing not
  acknowledged" until then. Repositories registered before M11 need it once. `RepositoryResponse`
  and `RepositorySummary` carry `llmSharingAcknowledged`; `ActionResponse` carries `createdBy`.
- Settings: `POST /api/settings/connections/{kind}/test` answers 200 with `{ ok, message }` whether or not
  the connection works (the OpenAPI contract's 502 is reserved for the API itself failing); an unknown
  kind is 404. Secrets shorter than 12 characters are masked as "••••" only.
- Hardening after review: the cookie answers every challenge (only `/api/auth/login` challenges OIDC);
  OIDC redirect URIs come from `App:PublicUrl`; `returnUrl` must be local and free of control
  characters; unsafe `/api` and `/hubs` requests that browsers label `Sec-Fetch-Site: cross-site` or
  `same-site` are refused (CSRF); sign-out ends the provider session only when its metadata has an
  end-session endpoint; `CreatedBy` is the email (else subject, else name); compose binds its ports to
  127.0.0.1 and the api warns when Local mode runs outside Development; logging levels (not sinks) come
  from configuration, so nothing bypasses redaction; the failure metric's `reason` is a fixed category.

## D27 — Haiku 5.5 as the default scan model (2026-11-19)
The user's choice: scans default to `claude-haiku-5-5` (USD 0.10 / 0.50 per million tokens, prompts
up to 100K tokens) to keep the Sparkathon demo cheap; D20's Sonnet 5.5 stays a one-line switch.
Compose passes `LLM_MODEL`, `LLM_INPUT_USD_PER_MTOK` and `LLM_OUTPUT_USD_PER_MTOK` to both the api
(which queues a run with its model) and the worker (which refuses a run queued for another model).
Haiku has no server-side refusal fallback, so `ClaudeLlmClient` sends the fallback beta and
`fallbacks` only for non-Haiku models. Answer quality is still guarded by the schema check,
`CitationVerifier` and `ReportValidator`; borderline covered/weak grading is the known trade-off.

## D28 — Citations are checked where the LLM answers (2026-11-19)
A first live scan (TodoApp, Haiku 5.5) failed on four citations. P6 now checks its answer the way P5
already did: evidence in the scorecard, findings and risks must be `path::Name`, and every citation
(including those in the evidence bullet) must name a method declared in a test or fixture file the
model was given, so a slip is re-asked (max 2 retries) instead of failing the whole run at the end.
If the model keeps citing something that is not there, quality is not graded and the phase note says
why (principle 5); the bad citation never reaches the report. `CitationVerifier` stays the final gate
(it re-reads files at the pinned commit), and a citation in text no longer swallows a sentence's
closing period (`Tests.cs::Name.` cites `Name`).

## D29 — The AI draft proposes the profile rules (2026-11-19)
The user's choices after two empty live scans whose profile kept the template rules: "Auto-draft
with AI" now also returns `integrationMappedTo` and the seven typed rule lists (prompt
`draft-profile` v1.1.0, prompt set 1.1.0). This reverses D22's "the AI draft fills only the
Markdown"; D18 stands (rules are typed columns, never parsed from the Markdown) and the draft is still
saved only by the user. Glob entries must be one pattern each (no whitespace or comma), else the
answer is re-asked; globs that match no file of the default-branch tree (minus the fixed excludes)
are re-asked once in a second call with `zeroMatchPatterns`, and any still matching nothing become an
open question. The editor fills every field from the draft and shows per-pattern match counts, with a
"Check patterns" button (`POST …/profile/matches`) to recount after edits; a save with a malformed
pattern is refused (400).
