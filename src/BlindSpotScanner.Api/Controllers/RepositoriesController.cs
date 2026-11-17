using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

public sealed record CreateRepositoryRequest(string? Url, string? DefaultBranch);

public sealed record RepositoryResponse(Guid Id, string Owner, string Name, string FullName, string DefaultBranch, string HtmlUrl)
{
    public static RepositoryResponse From(Repository r) => new(r.Id, r.Owner, r.Name, $"{r.Owner}/{r.Name}", r.DefaultBranch, r.HtmlUrl);
}

public sealed record RunHistoryItemResponse(
    Guid RunId, string Status, string Branch, string CommitSha, DateTimeOffset? QueuedAt, DateTimeOffset? GeneratedAt,
    decimal? Score, decimal? WeightAssessablePct, int? EndpointsTotal, int? EndpointsCovered, int? EndpointsWeak, int? EndpointsNone, int? EndpointsUnverified,
    decimal CostUsd, IReadOnlyList<string> FailureReasons)
{
    public static RunHistoryItemResponse From(ScanRun r) => new(
        r.Id, r.Status.ToString(), r.Branch, r.CommitSha, r.QueuedAt, r.GeneratedAt, r.Score, r.WeightAssessablePct,
        r.EndpointsTotal, r.EndpointsCovered, r.EndpointsWeak, r.EndpointsNone, r.EndpointsUnverified, r.CostUsd, r.FailureReasons);
}

[ApiController]
[Route("api/repositories")]
public sealed class RepositoriesController(RepositoryService repositories, Application.Remediation.RemediationActions actions) : ControllerBase
{
    /// <summary>The repository's Jira tickets and fix PRs, statuses refreshed when older than 10 minutes (D24, D25).</summary>
    [HttpGet("{id:guid}/actions")]
    public async Task<IActionResult> Actions(Guid id, CancellationToken ct) =>
        await repositories.GetAsync(id, ct) is null ? NotFound() : Ok((await actions.ActionsAsync(id, ct)).Select(ActionResponse.From));

    [HttpGet]
    public async Task<IReadOnlyList<RepositorySummary>> List(CancellationToken ct) => await repositories.ListAsync(ct);

    /// <summary>Registers a repository by its GitHub URL and gives it profile v1 (D22).</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] CreateRepositoryRequest request, CancellationToken ct)
    {
        var result = await repositories.AddAsync(request.Url ?? string.Empty, request.DefaultBranch, ct);
        return result.Status switch
        {
            AddRepositoryStatus.Created => CreatedAtAction(nameof(Get), new { id = result.Repository!.Id }, RepositoryResponse.From(result.Repository)),
            AddRepositoryStatus.AlreadyRegistered => AlreadyRegistered(result.Repository!),
            AddRepositoryStatus.NotFound => Problem(result.Error, statusCode: StatusCodes.Status404NotFound, title: "Repository not accessible"),
            AddRepositoryStatus.GitHubUnavailable => Problem(result.Error, statusCode: StatusCodes.Status502BadGateway, title: "GitHub is unavailable"),
            _ => Problem(result.Error, statusCode: StatusCodes.Status400BadRequest, title: "Invalid repository"),
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await repositories.GetAsync(id, ct) is { } repository ? Ok(RepositoryResponse.From(repository)) : NotFound();

    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> Runs(Guid id, CancellationToken ct) =>
        await repositories.RunsAsync(id, ct) is { } runs ? Ok(runs.Select(RunHistoryItemResponse.From)) : NotFound();

    /// <summary>409 with the registered repository's id, so the UI can open it.</summary>
    private ObjectResult AlreadyRegistered(Repository existing)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext, StatusCodes.Status409Conflict, "Repository already registered", detail: $"{existing.Owner}/{existing.Name} is already registered.");
        problem.Extensions["id"] = existing.Id;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict, ContentTypes = { "application/problem+json" } };
    }
}
