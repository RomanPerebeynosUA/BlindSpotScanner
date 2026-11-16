# 10 — Security, configuration, operations

## Authentication and authorization (the app itself)

- v1 uses OIDC login (any provider; configurable authority and client ID). Development mode can
  use a single local admin.
- Roles:
  - `Viewer`: reads reports
  - `Member`: also scans, and creates Jira tickets and PRs
  - `Admin`: also manages connections and settings
- Every remediation action records `CreatedBy`.

## Secrets

| Secret | Where |
|---|---|
| GitHub App private key / PAT | Env var or vault, or encrypted in the DB with ASP.NET Data Protection (key ring in a mounted volume) |
| Jira API token / OAuth tokens | Same |
| Claude API key | Env var `ANTHROPIC_API_KEY` or vault |
| DB connection string | Env var |

- Secrets are never returned by the API (masked as `••••last4`) and never logged. Serilog has a
  destructuring policy that redacts known keys.
- Never commit `appsettings.*.json` with real values. Provide `appsettings.Example.json` and
  `.env.example`.

## Sending repository code to the LLM

- `SecretScrubber` runs before every LLM call. It uses regexes for common key formats
  (AWS, GitHub, JWT, private-key blocks, connection strings, `password=`) and replaces matches
  with `«redacted»`.
- **Denylisted paths** are never sent: `.env*`, `*.pem`, `*.pfx`, `appsettings.*.json` (unless it
  is explicitly allowed by the profile), `secrets.*`.
- Show a per-repo setting "LLM data sharing acknowledged", which the user must accept before the
  first scan.

## Configuration (`appsettings.json` shape)

```json
{
  "GitHub": { "Mode": "App|Pat", "AppId": "", "InstallationId": "", "ApiBaseUrl": "https://api.github.com" },
  "Jira": { "BaseUrl": "", "DefaultProjectKey": "", "DefaultIssueType": "Task", "Labels": ["integration-coverage"] },
  "Llm": { "Provider": "Anthropic", "Model": "<latest Sonnet id>", "MaxTokensPerCall": 8000, "MaxCostUsdPerScan": 5.0, "PromptSetVersion": "1.0.0" },
  "Scan": { "MaxConcurrentScans": 2, "MaxFileBytes": 200000, "ParallelLlmCalls": 4 },
  "Features": { "PullRequests": true, "Jira": true }
}
```

## Deployment (Docker Compose)

Four services:
- `db`: postgres:16, with a volume
- `api`: an ASP.NET image, with ports and a health check at `/health`
- `worker`: the same image with a different entry point, or a separate image
- `web`: nginx serving the Angular `dist`, and proxying `/api` and `/hubs` to `api`

EF migrations are applied by the api on startup (behind a flag) or by a migration job.

## Observability

- Serilog JSON logs with `runId` and `phase` scopes.
- OpenTelemetry traces: one span per phase, child spans per GitHub and LLM call.
- Metrics: scan duration, tokens and cost per scan, GitHub rate-limit remaining, failure reasons.
- Each run stores its per-step tokens and cost. The UI shows the cost per run.

## Failure handling

| Failure | Behaviour |
|---|---|
| Repo not found / no access | Fail fast at P1 with a clear message |
| Tree truncated | Walk subtrees; if still incomplete, record it in `inventory.unverifiedReasons` |
| LLM schema failure after retries | Affected items become `unverified` |
| Budget cap | Remaining items become `unverified`; the run still succeeds, but confidence is lowered and the reason is recorded |
| Citation unresolved | Run `Failed`, with the citations listed |
| Worker crash | The job lease expires (heartbeat) and the job is retried once, then `Failed` |
