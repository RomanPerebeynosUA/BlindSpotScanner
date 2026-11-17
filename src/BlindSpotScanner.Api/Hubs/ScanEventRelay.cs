using BlindSpotScanner.Infrastructure.Scanning;
using Microsoft.AspNetCore.SignalR;

namespace BlindSpotScanner.Api.Hubs;

/// <summary>Relays the worker's scan events (PostgreSQL NOTIFY) to the subscribers of each run.</summary>
public sealed partial class ScanEventRelay(ScanEventListener listener, IHubContext<ScanHub> hub, TimeProvider time, ILogger<ScanEventRelay> logger) : BackgroundService
{
    private static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(2);

    /// <summary>Completes once the relay is listening (tests wait for it before starting a scan).</summary>
    public Task Listening => _listening.Task;

    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await listener.ListenAsync(RelayAsync, () => _listening.TrySetResult(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The database may still be starting; listen again shortly.
                LogListenError(ex.GetType().Name);
                await Task.Delay(Reconnect, time, stoppingToken);
            }
        }
    }

    private Task RelayAsync(ScanEvent e) => e.Kind == "completed"
        ? hub.Clients.Group(ScanHub.Group(e.RunId)).SendAsync("ScanCompleted", e.RunId, e.Status)
        : hub.Clients.Group(ScanHub.Group(e.RunId)).SendAsync("ScanProgress", e.RunId, e.Phase, e.Pct, e.Message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listening for scan events failed with {ExceptionType}; retrying")]
    private partial void LogListenError(string exceptionType);
}
