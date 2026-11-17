using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Remediation;

/// <summary>Findings rows and the Jira tickets and PRs created from them (doc 09).</summary>
public interface IRemediationStore
{
    /// <summary>The finding's row for this run, inserted from the report the first time an action needs it (D24).</summary>
    Task<Finding> EnsureFindingAsync(Guid runId, ReportFinding finding, CancellationToken ct);

    /// <summary>The newest action of this type for the stable key, from any run of the repository.</summary>
    Task<RemediationAction?> FindActionAsync(Guid repositoryId, string stableKey, ActionType type, CancellationToken ct);

    Task AddActionAsync(RemediationAction action, CancellationToken ct);

    /// <summary>The repository's actions, newest first; tracked, so status changes save with <see cref="SaveChangesAsync"/>.</summary>
    Task<IReadOnlyList<RemediationAction>> ActionsAsync(Guid repositoryId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
