# BlindSpotScanner

Web application: Angular frontend + ASP.NET Core (.NET 10, C#) backend.

> Layout and commands below are the intended defaults. Update them once the solution is scaffolded.

## Layout
```
src/
  Api/            ASP.NET Core Web API (.NET 10)
  Api.Tests/      xUnit tests for Api
  web/            Angular app (standalone components)
BlindSpotScanner.sln
```

## Commands
| Task | Backend (repo root) | Frontend (`src/web`) |
|---|---|---|
| Restore/install | `dotnet restore` | `npm ci` |
| Build | `dotnet build` | `npx ng build` |
| Test | `dotnet test` | `npx ng test --watch=false` |
| Run | `dotnet run --project src/Api` | `npx ng serve` |
| Lint/format | `dotnet format --verify-no-changes` | `npx ng lint` |

Run backend and frontend tests before saying a change is done.

## Development rules
- **TDD:** write a failing test first, then the minimal code to pass it, then refactor.
- **No speculative complexity:** no extra abstractions, interfaces, locks, caching or config switches without a real, present need.
- Look up Angular and ASP.NET Core APIs with context7. Both change between major versions; don't rely on memory.

## Backend (C#)
- Nullable reference types and `TreatWarningsAsErrors` on.
- Layering: controllers/endpoints stay thin → services hold logic → data access behind services. No business logic in controllers.
- Async all the way down (`async`/`await`, pass `CancellationToken`); no `.Result` or `.Wait()`.
- Use built-in DI; register services in `Program.cs` (or extension methods once it grows).
- Naming: PascalCase types and members, `_camelCase` private fields, `Async` suffix on async methods.

## Frontend (Angular)
- Standalone components, signals for local state, `inject()` for DI.
- Strict TypeScript; no `any` without a comment explaining why.
- One component per folder (`name.component.ts/.html/.scss/.spec.ts`).
- HTTP calls go through services, never directly from components.

## Tests
- Backend: xUnit. Name tests `Method_Scenario_ExpectedResult`.
- Frontend: the Angular default runner; `*.spec.ts` next to the code.
- Use `// Arrange`, `// Act`, `// Assert` comments.
- One behavior per test; no branching (`if`/loops) inside tests.

## Before every commit (self-review)
1. Re-read the full diff.
2. Grep for stale names after any rename.
3. Run build, lint and tests for whichever side changed.
4. Check `git status` for files that shouldn't be committed.

## Git
- Branches: `feature/<short-name>`, `fix/<short-name>`.
- Commit messages: Conventional Commits, e.g. `feat(api): add order endpoint`, `fix(web): handle empty list`. Imperative, ≤72-char subject.
- Small, focused commits. Commit only when asked; ask before pushing.

## Automated review comments
- Verify each comment against the code before applying it.
- Reply on the thread only for false positives, explaining why.
