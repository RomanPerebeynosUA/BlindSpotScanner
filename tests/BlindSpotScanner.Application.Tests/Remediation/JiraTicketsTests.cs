using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Application.Tests.Reports;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Remediation;

public class JiraTicketsTests
{
    private const string CancelKey = "uncovered-endpoint:POST api/orders/{}/cancel";
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly RunStore _runs = new();
    private readonly Store _store = new();
    private readonly FakeJira _jira = new();
    private readonly Clock _clock = new(Now);
    private readonly Guid _runId;
    private readonly Guid _repositoryId = Guid.NewGuid();

    public JiraTicketsTests() => _runId = AddRun(Fixtures.Current);

    private JiraTickets Tickets => new(_runs, _store, _jira, _clock, new JiraDefaults("BSS", "Task", ["integration-coverage", "qa"], "http://localhost:8080"));

    private Guid AddRun(IntegrationReport report)
    {
        var meta = report.Meta;
        var run = new ScanRun(meta.RunId, _repositoryId, meta.Branch, meta.CommitSha, meta.ModelId, meta.PromptSetVersion, meta.ProfileVersion);
        run.RecordReport(report);
        run.Succeed();
        _runs.Runs.Add(run);
        return run.Id;
    }

    private RemediationAction StoredTicket(string key = "BSS-1", DateTimeOffset? checkedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = _repositoryId,
        StableKey = CancelKey,
        CreatedFromFindingId = Guid.NewGuid(),
        Type = ActionType.JiraTicket,
        ExternalKey = key,
        Url = $"https://example.atlassian.net/browse/{key}",
        Status = "To Do",
        StatusCheckedAt = checkedAt ?? Now,
        CreatedAt = Now.AddDays(-1),
        CreatedBy = "anonymous",
    };

    private static JiraCreate Create(JiraDraft draft, bool force = false) =>
        new(draft.Summary, draft.Description, draft.ProjectKey, draft.IssueType, draft.Priority, draft.Labels, force);

    private async Task<JiraDraft> DraftAsync(string findingId = "f2") =>
        (await Tickets.PreviewAsync(_runId, findingId, CancellationToken.None)).Value!;

    [Fact]
    public async Task PreviewAsync_UncoveredEndpoint_BuildsSummaryAndPriority()
    {
        // Act
        var draft = await DraftAsync();

        // Assert
        new { draft.Summary, draft.Priority, draft.StableKey }.Should().Be(new
        {
            Summary = "[Integration coverage] Add test for POST api/orders/{id}/cancel",
            Priority = "High",
            StableKey = CancelKey,
        });
    }

    [Fact]
    public async Task PreviewAsync_FalsePositive_DescriptionHasEvidenceBlobLink()
    {
        // Act
        var draft = await DraftAsync("f3");

        // Assert
        draft.Description.Should().Contain(
            "- [tests/Bookstore.IntegrationTests/BooksApiTests.cs::Get_by_id_returns_ok](https://github.com/example-org/bookstore-api/blob/a1b2c3d4e5f60718293a4b5c6d7e8f9012345678/tests/Bookstore.IntegrationTests/BooksApiTests.cs)");
    }

    [Fact]
    public async Task PreviewAsync_UncoveredEndpoint_DescriptionMatchesTemplate()
    {
        // Act
        var draft = await DraftAsync();

        // Assert
        draft.Description.Should().Be(
            "## Problem\nCancelling an order has no integration test\n\n" +
            "## Details\n- POST api/orders/{id}/cancel\n\n" +
            "## Acceptance criteria\n- An integration test exercises POST api/orders/{id}/cancel against a real database and asserts the response values and the persisted state.\n\n" +
            $"[View the report](http://localhost:8080/runs/{_runId})");
    }

    [Fact]
    public async Task PreviewAsync_CriticalSeverity_MapsToHighest()
    {
        // Arrange
        var report = Fixtures.Current;
        var critical = AddRun(report with
        {
            Meta = report.Meta with { RunId = Guid.NewGuid() },
            Findings = [.. report.Findings.Select(f => f.Id == "f2" ? f with { Severity = Severity.Critical } : f)],
        });

        // Act
        var draft = (await Tickets.PreviewAsync(critical, "f2", CancellationToken.None)).Value!;

        // Assert
        draft.Priority.Should().Be("Highest");
    }

    [Fact]
    public async Task PreviewAsync_StoredTicket_ReturnsExisting()
    {
        // Arrange
        var stored = StoredTicket();
        _store.Actions.Add(stored);

        // Act
        var draft = await DraftAsync();

        // Assert
        draft.Existing.Should().BeSameAs(stored);
    }

    [Fact]
    public async Task PreviewAsync_UnknownFinding_ReturnsNotFound()
    {
        // Act
        var result = await Tickets.PreviewAsync(_runId, "f99", CancellationToken.None);

        // Assert
        result.Status.Should().Be(TicketStatus.NotFound);
    }

    [Fact]
    public async Task PreviewAsync_NotConfigured_ReturnsNotConfigured()
    {
        // Arrange
        _jira.IsConfigured = false;

        // Act
        var result = await Tickets.PreviewAsync(_runId, "f2", CancellationToken.None);

        // Assert
        result.Status.Should().Be(TicketStatus.NotConfigured);
    }

    [Fact]
    public async Task CreateAsync_New_CreatesAndStoresAction()
    {
        // Arrange
        var draft = await DraftAsync();

        // Act
        var result = await Tickets.CreateAsync(_runId, "f2", Create(draft), CancellationToken.None);

        // Assert
        new { result.Status, result.Value!.ExternalKey, result.Value.StableKey, Stored = _store.Actions.Count, result.Value.StatusCheckedAt }
            .Should().Be(new { Status = TicketStatus.Ok, ExternalKey = "BSS-1", StableKey = CancelKey, Stored = 1, StatusCheckedAt = Now });
    }

    [Fact]
    public async Task CreateAsync_StoredTicket_ReturnsExistsWithoutCallingJira()
    {
        // Arrange
        var draft = await DraftAsync();
        _store.Actions.Add(StoredTicket());

        // Act
        var result = await Tickets.CreateAsync(_runId, "f2", Create(draft), CancellationToken.None);

        // Assert
        new { result.Status, result.Existing!.ExternalKey, Created = _jira.Created.Count, Searched = _jira.Searches }
            .Should().Be(new { Status = TicketStatus.Exists, ExternalKey = "BSS-1", Created = 0, Searched = 0 });
    }

    [Fact]
    public async Task CreateAsync_JqlFindsTicket_AdoptsItAndReturnsExists()
    {
        // Arrange
        var draft = await DraftAsync();
        _jira.Found = new JiraIssueRef("BSS-7", "https://example.atlassian.net/browse/BSS-7", "In Progress");

        // Act
        var result = await Tickets.CreateAsync(_runId, "f2", Create(draft), CancellationToken.None);

        // Assert
        new { result.Status, result.Existing!.ExternalKey, Stored = _store.Actions.Single().ExternalKey, Created = _jira.Created.Count }
            .Should().Be(new { Status = TicketStatus.Exists, ExternalKey = "BSS-7", Stored = "BSS-7", Created = 0 });
    }

    [Fact]
    public async Task CreateAsync_Force_CreatesDespiteStoredTicket()
    {
        // Arrange
        var draft = await DraftAsync();
        _store.Actions.Add(StoredTicket());

        // Act
        var result = await Tickets.CreateAsync(_runId, "f2", Create(draft, force: true), CancellationToken.None);

        // Assert
        new { result.Status, Created = _jira.Created.Count }.Should().Be(new { Status = TicketStatus.Ok, Created = 1 });
    }

    [Fact]
    public async Task CreateAsync_EditedDraftWithoutLabel_StillSendsLabelAndFooter()
    {
        // Arrange
        var draft = await DraftAsync();
        var edited = Create(draft) with { Labels = ["qa"], Description = "Just do it" };

        // Act
        await Tickets.CreateAsync(_runId, "f2", edited, CancellationToken.None);

        // Assert
        var sent = _jira.Created.Single();
        new { Labels = string.Join(",", sent.Labels), sent.StableKey, sent.DescriptionMarkdown }
            .Should().Be(new { Labels = "integration-coverage,qa", StableKey = CancelKey, DescriptionMarkdown = "Just do it" });
    }

    [Fact]
    public async Task CreateAsync_FindingRowFails_DoesNotCallJira()
    {
        // Arrange (the database fails before the outward action, never after it)
        var draft = await DraftAsync();
        _store.EnsureFailure = new InvalidOperationException("db down");

        // Act
        var act = () => Tickets.CreateAsync(_runId, "f2", Create(draft), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _jira.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ActionsAsync_StaleStatus_Refreshes()
    {
        // Arrange
        _store.Actions.Add(StoredTicket(checkedAt: Now.AddMinutes(-10)));
        _jira.Statuses["BSS-1"] = "Done";

        // Act
        var actions = await Tickets.ActionsAsync(_repositoryId, CancellationToken.None);

        // Assert
        new { actions.Single().Status, actions.Single().StatusCheckedAt, _store.Saves }.Should().Be(new { Status = "Done", StatusCheckedAt = Now, Saves = 1 });
    }

    [Fact]
    public async Task ActionsAsync_FreshStatus_DoesNotCallJira()
    {
        // Arrange
        _store.Actions.Add(StoredTicket(checkedAt: Now.AddMinutes(-9)));
        _jira.Statuses["BSS-1"] = "Done";

        // Act
        var actions = await Tickets.ActionsAsync(_repositoryId, CancellationToken.None);

        // Assert
        actions.Single().Status.Should().Be("To Do");
    }

    [Fact]
    public async Task ActionsAsync_JiraFails_KeepsCachedStatus()
    {
        // Arrange
        _store.Actions.Add(StoredTicket(checkedAt: Now.AddHours(-1)));
        _jira.StatusFailure = new JiraException("Jira returned 503 for reading an issue.");

        // Act
        var actions = await Tickets.ActionsAsync(_repositoryId, CancellationToken.None);

        // Assert
        new { actions.Single().Status, actions.Single().StatusCheckedAt }.Should().Be(new { Status = "To Do", StatusCheckedAt = Now.AddHours(-1) });
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeJira : IJiraClient
    {
        public bool IsConfigured { get; set; } = true;

        public List<JiraIssueRequest> Created { get; } = [];

        public JiraIssueRef? Found { get; set; }

        public int Searches { get; private set; }

        public Dictionary<string, string> Statuses { get; } = [];

        public Exception? StatusFailure { get; set; }

        public Task<JiraIssueRef> CreateIssueAsync(JiraIssueRequest request, CancellationToken ct)
        {
            Created.Add(request);
            var key = $"BSS-{Created.Count}";
            return Task.FromResult(new JiraIssueRef(key, $"https://example.atlassian.net/browse/{key}", "To Do"));
        }

        public Task<JiraIssueRef?> FindByStableKeyAsync(string stableKey, CancellationToken ct)
        {
            Searches++;
            return Task.FromResult(Found);
        }

        public Task<string> GetStatusAsync(string key, CancellationToken ct) =>
            StatusFailure is null ? Task.FromResult(Statuses[key]) : Task.FromException<string>(StatusFailure);
    }

    private sealed class Store : IRemediationStore
    {
        public List<Finding> Findings { get; } = [];

        public List<RemediationAction> Actions { get; } = [];

        public int Saves { get; private set; }

        public Exception? EnsureFailure { get; set; }

        public Task<Finding> EnsureFindingAsync(Guid runId, ReportFinding finding, CancellationToken ct)
        {
            if (EnsureFailure is not null)
            {
                return Task.FromException<Finding>(EnsureFailure);
            }

            var row = Findings.SingleOrDefault(f => f.ScanRunId == runId && f.ReportFindingId == finding.Id);
            if (row is null)
            {
                row = new Finding
                {
                    Id = Guid.NewGuid(),
                    ScanRunId = runId,
                    ReportFindingId = finding.Id,
                    Kind = finding.Kind,
                    Severity = finding.Severity,
                    StableKey = finding.StableKey,
                    Headline = finding.Headline,
                    Detail = [],
                    Evidence = [],
                };
                Findings.Add(row);
            }

            return Task.FromResult(row);
        }

        public Task<RemediationAction?> FindActionAsync(Guid repositoryId, string stableKey, ActionType type, CancellationToken ct) =>
            Task.FromResult(Actions.Where(a => a.RepositoryId == repositoryId && a.StableKey == stableKey && a.Type == type).MaxBy(a => a.CreatedAt));

        public Task AddActionAsync(RemediationAction action, CancellationToken ct)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RemediationAction>> ActionsAsync(Guid repositoryId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RemediationAction>>([.. Actions.Where(a => a.RepositoryId == repositoryId)]);

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class RunStore : IScanRequestStore
    {
        public List<ScanRun> Runs { get; } = [];

        public Task<ScanRun?> FindRunAsync(Guid runId, CancellationToken ct) => Task.FromResult(Runs.SingleOrDefault(r => r.Id == runId));

        public Task<bool> RepositoryExistsAsync(Guid repositoryId, CancellationToken ct) => throw new NotSupportedException();

        public Task<int?> LatestProfileVersionAsync(Guid repositoryId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<ScanStep>> StepsAsync(Guid runId, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> SaveChangesAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task ReloadAsync(ScanRun run, CancellationToken ct) => throw new NotSupportedException();
    }
}
