using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Extraction;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Extraction;

/// <summary>M4 acceptance (doc 11): the fixture mini-repo yields the expected endpoints and integration tests.</summary>
public class BookstoreFixtureTests
{
    private static readonly RunContext Run = new("example-org", "bookstore", "main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "https://github.com/example-org/bookstore");

    private static readonly RepoProfile Profile = new()
    {
        RepositoryId = Guid.NewGuid(),
        Version = 1,
        Markdown = "# Profile — bookstore",
        IntegrationMappedTo = IntegrationMappedTo.RealSeam,
        ControllerGlobs = ["src/**/Controllers/**/*Controller.cs"],
        MinimalApiGlobs = ["src/**/Endpoints/**/*.cs"],
        TestGlobs = ["tests/**/*.cs"],
        IntegrationGlobs = ["tests/*.IntegrationTests/**"],
        IntegrationAttributes = ["Trait(\"Category\",\"Integration\")"],
        IntegrationBaseTypes = ["WebApplicationFactory"],
        NotIntegrationGlobs = ["**/*Fixture.cs"],
    };

    private static async Task<(EndpointInventory Endpoints, TestClassification Tests)> ScanAsync()
    {
        var repo = FixtureRepository.Bookstore();
        var inventory = await new EnumeratePhase(repo).EnumerateAsync(Run, Profile, CancellationToken.None);
        var endpoints = await new ExtractPhase(repo, new AspNetCoreEndpointExtractor()).ExtractAsync(Run, inventory, CancellationToken.None);
        var tests = await new ClassifyPhase(repo, new CSharpTestSyntaxReader()).ClassifyAsync(Run, inventory, Profile, CancellationToken.None);
        return (endpoints, tests);
    }

    [Fact]
    public async Task Bookstore_Extract_YieldsExpectedEndpoints()
    {
        // Act
        var (endpoints, _) = await ScanAsync();

        // Assert
        new
        {
            Endpoints = endpoints.Controllers.SelectMany(c => c.Endpoints.Select(e => $"{c.Controller} {e.Method} {e.Route}")),
            endpoints.UnverifiedReasons,
        }.Should().BeEquivalentTo(new
        {
            Endpoints = new[]
            {
                "BooksController Get api/books",
                "BooksController Get api/books/{id}",
                "BooksController Post api/books",
                "OrdersController Post api/orders",
                "OrdersController Post api/orders/{id}/cancel",
                "HealthEndpoints Get health",
            },
            UnverifiedReasons = Array.Empty<string>(),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Bookstore_Classify_YieldsExpectedIntegrationTests()
    {
        // Act
        var (_, tests) = await ScanAsync();

        // Assert
        new
        {
            Integration = tests.IntegrationTests.Select(f => f.Path),
            Ambiguous = tests.Ambiguous.Select(f => f.Path),
        }.Should().BeEquivalentTo(new
        {
            Integration = new[]
            {
                "tests/Bookstore.IntegrationTests/BooksApiTests.cs",
                "tests/Bookstore.IntegrationTests/OrdersApiTests.cs",
                "tests/Bookstore.UnitTests/StockQueryTests.cs",
            },
            Ambiguous = new[] { "tests/Bookstore.UnitTests/PriceTests.cs" },
        }, options => options.WithStrictOrdering());
    }
}
