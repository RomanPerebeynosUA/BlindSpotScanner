# M3 GitHub Access and Enumeration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pipeline phases P1 (Resolve) and P2 (Enumerate) against the GitHub REST API:
- a URL parser;
- a typed `GitHubClient` with PAT auth and rate-limit backoff;
- a recursive Git Trees walk that handles truncation;
- profile-glob filtering into controller and test files;
- a blob cache in `file_cache`.

With WireMock fixtures, P1 and P2 produce the inventory counts.

**Architecture:**
- **Domain:** `GitHubRepoUrl`, a pure value object.
- **Application:**
  - the `IGitHubClient` and `IBlobReader` ports, their records and exceptions;
  - `ResolvePhase` (P1) and `EnumeratePhase` (P2), whose logic is pure apart from the ports.
- **Infrastructure:**
  - `GitHubClient`, a typed `HttpClient`;
  - `GitHubRateLimitHandler`, a `DelegatingHandler` that uses `TimeProvider`;
  - `CachingBlobReader`, backed by EF `file_cache`.
- No DI registration yet: the first consumer is the M6 orchestrator, the same rule as the DbContext in M1.

**Tech Stack:** .NET 10, Microsoft.Extensions.FileSystemGlobbing 10.0.15 (Application), WireMock.Net 2.31.0 and Microsoft.Extensions.TimeProvider.Testing 10.11.0 (Infrastructure.Tests), Testcontainers (M1 fixture).

**Spec:** `BlindSpotScanner-Blueprint/docs/11-implementation-plan.md` (M3), `05-scan-pipeline.md` (P1, P2), `10-security-config-ops.md` (secrets, config shape, failure table), principle 4 in the root `CLAUDE.md`, `docs/DECISIONS.md` (D16 added here).

## Global Constraints

- **D16, the user's choice:** PAT auth only, and a typed `HttpClient` rather than Octokit. GitHub App auth comes later (M11).
- Requests go to `{ApiBaseUrl}` (default `https://api.github.com`) with these headers:
  - `Authorization: Bearer {token}`, only when a token is configured;
  - `Accept: application/vnd.github+json`;
  - `X-GitHub-Api-Version: 2022-11-28`;
  - `User-Agent: BlindSpotScanner`.
- The token is never logged or put in exception messages (principle 9).
- **Counts come from the Git Trees API with `recursive=1`, never from code search** (principle 4). After P1, every read is pinned to the commit SHA.
- **Denominator files come from production code** (principle 3). A file that matches a test glob is never a controller file, even when it also matches a controller glob.
- Default excludes, always applied: `**/bin/**`, `**/obj/**`, `**/node_modules/**`, `**/vendor/**`, `**/Generated/**`, `**/*.Designer.cs`, `**/*.g.cs`. Glob matching is case-sensitive (`StringComparison.Ordinal`), because Git paths are.
- **Rate limits:**
  - When a 403 or 429 carries `Retry-After`, wait that many seconds.
  - When it carries `x-ratelimit-remaining: 0`, wait until `x-ratelimit-reset` (epoch seconds) + 1s.
  - Retry at most 3 times.
  - If a required wait exceeds `MaxRateLimitWait` (default 60s), throw `GitHubRateLimitException(resetAt)` at once instead of sleeping.
  - Any other 403 is not retried.
  - All waits go through the injected `TimeProvider`.
- Each config key in doc 10's shape becomes a `GitHubOptions { ApiBaseUrl, Token, MaxRateLimitWait }` property. No other config.
- TDD. Tests are named `Method_Scenario_ExpectedResult`, use Arrange/Act/Assert, and test one behavior each. Use WireMock for all GitHub HTTP and never call the real API in tests. Look up FileSystemGlobbing in-memory matching, WireMock scenarios and FakeTimeProvider with context7 before use. Check packages with `dotnet package search`.
- Serena for code where it works (otherwise say so). One commit per task on `feature/m3-github-enumeration`. Ask before pushing.

## Review Focus

1. **A branch name containing `/`** (`/tree/feature/x`, and `GET …/branches/feature%2Fx`) must resolve without being cut at the slash. Tested in Tasks 1 and 2.
2. **A truncated recursive tree.** Fall back to walking each subtree without `recursive`. If a single tree is still truncated, record an `unverifiedReasons` entry naming that path, and never report a partial count as complete. Tested in Task 3.
3. **A 403 without rate-limit headers** (no permission) must fail at once, not wait or retry. Tested in Task 2.
4. **A blob containing a NUL byte** (a binary file). PostgreSQL `text` rejects `\0`, so the cache must skip storing it without failing the read. Tested in Task 4.
5. **Two scans caching the same blob concurrently.** The second insert hits a duplicate key, and it must not fail the read. Tested in Task 4.

---

### Task 1: GitHub repository URL parser (Domain)

**Files:**
- Create: `src/BlindSpotScanner.Domain/GitHubRepoUrl.cs`
- Test: `tests/BlindSpotScanner.Domain.Tests/GitHubRepoUrlTests.cs`

**Interfaces:**
- Produces: `public sealed record GitHubRepoUrl(string Owner, string Name, string? Branch) { public static bool TryParse(string? text, out GitHubRepoUrl? url); public static GitHubRepoUrl Parse(string text); }`.
  - `Parse` throws `FormatException` with a message naming the expected form `https://github.com/{owner}/{repo}[/tree/{branch}]`.
  - It accepts an optional trailing `/`, a `.git` suffix and `http`/`https`. The host is case-insensitive and must be `github.com`. Owner and repo keep their case.

- [ ] **Step 1: Write the failing tests:**
  - `TryParse_OwnerAndRepo_ReturnsThem`
  - `TryParse_TreeBranch_ReturnsBranch`
  - `TryParse_BranchWithSlash_KeepsWholeBranch` (`/tree/feature/x` → `feature/x`; Review Focus 1)
  - `TryParse_DotGitAndTrailingSlash_AreIgnored`
  - `TryParse_OtherHost_ReturnsFalse` (a Theory over `gitlab.com/a/b`, `github.com/onlyowner`, an empty string, `not a url`)
  - `Parse_Invalid_ThrowsFormatException`
- [ ] **Step 2: Run them.** Expected: a compile failure, which counts as RED.
- [ ] **Step 3: Implement** with `Uri.TryCreate` plus path segments, and no regex soup.
- [ ] **Step 4: Run the full `dotnet test`.** Expected: green, 0 warnings, format clean.
- [ ] **Step 5: Commit** `feat(domain): parse GitHub repository URLs`

### Task 2: GitHub client and rate-limit backoff (Infrastructure, WireMock)

**Files:**
- Create in `src/BlindSpotScanner.Application/GitHub/`:
  - `IGitHubClient.cs`, with the records `GitHubRepository(string Owner, string Name, string DefaultBranch, string HtmlUrl)`, `GitTree(IReadOnlyList<GitTreeEntry> Entries, bool Truncated)` and `GitTreeEntry(string Path, GitEntryType Type, string Sha, long? Size)`, and `enum GitEntryType { Blob, Tree, Commit }`
  - `GitHubExceptions.cs`: `GitHubNotFoundException`, `GitHubAuthenticationException`, and `GitHubRateLimitException(DateTimeOffset ResetAt)`
- Create in `src/BlindSpotScanner.Infrastructure/GitHub/`: `GitHubOptions.cs`, `GitHubClient.cs`, `GitHubRateLimitHandler.cs`
- Modify: `tests/BlindSpotScanner.Infrastructure.Tests/*.csproj` (add WireMock.Net 2.31.0 and Microsoft.Extensions.TimeProvider.Testing 10.11.0)
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/GitHub/GitHubClientTests.cs`, `GitHubRateLimitHandlerTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IGitHubClient
  {
      Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct);
      Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct);
      Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct);
      Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct);
  }
  ```
  - **Endpoints:**
    - `GET /repos/{o}/{n}`
    - `GET /repos/{o}/{n}/branches/{Uri.EscapeDataString(branch)}` → `commit.sha`
    - `GET /repos/{o}/{n}/git/trees/{sha}[?recursive=1]`
    - `GET /repos/{o}/{n}/git/blobs/{sha}` → base64 `content`, decoded as UTF-8
  - **Errors:** 404 → `GitHubNotFoundException("Repository {o}/{n} was not found or the token has no access to it")`. 401 → `GitHubAuthenticationException`. The messages never include the token.
  - `GitHubClient(HttpClient http, IOptions<GitHubOptions> options)`: the HttpClient's handler chain includes `GitHubRateLimitHandler`.
  - `GitHubRateLimitHandler(TimeProvider time, IOptions<GitHubOptions> options) : DelegatingHandler`, implementing the rules in Global Constraints.

- [ ] **Step 1: Write the failing client tests** (a WireMock server and a real `HttpClient` aimed at it):
  - `GetRepository_Exists_ReturnsDefaultBranch`
  - `GetRepository_NotFound_ThrowsNotFoundWithoutToken`: the exception message doesn't contain the configured token value
  - `GetRepository_Unauthorized_ThrowsAuthentication`
  - `AnyRequest_WithToken_SendsBearerAndApiHeaders`: WireMock matches the headers from Global Constraints
  - `GetBranchHeadSha_BranchWithSlash_IsEscaped` (Review Focus 1)
  - `GetTree_Recursive_ParsesEntriesAndTruncatedFlag`
  - `GetBlobText_Base64Content_IsDecoded` (the base64 content includes the newlines GitHub inserts)
- [ ] **Step 2: Write the failing handler tests** (WireMock scenarios for sequential responses, plus a `FakeTimeProvider`):
  - `Send_PrimaryLimitExhausted_WaitsUntilResetThenRetries`: the first response is 403 with remaining 0 and reset = now+30. The task is still running after the clock advances 30s and completes after 31s.
  - `Send_RetryAfter_WaitsThatLong`: 429 with `Retry-After: 5`
  - `Send_ResetBeyondMaxWait_ThrowsRateLimitWithoutWaiting`
  - `Send_RateLimitedFourTimes_ThrowsAfterThreeRetries`
  - `Send_ForbiddenWithoutRateLimitHeaders_IsNotRetried` (WireMock sees exactly 1 request; Review Focus 3)
- [ ] **Step 3: Run them.** Expected: a compile failure, which counts as RED.
- [ ] **Step 4: Implement** the ports, records, exceptions, options, client and handler.
- [ ] **Step 5: Run the full `dotnet test`.** Expected: green, 0 warnings, format clean.
- [ ] **Step 6: Docs.** D16: "GitHub access uses a typed HttpClient and a PAT (`GitHub:Token`, from user secrets or an env var). The GitHub App mode arrives in M11. Rate-limit backoff waits on `Retry-After` or `x-ratelimit-reset` up to `GitHub:MaxRateLimitWait` (60s), retries at most 3 times, and otherwise fails with the reset time."
- [ ] **Step 7: Commit** `feat(infra): add GitHub client with rate-limit backoff`

### Task 3: P1 Resolve and P2 Enumerate (Application), and the M3 AC

**Files:**
- Create in `src/BlindSpotScanner.Application/Scanning/`:
  - `RunContext.cs`
  - `RepositoryInventory.cs`, with `RepoFile(string Path, string BlobSha, long? Size)`
  - `ResolvePhase.cs`
  - `EnumeratePhase.cs`
- Modify: `src/BlindSpotScanner.Application/*.csproj` (add Microsoft.Extensions.FileSystemGlobbing 10.0.15)
- Test: `tests/BlindSpotScanner.Application.Tests/Scanning/EnumeratePhaseTests.cs` and `ResolvePhaseTests.cs`, which use an in-memory `FakeGitHubClient` test double
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/GitHub/ResolveAndEnumerateTests.cs`. This is the AC against WireMock, with JSON fixtures in `tests/BlindSpotScanner.Infrastructure.Tests/GitHub/Fixtures/bookstore-*.json`.

**Interfaces:**
- Consumes: `GitHubRepoUrl.Parse` (Task 1), `IGitHubClient` (Task 2), and `RepoProfile.ControllerGlobs` and `TestGlobs` (M1).
- Produces:
  - `public sealed record RunContext(string Owner, string Name, string Branch, string CommitSha, string HtmlUrl)`
  - `public sealed class ResolvePhase(IGitHubClient github) { Task<RunContext> ResolveAsync(string repositoryUrl, CancellationToken ct); }`. It uses the URL's branch, or the repository's default branch, then pins the SHA. It lets `FormatException` and `GitHubNotFoundException` surface unchanged, so they fail fast with a clear message (doc 10). Loading the profile from the DB belongs to the M6 orchestrator, so P2 takes the profile as an argument.
  - `public sealed record RepositoryInventory(IReadOnlyList<RepoFile> ControllerFiles, IReadOnlyList<RepoFile> TestFiles, IReadOnlyList<string> UnverifiedReasons)`, with `ControllerFileCount` and `TestFileCount` properties
  - `public sealed class EnumeratePhase(IGitHubClient github) { Task<RepositoryInventory> EnumerateAsync(RunContext run, RepoProfile profile, CancellationToken ct); }`
    - First call `GetTreeAsync(commitSha, recursive: true)`.
    - If it comes back truncated, walk breadth-first with `recursive: false` from the commit SHA, prefixing paths. A sub-tree that is still truncated adds `"tree truncated at '{path}'; files below it were not enumerated"`.
    - Keep only blobs, then apply the default excludes. Test files = files matching `TestGlobs`. Controller files = files matching `ControllerGlobs` that are not test files. Order is by path (ordinal).

- [ ] **Step 1: Write the failing tests:**
  - **Application** (FakeGitHubClient):
    - `Enumerate_ProfileGlobs_SplitControllersAndTests`
    - `Enumerate_ControllerUnderTestFolder_IsNotDenominator` (principle 3)
    - `Enumerate_BinObjAndGenerated_AreExcluded`
    - `Enumerate_TruncatedRecursiveTree_WalksSubtrees` (the same counts as the non-truncated case; Review Focus 2)
    - `Enumerate_SubtreeStillTruncated_RecordsUnverifiedReason`
    - `Resolve_UrlWithoutBranch_UsesDefaultBranchAndPinsSha`
    - `Resolve_UrlWithBranch_PinsThatBranch`
  - **Infrastructure AC** (WireMock serves the repo, branch and recursive-tree fixtures for a bookstore repo):
    - `ResolveAndEnumerate_BookstoreFixture_ProducesInventoryCounts`. The profile is `ControllerGlobs ["**/Controllers/*Controller.cs"]` and `TestGlobs ["tests/**/*.cs"]`. Expected: `ControllerFileCount` 2 and `TestFileCount` 10, matching `examples/sample-report.json` `inventory.controllerFiles` and `testFilesTotal`.
    - The fixture tree includes `tests/Bookstore.IntegrationTests/Controllers/FakeGatewayController.cs` (a test, not a controller), plus `bin/`, `obj/` and `Generated/` decoys.
- [ ] **Step 2: Run them.** Expected: a compile failure, which counts as RED.
- [ ] **Step 3: Implement** `RunContext`, `RepositoryInventory`, `ResolvePhase` and `EnumeratePhase` (FileSystemGlobbing `Matcher(StringComparison.Ordinal)` with in-memory matching).
- [ ] **Step 4: Run the full `dotnet test`.** Expected: green, 0 warnings, format clean.
- [ ] **Step 5: Commit** `feat(app): add P1 resolve and P2 enumerate phases`

### Task 4: Blob cache (file_cache)

**Files:**
- Create: `src/BlindSpotScanner.Application/GitHub/IBlobReader.cs`, `src/BlindSpotScanner.Infrastructure/GitHub/CachingBlobReader.cs`
- Test: `tests/BlindSpotScanner.Infrastructure.Tests/GitHub/CachingBlobReaderTests.cs` (the `postgres` collection fixture plus WireMock)

**Interfaces:**
- Consumes: `IGitHubClient.GetBlobTextAsync` (Task 2), and `BlindSpotScannerDbContext.FileCache` (M1).
- Produces:
  - `public interface IBlobReader { Task<string> ReadAsync(string owner, string name, string blobSha, CancellationToken ct); }`
  - `public sealed class CachingBlobReader(BlindSpotScannerDbContext db, IGitHubClient github) : IBlobReader`
    - It returns the cached `file_cache.content` for the SHA when present (blobs are content-addressed, so a SHA never changes).
    - Otherwise it fetches the blob and stores it, unless the content contains `\0`, in which case it returns the content without caching.
    - A duplicate-key `DbUpdateException` on insert (a concurrent scan) is swallowed after detaching the entry.

- [ ] **Step 1: Write the failing tests:**
  - `Read_SameBlobTwice_FetchesFromGitHubOnce` (WireMock's log shows 1 blob request)
  - `Read_CachedBlob_ReturnsWithoutGitHub` (pre-seed `file_cache`, so WireMock sees 0 requests)
  - `Read_BlobWithNulByte_ReturnsContentWithoutCaching` (Review Focus 4)
  - `Read_BlobCachedConcurrently_DoesNotThrow`: the WireMock blob response uses `WithCallback`, which inserts the same SHA into `file_cache` through a separate context before returning. This is exactly what a concurrent scan does between the reader's cache miss and its insert. The read returns the content and doesn't throw (Review Focus 5).
- [ ] **Step 2: Run them.** Expected: a compile failure, which counts as RED.
- [ ] **Step 3: Implement** `IBlobReader` and `CachingBlobReader`.
- [ ] **Step 4: Run the full `dotnet test`** (Docker running). Expected: green, 0 warnings, format clean.
- [ ] **Step 5: Commit** `feat(infra): cache GitHub blobs by SHA in file_cache`
