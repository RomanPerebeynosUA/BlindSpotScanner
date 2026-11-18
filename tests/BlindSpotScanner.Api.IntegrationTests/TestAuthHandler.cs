using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>
/// Signs each request in with the role in <c>X-Test-Role</c> (Admin when absent; "none" = anonymous),
/// so tests exercise the real policies without an identity provider.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string RoleHeader = "X-Test-Role";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers[RoleHeader].FirstOrDefault() ?? "Admin";
        if (role == "none")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var name = Request.Headers[UserHeader].FirstOrDefault() ?? "test-admin";
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name), new Claim("name", name), new Claim(ClaimTypes.Role, role)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
