using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Remediation;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

/// <summary>A fix file as the prompt and the web app spell it: <c>action</c> is "create" or "modify".</summary>
public sealed record FixFileResponse(string Path, string Action, string Content, string? Original);

public sealed record FixDraftResponse(
    IReadOnlyList<FixFileResponse> Files, string Rationale, IReadOnlyList<string> Assumptions, IReadOnlyList<string> RiskNotes,
    string Branch, string Title, string Body, decimal CostUsd, ActionResponse? Existing, string BaseSha)
{
    public static FixDraftResponse From(FixDraft d) => new(
        [.. d.Files.Select(f => new FixFileResponse(f.Path, Name(f.Action), f.Content, f.Original))],
        d.Rationale, d.Assumptions, d.RiskNotes, d.Branch, d.Title, d.Body, d.CostUsd, d.Existing is null ? null : ActionResponse.From(d.Existing), d.BaseSha);

    public static string Name(FixAction action) => action == FixAction.Modify ? "modify" : "create";
}

public sealed record FixFileRequest(string? Path, string? Action, string? Content);

public sealed record FixCreateRequest(IReadOnlyList<FixFileRequest>? Files, string? Branch, string? Title, string? Body, bool Force, string? BaseSha);

/// <summary>Doc 09 fix PRs (D25): a generated, guarded, editable draft, then a draft PR only on the user's request (principle 8).</summary>
[ApiController]
[Route("api/runs/{runId:guid}/findings/{findingId}")]
public sealed class FixController(FixPullRequests fixes) : ControllerBase
{
    [HttpPost("fix-preview")]
    /// <param name="regenerate">Generate from the default branch head (after "files changed", D25).</param>
    public async Task<IActionResult> Preview(Guid runId, string findingId, [FromQuery] bool regenerate, CancellationToken ct)
    {
        FixResult<FixDraft> result;
        try
        {
            result = await fixes.PreviewAsync(runId, findingId, regenerate, ct);
        }
        catch (Exception e) when (IsGitHubFailure(e))
        {
            return GitHubError(e);
        }

        return result.Status == FixStatus.Ok ? Ok(FixDraftResponse.From(result.Value!)) : Failure(result.Status, result.Reasons, result.Existing);
    }

    [HttpPost("pull-request")]
    public async Task<IActionResult> Create(Guid runId, string findingId, [FromBody] FixCreateRequest request, CancellationToken ct)
    {
        var files = (request.Files ?? []).Select(f => new FixFile(f.Path ?? string.Empty, f.Action == "modify" ? FixAction.Modify : FixAction.Create, f.Content ?? string.Empty)).ToList();
        FixResult<Domain.Entities.RemediationAction> result;
        try
        {
            result = await fixes.CreateAsync(runId, findingId, new FixCreate(files, request.Branch ?? string.Empty, request.Title ?? string.Empty, request.Body ?? string.Empty, request.Force, request.BaseSha ?? string.Empty), ct);
        }
        catch (GitHubBranchExistsException e)
        {
            return Problem(e.Message, statusCode: StatusCodes.Status409Conflict, title: "Branch exists");
        }
        catch (Exception e) when (IsGitHubFailure(e))
        {
            return GitHubError(e);
        }

        return result.Status == FixStatus.Ok
            ? StatusCode(StatusCodes.Status201Created, ActionResponse.From(result.Value!))
            : Failure(result.Status, result.Reasons, result.Existing);
    }

    /// <summary>GitHub unreachable, rate-limited, rejecting the token, or not finding the repository or branch.</summary>
    private static bool IsGitHubFailure(Exception e) => GitHubFailures.IsUnavailable(e) || e is GitHubNotFoundException;

    /// <summary>Nothing was stored; a branch created before the failure stays on GitHub (D25).</summary>
    private ObjectResult GitHubError(Exception e) =>
        Problem(
            $"{(e is HttpRequestException ? "GitHub did not accept the request." : e.Message)} Nothing was saved here; if a branch was already created on GitHub, delete or rename it before retrying.",
            statusCode: StatusCodes.Status502BadGateway,
            title: "GitHub error");

    private IActionResult Failure(FixStatus status, IReadOnlyList<string>? reasons, Domain.Entities.RemediationAction? existing) => status switch
    {
        FixStatus.NotFound => NotFound(),
        FixStatus.Disabled => Problem("Pull requests are turned off (read-only mode): set Features:PullRequests and a GitHub token with write access.", statusCode: StatusCodes.Status403Forbidden, title: "Read-only"),
        FixStatus.NoReport => Problem("Only succeeded scans have findings to fix.", statusCode: StatusCodes.Status409Conflict, title: "No report"),
        FixStatus.FilesChanged => WithExtension(StatusCodes.Status409Conflict, "Files changed since the scan", "Regenerate the fix against the current code.", "paths", reasons ?? []),
        FixStatus.Exists => WithExtension(StatusCodes.Status409Conflict, "Pull request already exists", $"{existing!.ExternalKey} already tracks this finding.", "existing", ActionResponse.From(existing)),
        FixStatus.NotFixable => WithExtension(StatusCodes.Status422UnprocessableEntity, "Not fixable", "Only untested endpoints, weak or false-positive tests and skipped tests get a fix PR.", "reasons", Array.Empty<string>()),
        FixStatus.NoContext => WithExtension(StatusCodes.Status422UnprocessableEntity, "No test project", "No integration test project was found to follow.", "reasons", Array.Empty<string>()),
        FixStatus.Declined => WithExtension(StatusCodes.Status422UnprocessableEntity, "Declined", reasons?.FirstOrDefault() ?? "The fix was declined.", "reasons", reasons ?? []),
        _ => WithExtension(StatusCodes.Status422UnprocessableEntity, "Fix rejected", "The fix failed the guard.", "reasons", reasons ?? []),
    };

    private static ObjectResult WithExtension(int status, string title, string detail, string key, object value)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions[key] = value;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
