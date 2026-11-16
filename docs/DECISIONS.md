# Decisions

Decisions that override or fill gaps in `BlindSpotScanner-Blueprint/`. Newest last.

## D1 — Versions: .NET 10 + Angular 22 (2026-11-16)
The blueprint says .NET 9 + Angular 19. We use **.NET 10** (LTS) and **Angular 22**, which are
installed and current. Every blueprint feature exists in these versions.

## D2 — One root CLAUDE.md wins (2026-11-16)
The blueprint's `CLAUDE.md` claims precedence. Instead, the root `CLAUDE.md` wins: it keeps the
project dev rules (TDD, no speculative complexity, naming, git) and adds the blueprint's
product principles. The blueprint's `CLAUDE.md` is reference only.

## D3 — Full Clean Architecture, plain services (2026-11-16)
We follow blueprint doc 03: Domain, Application, Infrastructure, Api and a separate Worker
(4 containers: db, api, worker, web). Projects are named `BlindSpotScanner.*`. Use plain
application services, not MediatR (the blueprint allows either).

## D4 — Docker Desktop (2026-11-16)
We install Docker Desktop (WSL2) for PostgreSQL, Docker Compose and Testcontainers, as the
blueprint requires.

## D5 — Name: BlindSpotScanner everywhere (2026-11-16)
It replaces "Integration Coverage Analyzer" and `Coverage.*` in project names, the UI title,
the PR footer and docs.

## D6 — Credentials when needed (2026-11-16)
Build with fakes. Get a Claude API key (billed separately from a Claude Pro plan) at M5 for the
opt-in live test, and a free Jira Cloud site at M9.
