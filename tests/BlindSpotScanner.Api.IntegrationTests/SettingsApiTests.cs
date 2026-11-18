using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>The read-only settings page (D26): Admins see which connections are configured, secrets masked.</summary>
public class SettingsApiTests(QueueOnlyApiFactory factory) : IClassFixture<QueueOnlyApiFactory>
{
    [Fact]
    public async Task Connections_Admin_ListsMaskedValuesOnly()
    {
        // Arrange
        using var client = factory.CreateClient("Admin");

        // Act
        var text = await client.GetStringAsync("/api/settings/connections");

        // Assert
        var jira = JsonDocument.Parse(text).RootElement.EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "jira");
        new
        {
            LeaksJira = text.Contains("jira-test-token", StringComparison.Ordinal),
            LeaksClaude = text.Contains("test-key", StringComparison.Ordinal) && !text.Contains("••••", StringComparison.Ordinal),
            Configured = jira.GetProperty("configured").GetBoolean(),
            Masked = jira.GetProperty("masked").GetString(),
        }.Should().Be(new { LeaksJira = false, LeaksClaude = false, Configured = true, Masked = (string?)"••••oken" });
    }

    [Fact]
    public async Task Connections_Member_Returns403()
    {
        // Arrange
        using var client = factory.CreateClient("Member");

        // Act
        using var response = await client.GetAsync("/api/settings/connections");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Test_UnknownKind_Returns404()
    {
        // Arrange
        using var client = factory.CreateClient("Admin");

        // Act
        using var response = await client.PostAsync("/api/settings/connections/slack/test", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
