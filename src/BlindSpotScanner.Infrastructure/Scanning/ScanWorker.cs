using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.Scanning;

/// <summary>
/// Runs scans from the queue (doc 02), <c>Scan:MaxConcurrentScans</c> at a time. Each dequeue gets
/// its own lease token, kept alive by a <see cref="LeaseKeeper"/>; a lost lease cancels the scan.
/// A cancel request is honoured by the orchestrator at the next phase boundary (D21).
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
                var job = await TakeNextAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Options.PollMilliseconds), time, stoppingToken);
                    continue;
                }

                await RunAsync(job.Value.RunId, job.Value.Owner, stoppingToken);
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

    private async Task<(Guid RunId, Guid Owner)?> TakeNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IScanQueue>();
        var events = scope.ServiceProvider.GetRequiredService<IScanEvents>();
        foreach (var (abandoned, status) in await queue.FailAbandonedAsync(ct))
        {
            await events.CompletedAsync(abandoned, status, ct);
        }

        var owner = Guid.NewGuid();
        return await queue.TryDequeueAsync(owner, TimeSpan.FromSeconds(Options.LeaseSeconds), ct) is { } runId ? (runId, owner) : null;
    }

    private async Task RunAsync(Guid runId, Guid owner, CancellationToken stoppingToken)
    {
        using var scan = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = new LeaseKeeper(time, TimeSpan.FromSeconds(Options.HeartbeatSeconds), logger).KeepAsync(
            async ct =>
            {
                await using var scope = scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<IScanQueue>().HeartbeatAsync(runId, owner, TimeSpan.FromSeconds(Options.LeaseSeconds), ct);
            },
            scan);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ScanOrchestrator>().RunAsync(runId, scan.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or the lease was lost: the run is left to its lease (a retry, or the worker that took it over).
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Scan {RunId} ended with an unexpected {ExceptionType}")]
    private partial void LogScanError(Guid runId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "The scan loop hit an unexpected {ExceptionType}; retrying")]
    private partial void LogSlotError(string exceptionType);
}
