using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlindSpotScanner.Api.Auth;
using FluentAssertions;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>Doc 10 roles: Viewer reads, Member also scans and creates tickets/PRs, Admin also gets settings (D26).</summary>
public class AuthApiTests(QueueOnlyApiFactory factory) : IClassFixture<QueueOnlyApiFactory>
{
    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        // Arrange
        using var client = factory.CreateClient("none");

        // Act
        var response = await client.GetAsync("/api/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_Member_ReturnsNameAndRoles()
    {
        // Arrange
        using var client = factory.CreateClient(Roles.Member, "dev@example.com");

        // Act
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");

        // Assert
        new { Name = me.GetProperty("name").GetString(), Roles = string.Join(",", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString())) }
            .Should().Be(new { Name = "dev@example.com", Roles = "Member" });
    }

    [Fact]
    public async Task StartScan_Viewer_Returns403()
    {
        // Arrange
        var repositoryId = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient(Roles.Viewer);

        // Act
        var response = await client.PostAsync($"/api/repositories/{repositoryId}/scans", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StartScan_Member_Returns202()
    {
        // Arrange
        var repositoryId = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient(Roles.Member);

        // Act
        var response = await client.PostAsync($"/api/repositories/{repositoryId}/scans", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GetRepositories_Viewer_Returns200()
    {
        // Arrange
        using var client = factory.CreateClient(Roles.Viewer);

        // Act
        var response = await client.GetAsync("/api/repositories");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hub_Anonymous_Returns401()
    {
        // Arrange
        using var client = factory.CreateClient("none");

        // Act
        var response = await client.PostAsync("/hubs/scans/negotiate?negotiateVersion=1", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_Anonymous_Returns200()
    {
        // Arrange
        using var client = factory.CreateClient("none");

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateJira_AsMember_RecordsUserName()
    {
        // Arrange
        var repositoryId = await factory.SeedRepositoryAsync();
        var runId = Guid.NewGuid();
        await factory.SeedSucceededRunAsync(repositoryId, "sample-report.json", runId, null);
        using var client = factory.CreateClient(Roles.Member, "dev@example.com");
        var draft = (await (await client.PostAsync($"/api/runs/{runId}/findings/f2/jira-preview", null)).Content.ReadFromJsonAsync<JsonElement>());
        await client.PostAsJsonAsync($"/api/runs/{runId}/findings/f2/jira", new
        {
            summary = draft.GetProperty("summary").GetString(),
            description = draft.GetProperty("description").GetString(),
            projectKey = "BSS",
            issueType = "Task",
            priority = "High",
            labels = new[] { "integration-coverage" },
            force = false,
        });

        // Act
        var actions = await client.GetFromJsonAsync<JsonElement>($"/api/repositories/{repositoryId}/actions");

        // Assert
        actions[0].GetProperty("createdBy").GetString().Should().Be("dev@example.com");
    }

    [Fact]
    public async Task Login_TabSmuggledReturnUrl_RedirectsHome()
    {
        // Arrange (browsers drop tabs and newlines while parsing, so "/\t/evil" becomes "//evil")
        using var client = factory.CreateClient("none");

        // Act
        var response = await client.GetAsync("/api/auth/login?returnUrl=%2F%09%2Fevil.example");

        // Assert
        response.Headers.Location?.ToString().Should().Be("/");
    }

    [Fact]
    public async Task Login_ExternalReturnUrl_RedirectsHome()
    {
        // Arrange (Local mode: no identity provider to visit)
        using var client = factory.CreateClient("none");

        // Act
        var response = await client.GetAsync("/api/auth/login?returnUrl=https://evil.example.com/");

        // Assert
        new { response.StatusCode, Location = response.Headers.Location?.ToString() }.Should().Be(new { StatusCode = HttpStatusCode.Redirect, Location = (string?)"/" });
    }
}

public class RoleClaimsTests
{
    [Fact]
    public void Roles_UnknownClaimValue_FallsBackToDefault()
    {
        // Act
        var role = RoleClaims.Effective(["SuperUser", "admin "], Roles.Viewer);

        // Assert
        role.Should().Be(Roles.Viewer);
    }

    [Fact]
    public void Roles_ViewerAndAdmin_EffectiveIsAdmin()
    {
        // Act
        var role = RoleClaims.Effective([Roles.Viewer, Roles.Admin], Roles.Viewer);

        // Assert
        role.Should().Be(Roles.Admin);
    }
}
