namespace BlindSpotScanner.Api.Auth;

/// <summary>Doc 10 roles; each policy has the role's name, and higher roles include lower ones (D26).</summary>
public static class Roles
{
    public const string Viewer = "Viewer";
    public const string Member = "Member";
    public const string Admin = "Admin";

    /// <summary>Lowest to highest.</summary>
    public static readonly string[] Ordered = [Viewer, Member, Admin];
}
