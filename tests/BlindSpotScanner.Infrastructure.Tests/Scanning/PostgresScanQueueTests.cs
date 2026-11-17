using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Scanning;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace BlindSpotScanner.Infrastructure.Tests.Scanning;

[Collection("scan-queue")]
public sealed class PostgresScanQueueTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ScanRun> EnqueueAsync(int minutesAgo = 0)
    {
        var repository = await db.AddRepositoryAsync();
        var run = ScanRun.Enqueue(Guid.NewGuid(), repository.Id, null, 5m, 1, "claude-sonnet-5-5", "1.0.0", _time.GetUtcNow().AddMinutes(-minutesAgo));
        await using var context = db.CreateContext();
        await new PostgresScanQueue(context, _time).EnqueueAsync(run, CancellationToken.None);
        return run;
    }

    private async Task<Guid?> DequeueAsync()
    {
        await using var context = db.CreateContext();
        return await new PostgresScanQueue(context, _time).TryDequeueAsync(Lease, CancellationToken.None);
    }

    private async Task<ScanRun> LoadAsync(Guid id)
    {
        await using var context = db.CreateContext();
        return await context.ScanRuns.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    [Fact]
    public async Task Dequeue_Queued_TakesOldestAndLeases()
    {
        // Arrange
        var newer = await EnqueueAsync(minutesAgo: 1);
        var older = await EnqueueAsync(minutesAgo: 5);

        // Act
        var taken = await DequeueAsync();

        // Assert
        var run = await LoadAsync(older.Id);
        new { Taken = taken, run.Status, run.Attempts, run.LeasedUntil, NewerStatus = (await LoadAsync(newer.Id)).Status }.Should().BeEquivalentTo(new
        {
            Taken = (Guid?)older.Id,
            Status = RunStatus.Running,
            Attempts = 1,
            LeasedUntil = (DateTimeOffset?)_time.GetUtcNow().Add(Lease),
            NewerStatus = RunStatus.Queued,
        });
    }

    [Fact]
    public async Task Dequeue_Empty_ReturnsNull()
    {
        // Act
        var taken = await DequeueAsync();

        // Assert
        taken.Should().BeNull();
    }

    [Fact]
    public async Task Dequeue_TwoWorkersConcurrently_GetDistinctJobs()
    {
        // Arrange
        var first = await EnqueueAsync(minutesAgo: 2);
        var second = await EnqueueAsync(minutesAgo: 1);

        // Act
        var taken = await Task.WhenAll(DequeueAsync(), DequeueAsync());

        // Assert
        taken.Should().BeEquivalentTo(new Guid?[] { first.Id, second.Id });
    }

    [Fact]
    public async Task Dequeue_ExpiredLease_RetriedOnceThenFailed()
    {
        // Arrange (the worker dies twice: each lease runs out without a heartbeat)
        var run = await EnqueueAsync();
        await DequeueAsync();
        _time.Advance(Lease + TimeSpan.FromSeconds(1));
        var retried = await DequeueAsync();
        _time.Advance(Lease + TimeSpan.FromSeconds(1));
        var third = await DequeueAsync();

        // Act
        await using var context = db.CreateContext();
        var abandoned = await new PostgresScanQueue(context, _time).FailAbandonedAsync(CancellationToken.None);

        // Assert
        var failed = await LoadAsync(run.Id);
        new { Retried = retried, Third = third, Abandoned = abandoned, failed.Status, failed.FailureReasons }.Should().BeEquivalentTo(new
        {
            Retried = (Guid?)run.Id,
            Third = (Guid?)null,
            Abandoned = new[] { run.Id },
            Status = RunStatus.Failed,
            FailureReasons = new[] { "worker lease expired twice; the scan was abandoned" },
        });
    }

    [Fact]
    public async Task Heartbeat_ExtendsLease()
    {
        // Arrange
        var run = await EnqueueAsync();
        await DequeueAsync();
        _time.Advance(TimeSpan.FromSeconds(30));

        // Act
        await using var context = db.CreateContext();
        var alive = await new PostgresScanQueue(context, _time).HeartbeatAsync(run.Id, Lease, CancellationToken.None);

        // Assert
        new { Alive = alive, (await LoadAsync(run.Id)).LeasedUntil }.Should().BeEquivalentTo(new { Alive = true, LeasedUntil = (DateTimeOffset?)_time.GetUtcNow().Add(Lease) });
    }

    [Fact]
    public async Task Heartbeat_CancelledRun_ReturnsFalse()
    {
        // Arrange
        var run = await EnqueueAsync();
        await using (var cancel = db.CreateContext())
        {
            var tracked = await cancel.ScanRuns.SingleAsync(r => r.Id == run.Id);
            tracked.RequestCancel();
            await cancel.SaveChangesAsync();
        }

        // Act
        await using var context = db.CreateContext();
        var alive = await new PostgresScanQueue(context, _time).HeartbeatAsync(run.Id, Lease, CancellationToken.None);

        // Assert
        alive.Should().BeFalse();
    }

    [Fact]
    public async Task RequestCancel_RunningRun_SetsFlag()
    {
        // Arrange
        var run = await EnqueueAsync();
        await DequeueAsync();

        // Act
        await using (var context = db.CreateContext())
        {
            var running = await context.ScanRuns.SingleAsync(r => r.Id == run.Id);
            running.RequestCancel();
            await context.SaveChangesAsync();
        }

        // Assert
        var loaded = await LoadAsync(run.Id);
        new { loaded.Status, loaded.CancelRequested }.Should().BeEquivalentTo(new { Status = RunStatus.Running, CancelRequested = true });
    }

    [Fact]
    public async Task Pin_RunningRun_SetsBranchAndSha()
    {
        // Arrange
        var run = await EnqueueAsync();
        await DequeueAsync();

        // Act
        await using (var context = db.CreateContext())
        {
            var running = await context.ScanRuns.SingleAsync(r => r.Id == run.Id);
            running.Pin("main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678");
            await context.SaveChangesAsync();
        }

        // Assert
        var loaded = await LoadAsync(run.Id);
        new { loaded.Branch, loaded.CommitSha }.Should().BeEquivalentTo(new { Branch = "main", CommitSha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678" });
    }
}
