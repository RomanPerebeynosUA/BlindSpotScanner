namespace BlindSpotScanner.Api.Auth;

/// <summary>The "Auth" configuration section (D26). The client secret comes from an env var, never appsettings.</summary>
public sealed class AuthOptions
{
    public const string LocalMode = "Local";
    public const string OidcMode = "Oidc";

    /// <summary>"Local" signs every request in as a local admin (development, compose default); "Oidc" uses OpenID Connect.</summary>
    public string Mode { get; set; } = LocalMode;

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The claim carrying role names from the identity provider.</summary>
    public string RoleClaim { get; set; } = "roles";

    /// <summary>The role of a signed-in user with no known role claim.</summary>
    public string DefaultRole { get; set; } = Roles.Viewer;

    /// <summary>Only a local test provider may serve its metadata over plain HTTP.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    public bool IsOidc => string.Equals(Mode, OidcMode, StringComparison.OrdinalIgnoreCase);
}
