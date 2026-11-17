using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Infrastructure.Repositories;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Repositories;

[Collection("postgres")]
public class EfProfileStoreTests(PostgresFixture db)
{
    [Fact]
    public async Task TryAddAsync_ExistingVersion_ReturnsFalse()
    {
        // Arrange
        var repository = await db.AddRepositoryAsync();
        await using (var write = db.CreateContext())
        {
            await new EfProfileStore(write).TryAddAsync(ProfileTemplate.Default(repository.Id, "x"), CancellationToken.None);
        }

        await using var context = db.CreateContext();

        // Act
        var added = await new EfProfileStore(context).TryAddAsync(ProfileTemplate.Default(repository.Id, "y"), CancellationToken.None);

        // Assert
        added.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_NoVersion_ReturnsLatest()
    {
        // Arrange
        var repository = await db.AddRepositoryAsync();
        await using (var write = db.CreateContext())
        {
            var store = new EfProfileStore(write);
            await store.TryAddAsync(ProfileTemplate.Default(repository.Id, "x"), CancellationToken.None);
            await store.TryAddAsync(new Domain.Entities.RepoProfile
            {
                RepositoryId = repository.Id,
                Version = 2,
                Markdown = "# v2",
                IntegrationMappedTo = Domain.Enums.IntegrationMappedTo.Other,
                TestGlobs = ["t"],
                ControllerGlobs = ["c"],
            }, CancellationToken.None);
        }

        await using var context = db.CreateContext();

        // Act
        var latest = await new EfProfileStore(context).GetAsync(repository.Id, null, CancellationToken.None);

        // Assert
        latest!.Markdown.Should().Be("# v2");
    }
}
