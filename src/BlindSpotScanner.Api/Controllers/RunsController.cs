using BlindSpotScanner.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

public sealed record RunStatusResponse(Guid RunId, string Status, IReadOnlyList<string> FailureReasons);

public sealed record ScanStepResponse(string Phase, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int InputTokens, int OutputTokens, decimal CostUsd, int ReadInFull, string? Notes);

[ApiController]
[Route("api/runs/{runId:guid}")]
public sealed class RunsController(ScanRequests scans, Application.Repositories.RepositoryService repositories) : ControllerBase
{
    /// <summary>The run's repository (D23), so the report page can list the repository's other runs.</summary>
    [HttpGet("repository")]
    public async Task<IActionResult> Repository(Guid runId, CancellationToken ct) =>
        await repositories.FindByRunAsync(runId, ct) is { } repository ? Ok(RepositoryResponse.From(repository)) : NotFound();

    /// <summary>The report once the run succeeded; otherwise the run's status (OpenAPI).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid runId, CancellationToken ct)
    {
        if (await scans.FindAsync(runId, ct) is not { } run)
        {
            return NotFound();
        }

        // The stored report is already canonical JSON; it is served as stored, not re-serialized.
        return run is { Status: RunStatus.Succeeded, ReportJson: { } report }
            ? Content(report, "application/json")
            : Ok(new RunStatusResponse(run.Id, run.Status.ToString(), run.FailureReasons));
    }

    [HttpGet("steps")]
    public async Task<IActionResult> Steps(Guid runId, CancellationToken ct)
    {
        if (await scans.FindAsync(runId, ct) is null)
        {
            return NotFound();
        }

        var steps = await scans.StepsAsync(runId, ct);
        return Ok(steps.Select(s => new ScanStepResponse(s.Phase, s.Status.ToString(), s.StartedAt, s.CompletedAt, s.InputTokens, s.OutputTokens, s.CostUsd, s.ReadInFull, s.Notes)));
    }

    /// <summary>Cancels a queued or running scan; a finished one is left as it is.</summary>
    [Authorize(Policy = Roles.Member)]
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(Guid runId, CancellationToken ct) =>
        await scans.CancelAsync(runId, ct) ? Accepted() : NotFound();
}
