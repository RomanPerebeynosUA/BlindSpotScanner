using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Reports;

public class RunComparisonTests
{
    private static readonly Guid PreviousId = Fixtures.Previous.Meta.RunId;
    private static readonly Guid CurrentId = Fixtures.Current.Meta.RunId;

    private readonly RunStore _runs = new();
    private readonly Guid _repositoryId = Guid.NewGuid();

    private RunComparison Comparison => new(_runs);

    private ScanRun Add(IntegrationReport report, Guid? repositoryId = null)
    {
        var meta = report.Meta;
        var run = new ScanRun(meta.RunId, repositoryId ?? _repositoryId, meta.Branch, meta.CommitSha, meta.ModelId, meta.PromptSetVersion, meta.ProfileVersion);
        run.RecordReport(report);
        run.Succeed();
        _runs.Runs.Add(run);
        return run;
    }

    [Fact]
    public async Task CompareAsync_AdjacentExampleRuns_ReturnsStoredDeltaNotAdHoc()
    {
        // Arrange
        Add(Fixtures.Previous);
        Add(Fixtures.Current);

        // Act
        var result = await Comparison.CompareAsync(PreviousId, CurrentId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(CompareStatus.Compared);
        result.Comparison!.AdHoc.Should().BeFalse();
        result.Comparison.Delta.Endpoints.NewlyCovered.Should().Equal("POST api/orders");
        result.Comparison.Delta.Endpoints.Regressed.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareAsync_NonAdjacentPair_ComputesAdHoc()
    {
        // Arrange (the current report's stored delta points at some other run)
        var current = Fixtures.Current with { Delta = Fixtures.Current.Delta! with { BaseRunId = Guid.NewGuid() } };
        Add(Fixtures.Previous);
        Add(current);

        // Act
        var result = await Comparison.CompareAsync(PreviousId, CurrentId, CancellationToken.None);

        // Assert
        result.Comparison!.AdHoc.Should().BeTrue();
        Fixtures.Canonical(result.Comparison.Delta).Should().Be(Fixtures.Canonical(RunComparer.Compare(Fixtures.Previous, current)));
    }

    [Fact]
    public async Task CompareAsync_BaseNewerThanTarget_SwapsToOldToNew()
    {
        // Arrange
        Add(Fixtures.Previous);
        Add(Fixtures.Current);

        // Act
        var result = await Comparison.CompareAsync(CurrentId, PreviousId, CancellationToken.None);

        // Assert
        result.Comparison!.Swapped.Should().BeTrue();
        result.Comparison.Base.RunId.Should().Be(PreviousId);
        result.Comparison.Delta.Endpoints.NewlyCovered.Should().Equal("POST api/orders");
    }

    [Fact]
    public async Task CompareAsync_DifferentMapping_IsNotComparable()
    {
        // Arrange
        var current = Fixtures.Current with
        {
            Meta = Fixtures.Current.Meta with { IntegrationMappedTo = IntegrationMappedTo.E2eMockedData },
            Delta = Fixtures.Current.Delta! with { BaseRunId = Guid.NewGuid() },
        };
        Add(Fixtures.Previous);
        Add(current);

        // Act
        var result = await Comparison.CompareAsync(PreviousId, CurrentId, CancellationToken.None);

        // Assert
        result.Comparison!.Delta.Comparable.Should().BeFalse();
        result.Comparison.Delta.Comparability.Should().Contain("integrationMappedTo changed");
    }

    [Fact]
    public async Task CompareAsync_SameRun_ReturnsSameRun()
    {
        // Arrange
        Add(Fixtures.Current);

        // Act
        var result = await Comparison.CompareAsync(CurrentId, CurrentId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(CompareStatus.SameRun);
    }

    [Fact]
    public async Task CompareAsync_RunWithoutReport_ReturnsNoReport()
    {
        // Arrange
        Add(Fixtures.Previous);
        _runs.Runs.Add(ScanRun.Enqueue(CurrentId, _repositoryId, null, 5m, 1, "claude-sonnet-5-5", "1.0.0", DateTimeOffset.UtcNow));

        // Act
        var result = await Comparison.CompareAsync(PreviousId, CurrentId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(CompareStatus.NoReport);
    }

    [Fact]
    public async Task CompareAsync_DifferentRepositories_ReturnsDifferentRepositories()
    {
        // Arrange
        Add(Fixtures.Previous);
        Add(Fixtures.Current, Guid.NewGuid());

        // Act
        var result = await Comparison.CompareAsync(PreviousId, CurrentId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(CompareStatus.DifferentRepositories);
    }

    [Fact]
    public async Task CompareAsync_QueuedRunOfAnotherRepository_ReturnsDifferentRepositories()
    {
        // Arrange
        Add(Fixtures.Previous);
        _runs.Runs.Add(ScanRun.Enqueue(CurrentId, Guid.NewGuid(), null, 5m, 1, "claude-sonnet-5-5", "1.0.0", DateTimeOffset.UtcNow));

        // Act
        var result = await Comparison.CompareAsync(PreviousId, CurrentId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(CompareStatus.DifferentRepositories);
    }

    [Fact]
    public async Task CompareAsync_UnknownRun_ReturnsNotFound()
    {
        // Arrange
        Add(Fixtures.Previous);

        // Act
        var result = await Comparison.CompareAsync(PreviousId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(CompareStatus.NotFound);
    }

    /// <summary>Only the read the comparison needs.</summary>
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
