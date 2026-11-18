using BlindSpotScanner.Application.Scanning;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class ScanTelemetryTests
{
    [Theory]
    [InlineData("citation: tests/A.cs::B not found", "citation")]
    [InlineData("validation: summary.endpoints.covered mismatch", "validation")]
    [InlineData("This worker runs claude-x with prompts 1.0.0, but the scan was queued for claude-y with prompts 1.0.0.", "model-mismatch")]
    [InlineData("Profile version 3 of the repository no longer exists.", "profile")]
    [InlineData("GitHub did not answer; try again later.", "error")]
    public void FailureCategory_KnownReasons_MapToFixedTags(string reason, string category)
    {
        // Act (doc 10's failure-reasons metric must not grow a series per reason text)
        var tag = ScanTelemetry.FailureCategory(reason);

        // Assert
        tag.Should().Be(category);
    }
}
