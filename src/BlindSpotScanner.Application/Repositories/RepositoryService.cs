using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Domain;
using BlindSpotScanner.Domain.Entities;

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
}

/// <summary>Registers GitHub repositories (D22): GitHub's canonical names, a branch that exists, and profile v1.</summary>
public sealed class RepositoryService(IRepositoryStore store, IGitHubClient github)
{
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
        catch (Exception ex) when (ex is GitHubAuthenticationException or GitHubRateLimitException)
        {
            return new AddRepositoryResult(AddRepositoryStatus.GitHubUnavailable, Error: ex.Message);
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

        var repository = new Repository { Id = Guid.NewGuid(), Owner = remote.Owner, Name = remote.Name, DefaultBranch = branch, HtmlUrl = remote.HtmlUrl };
        if (!await store.TryAddAsync(repository, ProfileTemplate.Default(repository.Id, $"{remote.Owner}/{remote.Name}"), ct))
        {
            // A concurrent add won the unique index; answer with its row.
            return new AddRepositoryResult(AddRepositoryStatus.AlreadyRegistered, await store.FindByNameAsync(remote.Owner, remote.Name, ct));
        }

        return new AddRepositoryResult(AddRepositoryStatus.Created, repository);
    }
}
