using BlindSpotScanner.Application.Scanning;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

public sealed record StartScanRequest(string? Branch, decimal? MaxCostUsd);

public sealed record StartScanResponse(Guid RunId);

[ApiController]
[Route("api/repositories/{repoId:guid}/scans")]
public sealed class ScansController(ScanRequests scans) : ControllerBase
{
    /// <summary>Starts a scan (async): it is queued and a worker runs it.</summary>
    [HttpPost]
    public async Task<IActionResult> Start(Guid repoId, [FromBody] StartScanRequest? request, CancellationToken ct)
    {
        var result = await scans.StartAsync(repoId, request?.Branch, request?.MaxCostUsd, ct);
        return result.Status switch
        {
            StartScanStatus.Accepted => Accepted(new StartScanResponse(result.RunId!.Value)),
            StartScanStatus.RepositoryNotFound => NotFound(),
            StartScanStatus.NoProfile => Problem("The repository has no profile yet.", statusCode: StatusCodes.Status409Conflict, title: "No profile"),
            _ => Problem("maxCostUsd must be greater than 0.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid scan request"),
        };
    }
}
