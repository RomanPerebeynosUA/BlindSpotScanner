using System.Text.Json;
using System.Text.Json.Nodes;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Reports;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

[ApiController]
[Route("api/runs/compare")]
public sealed class CompareController(RunComparison comparison) : ControllerBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The delta between two runs of one repository (doc 08), always old → new (D23). The delta is
    /// written in the report's own JSON format, so it reads exactly like a stored report delta.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Compare([FromQuery(Name = "base")] Guid baseRunId, [FromQuery(Name = "target")] Guid targetRunId, CancellationToken ct)
    {
        var result = await comparison.CompareAsync(baseRunId, targetRunId, ct);
        return result.Status switch
        {
            CompareStatus.Compared => Content(Json(result.Comparison!).ToJsonString(), "application/json"),
            CompareStatus.NotFound => NotFound(),
            CompareStatus.SameRun => Problem("Pick two different runs.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid comparison"),
            CompareStatus.DifferentRepositories => Problem("Runs from different repositories can't be compared.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid comparison"),
            _ => Problem("Only succeeded scans can be compared.", statusCode: StatusCodes.Status409Conflict, title: "No report"),
        };
    }

    private static JsonObject Json(Comparison c) => new()
    {
        ["base"] = JsonSerializer.SerializeToNode(c.Base, Web),
        ["target"] = JsonSerializer.SerializeToNode(c.Target, Web),
        ["swapped"] = c.Swapped,
        ["adHoc"] = c.AdHoc,
        ["delta"] = JsonSerializer.SerializeToNode(c.Delta, ReportJsonSerializer.Options),
    };
}
