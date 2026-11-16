# 06 — Scoring: coverage legend + integration quality rubric

## Coverage legend (per endpoint and per controller)

| Status | Icon | Meaning |
|---|---|---|
| `covered` | ✅ | A happy-path test with **meaningful value assertions** (response body values, persisted state) |
| `weak` | ⚠️ | Exercised but status/shape-only, negative-only (401/400 only), or partial |
| `skipped` | ⏭️ | The test exists but is skipped or disabled |
| `none` | ❌ | All integration tests were enumerated and **confirmed** none target it |
| `unverified` | ❔ | Not fully read (budget/size). An honest "don't know". |

Controller status rollup (in code):
- `covered` if all of its endpoints are covered
- `none` if all are none
- `unverified` if any endpoint is unverified and none are covered or weak
- otherwise `weak`

The endpoint-level counts are the headline. The controller rollup is secondary.

## Quality rubric (7 dimensions)

| # | Dimension | Measures | Weight |
|---|---|---|---|
| 1 | Integration-point coverage | How many distinct real seams (DB, filesystem, network, HTTP pipeline, message broker) are crossed **for real** vs. mocked | 0.20 |
| 2 | Critical-path coverage | Do whole business journeys run end-to-end in one test | 0.20 |
| 3 | Failure & edge paths | Timeouts, retries, rollback, malformed payloads, constraint violations, auth failures | 0.15 |
| 4 | Environmental fidelity | How realistic each dependency is (Testcontainers / real broker vs. in-memory fakes) | 0.15 |
| 5 | Assertion strength | Asserts persisted state and field values vs. stopping at HTTP 200 | 0.15 |
| 6 | Determinism / reliability | Isolation, data cleanup, no sleeps or ordering dependence, flakiness signals | 0.10 |
| 7 | Diagnosability / maintainability | Clear failure messages, shared fixtures, teardown | 0.05 |

Do not double-count:
- #1 is *breadth* (did the test cross the seam)
- #4 is *fidelity* (how real the dependency was)
- #2 is *chain completeness* (did the whole journey run)

### Band anchors

- **1–2:** theatrical; everything mocked, or nothing asserted.
- **3–4:** the happy path of a few flows; real dependencies are rare.
- **5–6:** main flows run against real dependencies; failure paths are thin.
- **7–8:** critical and most failure paths; strong state assertions; stable.
- **9–10:** near-complete seam and failure coverage; contract checks; traceable.

### Calibration example (dim 5)

```csharp
var resp = await client.PostAsync("/orders", payload);
resp.StatusCode.Should().Be(HttpStatusCode.OK);
(await db.Orders.CountAsync()).Should().Be(1);
```

This scores **4**: a row was written, but its values are never asserted. A 7+ asserts that the
persisted fields match the request.

## Rollup (computed in `ReportAssembler`, never by the LLM)

```
scored    = dimensions.Where(d => d.Scorable)
score     = Σ(score_i × weight_i) / Σ(weight_i)   over scored   (null if none scored)
assessable= Σ(weight_i over scored) / Σ(all weights) × 100
display   = $"{score:0.0} / 10 across scored dimensions ({assessable:0}% of weight assessable)"
```

An unscorable dimension is `scorable:false, score:null`, labelled "N/A — insufficient input".
Do not renormalize silently: the `assessable` percentage always rides with the score. Guard
against division by zero: if no dimension is scored, the score is `null` and is displayed as
"Not scorable".

## Verdict ladder (manager bullet)

The verdict is based on the covered share `covered / (total − unverified)`, with `weak` counted
as 0.5:

| Share | Verdict |
|---|---|
| ≥ 0.80 | strong |
| ≥ 0.60 | moderate |
| ≥ 0.35 | partial |
| below 0.35 | weak |

If `unverified / total > 0.25`, append "(low confidence: many endpoints unverified)".

## Labelling (`integrationMappedTo`)

`real-seam` (default) | `e2e-mocked-data` | `e2e-real-backend` | `contract` | `other`. If the
value is not `real-seam`, the score label must state that it is not comparable to a real-seam
score. Dimension #1 is still scored honestly: near zero for mocked backends.

## Findings to always hunt

- **False positive:** the request goes through the whole stack, but only the status code is
  asserted. List every instance by `path::Test`.
- **Uncovered real seam:** a dependency used in production (DB, queue, external HTTP) that no
  integration test crosses for real. This is `coveredByNoneRealSeam` and should usually be
  `topRisks[0]`.
- **Skipped or disabled tests.**
