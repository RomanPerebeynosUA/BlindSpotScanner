using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Entities;

/// <summary>
/// A Jira ticket or PR created from a finding. Actions outlive runs: they are looked up by
/// (RepositoryId, StableKey), so a ticket from run N still shows on the same finding in run N+1.
/// </summary>
public class RemediationAction
{
    public required Guid Id { get; init; }
    public required Guid RepositoryId { get; init; }
    public required string StableKey { get; init; }
    public required Guid CreatedFromFindingId { get; init; }
    public required ActionType Type { get; init; }
    public required string ExternalKey { get; init; }
    public required string Url { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
}
