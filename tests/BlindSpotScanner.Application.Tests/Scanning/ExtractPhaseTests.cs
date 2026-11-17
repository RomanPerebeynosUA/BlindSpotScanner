using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class ExtractPhaseTests
{
    private const string Books = "src/Api/Controllers/BooksController.cs";
    private const string Orders = "src/Api/Controllers/OrdersController.cs";

    private static readonly RunContext Run = new("example-org", "bookstore-api", "main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "https://github.com/example-org/bookstore-api");

    private static readonly FakeBlobReader Blobs = new(new Dictionary<string, string> { ["sha-books"] = "books source", ["sha-orders"] = "orders source" });

    private static RepositoryInventory Inventory(IReadOnlyList<string>? reasons = null) =>
        new([new RepoFile(Orders, "sha-orders", 10), new RepoFile(Books, "sha-books", 10)], [], reasons ?? []);

    private static ExtractedController Controller(string name, string path, int endpoints) =>
        new(name, path, "api/x", Enumerable.Range(0, endpoints).Select(i => new ExtractedEndpoint(EndpointMethod.Get, $"api/x/{i}")).ToList());

    private static FakeEndpointExtractor Extractor(IReadOnlyList<string>? booksWarnings = null) => new(new Dictionary<string, ExtractionResult>
    {
        [Books] = new([Controller("BooksController", Books, 3)], booksWarnings ?? []),
        [Orders] = new([Controller("OrdersController", Orders, 2)], []),
    });

    [Fact]
    public async Task Extract_EachControllerFile_ReadOnceAtPinnedSha()
    {
        // Arrange
        var blobs = new FakeBlobReader(new Dictionary<string, string> { ["sha-books"] = "b", ["sha-orders"] = "o" });
        var phase = new ExtractPhase(blobs, Extractor());

        // Act
        await phase.ExtractAsync(Run, Inventory(), CancellationToken.None);

        // Assert
        blobs.Reads.Should().BeEquivalentTo("sha-orders", "sha-books");
    }

    [Fact]
    public async Task Extract_Controllers_AreOrderedByPath()
    {
        // Arrange
        var phase = new ExtractPhase(Blobs, Extractor());

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(), CancellationToken.None);

        // Assert
        endpoints.Controllers.Select(c => c.Controller).Should().Equal("BooksController", "OrdersController");
    }

    [Fact]
    public async Task Extract_ExtractorWarnings_BecomeUnverifiedReasons()
    {
        // Arrange
        var phase = new ExtractPhase(Blobs, Extractor(booksWarnings: ["w1"]));

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(["submodule at 'libs/x' was not enumerated"]), CancellationToken.None);

        // Assert
        endpoints.UnverifiedReasons.Should().Equal("submodule at 'libs/x' was not enumerated", "w1");
    }

    [Fact]
    public async Task Extract_EndpointCount_SumsAllControllers()
    {
        // Arrange
        var phase = new ExtractPhase(Blobs, Extractor());

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(), CancellationToken.None);

        // Assert
        endpoints.EndpointCount.Should().Be(5);
    }
}
