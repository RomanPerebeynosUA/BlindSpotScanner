using System.Text.Json;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlindSpotScanner.Infrastructure.Scanning;

/// <summary>A progress event as it travels from the worker to the api over PostgreSQL NOTIFY (D21).</summary>
/// <param name="Kind">"progress" or "completed".</param>
/// <param name="Status">The <see cref="RunStatus"/> name, for "completed".</param>
public sealed record ScanEvent(string Kind, Guid RunId, string? Phase, int? Pct, string? Message, string? Status)
{
    public const string Channel = "scan_events";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ScanEvent Progress(Guid runId, string phase, int pct, string message) => new("progress", runId, phase, pct, message, null);

    public static ScanEvent Completed(Guid runId, RunStatus status) => new("completed", runId, null, null, null, status.ToString());
}

/// <summary>Publishes scan events with <c>pg_notify</c>, so the api can relay them without a broker (doc 02).</summary>
public sealed class PostgresScanEvents(BlindSpotScannerDbContext db) : IScanEvents
{
    public Task ProgressAsync(Guid runId, string phase, int pct, string message, CancellationToken ct) =>
        NotifyAsync(ScanEvent.Progress(runId, phase, pct, message), ct);

    public Task CompletedAsync(Guid runId, RunStatus status, CancellationToken ct) =>
        NotifyAsync(ScanEvent.Completed(runId, status), ct);

    private async Task NotifyAsync(ScanEvent scanEvent, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(scanEvent, ScanEvent.Json);
        await db.Database.ExecuteSqlAsync($"SELECT pg_notify({ScanEvent.Channel}, {payload})", ct);
    }
}

/// <summary>Listens on its own connection (LISTEN needs one held open) and hands each event to a callback.</summary>
public sealed class ScanEventListener(string connectionString)
{
    public async Task ListenAsync(Func<ScanEvent, Task> onEvent, Action? onListening, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        var pending = new Queue<string>();
        connection.Notification += (_, e) => pending.Enqueue(e.Payload);
        await using (var listen = new NpgsqlCommand($"LISTEN {ScanEvent.Channel}", connection))
        {
            await listen.ExecuteNonQueryAsync(ct);
        }

        onListening?.Invoke();
        while (!ct.IsCancellationRequested)
        {
            // Notifications are raised on this flow while WaitAsync runs, so the queue needs no lock.
            await connection.WaitAsync(ct);
            while (pending.TryDequeue(out var payload))
            {
                if (JsonSerializer.Deserialize<ScanEvent>(payload, ScanEvent.Json) is { } scanEvent)
                {
                    try
                    {
                        await onEvent(scanEvent);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // One failed delivery must not drop the connection, or every later event is lost while it reconnects.
                    }
                }
            }
        }
    }
}
