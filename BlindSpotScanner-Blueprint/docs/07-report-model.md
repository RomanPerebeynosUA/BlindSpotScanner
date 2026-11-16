# 07 — Report model

The canonical artifact is one **immutable JSON document per run**, validated against
`contracts/integration-report.schema.json`. The UI, exports, comparison and remediation all read
from it. `examples/sample-report.json` is a complete synthetic instance.

## Top-level sections

| Section | Authored by | Notes |
|---|---|---|
| `meta` | code | schemaVersion, runId, repo {owner,name,url}, branch, commitSha, generatedAt, modelId, promptSetVersion, profileVersion, integrationMappedTo, costUsd |
| `inventory` | code | controllerFiles, endpointsTotal, testFilesTotal, integrationTestFiles, classification counts by kind, readInFull, unverifiedReasons |
| `endpointCoverage[]` | LLM (P5), checked by code | One row per controller with `endpoints[]` (method, route, status, verified, evidence[], notes) |
| `scorecard[]` | LLM (P6), checked by code | 7 dimensions: id, name, weight, score, scorable, confidence, justificationBullets[4], evidence[] |
| `findings[]` | LLM (P6) + code | kind, severity, stableKey, headline, whyItMatters, detail[], evidence[] |
| `topRisks[]` | LLM (P6) | Top 5. A plain-language headline (≤25 words) and whyItMatters (≤35 words) with no file paths or jargon; the specifics go in detail[]. `rank` = array order. |
| `summary` | **code only** | Computed projection (see below) |
| `delta` | **code only** | Present when a previous run exists (doc 08) |
| `validation` | code | `{ passed, method:"script", violations[], citationsChecked, citationsResolved }` |

## `summary`: always computed

```json
"summary": {
  "endpoints": { "total": 0, "covered": 0, "weak": 0, "skipped": 0, "none": 0, "unverified": 0 },
  "controllers": { "total": 0, "covered": 0, "weak": 0, "skipped": 0, "none": 0, "unverified": 0 },
  "qualityScore": 6.1, "weightAssessablePct": 90, "scoreLabel": "6.1 / 10 across scored dimensions (90% of weight assessable)",
  "integrationMappedTo": "real-seam",
  "falsePositiveCount": 0, "skippedTestCount": 0, "uncoveredRealSeams": 0,
  "verdict": "moderate",
  "bullets": [ { "label": "Overall", "text": "…" }, { "label": "Coverage", "text": "…" }, { "label": "Test quality", "text": "…" }, { "label": "Since last scan", "text": "…" } ],
  "previousRunId": null
}
```

The `bullets` are produced by templated C# from the numbers above, never by the LLM, so they can
be re-derived byte for byte. The v1 templates (invariant culture; dates `dd MMM yyyy`; score
`0.0`) are:

| Label | Template |
|---|---|
| Overall | `Integration testing is {verdict}: {covered} of {total} API operations are properly tested; {weak} more lightly tested.` |
| Coverage | `{none} operation(s) have no integration test; {unverified} could not be checked.` |
| Test quality | `Test quality scores {score} out of 10 ({pct}% of the rubric could be assessed); {falsePositiveCount} test(s) only check that the request succeeded.` If the score is null: `Test quality could not be scored.` |
| Since last scan | `Since {prevDate}: score {prev} → {curr}; {newlyCovered} operation(s) newly covered, {regressed} regressed.` On the first run: `First scan of this repository — no comparison yet.` If the runs are not comparable, append ` (not like-for-like — see comparability notes)`. |

## Validator checklist (`ReportValidator`)

1. The report conforms to the JSON Schema.
2. `summary.endpoints` equals a fresh tally of `endpointCoverage[].endpoints[].status`, and its
   total equals `inventory.endpointsTotal`.
3. The controller rollup matches the doc 06 rule.
4. The score and the assessable percentage re-derive from `scorecard`. Each weight is in (0,1]
   and the weights sum to 1.0.
5. Every covered or weak endpoint has `verified:true` and well-formed evidence. Each scorable
   dimension's 3rd justification bullet ("Evidence") contains `::`.
6. `topRisks` headlines contain no `/`, `::` or file extensions, and respect the length limits.
7. `bullets` re-derive exactly.
8. `delta` re-derives from (previous run, this run).
9. All citations resolved (from the CitationVerifier result).
10. `validation.method == "script"`. Any other value is rejected.

A failure leaves the run `Failed`, with violations stored and viewable. It is never published as
`Succeeded`.
