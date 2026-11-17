using System.Net;
using FluentAssertions;

namespace BlindSpotScanner.Api.IntegrationTests;

public class HealthEndpointTests(QueueOnlyApiFactory factory) : IClassFixture<QueueOnlyApiFactory>
{
    [Fact]
    public async Task GetHealth_WhenApiIsRunning_Returns200()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
