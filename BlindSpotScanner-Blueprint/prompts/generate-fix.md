# Task: generate an integration test that fixes a finding — v1.0.0

**Input:**
```json
{ "finding": { "kind": "UncoveredEndpoint|WeakTest|FalsePositive|SkippedTest", "stableKey": "…",
    "endpoint": { "method": "POST", "route": "api/orders/{id}/cancel" }, "detail": ["…"] },
  "controllerSource": { "path": "…", "content": "…" },
  "siblingTests": [ { "path": "…", "content": "…" } ],
  "fixtures": [ { "path": "…", "content": "…" } ],
  "testProjectFile": { "path": "…", "content": "…" },
  "targetFolder": "tests/Api.IntegrationTests/Orders/" }
```

**Rules:**
- Follow the sibling tests' conventions exactly: base class or fixture, client creation, data
  seeding, naming, assertion library, namespaces.
- Assert response **values** and **persisted state**, not just the status code. Add one cheap
  failure-path test (for example not-found or validation).
- For `WeakTest` / `FalsePositive`, **modify** the existing test by adding assertions. Do not
  rewrite unrelated code.
- For `SkippedTest`, remove the skip only if the reason is clearly obsolete. Otherwise return
  `files: []` and explain in `rationale`.
- Use only packages already referenced in `testProjectFile`. No production-code changes. No
  secrets, sleeps, or randomness without a seed.

**Output schema:**
```json
{ "files": [ { "path": "…", "action": "create|modify", "content": "…full file content…" } ],
  "rationale": "…", "assumptions": ["…"], "riskNotes": ["…"],
  "prTitle": "test: add integration test for POST api/orders/{id}/cancel",
  "prBody": "…markdown…" }
```
