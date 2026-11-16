namespace BlindSpotScanner.Application.Scanning;

/// <summary>The output of P1: the repository and the commit SHA every later read is pinned to.</summary>
public sealed record RunContext(string Owner, string Name, string Branch, string CommitSha, string HtmlUrl);
