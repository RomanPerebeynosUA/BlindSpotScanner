namespace BlindSpotScanner.Application.Remediation;

public enum FixAction
{
    Create,
    Modify,
}

public sealed record FixFile(string Path, FixAction Action, string Content);

/// <summary>Doc 09 step 3: the static checks a generated (or user-edited) fix must pass before it can be committed.</summary>
public interface IFixGuard
{
    /// <param name="testProjectFolder">The test project's folder, without a trailing slash.</param>
    /// <param name="existingPaths">Every file path at the scanned commit.</param>
    /// <returns>One message per violation, each naming its path; empty when the fix passes.</returns>
    IReadOnlyList<string> Check(IReadOnlyList<FixFile> files, string testProjectFolder, IReadOnlySet<string> existingPaths);
}
