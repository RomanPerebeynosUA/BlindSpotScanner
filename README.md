# BlindSpotScanner

BlindSpotScanner audits the **integration tests** of a GitHub repository (ASP.NET Core today). It
lists every API endpoint from the production code and checks which ones real integration tests
cover. Each covered or weak grade cites a test at a pinned commit. It also scores test quality
from 0 to 10. Scans are kept, so later runs can be compared.

- **Stack:** Angular 22 web app · ASP.NET Core (.NET 10) API and scan worker · PostgreSQL 16 ·
  Claude API · Docker Compose
- **Specs:** product spec in [`BlindSpotScanner-Blueprint/`](BlindSpotScanner-Blueprint/),
  decisions in [`docs/DECISIONS.md`](docs/DECISIONS.md), contributor rules in [`CLAUDE.md`](CLAUDE.md)

---

## 1. What to install

| Tool | Version | Needed for |
|---|---|---|
| [Git](https://git-scm.com/downloads) | any recent | cloning |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) (WSL2 backend on Windows) | recent, with Compose v2 | running the app; backend tests (Testcontainers) |
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | **10.0.102** or a later 10.0 feature band (pinned in `global.json`) | backend development and tests |
| [Node.js](https://nodejs.org/) | **26.x** (npm comes with it) | frontend development and tests |

To run the app with Docker only, you need just **Git and Docker Desktop**. The .NET SDK and Node.js
are for development.

### macOS (Apple Silicon or Intel)

Use the official installers from the links above, or [Homebrew](https://brew.sh/):

```bash
brew install git
brew install --cask docker        # then open Docker.app once and wait for "Docker Desktop is running"
brew install --cask dotnet-sdk    # check that `dotnet --version` is 10.0.102 or later; otherwise use the official installer
brew install node                 # check that `node --version` is v26.x; otherwise use the official installer
```

- Docker Desktop for Mac needs no extra setup. All images used here run natively on Apple
  Silicon.
- Commands below that are marked *macOS / Linux* run in Terminal (zsh or bash).

### Windows 10/11

Use the official installers from the links above, or `winget` in PowerShell:

```powershell
winget install Git.Git
winget install Docker.DockerDesktop   # turn on the WSL2 backend when asked, restart, then start Docker Desktop
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS          # check that `node --version` is v26.x
```

- Docker Desktop needs **WSL2**. If it asks, run `wsl --install` in an administrator PowerShell
  and restart.
- Commands below that are marked *Windows* are for PowerShell. Git Bash can run the *macOS / Linux*
  ones as well.

### Check your setup (any OS)

```bash
git --version
docker --version && docker compose version
dotnet --version      # 10.0.102 or later 10.0.x
node --version        # v26.x
```

## 2. Credentials (optional, but needed for real scans)

| Variable | What it is | Without it |
|---|---|---|
| `GITHUB_TOKEN` | A GitHub [personal access token](https://github.com/settings/personal-access-tokens) with read access to the repositories you want to scan. For "Fix with PR" it also needs **Contents: write** and **Pull requests: write** on those repositories. | Public repositories work, but GitHub's anonymous rate limit (60 requests/hour) is quickly used up by a scan. Private repositories can't be read. "Fix with PR" is hidden. |
| `FEATURES_PULL_REQUESTS` | `true` (default) or `false`. `false` is read-only mode: the app never writes to GitHub. | |
| `ANTHROPIC_API_KEY` | A [Claude API key](https://console.anthropic.com/). **Calls are billed** (a Claude Pro plan does not include API use). The cap is $5 per scan by default. | Scans still run, but the AI steps fail, so endpoints are reported as *unverified* and quality can't be scored. "Auto-draft with AI" doesn't work either. |

| `JIRA_BASE_URL`, `JIRA_EMAIL`, `JIRA_API_TOKEN`, `JIRA_PROJECT_KEY` | Your Jira Cloud site (`https://<site>.atlassian.net`), the account's email, an [API token](https://id.atlassian.com/manage-profile/security/api-tokens) and the project key that tickets go into. A [free Jira Cloud site](https://www.atlassian.com/software/jira/free) works. | "Create Jira" on the report shows "Jira not configured". Everything else works. |

Secrets go into `.env`, which git ignores. Never commit them. For local development (section 4),
set the Jira values as environment variables the same way: `Jira__BaseUrl`, `Jira__Email`,
`Jira__ApiToken` and `Jira__ProjectKey` for the api.

## 3. Run the app (Docker Compose)

Make sure Docker Desktop is running first.

*macOS / Linux:*

```bash
git clone https://github.com/RomanPerebeynosUA/BlindSpotScanner.git
cd BlindSpotScanner
cp .env.example .env
open -e .env                  # macOS: opens it in TextEdit (any editor works)
docker compose up --build
```

*Windows (PowerShell):*

```powershell
git clone https://github.com/RomanPerebeynosUA/BlindSpotScanner.git
cd BlindSpotScanner
Copy-Item .env.example .env
notepad .env
docker compose up --build
```

In `.env`, set `POSTGRES_PASSWORD`. You can also set `GITHUB_TOKEN` and `ANTHROPIC_API_KEY` there
(see section 2).

The first build takes a few minutes. Then open:

- **Web app:** http://localhost:8080
- **API health check:** http://localhost:5080/health (returns `Healthy`)

Stop with `Ctrl+C`, or run `docker compose down`. Data is kept in the `pgdata` volume.
`docker compose down -v` also deletes it.

Compose starts four containers:

| Container | Role |
|---|---|
| `db` | PostgreSQL 16 |
| `api` | REST API, plus the SignalR hub `/hubs/scans`. It applies database migrations at startup. |
| `worker` | Runs the queued scans |
| `web` | nginx. Serves the Angular app and proxies `/api` and `/hubs` to the API. |

### First scan

1. **Repositories → Add repository.** Paste a GitHub URL, for example
   `https://github.com/owner/repo`, or `.../tree/<branch>` for a specific branch.
2. **Check the profile page.** It describes how integration tests look in that repository: test
   globs, controller globs, attributes and base classes. A default profile is created for you.
   Adjust it, or click **Auto-draft with AI** (needs the Claude key). Then click **Save**.
3. **Runs → Scan now.** The live page shows the phases P1–P7 as they run.
4. **The report opens when the scan finishes.** It has the score, endpoint coverage with status
   filters and search, evidence links to the exact commit, the scorecard and the findings.

## 4. Local development (without containers for the app)

You still need PostgreSQL. Start one in Docker; the compose `db` service doesn't publish a port.
The command is the same on every OS:

```bash
docker run -d --name bss-postgres -p 5432:5432 -e POSTGRES_USER=blindspot -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=blindspot postgres:16
```

Run the API and the worker in **two terminals**. Set the variables in each terminal first. The
tokens are optional, and Claude calls are billed.

*macOS / Linux:*

```bash
export ConnectionStrings__Postgres="Host=localhost;Database=blindspot;Username=blindspot;Password=dev"
export GitHub__Token="..."
export ANTHROPIC_API_KEY="..."

dotnet run --project src/BlindSpotScanner.Api --urls http://localhost:5080   # terminal 1
dotnet run --project src/BlindSpotScanner.Worker                             # terminal 2
```

*Windows (PowerShell):*

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Database=blindspot;Username=blindspot;Password=dev"
$env:GitHub__Token = "..."
$env:ANTHROPIC_API_KEY = "..."

dotnet run --project src/BlindSpotScanner.Api --urls http://localhost:5080   # terminal 1
dotnet run --project src/BlindSpotScanner.Worker                             # terminal 2
```

Start the web app in a third terminal (any OS). It proxies `/api` and `/hubs` to
`http://localhost:5080`:

```bash
cd src/web
npm ci
npx ng serve                       # http://localhost:4200
```

Afterwards, remove the database container with `docker rm -f bss-postgres`.

## 5. Tests and checks

| What | Command | Notes |
|---|---|---|
| Backend build | `dotnet build` | warnings are errors |
| Backend tests | `dotnet test` | Same commands on macOS, Linux and Windows. **Docker must be running.** Testcontainers starts PostgreSQL; GitHub and Claude are faked with WireMock. One live test is skipped unless `ANTHROPIC_API_KEY` and `BSS_LIVE_REPO_URL` are set (billed). |
| Backend format | `dotnet format --verify-no-changes` | |
| Frontend unit tests | `cd src/web && npx ng test --watch=false` | Vitest |
| Frontend lint / build | `npx ng lint` / `npx ng build` | |
| End-to-end tests | `cd src/web && npx playwright install chromium` (once), then `npx playwright test` | Starts `ng serve` itself. The API and the SignalR hub are mocked from `BlindSpotScanner-Blueprint/examples/`, so no backend is needed. |

CI (`.github/workflows/ci.yml`) runs the backend, frontend, e2e and Docker build jobs on every pull
request.

## 6. Troubleshooting

- **`Copy .env.example to .env` when starting Compose:** the `.env` file is missing.
- **Backend tests fail with Docker or Testcontainers errors:** start Docker Desktop and wait until
  it is running.
- **Port 8080, 5080 or 5432 is already in use:** stop the other program, or change the left-hand
  port in `docker-compose.yml` (or in the `docker run` command). To see what holds a port, run
  `lsof -i :8080` on macOS or `netstat -ano | findstr :8080` on Windows. A local PostgreSQL (for
  example from Homebrew) often holds 5432.
- **macOS: `docker: command not found` right after installing:** open Docker.app once. It installs
  the command-line tools on first start.
- **Windows: Docker Desktop says WSL2 is missing:** run `wsl --install` in an administrator
  PowerShell, then restart.
- **Windows: "running scripts is disabled" when running `npx`:** run
  `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, or use Git Bash.
- **The scan fails at P1 with "not found":** the URL or branch is wrong, or the token can't see a
  private repository.
- **Most endpoints show ❔ Unverified:** the Claude key is missing, or the per-scan cost cap was
  reached. The app never guesses a result it didn't check.
- **The migration `AddRepositoryUniqueName` fails on an old local database:** the database has
  duplicate repository rows from earlier test data. Recreate it with `docker compose down -v`.

## Project layout

```
src/
  BlindSpotScanner.Domain/          entities, value objects, report model
  BlindSpotScanner.Application/     use cases, scan pipeline, report assembler/validator
  BlindSpotScanner.Infrastructure/  EF Core, GitHub and Claude clients, extractors, queue
  BlindSpotScanner.Api/             REST controllers, SignalR hub
  BlindSpotScanner.Worker/          scan worker host
  web/                              Angular app (+ e2e/ Playwright tests)
tests/                              xUnit test projects and the bookstore fixture
BlindSpotScanner-Blueprint/         product spec, contracts, prompts, examples
docs/                               decisions and implementation plans
```
