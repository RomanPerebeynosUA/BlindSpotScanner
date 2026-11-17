using BlindSpotScanner.Application.Remediation;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

public sealed record FeaturesResponse(bool PullRequests, bool Jira);

/// <summary>What the UI may offer (D25): fix PRs are off in read-only mode, Jira until it is configured.</summary>
[ApiController]
[Route("api/features")]
public sealed class FeaturesController(FixPullRequests fixes, IJiraClient jira) : ControllerBase
{
    [HttpGet]
    public FeaturesResponse Get() => new(fixes.Enabled, jira.IsConfigured);
}
