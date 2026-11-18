using BlindSpotScanner.Application.Scanning;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class TestAssertionRulesTests
{
    [Fact]
    public void UncheckedFields_LeafMatchesIgnoringCase_IsChecked()
    {
        // Arrange
        string[] sent = ["title"];
        string[] checkedFields = ["Title"];

        // Act
        var missing = TestAssertionRules.UncheckedFields(sent, checkedFields);

        // Assert
        missing.Should().BeEmpty();
    }

    [Fact]
    public void UncheckedFields_NestedLeafMissing_IsUnchecked()
    {
        // Arrange
        string[] sent = ["lines[].bookId", "lines[].quantity"];
        string[] checkedFields = ["Lines", "Total"];

        // Act
        var missing = TestAssertionRules.UncheckedFields(sent, checkedFields);

        // Assert
        missing.Should().Equal("lines[].bookId", "lines[].quantity");
    }
}
