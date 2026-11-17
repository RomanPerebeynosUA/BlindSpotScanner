namespace BlindSpotScanner.Domain.Entities;

public class Repository
{
    public required Guid Id { get; init; }
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string DefaultBranch { get; init; }
    public required string HtmlUrl { get; init; }

    /// <summary>When a user accepted that code excerpts go to the LLM (doc 10); scans are refused until then.</summary>
    public DateTimeOffset? LlmSharingAcknowledgedAt { get; set; }

    public string? LlmSharingAcknowledgedBy { get; set; }
}
