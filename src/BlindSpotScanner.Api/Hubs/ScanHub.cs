using Microsoft.AspNetCore.SignalR;

namespace BlindSpotScanner.Api.Hubs;

/// <summary>
/// <c>/hubs/scans</c> (doc 03): a client subscribes to a run and receives
/// <c>ScanProgress(runId, phase, pct, message)</c> and <c>ScanCompleted(runId, status)</c>.
/// </summary>
public sealed class ScanHub : Hub
{
    public static string Group(Guid runId) => $"run:{runId}";

    public Task Subscribe(Guid runId) => Groups.AddToGroupAsync(Context.ConnectionId, Group(runId));

    public Task Unsubscribe(Guid runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(runId));
}
