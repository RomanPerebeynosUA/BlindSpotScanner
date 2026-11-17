using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Scanning;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Scanning;

[Collection("postgres")]
public class ScanEventsTests(PostgresFixture db)
{
    [Fact]
    public async Task Notify_Progress_ReachesListener()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var received = new TaskCompletionSource<ScanEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listener = new ScanEventListener(db.ConnectionString).ListenAsync(
            e =>
            {
                received.TrySetResult(e);
                return Task.CompletedTask;
            },
            onListening: () => listening.TrySetResult(),
            stop.Token);
        await listening.Task.WaitAsync(stop.Token);

        // Act
        await using (var context = db.CreateContext())
        {
            await new PostgresScanEvents(context).ProgressAsync(runId, "P3", 25, "P3 started", CancellationToken.None);
        }

        // Assert
        var scanEvent = await received.Task.WaitAsync(stop.Token);
        await stop.CancelAsync();
        await listener.ContinueWith(_ => { }, TaskScheduler.Default);
        scanEvent.Should().Be(new ScanEvent("progress", runId, "P3", 25, "P3 started", null));
    }

    [Fact]
    public void Event_Completed_CarriesStatusName()
    {
        // Act
        var scanEvent = ScanEvent.Completed(Guid.Empty, RunStatus.Cancelled);

        // Assert
        scanEvent.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Listen_CallbackThrows_KeepsListening()
    {
        // Arrange (relaying the first event fails, e.g. a SignalR send error)
        var runId = Guid.NewGuid();
        var second = new TaskCompletionSource<ScanEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listener = new ScanEventListener(db.ConnectionString).ListenAsync(
            e => ++calls == 1 ? throw new InvalidOperationException("send failed") : Task.FromResult(second.TrySetResult(e)),
            onListening: () => listening.TrySetResult(),
            stop.Token);
        await listening.Task.WaitAsync(stop.Token);

        // Act
        await using (var context = db.CreateContext())
        {
            var events = new PostgresScanEvents(context);
            await events.ProgressAsync(runId, "P1", 0, "P1 started", CancellationToken.None);
            await events.ProgressAsync(runId, "P2", 12, "P2 started", CancellationToken.None);
        }

        // Assert
        var received = await second.Task.WaitAsync(stop.Token);
        await stop.CancelAsync();
        await listener.ContinueWith(_ => { }, TaskScheduler.Default);
        received.Phase.Should().Be("P2");
    }
}
