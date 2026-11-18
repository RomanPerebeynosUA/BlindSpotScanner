using System.Security.Claims;
using BlindSpotScanner.Application;

namespace BlindSpotScanner.Api.Auth;

/// <summary>
/// The signed-in user. <c>CreatedBy</c> uses a unique id (email, then subject); the display name is
/// only for showing who is signed in (the local admin is "local-admin").
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Name => IdOf(accessor.HttpContext?.User);

    /// <summary>Email, then subject, then the display name: two people can share a display name.</summary>
    public static string IdOf(ClaimsPrincipal? user) =>
        user?.FindFirstValue("email") ?? user?.FindFirstValue("sub") ?? NameOf(user);

    public static string NameOf(ClaimsPrincipal? user) =>
        user?.FindFirstValue("name") ?? user?.FindFirstValue("email") ?? user?.FindFirstValue("sub") ?? user?.Identity?.Name ?? "unknown";
}
