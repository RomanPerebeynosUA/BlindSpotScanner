using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Scanning;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Tests.Scanning;

/// <summary>Lease times come from the database clock, so expiry tests use short real leases.</summary>
[Collection("scan-queue")]
public sealed class PostgresScanQueueTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ShortLease = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PastShortLease = TimeSpan.FromMilliseconds(1500);
    private static readonly DateTimeOffset QueuedAt = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ScanRun> EnqueueAsync(int minutesAgo = 0)
    {
        var repository = await db.AddRepositoryAsync();
        var run = ScanRun.Enqueue(Guid.NewGuid(), repository.Id, null, 5m, 1, "claude-sonnet-5-5", "1.0.0", QueuedAt.AddMinutes(-minutesAgo));
        await using var context = db.CreateContext();
        await new PostgresScanQueue(context).EnqueueAsync(run, CancellationToken.None);
        return run;
    }

    private async Task<Guid?> DequeueAsync(Guid? owner = null, TimeSpan? lease = null)
    {
        await using var context = db.CreateContext();
        return await new PostgresScanQueue(context).TryDequeueAsync(owner ?? Guid.NewGuid(), lease ?? Lease, CancellationToken.None);
    }

    private async Task<bool> HeartbeatAsync(Guid runId, Guid owner, TimeSpan? lease = null)
    {
        await using var context = db.CreateContext();
        return await new PostgresScanQueue(context).HeartbeatAsync(runId, owner, lease ?? Lease, CancellationToken.None);
    }

    private async Task<ScanRun> LoadAsync(Guid id)
    {
        await using var context = db.CreateContext();
        return await context.ScanRuns.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<IReadOnlyList<(Guid RunId, RunStatus Status)>> FailAbandonedAsync()
    {
        await using var context = db.CreateContext();
        return await new PostgresScanQueue(context).FailAbandonedAsync(CancellationToken.None);
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
        new { Taken = taken, run.Status, run.Attempts, NewerStatus = (await LoadAsync(newer.Id)).Status }.Should().BeEquivalentTo(new
        {
            Taken = (Guid?)older.Id,
            Status = RunStatus.Running,
            Attempts = 1,
            NewerStatus = RunStatus.Queued,
        });
        run.LeasedUntil.Should().BeCloseTo(DateTimeOffset.UtcNow + Lease, TimeSpan.FromSeconds(30));
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
        await DequeueAsync(lease: ShortLease);
        await Task.Delay(PastShortLease);
        var retried = await DequeueAsync(lease: ShortLease);
        await Task.Delay(PastShortLease);
        var third = await DequeueAsync(lease: ShortLease);

        // Act
        var abandoned = await FailAbandonedAsync();

        // Assert
        var failed = await LoadAsync(run.Id);
        new { Retried = retried, Third = third, Abandoned = abandoned, failed.Status, failed.FailureReasons }.Should().BeEquivalentTo(new
        {
            Retried = (Guid?)run.Id,
            Third = (Guid?)null,
            Abandoned = new[] { (run.Id, RunStatus.Failed) },
            Status = RunStatus.Failed,
            FailureReasons = new[] { "worker lease expired twice; the scan was abandoned" },
        });
    }

    [Fact]
    public async Task FailAbandoned_CancelRequested_EndsCancelled()
    {
        // Arrange (cancel was requested, then both attempts' workers died)
        var run = await EnqueueAsync();
        await DequeueAsync(lease: ShortLease);
        await using (var context = db.CreateContext())
        {
            var running = await context.ScanRuns.SingleAsync(r => r.Id == run.Id);
            running.RequestCancel();
            await context.SaveChangesAsync();
        }

        await Task.Delay(PastShortLease);
        await DequeueAsync(lease: ShortLease);
        await Task.Delay(PastShortLease);

        // Act
        var abandoned = await FailAbandonedAsync();

        // Assert
        new { Abandoned = abandoned, (await LoadAsync(run.Id)).Status }.Should().BeEquivalentTo(new
        {
            Abandoned = new[] { (run.Id, RunStatus.Cancelled) },
            Status = RunStatus.Cancelled,
        });
    }

    [Fact]
    public async Task Heartbeat_ExtendsLease()
    {
        // Arrange
        var run = await EnqueueAsync();
        var owner = Guid.NewGuid();
        await DequeueAsync(owner, lease: TimeSpan.FromMinutes(1));
        var before = (await LoadAsync(run.Id)).LeasedUntil;

        // Act
        var alive = await HeartbeatAsync(run.Id, owner, TimeSpan.FromMinutes(10));

        // Assert
        new { Alive = alive, Extended = (await LoadAsync(run.Id)).LeasedUntil > before + TimeSpan.FromMinutes(5) }.Should().BeEquivalentTo(new { Alive = true, Extended = true });
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
        var alive = await HeartbeatAsync(run.Id, Guid.NewGuid());

        // Assert
        alive.Should().BeFalse();
    }

    [Fact]
    public async Task Heartbeat_LeaseTakenByAnotherWorker_ReturnsFalse()
    {
        // Arrange (worker A stalls past its lease; worker B retries the run)
        var run = await EnqueueAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await DequeueAsync(a, ShortLease);
        await Task.Delay(PastShortLease);
        await DequeueAsync(b);

        // Act
        var alive = new { A = await HeartbeatAsync(run.Id, a), B = await HeartbeatAsync(run.Id, b) };

        // Assert
        alive.Should().BeEquivalentTo(new { A = false, B = true });
    }

    [Fact]
    public async Task Save_StatusChangedByOthers_IsRejected()
    {
        // Arrange (a worker holds the run while the reaper fails it)
        var run = await EnqueueAsync();
        await DequeueAsync();
        await using var worker = db.CreateContext();
        var held = await worker.ScanRuns.SingleAsync(r => r.Id == run.Id);
        await using (var other = db.CreateContext())
        {
            await other.Database.ExecuteSqlAsync($"UPDATE scan_runs SET status = 'Failed' WHERE id = {run.Id}");
        }

        // Act
        held.Cancel();
        var act = () => worker.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
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
