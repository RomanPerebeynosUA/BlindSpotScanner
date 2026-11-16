# Task: grade integration-test quality — v1.0.0

**Input:**
```json
{ "integrationMappedTo": "…", "profileMarkdown": "…",
  "productionSeams": ["PostgreSQL", "…"],
  "tests": [ { "path": "…", "content": "…" } ],
  "fixtures": [ { "path": "…", "content": "…" } ],
  "coverageSummaryForContext": { "endpointsTotal": 0, "none": ["POST api/orders/{id}/cancel"] } }
```

Score each of the 7 dimensions (1–10, or `scorable:false` with `score:null` when the input is
insufficient). **Do not guess a number.** Use the rubric and band anchors in doc 06 (they are
embedded at runtime).

Use `integrationMappedTo` to decide how honestly to score dimension #1. If the value is not
`real-seam`, dimension #1 is near zero for mocked backends.

Each dimension has exactly 4 `justificationBullets`:
1. `What is tested: …`
2. `What is missing: …`
3. `Evidence: path::TestName[, …]` (required when scorable)
4. `Why this score: …` (reference the band)

Also return the findings and the top risks.
- **falsePositives:** every status-only test.
- **skippedTests**
- **uncoveredSeams:** production seams that no test crosses for real.
- **topRisks** (≤5), in plain language:
  - `headline` ≤25 words and `whyItMatters` ≤35 words, with no file paths, no `::` and no jargon
    (say "tests that only check the request succeeded" instead of "status-only assertions")
  - the specifics go in `detail[]` (1–8 bullets)
  - the evidence goes in `evidence[]`
  - put the most important uncovered real seam first

**Output schema:**
```json
{ "scorecard": [ { "id": 1, "score": 6, "scorable": true, "confidence": "High|Med|Low",
    "justificationBullets": ["What is tested: …","What is missing: …","Evidence: …","Why this score: …"],
    "evidence": ["path::Test"] } ],
  "findings": [ { "kind": "FalsePositive|SkippedTest|UncoveredSeam", "severity": "Critical|High|Medium|Low",
    "subject": "string used for the stable key", "headline": "…", "detail": ["…"], "evidence": ["path::Test"] } ],
  "topRisks": [ { "headline": "…", "whyItMatters": "…", "severity": "…", "detail": ["…"], "evidence": ["…"] } ] }
```
