using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Application.Tests.Reports;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Remediation;

public class FixContextBuilderTests
{
    internal static readonly string[] BookstoreFiles =
    [
        "src/Bookstore.Api/Controllers/BooksController.cs",
        "src/Bookstore.Api/Controllers/OrdersController.cs",
        "src/Bookstore.Api/Bookstore.Api.csproj",
        "tests/Bookstore.IntegrationTests/ApiFactory.cs",
        "tests/Bookstore.IntegrationTests/BooksApiTests.cs",
        "tests/Bookstore.IntegrationTests/Bookstore.IntegrationTests.csproj",
        "tests/Bookstore.IntegrationTests/DbFixture.cs",
        "tests/Bookstore.IntegrationTests/OrdersApiTests.cs",
    ];

    private static readonly IntegrationReport Report = Fixtures.Current;

    private static ReportFinding Finding(string id) => Report.Findings.Single(f => f.Id == id);

    private static Task<FixContext?> BuildAsync(string findingId, IReadOnlyList<string>? files = null, Dictionary<string, string>? contents = null)
    {
        var github = new TreeGitHub(files ?? BookstoreFiles);
        foreach (var (path, content) in contents ?? [])
        {
            github.Contents[path] = content;
        }

        return new FixContextBuilder(github).BuildAsync(Report, Finding(findingId), Report.Meta.CommitSha, CancellationToken.None);
    }

    [Fact]
    public async Task BuildAsync_UncoveredEndpoint_ReadsControllerAndSiblings()
    {
        // Act
        var context = (await BuildAsync("f2"))!;

        // Assert
        new
        {
            Controller = context.Controller!.Path,
            ControllerContent = context.Controller.Content,
            FirstSibling = context.SiblingTests[0].Path,
            context.TestProjectFolder,
            context.TargetFolder,
            Project = context.TestProjectFile.Path,
            Target = context.TargetTest,
        }.Should().Be(new
        {
            Controller = "src/Bookstore.Api/Controllers/OrdersController.cs",
            ControllerContent = "// src/Bookstore.Api/Controllers/OrdersController.cs",
            FirstSibling = "tests/Bookstore.IntegrationTests/OrdersApiTests.cs",
            TestProjectFolder = "tests/Bookstore.IntegrationTests",
            TargetFolder = "tests/Bookstore.IntegrationTests/",
            Project = "tests/Bookstore.IntegrationTests/Bookstore.IntegrationTests.csproj",
            Target = (SourceFile?)null,
        });
    }

    [Fact]
    public async Task BuildAsync_FalsePositive_TargetTestIsEvidenceFile()
    {
        // Act
        var context = (await BuildAsync("f3"))!;

        // Assert
        new { Target = context.TargetTest!.Path, Controller = context.Controller }
            .Should().Be(new { Target = "tests/Bookstore.IntegrationTests/BooksApiTests.cs", Controller = (SourceFile?)null });
    }

    [Fact]
    public async Task BuildAsync_FixturesAreFactoryAndFixtureFiles()
    {
        // Act
        var context = (await BuildAsync("f2"))!;

        // Assert
        context.Fixtures.Select(f => f.Path).Should().Equal("tests/Bookstore.IntegrationTests/ApiFactory.cs", "tests/Bookstore.IntegrationTests/DbFixture.cs");
    }

    [Fact]
    public async Task BuildAsync_NoCsproj_ReturnsNull()
    {
        // Act
        var context = await BuildAsync("f2", [.. BookstoreFiles.Where(f => !f.EndsWith("IntegrationTests.csproj", StringComparison.Ordinal))]);

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_NearestProjectIsNotATestProject_ReturnsNull()
    {
        // Arrange (tests living inside a production project would let the guard accept production edits)
        var files = BookstoreFiles.Where(f => !f.EndsWith("IntegrationTests.csproj", StringComparison.Ordinal)).Append("tests/Bookstore.Tests.csproj").ToList();

        // Act
        var context = await BuildAsync("f2", files, new() { ["tests/Bookstore.Tests.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />" });

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_TestProjectAtRepositoryRoot_ReturnsNull()
    {
        // Arrange (a root project makes every path "inside the test project")
        var files = BookstoreFiles.Where(f => !f.EndsWith("IntegrationTests.csproj", StringComparison.Ordinal)).Append("Bookstore.Tests.csproj").ToList();

        // Act
        var context = await BuildAsync("f2", files);

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_BlobShasCoverTheTree()
    {
        // Act
        var context = (await BuildAsync("f2"))!;

        // Assert
        context.BlobShas.Should().HaveCount(BookstoreFiles.Length).And.ContainKey("src/Bookstore.Api/Controllers/OrdersController.cs")
            .WhoseValue.Should().Be("sha:src/Bookstore.Api/Controllers/OrdersController.cs");
    }

    /// <summary>A flat tree at one commit; blob SHAs are "sha:{path}" and contents "// {path}".</summary>
    internal sealed class TreeGitHub(IReadOnlyList<string> files) : IGitHubClient
    {
        public Dictionary<string, IReadOnlyList<string>> TreesAt { get; } = [];

        /// <summary>Commits whose tree read reports truncated:true.</summary>
        public HashSet<string> TruncatedAt { get; } = [];

        public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct) => throw new NotSupportedException();

        /// <summary>The branches whose head was asked for, in order.</summary>
        public List<string> HeadOf { get; } = [];

        public Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct)
        {
            HeadOf.Add(branch);
            return Task.FromResult("head-sha");
        }

        public Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct)
        {
            var at = TreesAt.GetValueOrDefault(treeSha, files);
            return Task.FromResult(new GitTree([.. at.Select(f => new GitTreeEntry(f, GitEntryType.Blob, BlobSha(treeSha, f), 100))], TruncatedAt.Contains(treeSha)));
        }

        /// <summary>Blob SHAs per commit; a test overriding a commit's tree can mark a changed file with "changed:".</summary>
        public Dictionary<string, string> ChangedAtHead { get; } = [];

        private string BlobSha(string treeSha, string path) =>
            treeSha == "head-sha" && ChangedAtHead.TryGetValue(path, out var sha) ? sha : $"sha:{path}";

        public Task<string?> GetFileTextAsync(string owner, string name, string path, string commitSha, CancellationToken ct) => throw new NotSupportedException();

        /// <summary>Content overrides by path; other .csproj files reference xunit (a test project), other files are "// {path}".</summary>
        public Dictionary<string, string> Contents { get; } = [];

        public Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct)
        {
            var path = blobSha["sha:".Length..];
            return Task.FromResult(Contents.TryGetValue(path, out var content) ? content
                : path.EndsWith(".csproj", StringComparison.Ordinal) ? "<Project><ItemGroup><PackageReference Include=\"xunit\" /></ItemGroup></Project>"
                : $"// {path}");
        }
    }
}
