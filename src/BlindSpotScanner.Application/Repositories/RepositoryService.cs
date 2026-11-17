using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Domain;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Repositories;

public enum AddRepositoryStatus
{
    Created,
    InvalidUrl,
    NotFound,
    BranchNotFound,
    AlreadyRegistered,
    GitHubUnavailable,
}

/// <param name="Repository">The new repository, or the registered one for <see cref="AddRepositoryStatus.AlreadyRegistered"/>.</param>
public sealed record AddRepositoryResult(AddRepositoryStatus Status, Repository? Repository = null, string? Error = null);

/// <summary>What the repository endpoints read and write.</summary>
public interface IRepositoryStore
{
    Task<Repository?> FindAsync(Guid id, CancellationToken ct);

    Task<Repository?> FindByNameAsync(string owner, string name, CancellationToken ct);

    /// <summary>Saves both in one transaction; false when (Owner, Name) is already registered.</summary>
    Task<bool> TryAddAsync(Repository repository, RepoProfile profile, CancellationToken ct);

    /// <summary>Every repository with its latest succeeded run's headline and its active run, by full name.</summary>
    Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken ct);

    /// <summary>The repository's runs, newest first (by generation, else queue time).</summary>
    Task<IReadOnlyList<ScanRun>> RunsAsync(Guid repositoryId, CancellationToken ct);
}

/// <summary>A row of the repositories page (OpenAPI <c>RepositorySummary</c>, plus D22's additions).</summary>
/// <param name="LastCoveredShare">Covered endpoints / total of the latest succeeded run; null when it had none.</param>
/// <param name="ActiveRunId">The newest queued or running run.</param>
/// <param name="LatestRunStatus">The newest run's status, whatever it is (so a repository whose scans all failed doesn't look unscanned).</param>
public sealed record RepositorySummary(
    Guid Id, string FullName, string DefaultBranch, Guid? LastRunId, decimal? LastScore, decimal? LastCoveredShare, DateTimeOffset? LastScanAt, Guid? ActiveRunId,
    RunStatus? LatestRunStatus = null);

/// <summary>Registers GitHub repositories (D22): GitHub's canonical names, a branch that exists, and profile v1.</summary>
public sealed class RepositoryService(IRepositoryStore store, IGitHubClient github)
{
    public Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken ct) => store.ListAsync(ct);

    public Task<Repository?> GetAsync(Guid id, CancellationToken ct) => store.FindAsync(id, ct);

    /// <summary>Null when the repository doesn't exist.</summary>
    public async Task<IReadOnlyList<ScanRun>?> RunsAsync(Guid id, CancellationToken ct) =>
        await store.FindAsync(id, ct) is null ? null : await store.RunsAsync(id, ct);

    public async Task<AddRepositoryResult> AddAsync(string url, string? defaultBranch, CancellationToken ct)
    {
        if (!GitHubRepoUrl.TryParse(url, out var parsed))
        {
            return new AddRepositoryResult(AddRepositoryStatus.InvalidUrl, Error: $"'{url}' is not a GitHub repository URL. Expected https://github.com/{{owner}}/{{repo}}.");
        }

        GitHubRepository remote;
        try
        {
            remote = await github.GetRepositoryAsync(parsed.Owner, parsed.Name, ct);
        }
        catch (GitHubNotFoundException ex)
        {
            return new AddRepositoryResult(AddRepositoryStatus.NotFound, Error: ex.Message);
        }
        catch (Exception ex) when (GitHubFailures.IsUnavailable(ex))
        {
            return Unavailable(ex);
        }

        // GitHub's names are canonical, so URLs that differ by case or form find the same row.
        if (await store.FindByNameAsync(remote.Owner, remote.Name, ct) is { } registered)
        {
            return new AddRepositoryResult(AddRepositoryStatus.AlreadyRegistered, registered);
        }

        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? parsed.Branch ?? remote.DefaultBranch : defaultBranch.Trim();
        try
        {
            await github.GetBranchHeadShaAsync(remote.Owner, remote.Name, branch, ct);
        }
        catch (GitHubNotFoundException)
        {
            return new AddRepositoryResult(AddRepositoryStatus.BranchNotFound, Error: $"Branch '{branch}' doesn't exist in {remote.Owner}/{remote.Name}.");
        }
        catch (Exception ex) when (GitHubFailures.IsUnavailable(ex))
        {
            return Unavailable(ex);
        }

        var repository = new Repository { Id = Guid.NewGuid(), Owner = remote.Owner, Name = remote.Name, DefaultBranch = branch, HtmlUrl = remote.HtmlUrl };
        if (!await store.TryAddAsync(repository, ProfileTemplate.Default(repository.Id, $"{remote.Owner}/{remote.Name}"), ct))
        {
            // A concurrent add won the unique index; answer with its row.
            return new AddRepositoryResult(AddRepositoryStatus.AlreadyRegistered, await store.FindByNameAsync(remote.Owner, remote.Name, ct));
        }

        return new AddRepositoryResult(AddRepositoryStatus.Created, repository);
    }

    /// <remarks>An HTTP error's message can echo a URL, so only GitHub's own exceptions pass their message on.</remarks>
    private static AddRepositoryResult Unavailable(Exception ex) => new(
        AddRepositoryStatus.GitHubUnavailable, Error: ex is HttpRequestException ? "GitHub did not answer; try again later." : ex.Message);
}
