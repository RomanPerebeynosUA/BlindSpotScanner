using BlindSpotScanner.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using BlindSpotScanner.Application.Scanning;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

public sealed record StartScanRequest(string? Branch, decimal? MaxCostUsd);

public sealed record StartScanResponse(Guid RunId);

[ApiController]
[Route("api/repositories/{repoId:guid}/scans")]
public sealed class ScansController(ScanRequests scans, BlindSpotScanner.Application.ICurrentUser user) : ControllerBase
{
    /// <summary>Doc 10: the user accepts that code excerpts (scrubbed, denylisted paths excluded) go to the LLM; idempotent.</summary>
    [Authorize(Policy = Roles.Member)]
    [HttpPost("~/api/repositories/{repoId:guid}/llm-sharing")]
    public async Task<IActionResult> AcknowledgeLlmSharing(Guid repoId, CancellationToken ct) =>
        await scans.AcknowledgeLlmSharingAsync(repoId, user.Name, ct) ? NoContent() : NotFound();

    /// <summary>Starts a scan (async): it is queued and a worker runs it.</summary>
    [Authorize(Policy = Roles.Member)]
    [HttpPost]
    public async Task<IActionResult> Start(Guid repoId, [FromBody] StartScanRequest? request, CancellationToken ct)
    {
        var result = await scans.StartAsync(repoId, request?.Branch, request?.MaxCostUsd, ct);
        return result.Status switch
        {
            StartScanStatus.Accepted => Accepted(new StartScanResponse(result.RunId!.Value)),
            StartScanStatus.RepositoryNotFound => NotFound(),
            StartScanStatus.LlmSharingNotAcknowledged => Problem(
                "Accept that code excerpts are sent to the LLM (POST …/llm-sharing) before the first scan.", statusCode: StatusCodes.Status409Conflict, title: "LLM data sharing not acknowledged"),
            StartScanStatus.NoProfile => Problem("The repository has no profile yet.", statusCode: StatusCodes.Status409Conflict, title: "No profile"),
            _ => Problem("maxCostUsd must be greater than 0.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid scan request"),
        };
    }
}
