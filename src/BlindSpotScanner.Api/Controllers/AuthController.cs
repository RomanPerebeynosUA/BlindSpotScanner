using System.Security.Claims;
using BlindSpotScanner.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Api.Controllers;

public sealed record MeResponse(string Name, IReadOnlyList<string> Roles);

/// <summary>Sign-in endpoints (D26) and who is signed in.</summary>
[ApiController]
[Route("api")]
public sealed class AuthController(IOptions<AuthOptions> options) : ControllerBase
{
    [HttpGet("me")]
    public MeResponse Me() => new(
        User.FindFirstValue("name") ?? User.FindFirstValue("email") ?? User.Identity?.Name ?? User.FindFirstValue("sub") ?? "unknown",
        [.. User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal)]);

    /// <summary>Starts sign-in, then returns to <paramref name="returnUrl"/> when it is a local path (anything else goes home).</summary>
    [AllowAnonymous]
    [HttpGet("auth/login")]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        var target = IsLocalPath(returnUrl) ? returnUrl! : "/";
        return options.Value.IsOidc
            ? Challenge(new AuthenticationProperties { RedirectUri = target }, OpenIdConnectDefaults.AuthenticationScheme)
            : Redirect(target);
    }

    [AllowAnonymous]
    [HttpPost("auth/logout")]
    public IActionResult Logout() =>
        options.Value.IsOidc
            ? SignOut(new AuthenticationProperties { RedirectUri = "/" }, CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme)
            : NoContent();

    /// <summary>"/x" only: not "//host" or "/\host", which browsers treat as another site.</summary>
    private static bool IsLocalPath(string? url) =>
        url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
