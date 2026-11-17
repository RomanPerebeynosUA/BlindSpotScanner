using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Domain.Entities;

/// <summary>One scan of a repository at a pinned commit. Its report is immutable once it has succeeded.</summary>
public class ScanRun
{
    public ScanRun(Guid id, Guid repositoryId, string branch, string commitSha, string modelId, string promptSetVersion, int profileVersion)
    {
        Id = id;
        RepositoryId = repositoryId;
        Branch = branch;
        CommitSha = commitSha;
        ModelId = modelId;
        PromptSetVersion = promptSetVersion;
        ProfileVersion = profileVersion;
    }

    /// <summary>
    /// A scan requested through the API (M6). Branch and commit stay empty until the worker resolves
    /// them (<see cref="Pin"/>); status, lease and attempts are then driven by the scan queue.
    /// </summary>
    public static ScanRun Enqueue(Guid id, Guid repositoryId, string? requestedBranch, decimal maxCostUsd, int profileVersion, string modelId, string promptSetVersion, DateTimeOffset queuedAt) =>
        new(id, repositoryId, requestedBranch ?? string.Empty, string.Empty, modelId, promptSetVersion, profileVersion)
        {
            RequestedBranch = requestedBranch,
            MaxCostUsd = maxCostUsd,
            QueuedAt = queuedAt.ToUniversalTime(),
        };

    public Guid Id { get; private set; }
    public Guid RepositoryId { get; private set; }
    public string Branch { get; private set; }
    public string CommitSha { get; private set; }
    public DateTimeOffset? GeneratedAt { get; private set; }
    public RunStatus Status { get; private set; } = RunStatus.Queued;
    public string ModelId { get; private set; }
    public string PromptSetVersion { get; private set; }
    public int ProfileVersion { get; private set; }
    public decimal CostUsd { get; private set; }
    public Guid? PreviousRunId { get; private set; }

    /// <summary>The canonical report JSON (<see cref="ReportJsonSerializer"/>), stored as jsonb.</summary>
    public string? ReportJson { get; private set; }

    // Headline columns, copied from the report summary for listing and trends.
    public decimal? Score { get; private set; }
    public decimal? WeightAssessablePct { get; private set; }
    public int? EndpointsTotal { get; private set; }
    public int? EndpointsCovered { get; private set; }
    public int? EndpointsWeak { get; private set; }
    public int? EndpointsNone { get; private set; }
    public int? EndpointsUnverified { get; private set; }

    // Job state (M6, D21). Null QueuedAt: a run created directly, not through the queue.
    public DateTimeOffset? QueuedAt { get; private set; }
    public string? RequestedBranch { get; private set; }
    public decimal MaxCostUsd { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? LeasedUntil { get; private set; }
    public bool CancelRequested { get; private set; }
    public string[] FailureReasons { get; private set; } = [];

    private bool IsTerminal => Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Cancelled;

    /// <summary>Records the branch and commit P1 resolved; every later read is pinned to them.</summary>
    public void Pin(string branch, string commitSha)
    {
        if (Status != RunStatus.Running)
        {
            throw new InvalidOperationException($"Scan run {Id} is {Status}; only a running scan can be pinned.");
        }

        Branch = branch;
        CommitSha = commitSha;
    }

    public void Fail(IReadOnlyList<string> reasons)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"Scan run {Id} is already {Status}.");
        }

        Status = RunStatus.Failed;
        FailureReasons = [.. reasons];
    }

    /// <summary>A queued scan is cancelled at once; a running one is flagged and stops at its next phase.</summary>
    public void RequestCancel()
    {
        if (Status == RunStatus.Queued)
        {
            Status = RunStatus.Cancelled;
        }
        else if (Status == RunStatus.Running)
        {
            CancelRequested = true;
        }
    }

    public void Cancel()
    {
        if (Status != RunStatus.Running)
        {
            throw new InvalidOperationException($"Scan run {Id} is {Status}; only a running scan can be cancelled this way.");
        }

        Status = RunStatus.Cancelled;
    }

    public void RecordReport(IntegrationReport report)
    {
        if (Status == RunStatus.Succeeded)
        {
            throw new InvalidOperationException($"Scan run {Id} has succeeded; its report is immutable.");
        }

        if (report.Meta.RunId != Id)
        {
            throw new InvalidOperationException($"The report belongs to run {report.Meta.RunId}, not to run {Id}.");
        }

        // Citations are verified at the run's pinned commit, so the report must describe exactly that run.
        var meta = report.Meta;
        if (meta.CommitSha != CommitSha || meta.Branch != Branch || meta.ModelId != ModelId
            || meta.PromptSetVersion != PromptSetVersion || meta.ProfileVersion != ProfileVersion)
        {
            throw new InvalidOperationException(
                $"The report's pinned identity ({meta.Branch}@{meta.CommitSha}, {meta.ModelId}, prompts {meta.PromptSetVersion}, profile v{meta.ProfileVersion}) " +
                $"doesn't match run {Id} ({Branch}@{CommitSha}, {ModelId}, prompts {PromptSetVersion}, profile v{ProfileVersion}).");
        }

        ReportJson = ReportJsonSerializer.Serialize(report);
        GeneratedAt = report.Meta.GeneratedAt.ToUniversalTime();
        CostUsd = report.Meta.CostUsd ?? 0m;
        PreviousRunId = report.Summary.PreviousRunId;

        var summary = report.Summary;
        Score = summary.QualityScore;
        WeightAssessablePct = summary.WeightAssessablePct;
        EndpointsTotal = summary.Endpoints.Total;
        EndpointsCovered = summary.Endpoints.Covered;
        EndpointsWeak = summary.Endpoints.Weak;
        EndpointsNone = summary.Endpoints.None;
        EndpointsUnverified = summary.Endpoints.Unverified;
    }

    public void Succeed()
    {
        if (ReportJson is null)
        {
            throw new InvalidOperationException($"Scan run {Id} has no report; it cannot succeed.");
        }

        Status = RunStatus.Succeeded;
    }
}
