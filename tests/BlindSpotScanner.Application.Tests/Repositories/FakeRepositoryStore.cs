using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;

namespace BlindSpotScanner.Application.Tests.Repositories;

internal sealed class FakeRepositoryStore : IRepositoryStore
{
    public List<Repository> Repositories { get; } = [];

    public List<RepoProfile> Profiles { get; } = [];

    /// <summary>Simulates a concurrent add that won the unique index: the next TryAdd stores this one instead and fails.</summary>
    public Repository? ConcurrentWinner { get; set; }

    public Task<Repository?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Repositories.SingleOrDefault(r => r.Id == id));

    public Task<Repository?> FindByNameAsync(string owner, string name, CancellationToken ct) =>
        Task.FromResult(Repositories.SingleOrDefault(r => r.Owner == owner && r.Name == name));

    public Task<bool> TryAddAsync(Repository repository, RepoProfile profile, CancellationToken ct)
    {
        if (ConcurrentWinner is { } winner)
        {
            Repositories.Add(winner);
            return Task.FromResult(false);
        }

        Repositories.Add(repository);
        Profiles.Add(profile);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<ScanRun>> RunsAsync(Guid repositoryId, CancellationToken ct) => throw new NotSupportedException();
}
