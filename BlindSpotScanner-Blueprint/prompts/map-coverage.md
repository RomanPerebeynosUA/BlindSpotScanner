# Task: map integration tests → endpoints for ONE controller — v1.0.0

**Input:**
```json
{ "controller": "OrdersController", "routePrefix": "api/orders",
  "endpoints": [ { "id": "e1", "method": "POST", "route": "api/orders" } ],
  "allCandidateTestsIncluded": true,
  "tests": [ { "path": "tests/Api.IntegrationTests/OrdersTests.cs", "content": "…full file…" } ],
  "profileMarkdown": "…" }
```

**Rules:**
- Map by the **route and HTTP method actually invoked** (client calls, URL strings, route
  builders), not by file or class names.
- `covered`: the test invokes the endpoint on the happy path **and** asserts meaningful values
  (body fields or persisted state).
- `weak`: invoked, but it asserts only the status or shape, only negative cases (401/400/404),
  or only partially.
- `skipped`: the only tests that invoke it are skipped or disabled.
- `none`: only if `allCandidateTestsIncluded` is true and no test invokes it.
- `unverified`: otherwise. Also use it if a test is truncated or unclear.
- For each covered, weak or skipped endpoint, list **every** test that invokes it as evidence.
- Flag status-only tests as `falsePositive: true`.

**Output schema:**
```json
{ "endpoints": [ { "id": "e1", "status": "covered|weak|skipped|none|unverified",
  "evidence": ["path::TestName"], "falsePositive": false, "notes": "≤40 words" } ] }
```
