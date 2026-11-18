namespace BlindSpotScanner.Application;

/// <summary>Who is acting, for <c>CreatedBy</c> on remediation actions (doc 10, D26).</summary>
public interface ICurrentUser
{
    string Name { get; }
}

/// <summary>The worker's identity; it never creates remediation actions.</summary>
public sealed class SystemUser : ICurrentUser
{
    public string Name => "system";
}
