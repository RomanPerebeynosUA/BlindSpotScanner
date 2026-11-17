using BlindSpotScanner.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

public sealed record ActionResponse(Guid Id, string Type, string StableKey, string ExternalKey, string Url, string Status, DateTimeOffset CreatedAt)
{
    public static ActionResponse From(RemediationAction a) => new(a.Id, a.Type.ToString(), a.StableKey, a.ExternalKey, a.Url, a.Status, a.CreatedAt);
}

public sealed record JiraDraftResponse(
    string Summary, string Description, string ProjectKey, string IssueType, string Priority, IReadOnlyList<string> Labels, string StableKey, ActionResponse? Existing)
{
    public static JiraDraftResponse From(JiraDraft d) =>
        new(d.Summary, d.Description, d.ProjectKey, d.IssueType, d.Priority, d.Labels, d.StableKey, d.Existing is null ? null : ActionResponse.From(d.Existing));
}

/// <summary>Doc 09 Jira tickets: an editable preview, then creation only on the user's explicit request (principle 8).</summary>
[Authorize(Policy = Roles.Member)]
[ApiController]
[Route("api/runs/{runId:guid}/findings/{findingId}")]
public sealed class JiraController(JiraTickets tickets) : ControllerBase
{
    [HttpPost("jira-preview")]
    public async Task<IActionResult> Preview(Guid runId, string findingId, CancellationToken ct)
    {
        var result = await tickets.PreviewAsync(runId, findingId, ct);
        return result.Status == TicketStatus.Ok ? Ok(JiraDraftResponse.From(result.Value!)) : Failure(result.Status);
    }

    [HttpPost("jira")]
    public async Task<IActionResult> Create(Guid runId, string findingId, [FromBody] JiraCreate request, CancellationToken ct)
    {
        TicketResult<RemediationAction> result;
        try
        {
            result = await tickets.CreateAsync(runId, findingId, request, ct);
        }
        catch (JiraException e)
        {
            return Problem(e.Message, statusCode: StatusCodes.Status502BadGateway, title: "Jira error");
        }

        return result.Status switch
        {
            TicketStatus.Ok => StatusCode(StatusCodes.Status201Created, ActionResponse.From(result.Value!)),
            TicketStatus.Exists => Exists(result.Existing!),
            _ => Failure(result.Status),
        };
    }

    private ObjectResult Exists(RemediationAction existing)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Ticket already exists",
            Detail = $"{existing.ExternalKey} already tracks this finding.",
        };
        problem.Extensions["existing"] = ActionResponse.From(existing);
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }

    private IActionResult Failure(TicketStatus status) => status switch
    {
        TicketStatus.NotFound => NotFound(),
        TicketStatus.NoReport => Problem("Only succeeded scans have findings to ticket.", statusCode: StatusCodes.Status409Conflict, title: "No report"),
        _ => Problem("Set Jira:BaseUrl, Jira:Email, Jira:ApiToken and Jira:ProjectKey.", statusCode: StatusCodes.Status503ServiceUnavailable, title: "Jira not configured"),
    };
}
