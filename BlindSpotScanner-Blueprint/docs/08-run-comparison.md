# 08 — Run store and old vs. new comparison

## Run store

- Every scan is a new immutable `ScanRun` with its own report JSON. Nothing is overwritten.
  "Latest" = the newest `generatedAt` with status `Succeeded` for (repo, branch).
- `previousRunId` = the latest succeeded run of the same repo and branch before this one. It is
  stamped at assembly time.

## Delta (computed by `RunComparer`, a pure function)

```json
"delta": {
  "baseRunId": "…", "baseCommitSha": "…", "baseGeneratedAt": "…",
  "comparable": true,
  "comparability": [ "endpoint denominator changed: 42 → 45", "integrationMappedTo changed", "profile version changed 3 → 4", "schema version changed", "model or prompt set changed", "branch differs" ],
  "metrics": [ { "name": "qualityScore", "prev": 5.4, "curr": 6.1, "change": 0.7, "direction": "improved|regressed|unchanged" } ],
  "endpoints": {
    "newlyCovered": ["POST /orders"], "regressed": ["GET /orders/{id}"],
    "added": ["DELETE /orders/{id}"], "removed": []
  },
  "scorecard": [ { "dimension": "Assertion strength", "prev": 4, "curr": 6, "change": 2 } ],
  "findings": { "fixed": ["false-positive:tests/Api/OrdersTests.cs::Create_returns_ok"], "new": [] },
  "risks": { "dropped": ["…exact headline…"], "new": ["…"] }
}
```

Rules:
- **Polarity-aware direction.** A higher score or more covered endpoints is `improved`. More
  `none` / `weak` / `unverified` / false positives is `regressed`.
- **Endpoint identity** = `METHOD + normalized route` (lower-cased, `{param}` names normalized
  to `{}`).
- **Findings** are matched on `stableKey`. **Risks** are matched on the **exact headline**: a
  reworded risk shows as dropped + new, never a fuzzy "resolved".
- `regressed` covers covered→weak/none/skipped and weak→none/skipped. `unverified` transitions
  are listed separately as "confidence changed", not as regressions.
- **Comparability:** every reason the runs are not like-for-like goes into `comparability[]`.
  `comparable=false` if `integrationMappedTo` differs or the denominator moved by more than 20%.
  The UI shows a warning banner and does not colour non-comparable deltas as progress.
- The validator re-derives the stored delta from both reports. A mismatch fails the run.

## Ad-hoc compare (any two runs)

`GET /api/runs/compare?base=&target=` runs the same `RunComparer` on demand, so the UI never
computes deltas itself. Adjacent runs return the stored delta. Non-adjacent pairs are computed
live and labelled "ad-hoc comparison".

## UI

- KPI tiles show delta chips (▲ +0.7, ▼ −2).
- A "Changes since <date>" section lists newly covered, regressed, fixed and new risks.
- The repo page has a trend sparkline of score and covered share across runs.
- The **"Since last scan"** manager bullet is templated from the delta, for example:
  *"Since 12 Oct: score 5.4 → 6.1; 3 endpoints newly covered, 1 regressed."*
