using System.Text;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Remediation;

/// <param name="PublicUrl">Where the web app is served, for the report link in a ticket (D24).</param>
public sealed record JiraDefaults(string ProjectKey, string IssueType, IReadOnlyList<string> Labels, string PublicUrl);

/// <summary>The editable ticket draft (doc 04 Jira dialog). <paramref name="Existing"/> is a ticket already made for this stable key.</summary>
public sealed record JiraDraft(
    string Summary, string Description, string ProjectKey, string IssueType, string Priority, IReadOnlyList<string> Labels, string StableKey, RemediationAction? Existing);

/// <param name="Force">"Create anyway": skip the dedup checks (doc 04).</param>
public sealed record JiraCreate(string Summary, string Description, string ProjectKey, string IssueType, string Priority, IReadOnlyList<string> Labels, bool Force);

public enum TicketStatus
{
    Ok,
    NotFound,
    NoReport,
    NotConfigured,
    Exists,
}

public sealed record TicketResult<T>(TicketStatus Status, T? Value = default, RemediationAction? Existing = null);

/// <summary>
/// Doc 09 Jira tickets: an editable draft built from the stored report, dedup by stable key (stored
/// actions first, then Jira itself), creation only on an explicit request (principle 8), and a lazy
/// statuses are refreshed by <see cref="RemediationActions"/>.
/// </summary>
public sealed class JiraTickets(IScanRequestStore runs, IRemediationStore store, IJiraClient jira, TimeProvider time, ICurrentUser user, JiraDefaults defaults)
{
    public const string CoverageLabel = "integration-coverage";

    public async Task<TicketResult<JiraDraft>> PreviewAsync(Guid runId, string findingId, CancellationToken ct)
    {
        var (status, run, report, finding) = await FindAsync(runId, findingId, ct);
        if (status != TicketStatus.Ok)
        {
            return new TicketResult<JiraDraft>(status);
        }

        var existing = await store.FindActionAsync(run!.RepositoryId, finding!.StableKey, ActionType.JiraTicket, ct);
        var draft = new JiraDraft(
            Summary(finding), Description(runId, report!, finding), defaults.ProjectKey, defaults.IssueType, Priority(finding.Severity), Labels(defaults.Labels), finding.StableKey, existing);
        return new TicketResult<JiraDraft>(TicketStatus.Ok, draft);
    }

    public async Task<TicketResult<RemediationAction>> CreateAsync(Guid runId, string findingId, JiraCreate request, CancellationToken ct)
    {
        var (status, run, _, finding) = await FindAsync(runId, findingId, ct);
        if (status != TicketStatus.Ok)
        {
            return new TicketResult<RemediationAction>(status);
        }

        if (!request.Force)
        {
            if (await store.FindActionAsync(run!.RepositoryId, finding!.StableKey, ActionType.JiraTicket, ct) is { } stored)
            {
                return new TicketResult<RemediationAction>(TicketStatus.Exists, Existing: stored);
            }

            // A ticket made elsewhere (or whose action was lost) is adopted, not duplicated.
            if (await jira.FindByStableKeyAsync(finding.StableKey, ct) is { } found)
            {
                var adopted = await StoreAsync(await store.EnsureFindingAsync(runId, finding, ct), run.RepositoryId, found, ct);
                return new TicketResult<RemediationAction>(TicketStatus.Exists, Existing: adopted);
            }
        }

        // The finding's row first, so a database failure happens before the outward action, not after it.
        var row = await store.EnsureFindingAsync(runId, finding!, ct);

        // The label and the footer (added by the client from StableKey) keep the ticket findable whatever the user edited.
        var issue = await jira.CreateIssueAsync(
            new JiraIssueRequest(request.ProjectKey, request.IssueType, request.Summary, request.Description, request.Priority, Labels(request.Labels), finding!.StableKey),
            ct);
        return new TicketResult<RemediationAction>(TicketStatus.Ok, await StoreAsync(row, run!.RepositoryId, issue, ct));
    }

    private async Task<(TicketStatus Status, ScanRun? Run, IntegrationReport? Report, ReportFinding? Finding)> FindAsync(Guid runId, string findingId, CancellationToken ct)
    {
        if (await runs.FindRunAsync(runId, ct) is not { } run)
        {
            return (TicketStatus.NotFound, null, null, null);
        }

        if (run is not { Status: RunStatus.Succeeded, ReportJson: { } json })
        {
            return (TicketStatus.NoReport, null, null, null);
        }

        var report = ReportJsonSerializer.Deserialize(json);
        if (report.Findings.SingleOrDefault(f => f.Id == findingId) is not { } finding)
        {
            return (TicketStatus.NotFound, null, null, null);
        }

        return jira.IsConfigured ? (TicketStatus.Ok, run, report, finding) : (TicketStatus.NotConfigured, null, null, null);
    }

    private async Task<RemediationAction> StoreAsync(Finding row, Guid repositoryId, JiraIssueRef issue, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var action = new RemediationAction
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            StableKey = row.StableKey,
            CreatedFromFindingId = row.Id,
            Type = ActionType.JiraTicket,
            ExternalKey = issue.Key,
            Url = issue.Url,
            Status = issue.Status,
            StatusCheckedAt = now,
            CreatedAt = now,
            CreatedBy = user.Name,
        };
        await store.AddActionAsync(action, ct);
        return action;
    }

    /// <summary><c>integration-coverage</c> first, no blanks or duplicates; Jira rejects spaces, so they become hyphens.</summary>
    private static List<string> Labels(IEnumerable<string> labels) =>
        [.. new[] { CoverageLabel }
            .Concat(labels.Select(l => string.Join('-', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).Where(l => l.Length > 0))
            .Distinct(StringComparer.Ordinal)];

    private static string Priority(Severity severity) => severity == Severity.Critical ? "Highest" : severity.ToString();

    private static string Summary(ReportFinding finding) =>
        finding is { Kind: FindingKind.UncoveredEndpoint, Detail: [var endpoint, ..] }
            ? $"[Integration coverage] Add test for {endpoint}"
            : $"[Integration coverage] {finding.Headline}";

    private string Description(Guid runId, IntegrationReport report, ReportFinding finding)
    {
        var sections = new List<string> { $"## Problem\n{finding.Headline}" };
        if (!string.IsNullOrWhiteSpace(finding.WhyItMatters))
        {
            sections.Add($"## Why it matters\n{finding.WhyItMatters}");
        }

        if (finding.Detail is { Count: > 0 } detail)
        {
            sections.Add(Bullets("Details", detail));
        }

        if (finding.Evidence.Count > 0)
        {
            var repo = report.Meta.Repo.Url.TrimEnd('/');
            sections.Add(Bullets("Evidence", finding.Evidence.Select(c => $"[{c}]({repo}/blob/{report.Meta.CommitSha}/{c.Split("::")[0]})")));
        }

        sections.Add(Bullets("Acceptance criteria", [AcceptanceCriterion(finding)]));
        sections.Add($"[View the report]({defaults.PublicUrl.TrimEnd('/')}/runs/{runId})");
        return string.Join("\n\n", sections);
    }

    private static string Bullets(string heading, IEnumerable<string> items)
    {
        var text = new StringBuilder($"## {heading}");
        foreach (var item in items)
        {
            text.Append("\n- ").Append(item);
        }

        return text.ToString();
    }

    private static string AcceptanceCriterion(ReportFinding finding) => finding.Kind switch
    {
        FindingKind.UncoveredEndpoint =>
            $"An integration test exercises {finding.Detail?.FirstOrDefault() ?? "the endpoint"} against a real database and asserts the response values and the persisted state.",
        FindingKind.WeakTest or FindingKind.FalsePositive => "The test asserts response values and persisted state, not only the status code.",
        FindingKind.SkippedTest => "The test runs (is not skipped) and passes in CI.",
        FindingKind.UncoveredSeam => "At least one integration test crosses this seam for real instead of a fake.",
        FindingKind.NoAssertion => "The test asserts the response values and the persisted state.",
        FindingKind.UncheckedFields => "The test asserts every field it sends, in the response or the saved data.",
        FindingKind.ErrorWithoutStateCheck => "The test asserts the error body and that nothing was saved.",
        _ => "The risk is covered by an integration test or accepted with a written reason.",
    };
}
