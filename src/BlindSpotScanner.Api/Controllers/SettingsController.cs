using BlindSpotScanner.Api.Auth;
using BlindSpotScanner.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpotScanner.Api.Controllers;

/// <summary>The read-only settings page (OpenAPI /settings/connections, D26): Admin only, secrets masked.</summary>
[Authorize(Policy = Roles.Admin)]
[ApiController]
[Route("api/settings/connections")]
public sealed class SettingsController(IConnectionSettings settings) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<ConnectionStatus> Get() => settings.Statuses();

    [HttpPost("{kind}/test")]
    public async Task<IActionResult> Test(string kind, CancellationToken ct) =>
        await settings.TestAsync(kind, ct) is { } result ? Ok(result) : NotFound();
}
