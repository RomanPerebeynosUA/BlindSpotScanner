using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Repositories;

public class ProfileServiceTests
{
    private readonly FakeRepositoryStore _repositories = new();
    private readonly FakeProfileStore _profiles = new();
    private readonly Guid _repositoryId;

    public ProfileServiceTests()
    {
        var repository = new Repository { Id = Guid.NewGuid(), Owner = "example-org", Name = "bookstore", DefaultBranch = "main", HtmlUrl = "https://github.com/example-org/bookstore" };
        _repositories.Repositories.Add(repository);
        _profiles.Profiles.Add(ProfileTemplate.Default(repository.Id, "example-org/bookstore"));
        _repositoryId = repository.Id;
    }

    private ProfileService Service => new(_profiles, _repositories);

    private static ProfileInput Input(
        string markdown = "# Profile", string mapping = "e2e-mocked-data", string[]? testGlobs = null, string[]? controllerGlobs = null, string[]? minimalApiGlobs = null) =>
        new(markdown, mapping, testGlobs ?? ["tests/**/*.cs"], controllerGlobs ?? ["src/**/*Controller.cs"], minimalApiGlobs ?? [], [], [], [], []);

    [Fact]
    public async Task SaveAsync_ValidInput_SavesNextVersion()
    {
        // Act
        var result = await Service.SaveAsync(_repositoryId, 1, Input(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Saved);
        result.Profile.Should().BeEquivalentTo(new { RepositoryId = _repositoryId, Version = 2, Markdown = "# Profile", IntegrationMappedTo = IntegrationMappedTo.E2eMockedData });
        _profiles.Profiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAsync_BlankAndDuplicateEntries_AreTrimmedAndDropped()
    {
        // Act
        var result = await Service.SaveAsync(_repositoryId, 1, Input(testGlobs: [" a ", "", "a", "b"]), CancellationToken.None);

        // Assert
        result.Profile!.TestGlobs.Should().Equal("a", "b");
    }

    [Fact]
    public async Task SaveAsync_NullEntry_IsDropped()
    {
        // Act (JSON like "testGlobs": [null, "a"])
        var result = await Service.SaveAsync(_repositoryId, 1, Input(testGlobs: [null!, "a"]), CancellationToken.None);

        // Assert
        result.Profile!.TestGlobs.Should().Equal("a");
    }

    [Fact]
    public async Task SaveAsync_NegativeBaseVersion_ReturnsInvalid()
    {
        // Act
        var result = await Service.SaveAsync(_repositoryId, -1, Input(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Invalid);
    }

    [Fact]
    public async Task SaveAsync_NoProfileAndBaseNotZero_ReturnsConflict()
    {
        // Arrange
        _profiles.Profiles.Clear();

        // Act
        var result = await Service.SaveAsync(_repositoryId, 5, Input(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Conflict);
    }

    [Fact]
    public async Task SaveAsync_BlankMarkdown_ReturnsInvalid()
    {
        // Act
        var result = await Service.SaveAsync(_repositoryId, 1, Input(markdown: "  "), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Markdown");
    }

    [Fact]
    public async Task SaveAsync_UnknownMapping_ReturnsInvalid()
    {
        // Act
        var result = await Service.SaveAsync(_repositoryId, 1, Input(mapping: "realseam"), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("integrationMappedTo");
    }

    [Fact]
    public async Task SaveAsync_NoTestGlobs_ReturnsInvalid()
    {
        // Act
        var result = await Service.SaveAsync(_repositoryId, 1, Input(testGlobs: [" "]), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("test glob");
    }

    [Fact]
    public async Task SaveAsync_NoControllerOrMinimalApiGlobs_ReturnsInvalid()
    {
        // Act
        var result = await Service.SaveAsync(_repositoryId, 1, Input(controllerGlobs: [], minimalApiGlobs: []), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("controller");
    }

    [Fact]
    public async Task SaveAsync_StaleBaseVersion_ReturnsConflict()
    {
        // Arrange
        await Service.SaveAsync(_repositoryId, 1, Input(), CancellationToken.None);

        // Act
        var result = await Service.SaveAsync(_repositoryId, 1, Input(markdown: "# Mine"), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Conflict);
        _profiles.Profiles.Should().NotContain(p => p.Markdown == "# Mine");
    }

    [Fact]
    public async Task SaveAsync_StoreClash_ReturnsConflict()
    {
        // Arrange
        _profiles.ClashNext = true;

        // Act
        var result = await Service.SaveAsync(_repositoryId, 1, Input(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.Conflict);
    }

    [Fact]
    public async Task SaveAsync_UnknownRepository_ReturnsRepositoryNotFound()
    {
        // Act
        var result = await Service.SaveAsync(Guid.NewGuid(), 1, Input(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(SaveProfileStatus.RepositoryNotFound);
    }

    [Fact]
    public async Task VersionsAsync_UnknownRepository_ReturnsNull()
    {
        // Act
        var versions = await Service.VersionsAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        versions.Should().BeNull();
    }
}
