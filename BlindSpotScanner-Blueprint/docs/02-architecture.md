# 02 — Architecture

## System context

```mermaid
flowchart TB
  user([User: manager / lead / dev]) --> web[Angular SPA]
  web -->|REST + SignalR| api[.NET Web API]
  api --> db[(PostgreSQL)]
  api --> queue[[Scan job queue - DB table]]
  worker[.NET Scan Worker] --> queue
  worker --> db
  worker --> gh[GitHub REST API]
  worker --> llm[Claude API]
  api --> jira[Jira Cloud REST]
  api --> gh
  api --> llm
```

## Containers

| Container | Responsibility |
|---|---|
| **web** (Angular, served by nginx) | UI. It holds no secrets and talks only to `api`. |
| **api** (ASP.NET Core) | REST API (`contracts/openapi.yaml`); SignalR hub `/hubs/scans` for progress; auth; Jira/PR actions; enqueues scans |
| **worker** (.NET BackgroundService) | Runs the scan pipeline (doc 05). Dequeues jobs with `SELECT … FOR UPDATE SKIP LOCKED` and publishes progress (through PostgreSQL LISTEN/NOTIFY to the api, or Redis if added later). |
| **db** (PostgreSQL) | Repos, profiles, runs (report JSONB + indexed headline columns), scan steps, findings, actions, connection settings (encrypted) |

A DB-backed queue keeps the v1 infrastructure to three services. `IScanQueue` is an interface,
so a real broker can be swapped in later.

## Components (inside the backend)

```mermaid
flowchart LR
  subgraph Application
    ScanOrchestrator
    ReportAssembler
    ReportValidator
    RunComparer
    RemediationService
  end
  subgraph Infrastructure
    GitHubClient
    TreeEnumerator
    EndpointExtractor
    TestFileClassifier
    LlmClient
    CitationVerifier
    JiraClient
    SecretScrubber
    Repositories[EF Core repositories]
  end
  ScanOrchestrator --> TreeEnumerator --> GitHubClient
  ScanOrchestrator --> EndpointExtractor
  ScanOrchestrator --> TestFileClassifier
  ScanOrchestrator --> LlmClient
  ScanOrchestrator --> ReportAssembler --> ReportValidator
  ReportValidator --> CitationVerifier --> GitHubClient
  ScanOrchestrator --> RunComparer
  RemediationService --> JiraClient
  RemediationService --> GitHubClient
  RemediationService --> LlmClient
  LlmClient --> SecretScrubber
```

## Sequence — scan

```mermaid
sequenceDiagram
  participant U as Angular
  participant A as API
  participant Q as Queue(DB)
  participant W as Worker
  participant G as GitHub
  participant L as Claude
  U->>A: POST /repos/{id}/scans
  A->>Q: insert job (Queued)
  A-->>U: 202 {runId}
  U->>A: SignalR subscribe(runId)
  W->>Q: dequeue
  W->>G: resolve branch → commit SHA; GET tree?recursive=1
  W->>W: enumerate prod endpoints + candidate test files (deterministic)
  W->>G: fetch file contents (batched, cached by blob SHA)
  W->>L: classify tests / map tests→endpoints / grade (structured JSON)
  W->>W: assemble summary, validate, compare with previous run
  W->>G: verify every citation at pinned SHA
  W->>A: progress events per phase
  W->>Q: mark Succeeded (or Failed with reason)
  A-->>U: run completed → load report
```

## Sequence — fix PR

```mermaid
sequenceDiagram
  participant U as Angular
  participant A as API
  participant L as Claude
  participant G as GitHub
  U->>A: POST /runs/{runId}/findings/{fid}/fix-preview
  A->>G: fetch target controller + 1-3 sibling tests + fixtures (at run SHA)
  A->>L: generate test (structured: files[], rationale)
  A-->>U: diff preview
  U->>A: POST .../pull-request {approved diff}
  A->>G: create branch from default HEAD, commit files, open draft PR
  A-->>U: PR url (stored on finding)
```

## Cross-cutting

- **Idempotency:** GitHub file fetches are cached by blob SHA in the DB. LLM results are cached
  by a hash of (prompt version, model, input). Re-running a scan at the same SHA is cheap.
- **Budgets:** each scan has a max-token / max-cost cap. When the cap is hit, the remaining
  items become `unverified`. They are never guessed.
- **Rate limits:** the GitHub client honours `X-RateLimit-*` and retries with backoff (Polly).
  The LLM client honours 429/529 with backoff.
