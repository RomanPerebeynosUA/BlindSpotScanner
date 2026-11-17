using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.Scanning;

/// <summary>
/// Runs scans from the queue (doc 02), <c>Scan:MaxConcurrentScans</c> at a time. Each scan keeps its
/// lease alive with a heartbeat; a cancel request or a lost lease cancels it at the next phase.
/// Failures are logged by type and run id only: messages may name repositories, never secrets.
/// It lives here, not in the Worker project, so the end-to-end tests can host it next to the api.
/// </summary>
public sealed partial class ScanWorker(IServiceScopeFactory scopes, IOptions<ScanOptions> options, TimeProvider time, ILogger<ScanWorker> logger) : BackgroundService
{
    private ScanOptions Options => options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, Options.MaxConcurrentScans)).Select(_ => SlotAsync(stoppingToken)));

    private async Task SlotAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var runId = await TakeNextAsync(stoppingToken);
                if (runId is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Options.PollMilliseconds), time, stoppingToken);
                    continue;
                }

                await RunAsync(runId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogSlotError(ex.GetType().Name);
                await Task.Delay(TimeSpan.FromMilliseconds(Options.PollMilliseconds), time, stoppingToken);
            }
        }
    }

    private async Task<Guid?> TakeNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IScanQueue>();
        var events = scope.ServiceProvider.GetRequiredService<IScanEvents>();
        foreach (var abandoned in await queue.FailAbandonedAsync(ct))
        {
            await events.CompletedAsync(abandoned, RunStatus.Failed, ct);
        }

        return await queue.TryDequeueAsync(TimeSpan.FromSeconds(Options.LeaseSeconds), ct);
    }

    private async Task RunAsync(Guid runId, CancellationToken stoppingToken)
    {
        using var scan = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = HeartbeatAsync(runId, scan);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ScanOrchestrator>().RunAsync(runId, scan.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: the run stays Running and its lease expires into a retry (doc 10).
        }
        catch (Exception ex)
        {
            LogScanError(runId, ex.GetType().Name);
        }
        finally
        {
            await scan.CancelAsync();
            await heartbeat;
        }
    }

    /// <summary>Extends the lease until the scan ends; cancels the scan when the lease is lost or a cancel is requested.</summary>
    private async Task HeartbeatAsync(Guid runId, CancellationTokenSource scan)
    {
        try
        {
            while (!scan.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Options.HeartbeatSeconds), time, scan.Token);
                await using var scope = scopes.CreateAsyncScope();
                var alive = await scope.ServiceProvider.GetRequiredService<IScanQueue>().HeartbeatAsync(runId, TimeSpan.FromSeconds(Options.LeaseSeconds), scan.Token);
                var cancel = await scope.ServiceProvider.GetRequiredService<IScanStore>().IsCancelRequestedAsync(runId, scan.Token);
                if (!alive || cancel)
                {
                    await scan.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The scan ended.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Scan {RunId} ended with an unexpected {ExceptionType}")]
    private partial void LogScanError(Guid runId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "The scan loop hit an unexpected {ExceptionType}; retrying")]
    private partial void LogSlotError(string exceptionType);
}
