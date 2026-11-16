namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// The output of P2: production controller/route files (the denominator source) and candidate
/// test files, counted from the Git Trees API (principle 4).
/// </summary>
public sealed record RepositoryInventory(
    IReadOnlyList<RepoFile> ControllerFiles,
    IReadOnlyList<RepoFile> TestFiles,
    IReadOnlyList<string> UnverifiedReasons)
{
    public int ControllerFileCount => ControllerFiles.Count;

    public int TestFileCount => TestFiles.Count;
}

/// <param name="Path">Relative to the repository root.</param>
public sealed record RepoFile(string Path, string BlobSha, long? Size);
