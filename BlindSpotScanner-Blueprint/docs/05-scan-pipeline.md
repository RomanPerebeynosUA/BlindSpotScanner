# 05 — Scan pipeline (integration tests)

The pipeline is a fixed sequence of phases. **Deterministic phases are C#. Judgment phases
call the LLM** with a versioned prompt from `prompts/` and a JSON schema for the answer. Each
phase writes a `ScanStep` (status, duration, tokens, readInFull/sampled counts, notes).

| Phase | Kind | What it does | Output |
|---|---|---|---|
| **P1 Resolve** | code | Parse the URL (`https://github.com/{owner}/{repo}[/tree/{branch}]`), check access, resolve branch → **commit SHA** (all later reads are pinned to it), load the repo profile (or auto-draft one, see P1b) | `RunContext` |
| **P1b Profile draft** *(only if none)* | LLM | Reads the root tree plus build files and a few test files, and drafts `profiles/_template.md` | Profile, which the user should review |
| **P2 Enumerate** | code | Git Trees API `?recursive=1` (handle `truncated:true` by walking subtrees). Apply profile globs: **production controllers/route files** (the denominator source) and **candidate test files**. Exclude vendored, generated and `bin/obj/node_modules` paths. | `Inventory { controllers[], testFiles[] }` with counts |
| **P3 Endpoint extraction** | code (+LLM fallback) | Fetch each controller and extract `(controller, method, route)` with Roslyn for C#. Unknown frameworks go to the LLM prompt `extract-endpoints.md`, marked `inferred`. | **Denominator**: `endpoints[]` |
| **P4 Test classification** | code + LLM | Profile rules first: folder, naming, attributes (`[Trait("Category","Integration")]`), base classes (`WebApplicationFactory`, Testcontainers). Ambiguous files go to the LLM `classify-tests.md` → `integration` / `unit` / `component` / `e2e` / `other`, each with a reason. Only `integration` continues. | `integrationTests[]` |
| **P5 Coverage mapping** | LLM (batched per controller) | For each controller: give the LLM its endpoints plus the **full** text of the integration tests that plausibly target it (pre-filtered by route string / client-call grep). `map-coverage.md` returns a status and `evidence: path::TestName` per endpoint. Map by the **route actually exercised**, never by a similar file name. | `endpointCoverage[]` |
| **P6 Quality grading** | LLM | `grade-quality.md` scores the 7 dimensions (doc 06) using the integration tests plus a fixtures/setup summary and returns `justificationBullets[4]` and evidence. It also returns findings: false positives (status-only asserts), skipped tests, uncovered seams, and the top risks in plain language. | `scorecard[]`, `findings[]` |
| **P7 Assemble → Validate → Verify → Compare → Publish** | code | `ReportAssembler` computes the summary. `ReportValidator` re-derives and checks the rules. `CitationVerifier` checks every `path::Test` at the SHA. `RunComparer` builds the delta against the previous successful run of the same repo and branch. Then the report is stored immutably. | `Report` |

## Hard rules enforced in code

- **Every endpoint in the denominator gets exactly one status.** None may be dropped.
  `sum(legend) == endpoints.Count`.
- **`covered` / `weak`:**
  - requires `verified == true` and at least one evidence item matching
    `^[^:]+\.[A-Za-z0-9]+::[A-Za-z_][\w.]*$`
  - any citation that fails verification sets the run to **Failed (citation)**, with the list of
    failures. The run is not silently downgraded.
- `none` is allowed only if the mapper received **all** integration tests for that controller.
  Otherwise the endpoint is `unverified`.
- **Budget exhaustion:** items not yet processed become `unverified`, and the step notes say so.
- **Test reading:** in full, never sampled. If a file exceeds the context budget, split it by test
  method. If it is still too large, its endpoints become `unverified`.
- **Skipped tests count as `skipped`, not covered:** `[Fact(Skip=…)]`, `[Ignore]`, `.skip`,
  commented-out tests and CI-disabled suites.
- **Secret scrubbing** runs on all content before it goes to the LLM (doc 10).

## Prompt I/O pattern

Every LLM call:
1. uses a system prompt from `prompts/_system.md` and a task prompt
2. sends the input as JSON
3. requires the output as JSON matching a schema
4. validates the output with NJsonSchema, and on failure re-asks with the validation errors
   (max 2), then marks the affected items `unverified`
5. logs model, prompt version, token counts and cost to `ScanStep`

The LLM never receives the scoring arithmetic and never returns totals.

## Language support

v1 is first-class for **C# / ASP.NET Core** repos, because the app is built in .NET and Roslyn is
available. `IEndpointExtractor` and the test-attribute rules are strategy classes keyed by the
profile's `language`. Add Java/Spring, TypeScript/Express/Nest and Python/FastAPI as extractors
later. Until then they use the LLM fallback with `inferred` labels and a lower confidence.
