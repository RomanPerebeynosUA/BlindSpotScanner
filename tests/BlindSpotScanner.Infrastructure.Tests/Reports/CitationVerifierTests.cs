using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Extraction;
using BlindSpotScanner.Infrastructure.Tests.Extraction;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Reports;

/// <summary>examples/sample-report.json checked against the bookstore fixture (whose files it cites).</summary>
public class CitationVerifierTests
{
    private const string Books = "tests/Bookstore.IntegrationTests/BooksApiTests.cs";
    private const string Factory = "tests/Bookstore.IntegrationTests/ApiFactory.cs";

    private static readonly IntegrationReport Sample = PostgresFixture.LoadReport("sample-report.json", Guid.NewGuid());

    private static Task<CitationCheck> Verify(IntegrationReport report, IGitHubClient? github = null) =>
        new CitationVerifier(github ?? FixtureRepository.Bookstore(), new CSharpTestSyntaxReader()).VerifyAsync(report, CancellationToken.None);

    /// <summary>The sample report with the first endpoint's evidence replaced.</summary>
    private static IntegrationReport WithFirstEndpointEvidence(string citation)
    {
        var books = Sample.EndpointCoverage[0];
        var endpoints = books.Endpoints.Select((e, i) => i == 0 ? e with { Evidence = [citation] } : e).ToList();
        return Sample with { EndpointCoverage = [books with { Endpoints = endpoints }, .. Sample.EndpointCoverage.Skip(1)] };
    }

    private static IntegrationReport WithFirstDimensionEvidence(string citation) =>
        Sample with { Scorecard = [Sample.Scorecard[0] with { Evidence = [citation] }, .. Sample.Scorecard.Skip(1)] };

    [Fact]
    public async Task Verify_AllCitationsExist_NoFailures()
    {
        // Act
        var check = await Verify(Sample);

        // Assert
        check.Should().BeEquivalentTo(new { Checked = 6, Resolved = 6, Failures = Array.Empty<string>() });
    }

    [Fact]
    public async Task Verify_InventedTest_IsListed()
    {
        // Act
        var check = await Verify(WithFirstEndpointEvidence($"{Books}::Deletes_book"));

        // Assert
        check.Failures.Should().Equal($"{Books}::Deletes_book: no test named Deletes_book");
    }

    [Fact]
    public async Task Verify_MissingFile_IsListed()
    {
        // Act
        var check = await Verify(WithFirstDimensionEvidence("tests/Gone.cs::Anything"));

        // Assert
        check.Failures.Should().Equal("tests/Gone.cs::Anything: file not found at a1b2c3d");
    }

    [Fact]
    public async Task Verify_FixtureMethodInScorecard_IsAccepted()
    {
        // Act
        var check = await Verify(WithFirstDimensionEvidence($"{Factory}::InitializeAsync"));

        // Assert
        check.Failures.Should().BeEmpty();
    }


    [Fact]
    public async Task Verify_FixtureClassInScorecard_IsAccepted()
    {
        // Act
        var check = await Verify(WithFirstDimensionEvidence($"{Factory}::ApiFactory"));

        // Assert
        check.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_ClassAsCoverageEvidence_IsRejected()
    {
        // Act
        var check = await Verify(WithFirstEndpointEvidence($"{Factory}::ApiFactory"));

        // Assert
        check.Failures.Should().Equal($"{Factory}::ApiFactory: no test named ApiFactory");
    }

    [Fact]
    public async Task Verify_HelperMethodAsCoverageEvidence_IsRejected()
    {
        // Act
        var check = await Verify(WithFirstEndpointEvidence($"{Factory}::InitializeAsync"));

        // Assert
        check.Failures.Should().Equal($"{Factory}::InitializeAsync: no test named InitializeAsync");
    }

    [Fact]
    public async Task Verify_InventedTestInEvidenceBullet_IsListed()
    {
        // Arrange
        var dimension = Sample.Scorecard[0];
        var bullets = dimension.JustificationBullets.Select((b, i) => i == 2 ? $"Evidence: {Books}::Ghost_test" : b).ToList();
        var report = Sample with { Scorecard = [dimension with { JustificationBullets = bullets }, .. Sample.Scorecard.Skip(1)] };

        // Act
        var check = await Verify(report);

        // Assert
        check.Failures.Should().Equal($"{Books}::Ghost_test: no method or class named Ghost_test");
    }


    [Fact]
    public async Task Verify_CitationEndingASentence_IgnoresThePeriod()
    {
        // Arrange
        var dimension = Sample.Scorecard[0];
        var bullets = dimension.JustificationBullets.Select((b, i) => i == 2 ? $"Evidence: see {Books}::Create_persists_book." : b).ToList();
        var report = Sample with { Scorecard = [dimension with { JustificationBullets = bullets }, .. Sample.Scorecard.Skip(1)] };

        // Act
        var check = await Verify(report);

        // Assert
        check.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_SameFileCitedTwice_FetchedOnce()
    {
        // Arrange
        var github = new CountingGitHub(FixtureRepository.Bookstore());

        // Act
        await Verify(Sample, github);

        // Assert
        github.Fetches.Should().Equal(Factory, Books, "tests/Bookstore.IntegrationTests/OrdersApiTests.cs");
    }

    private sealed class CountingGitHub(IGitHubClient inner) : IGitHubClient
    {
        public List<string> Fetches { get; } = [];

        public Task<string?> GetFileTextAsync(string owner, string name, string path, string commitSha, CancellationToken ct)
        {
            Fetches.Add(path);
            return inner.GetFileTextAsync(owner, name, path, commitSha, ct);
        }

        public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct) => throw new NotSupportedException();

        public Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct) => throw new NotSupportedException();

        public Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct) => throw new NotSupportedException();

        public Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Verify_PathWithDotDot_RejectedWithoutFetching()
    {
        // Arrange
        var github = new CountingGitHub(FixtureRepository.Bookstore());

        // Act
        var check = await Verify(WithFirstDimensionEvidence("tests/../../../user/keys.cs::X"), github);

        // Assert
        new { check.Failures, Fetched = github.Fetches.Contains("tests/../../../user/keys.cs") }.Should().BeEquivalentTo(new
        {
            Failures = new[] { "tests/../../../user/keys.cs::X: not a repository-relative path" },
            Fetched = false,
        });
    }
}
