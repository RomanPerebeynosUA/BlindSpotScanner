using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Scanning;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace BlindSpotScanner.Infrastructure.Tests.Scanning;

[Collection("scan-queue")]
public sealed class ScanRequestsTests(PostgresFixture db) : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Cancel_RunDequeuedMeanwhile_FlagsItInsteadOfCancelling()
    {
        // Arrange (a worker takes the run between the API reading it and saving the cancel)
        var repository = await db.AddRepositoryAsync();
        var run = ScanRun.Enqueue(Guid.NewGuid(), repository.Id, null, 5m, 1, "claude-sonnet-5-5", "1.0.0", _time.GetUtcNow());
        await using (var setup = db.CreateContext())
        {
            await new PostgresScanQueue(setup).EnqueueAsync(run, CancellationToken.None);
        }

        await using var context = db.CreateContext();
        var store = new DequeueAfterFirstFind(new EfScanRequestStore(context), db);
        var events = new CountingEvents();
        var requests = new ScanRequests(store, new PostgresScanQueue(context), events, _time, new ScanRequestOptions("claude-sonnet-5-5", "1.0.0", 5m));

        // Act
        var found = await requests.CancelAsync(run.Id, CancellationToken.None);

        // Assert
        await using var read = db.CreateContext();
        var loaded = await read.ScanRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        new { found, loaded.Status, loaded.CancelRequested, events.Completed }.Should().BeEquivalentTo(new
        {
            found = true,
            Status = RunStatus.Running,
            CancelRequested = true,
            Completed = 0,
        });
    }

    private sealed class DequeueAfterFirstFind(IScanRequestStore inner, PostgresFixture db) : IScanRequestStore
    {
        private bool _dequeued;

        public async Task<ScanRun?> FindRunAsync(Guid runId, CancellationToken ct)
        {
            var run = await inner.FindRunAsync(runId, ct);
            if (!_dequeued)
            {
                _dequeued = true;
                await using var worker = db.CreateContext();
                await new PostgresScanQueue(worker).TryDequeueAsync(Guid.NewGuid(), TimeSpan.FromMinutes(2), ct);
            }

            return run;
        }

        public Task<bool> RepositoryExistsAsync(Guid repositoryId, CancellationToken ct) => inner.RepositoryExistsAsync(repositoryId, ct);

        public Task<bool> LlmSharingAcknowledgedAsync(Guid repositoryId, CancellationToken ct) => inner.LlmSharingAcknowledgedAsync(repositoryId, ct);

        public Task<bool> AcknowledgeLlmSharingAsync(Guid repositoryId, string by, DateTimeOffset at, CancellationToken ct) => inner.AcknowledgeLlmSharingAsync(repositoryId, by, at, ct);

        public Task<int?> LatestProfileVersionAsync(Guid repositoryId, CancellationToken ct) => inner.LatestProfileVersionAsync(repositoryId, ct);

        public Task<IReadOnlyList<ScanStep>> StepsAsync(Guid runId, CancellationToken ct) => inner.StepsAsync(runId, ct);

        public Task<bool> SaveChangesAsync(CancellationToken ct) => inner.SaveChangesAsync(ct);

        public Task ReloadAsync(ScanRun run, CancellationToken ct) => inner.ReloadAsync(run, ct);
    }

    private sealed class CountingEvents : IScanEvents
    {
        public int Completed { get; private set; }

        public Task ProgressAsync(Guid runId, string phase, int pct, string message, CancellationToken ct) => Task.CompletedTask;

        public Task CompletedAsync(Guid runId, RunStatus status, CancellationToken ct)
        {
            Completed++;
            return Task.CompletedTask;
        }
    }
}
