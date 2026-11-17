using Microsoft.Extensions.Logging;

namespace BlindSpotScanner.Infrastructure.Scanning;

/// <summary>
/// Keeps a scan's lease alive while it runs. A failed heartbeat (a database blip) is logged and
/// retried at the next interval; only a definite "lease lost" (the run is no longer this worker's)
/// cancels the scan, so a stale worker never keeps running next to the one that took over.
/// </summary>
public sealed partial class LeaseKeeper(TimeProvider time, TimeSpan interval, ILogger logger)
{
    public async Task KeepAsync(Func<CancellationToken, Task<bool>> heartbeat, CancellationTokenSource scan)
    {
        try
        {
            while (!scan.IsCancellationRequested)
            {
                await Task.Delay(interval, time, scan.Token);
                bool alive;
                try
                {
                    alive = await heartbeat(scan.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogHeartbeatError(ex.GetType().Name);
                    continue;
                }

                if (!alive)
                {
                    await scan.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The scan ended or was cancelled.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A lease heartbeat failed with {ExceptionType}; retrying at the next interval")]
    private partial void LogHeartbeatError(string exceptionType);
}
