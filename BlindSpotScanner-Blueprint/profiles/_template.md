# Repo profile — <repo display name>

> Per-repository definition of how integration tests look in **this** repo. It is the source of
> truth for classification. The app can auto-draft it (P1b), and a human confirms it. It is
> versioned: a changed profile is flagged in the run comparison.

## Basics
- **Language(s):** C# (.NET 8)            <!-- csharp | java | typescript | python -->
- **Web framework:** ASP.NET Core (controllers + minimal APIs)
- **Test framework(s):** xUnit, FluentAssertions

## Production surface (denominator)
- **Controller globs:** `src/**/Controllers/**/*Controller.cs`
- **Minimal API files:** `src/**/Endpoints/**/*.cs`
- **Route prefix conventions:** e.g. `api/v{version}/[controller]`

## Integration tests — how to recognize them
- **Folders / projects:** `tests/*.IntegrationTests/**`
- **Naming:** `*IntegrationTests.cs`, `*ApiTests.cs`
- **Attributes / traits:** `[Trait("Category","Integration")]`, `[Collection("Database")]`
- **Base classes / fixtures:** `WebApplicationFactory<Program>`, `IClassFixture<ApiFactory>`, Testcontainers
- **Not integration (exclude even if in that folder):** helpers, builders, `*Fixture.cs`

## What "integration" means here
- **integrationMappedTo:** `real-seam`    <!-- real-seam | e2e-mocked-data | e2e-real-backend | contract | other -->
- **Real seams in production:** PostgreSQL, message broker, outbound HTTP to <service kind>, blob storage
- **Seams intentionally faked in tests (and why):** …

## Skips / disabled suites
- Patterns: `[Fact(Skip = ...)]`, `[Trait("Category","Manual")]`, suites excluded in CI config `<path>`

## Remediation preferences
- **Test location for new tests:** `tests/Api.IntegrationTests/<Area>/`
- **Jira project / component override:** (optional)
- **PR base branch:** default branch
