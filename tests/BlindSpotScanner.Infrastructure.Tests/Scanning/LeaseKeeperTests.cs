using BlindSpotScanner.Infrastructure.Scanning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BlindSpotScanner.Infrastructure.Tests.Scanning;

public class LeaseKeeperTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly FakeTimeProvider _time = new();

    /// <summary>A heartbeat that answers from a script and signals each call.</summary>
    private sealed class ScriptedHeartbeat(params Func<bool>[] answers)
    {
        private int _calls;

        public SemaphoreSlim Called { get; } = new(0);

        public int Calls => _calls;

        public Task<bool> BeatAsync(CancellationToken ct)
        {
            var answer = answers[Math.Min(_calls, answers.Length - 1)];
            Interlocked.Increment(ref _calls);
            Called.Release();
            return Task.FromResult(answer());
        }
    }

    private async Task TickAsync(ScriptedHeartbeat heartbeat)
    {
        _time.Advance(Interval);
        (await heartbeat.Called.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("the heartbeat should run after each interval");
    }

    [Fact]
    public async Task Keep_TransientError_KeepsBeating()
    {
        // Arrange (the first heartbeat hits a database blip)
        var heartbeat = new ScriptedHeartbeat(() => throw new TimeoutException("blip"), () => true);
        using var scan = new CancellationTokenSource();
        var keeper = new LeaseKeeper(_time, Interval, NullLogger.Instance).KeepAsync(heartbeat.BeatAsync, scan);

        // Act
        await TickAsync(heartbeat);
        await TickAsync(heartbeat);

        // Assert
        new { heartbeat.Calls, Cancelled = scan.IsCancellationRequested }.Should().BeEquivalentTo(new { Calls = 2, Cancelled = false });
        await scan.CancelAsync();
        await keeper;
    }

    [Fact]
    public async Task Keep_LeaseLost_CancelsScan()
    {
        // Arrange
        var heartbeat = new ScriptedHeartbeat(() => false);
        using var scan = new CancellationTokenSource();
        var keeper = new LeaseKeeper(_time, Interval, NullLogger.Instance).KeepAsync(heartbeat.BeatAsync, scan);

        // Act
        await TickAsync(heartbeat);
        await keeper.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        scan.IsCancellationRequested.Should().BeTrue();
    }
}
