using BlindSpotScanner.Application.Settings;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Settings;

/// <summary>Doc 10: secrets are never returned by the API, only masked as ••••last4.</summary>
public class SecretMaskTests
{
    [Fact]
    public void Mask_LongSecret_ShowsLastFour()
    {
        // Act
        var masked = SecretMask.Mask("jira-token-abcd");

        // Assert
        masked.Should().Be("••••abcd");
    }

    [Fact]
    public void Mask_ShortSecret_RevealsNothing()
    {
        // Act
        var masked = SecretMask.Mask("abc");

        // Assert
        masked.Should().Be("••••");
    }

    [Fact]
    public void Mask_Empty_ReturnsNull()
    {
        // Act
        var masked = SecretMask.Mask("  ");

        // Assert
        masked.Should().BeNull();
    }
}
