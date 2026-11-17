using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;

namespace BlindSpotScanner.Application.Tests.Repositories;

internal sealed class FakeProfileStore : IProfileStore
{
    public List<RepoProfile> Profiles { get; } = [];

    /// <summary>Simulates a concurrent save of the same version: the next TryAdd fails.</summary>
    public bool ClashNext { get; set; }

    public Task<RepoProfile?> GetAsync(Guid repositoryId, int? version, CancellationToken ct) =>
        Task.FromResult(Profiles.Where(p => p.RepositoryId == repositoryId && (version is null || p.Version == version)).MaxBy(p => p.Version));

    public Task<IReadOnlyList<int>> VersionsAsync(Guid repositoryId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<int>>(Profiles.Where(p => p.RepositoryId == repositoryId).Select(p => p.Version).OrderDescending().ToList());

    public Task<bool> TryAddAsync(RepoProfile profile, CancellationToken ct)
    {
        if (ClashNext)
        {
            return Task.FromResult(false);
        }

        Profiles.Add(profile);
        return Task.FromResult(true);
    }
}
