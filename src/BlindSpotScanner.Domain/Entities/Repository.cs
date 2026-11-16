namespace BlindSpotScanner.Domain.Entities;

public class Repository
{
    public required Guid Id { get; init; }
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string DefaultBranch { get; init; }
    public required string HtmlUrl { get; init; }
}
