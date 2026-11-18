using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Api.Auth;

/// <summary>Doc 10's development mode: every request is the single local admin (<c>Auth:Mode = Local</c>).</summary>
public sealed class LocalAdminHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Local";
    public const string UserName = "local-admin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, UserName), new Claim("name", UserName), new Claim(ClaimTypes.Role, Roles.Admin)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
