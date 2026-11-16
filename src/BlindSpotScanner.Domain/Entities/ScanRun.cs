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
