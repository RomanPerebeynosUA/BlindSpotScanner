using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace BlindSpotScanner.Api.IntegrationTests;

public class ProfilesApiTests(QueueOnlyApiFactory factory) : IClassFixture<QueueOnlyApiFactory>
{
    private static object Body(int baseVersion, string markdown = "# Edited", string[]? testGlobs = null) => new
    {
        baseVersion,
        markdown,
        integrationMappedTo = "e2e-mocked-data",
        testGlobs = testGlobs ?? ["tests/**/*.cs"],
        controllerGlobs = new[] { "src/**/*Controller.cs" },
        minimalApiGlobs = Array.Empty<string>(),
        integrationGlobs = new[] { "tests/*.IntegrationTests/**" },
        integrationAttributes = Array.Empty<string>(),
        integrationBaseTypes = Array.Empty<string>(),
        notIntegrationGlobs = Array.Empty<string>(),
    };

    [Fact]
    public async Task Put_Valid_Returns200WithNewVersion()
    {
        // Arrange
        var id = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PutAsJsonAsync($"/api/repositories/{id}/profile", Body(1));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("version").GetInt32().Should().Be(2);
        body.GetProperty("integrationMappedTo").GetString().Should().Be("e2e-mocked-data");
    }

    [Fact]
    public async Task Put_Invalid_Returns400WithErrors()
    {
        // Arrange
        var id = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PutAsJsonAsync($"/api/repositories/{id}/profile", Body(1, testGlobs: []));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("profile").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Put_Invalid_DetailListsTheErrors()
    {
        // Arrange (the web shows ProblemDetails.detail, so the reasons must be in it)
        var id = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PutAsJsonAsync($"/api/repositories/{id}/profile", Body(1, testGlobs: []));

        // Assert
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Be("At least one test glob is required.");
    }

    [Fact]
    public async Task Put_StaleBase_Returns409()
    {
        // Arrange
        var id = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient();
        (await client.PutAsJsonAsync($"/api/repositories/{id}/profile", Body(1))).EnsureSuccessStatusCode();

        // Act
        using var response = await client.PutAsJsonAsync($"/api/repositories/{id}/profile", Body(1, "# Lost update"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetVersion_Old_ReturnsThatVersion()
    {
        // Arrange
        var id = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient();
        (await client.PutAsJsonAsync($"/api/repositories/{id}/profile", Body(1))).EnsureSuccessStatusCode();

        // Act
        var v1 = await client.GetFromJsonAsync<JsonElement>($"/api/repositories/{id}/profile?version=1");

        // Assert
        v1.GetProperty("version").GetInt32().Should().Be(1);
        v1.GetProperty("integrationMappedTo").GetString().Should().Be("real-seam");
    }

    [Fact]
    public async Task Versions_AfterSave_NewestFirst()
    {
        // Arrange
        var id = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient();
        (await client.PutAsJsonAsync($"/api/repositories/{id}/profile", Body(1))).EnsureSuccessStatusCode();

        // Act
        var versions = await client.GetFromJsonAsync<int[]>($"/api/repositories/{id}/profile/versions");

        // Assert
        versions.Should().Equal(2, 1);
    }

    [Fact]
    public async Task Post_Draft_Returns200WithMarkdownAndQuestions()
    {
        // Arrange
        var id = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync($"/api/repositories/{id}/profile/draft", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("markdown").GetString().Should().Contain("(drafted)");
        body.GetProperty("openQuestions").EnumerateArray().Single().GetString().Should().Be("Is the payment gateway a real seam?");
    }

    [Fact]
    public async Task Post_Draft_DoesNotSaveAVersion()
    {
        // Arrange
        var id = await factory.SeedRepositoryAsync();
        using var client = factory.CreateClient();

        // Act
        (await client.PostAsync($"/api/repositories/{id}/profile/draft", null)).EnsureSuccessStatusCode();

        // Assert
        (await client.GetFromJsonAsync<int[]>($"/api/repositories/{id}/profile/versions")).Should().Equal(1);
    }

    [Fact]
    public async Task Get_UnknownRepository_Returns404()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/repositories/{Guid.NewGuid()}/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
