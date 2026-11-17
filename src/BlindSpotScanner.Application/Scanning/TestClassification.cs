namespace BlindSpotScanner.Application.Scanning;

/// <summary>P4 kinds known after the rules. Ambiguous files go to the LLM step (M5).</summary>
public enum TestFileKind
{
    Integration,
    Other,
    Ambiguous,
}

/// <param name="Reason">The profile rule (or its absence) that decided the kind.</param>
public sealed record ClassifiedTestFile(string Path, string BlobSha, TestFileKind Kind, string Reason, IReadOnlyList<string> TestMethods);

/// <summary>The output of P4's rules, one entry per P2 test file, in P2's order.</summary>
public sealed record TestClassification(IReadOnlyList<ClassifiedTestFile> Files)
{
    public IReadOnlyList<ClassifiedTestFile> IntegrationTests => Files.Where(f => f.Kind == TestFileKind.Integration).ToList();

    public IReadOnlyList<ClassifiedTestFile> Ambiguous => Files.Where(f => f.Kind == TestFileKind.Ambiguous).ToList();
}
