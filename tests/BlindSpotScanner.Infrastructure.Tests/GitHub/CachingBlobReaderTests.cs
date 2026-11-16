using System.Text;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Infrastructure.GitHub;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Infrastructure.Tests.GitHub;

[Collection("postgres")]
public sealed class CachingBlobReaderTests(PostgresFixture db) : IDisposable
{
    private const string Owner = "example-org";
    private const string Name = "bookstore-api";
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private static string NewSha() => Guid.NewGuid().ToString("N") + "00000000";

    private GitHubClient GitHub() =>
        new(new HttpClient(), Options.Create(new GitHubOptions { ApiBaseUrl = _server.Url!, Token = "ghp_test" }));

    private void ServeBlob(string sha, string text) =>
        _server.Given(Request.Create().WithPath($"/repos/{Owner}/{Name}/git/blobs/{sha}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                sha,
                encoding = "base64",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
            }));

    private async Task<string> ReadWithFreshContextAsync(IGitHubClient github, string sha)
    {
        await using var context = db.CreateContext();
        return await new CachingBlobReader(context, github).ReadAsync(Owner, Name, sha, CancellationToken.None);
    }

    [Fact]
    public async Task Read_SameBlobTwice_FetchesFromGitHubOnce()
    {
        // Arrange
        var sha = NewSha();
        ServeBlob(sha, "public class BooksController {}");

        // Act
        var first = await ReadWithFreshContextAsync(GitHub(), sha);
        var second = await ReadWithFreshContextAsync(GitHub(), sha);

        // Assert
        new { first, second, Requests = _server.LogEntries.Count() }.Should().BeEquivalentTo(new
        {
            first = "public class BooksController {}",
            second = "public class BooksController {}",
            Requests = 1,
        });
    }

    [Fact]
    public async Task Read_CachedBlob_ReturnsWithoutGitHub()
    {
        // Arrange
        var sha = NewSha();
        await using (var seed = db.CreateContext())
        {
            seed.FileCache.Add(new FileCacheEntry { BlobSha = sha, Content = "cached text" });
            await seed.SaveChangesAsync();
        }

        // Act
        var text = await ReadWithFreshContextAsync(GitHub(), sha);

        // Assert
        new { text, Requests = _server.LogEntries.Count() }.Should().BeEquivalentTo(new { text = "cached text", Requests = 0 });
    }

    [Fact]
    public async Task Read_BlobWithNulByte_ReturnsContentWithoutCaching()
    {
        // Arrange
        var sha = NewSha();
        ServeBlob(sha, "PNG\0binary");

        // Act
        var text = await ReadWithFreshContextAsync(GitHub(), sha);

        // Assert
        await using var check = db.CreateContext();
        new { text, Cached = await check.FileCache.AnyAsync(f => f.BlobSha == sha) }
            .Should().BeEquivalentTo(new { text = "PNG\0binary", Cached = false });
    }

    [Fact]
    public async Task Read_BlobCachedConcurrently_DoesNotThrow()
    {
        // Arrange
        var sha = NewSha();
        var racing = new RacingGitHubClient(db, sha, "public class OrdersController {}");

        // Act
        var text = await ReadWithFreshContextAsync(racing, sha);

        // Assert
        text.Should().Be("public class OrdersController {}");
    }

    /// <summary>
    /// Simulates a concurrent scan: between the reader's cache miss and its insert, another context
    /// stores the same blob, so the reader's insert hits the primary key.
    /// </summary>
    private sealed class RacingGitHubClient(PostgresFixture db, string sha, string content) : IGitHubClient
    {
        public async Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct)
        {
            await using var other = db.CreateContext();
            other.FileCache.Add(new FileCacheEntry { BlobSha = sha, Content = content });
            await other.SaveChangesAsync(ct);
            return content;
        }

        public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct) => throw new NotSupportedException();

        public Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct) => throw new NotSupportedException();

        public Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct) => throw new NotSupportedException();
    }
}
