using System.Security.Claims;
using BlindSpotScanner.Application;

namespace BlindSpotScanner.Api.Auth;

/// <summary>The signed-in user: the name, email or subject claim (the local admin is "local-admin").</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Name => NameOf(accessor.HttpContext?.User);

    public static string NameOf(ClaimsPrincipal? user) =>
        user?.FindFirstValue("name") ?? user?.FindFirstValue("email") ?? user?.FindFirstValue("sub") ?? user?.Identity?.Name ?? "unknown";
}
