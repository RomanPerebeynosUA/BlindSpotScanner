using System.Security.Claims;
using BlindSpotScanner.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Api.Controllers;

/// <param name="LocalMode">Auth:Mode is Local: everyone is the built-in admin, so signing out means nothing (D26).</param>
public sealed record MeResponse(string Name, IReadOnlyList<string> Roles, bool LocalMode);

/// <summary>Sign-in endpoints (D26) and who is signed in.</summary>
[ApiController]
[Route("api")]
public sealed class AuthController(IOptions<AuthOptions> options) : ControllerBase
{
    [HttpGet("me")]
    public MeResponse Me() => new(
        HttpCurrentUser.NameOf(User),
        [.. User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal)],
        !options.Value.IsOidc);

    /// <summary>Starts sign-in, then returns to <paramref name="returnUrl"/> when it is a local path (anything else goes home).</summary>
    [AllowAnonymous]
    [HttpGet("auth/login")]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        // Url.IsLocalUrl also rejects control characters that browsers drop ("/\t/evil" → "//evil").
        var target = returnUrl is not null && Url.IsLocalUrl(returnUrl) && !returnUrl.Any(char.IsControl) ? returnUrl : "/";
        return options.Value.IsOidc
            ? Challenge(new AuthenticationProperties { RedirectUri = target }, OpenIdConnectDefaults.AuthenticationScheme)
            : Redirect(target);
    }

    /// <summary>
    /// Ends the session here, and at the provider when it offers an end-session endpoint (some, such
    /// as Google, don't; asking them anyway fails the sign-out).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout([FromServices] IOptionsMonitor<OpenIdConnectOptions> oidc, CancellationToken ct)
    {
        if (!options.Value.IsOidc)
        {
            return Redirect("/");
        }

        var home = new AuthenticationProperties { RedirectUri = "/" };
        return await ProviderEndsSessionAsync(oidc.Get(OpenIdConnectDefaults.AuthenticationScheme), ct)
            ? SignOut(home, CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme)
            : SignOut(home, CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static async Task<bool> ProviderEndsSessionAsync(OpenIdConnectOptions oidc, CancellationToken ct)
    {
        try
        {
            return oidc.ConfigurationManager is { } manager && !string.IsNullOrEmpty((await manager.GetConfigurationAsync(ct)).EndSessionEndpoint);
        }
        catch (InvalidOperationException)
        {
            // The provider's metadata can't be read right now: still end the session here.
            return false;
        }
    }
}
