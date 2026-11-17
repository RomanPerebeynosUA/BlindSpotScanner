using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Domain.Tests;

public class ScanRunLifecycleTests
{
    private static readonly DateTimeOffset QueuedAt = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private static ScanRun NewRun(string? branch = "main") =>
        ScanRun.Enqueue(Guid.NewGuid(), Guid.NewGuid(), branch, 5.0m, 1, "claude-sonnet-5-5", "1.0.0", QueuedAt);

    [Fact]
    public void Enqueue_NewRun_IsQueuedUnpinned()
    {
        // Act
        var run = NewRun(branch: null);

        // Assert
        run.Should().BeEquivalentTo(new
        {
            Status = RunStatus.Queued,
            Branch = "",
            CommitSha = "",
            RequestedBranch = (string?)null,
            MaxCostUsd = 5.0m,
            Attempts = 0,
            LeasedUntil = (DateTimeOffset?)null,
            CancelRequested = false,
            QueuedAt = (DateTimeOffset?)QueuedAt,
            FailureReasons = Array.Empty<string>(),
        }, options => options.ExcludingMissingMembers());
    }

    [Fact]
    public void RequestCancel_Queued_CancelsAtOnce()
    {
        // Arrange
        var run = NewRun();

        // Act
        run.RequestCancel();

        // Assert
        run.Status.Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public void Fail_Queued_RecordsReasons()
    {
        // Arrange
        var run = NewRun();

        // Act
        run.Fail(["Repository example-org/x was not found or the token has no access to it."]);

        // Assert
        new { run.Status, run.FailureReasons }.Should().BeEquivalentTo(new
        {
            Status = RunStatus.Failed,
            FailureReasons = new[] { "Repository example-org/x was not found or the token has no access to it." },
        });
    }

    [Fact]
    public void Fail_Cancelled_Throws()
    {
        // Arrange
        var run = NewRun();
        run.RequestCancel();

        // Act
        var act = () => run.Fail(["late failure"]);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pin_NotRunning_Throws()
    {
        // Arrange
        var run = NewRun();

        // Act
        var act = () => run.Pin("main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*only a running scan*");
    }
}
