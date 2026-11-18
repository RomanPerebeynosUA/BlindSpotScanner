namespace BlindSpotScanner.Api.Auth;

/// <summary>Maps an identity provider's role claim values to the one app role a user gets (D26).</summary>
public static class RoleClaims
{
    /// <summary>The highest known role claimed (exact names), or <paramref name="defaultRole"/>; unknown values are ignored, never elevated.</summary>
    public static string Effective(IEnumerable<string> claimed, string defaultRole)
    {
        var known = claimed.Where(c => Roles.Ordered.Contains(c, StringComparer.Ordinal)).ToHashSet(StringComparer.Ordinal);
        return Roles.Ordered.Reverse().FirstOrDefault(known.Contains)
            ?? (Roles.Ordered.Contains(defaultRole, StringComparer.Ordinal) ? defaultRole : Roles.Viewer);
    }
}
