using BlindSpotScanner.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

public sealed record ProfileResponse(
    int Version, string Markdown, string IntegrationMappedTo, string[] TestGlobs, string[] ControllerGlobs, string[] MinimalApiGlobs,
    string[] IntegrationGlobs, string[] IntegrationAttributes, string[] IntegrationBaseTypes, string[] NotIntegrationGlobs)
{
    public static ProfileResponse From(RepoProfile p) => new(
        p.Version, p.Markdown, ProfileService.MappingName(p.IntegrationMappedTo), p.TestGlobs, p.ControllerGlobs, p.MinimalApiGlobs,
        p.IntegrationGlobs, p.IntegrationAttributes, p.IntegrationBaseTypes, p.NotIntegrationGlobs);
}

/// <param name="BaseVersion">The version the user edited; 409 when another save came first.</param>
public sealed record SaveProfileRequest(
    int? BaseVersion, string? Markdown, string? IntegrationMappedTo, string[]? TestGlobs, string[]? ControllerGlobs, string[]? MinimalApiGlobs,
    string[]? IntegrationGlobs, string[]? IntegrationAttributes, string[]? IntegrationBaseTypes, string[]? NotIntegrationGlobs);

[ApiController]
[Route("api/repositories/{repoId:guid}/profile")]
public sealed class ProfilesController(ProfileService profiles, ProfileDrafter drafter, ProfilePatternChecker patterns) : ControllerBase
{
    /// <summary>An AI draft of the profile Markdown and rules, with match counts (a preview: nothing is saved, D22, D29).</summary>
    [Authorize(Policy = Roles.Member)]
    [HttpPost("draft")]
    public async Task<IActionResult> Draft(Guid repoId, CancellationToken ct)
    {
        var result = await drafter.DraftAsync(repoId, ct);
        return result.Status switch
        {
            DraftProfileStatus.Drafted => Ok(result.Draft),
            DraftProfileStatus.RepositoryNotFound => NotFound(),
            DraftProfileStatus.LlmSharingNotAcknowledged => Problem(
                "Accept that code excerpts are sent to the LLM (POST …/llm-sharing) before drafting with AI.", statusCode: StatusCodes.Status409Conflict, title: "LLM data sharing not acknowledged"),
            DraftProfileStatus.GitHubFailed => Problem(result.Errors![0], statusCode: StatusCodes.Status502BadGateway, title: "GitHub is unavailable"),

            // The LLM's errors can quote its answer, so they are not echoed.
            _ => Problem("The AI could not draft a profile. Try again, or edit the profile by hand.", statusCode: StatusCodes.Status502BadGateway, title: "Draft failed"),
        };
    }

    /// <summary>How many default-branch files each glob matches; a read, so every role may ask (D29).</summary>
    [HttpPost("matches")]
    public async Task<IActionResult> Matches(Guid repoId, [FromBody] SaveProfileRequest request, CancellationToken ct)
    {
        var rules = new ProfileRules(
            request.IntegrationMappedTo ?? string.Empty, request.TestGlobs ?? [], request.ControllerGlobs ?? [], request.MinimalApiGlobs ?? [],
            request.IntegrationGlobs ?? [], request.IntegrationAttributes ?? [], request.IntegrationBaseTypes ?? [], request.NotIntegrationGlobs ?? []);
        var result = await patterns.CheckAsync(repoId, rules, ct);
        return result.Status switch
        {
            PatternCheckStatus.Checked => Ok(new { matches = result.Matches, errors = result.Errors }),
            PatternCheckStatus.RepositoryNotFound => NotFound(),
            _ => Problem(result.Errors[0], statusCode: StatusCodes.Status502BadGateway, title: "GitHub is unavailable"),
        };
    }

    /// <summary>The latest profile, or <paramref name="version"/> when given.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid repoId, [FromQuery] int? version, CancellationToken ct) =>
        await profiles.GetAsync(repoId, version, ct) is { } profile ? Ok(ProfileResponse.From(profile)) : NotFound();

    [HttpGet("versions")]
    public async Task<IActionResult> Versions(Guid repoId, CancellationToken ct) =>
        await profiles.VersionsAsync(repoId, ct) is { } versions ? Ok(versions) : NotFound();

    /// <summary>Saves the next version (profiles are never edited in place).</summary>
    [Authorize(Policy = Roles.Member)]
    [HttpPut]
    public async Task<IActionResult> Save(Guid repoId, [FromBody] SaveProfileRequest request, CancellationToken ct)
    {
        var input = new ProfileInput(
            request.Markdown ?? string.Empty, request.IntegrationMappedTo ?? string.Empty, request.TestGlobs ?? [], request.ControllerGlobs ?? [],
            request.MinimalApiGlobs ?? [], request.IntegrationGlobs ?? [], request.IntegrationAttributes ?? [], request.IntegrationBaseTypes ?? [],
            request.NotIntegrationGlobs ?? []);
        if (request.BaseVersion is not { } baseVersion)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["baseVersion"] = ["baseVersion is required."] })
            {
                Title = "Invalid profile",
                Detail = "baseVersion is required.",
            });
        }

        var result = await profiles.SaveAsync(repoId, baseVersion, input, ct);
        return result.Status switch
        {
            SaveProfileStatus.Saved => Ok(ProfileResponse.From(result.Profile!)),
            SaveProfileStatus.RepositoryNotFound => NotFound(),
            SaveProfileStatus.Conflict => Problem(
                "The profile was saved by someone else meanwhile. Reload it and apply your changes again.", statusCode: StatusCodes.Status409Conflict, title: "Profile changed"),
            _ => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["profile"] = [.. result.Errors!] }) { Title = "Invalid profile", Detail = string.Join(" ", result.Errors!) }),
        };
    }
}
