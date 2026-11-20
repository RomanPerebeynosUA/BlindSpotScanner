using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class ExtractPhaseTests
{
    private const string Books = "src/Api/Controllers/BooksController.cs";
    private const string Orders = "src/Api/Controllers/OrdersController.cs";
    private const string Program = "src/Api/Program.cs";
    private const string Extensions = "src/Api/PaymentsServiceCollectionExtensions.cs";
    private const string SeamsNotDetected = "seams not detected: no Program.cs, Startup.cs or *ServiceCollectionExtensions.cs found";

    private static readonly ProductionSeam Db = new(SeamKind.Database, "bookstore-db", Program);
    private static readonly ProductionSeam Payments = new(SeamKind.OutboundHttp, "payment-gateway", Program);

    private static readonly RunContext Run = new("example-org", "bookstore-api", "main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "https://github.com/example-org/bookstore-api");

    private static readonly FakeBlobReader Blobs = new(new Dictionary<string, string> { ["sha-books"] = "books source", ["sha-orders"] = "orders source", ["sha-program"] = "program source", ["sha-ext"] = "ext source" });

    private static readonly RepoFile ProgramFile = new(Program, "sha-program", 10);

    private static RepositoryInventory Inventory(IReadOnlyList<string>? reasons = null, IReadOnlyList<RepoFile>? startup = null) =>
        new([new RepoFile(Orders, "sha-orders", 10), new RepoFile(Books, "sha-books", 10)], [], startup ?? [ProgramFile], reasons ?? []);

    private static ExtractedController Controller(string name, string path, int endpoints) =>
        new(name, path, "api/x", Enumerable.Range(0, endpoints).Select(i => new ExtractedEndpoint(EndpointMethod.Get, $"api/x/{i}")).ToList());

    private static FakeEndpointExtractor Extractor(IReadOnlyList<string>? booksWarnings = null, IReadOnlyDictionary<string, IReadOnlyList<ProductionSeam>>? seams = null) => new(
        new Dictionary<string, ExtractionResult>
        {
            [Books] = new([Controller("BooksController", Books, 3)], booksWarnings ?? []),
            [Orders] = new([Controller("OrdersController", Orders, 2)], []),
        },
        seams);

    [Fact]
    public async Task Extract_EachControllerFile_ReadOnceAtPinnedSha()
    {
        // Arrange
        var blobs = new FakeBlobReader(new Dictionary<string, string> { ["sha-books"] = "b", ["sha-orders"] = "o", ["sha-program"] = "p" });
        var phase = new ExtractPhase(blobs, Extractor());

        // Act
        await phase.ExtractAsync(Run, Inventory(), CancellationToken.None);

        // Assert
        blobs.Reads.Should().BeEquivalentTo("sha-orders", "sha-books", "sha-program");
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

    [Fact]
    public async Task Extract_StartupFiles_SeamsCollected()
    {
        // Arrange
        var phase = new ExtractPhase(Blobs, Extractor(seams: new Dictionary<string, IReadOnlyList<ProductionSeam>> { [Program] = [Db] }));

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(), CancellationToken.None);

        // Assert
        endpoints.Seams.Should().Equal(Db);
    }

    [Fact]
    public async Task Extract_SameSeamInTwoFiles_IsListedOnce()
    {
        // Arrange
        var seams = new Dictionary<string, IReadOnlyList<ProductionSeam>> { [Program] = [Payments], [Extensions] = [Payments with { SourcePath = Extensions }] };
        var phase = new ExtractPhase(Blobs, Extractor(seams: seams));

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(startup: [ProgramFile, new RepoFile(Extensions, "sha-ext", 10)]), CancellationToken.None);

        // Assert
        endpoints.Seams.Should().Equal(Payments with { SourcePath = Extensions });
    }

    [Fact]
    public async Task Extract_SeamsFromSeveralFiles_AreOrderedByName()
    {
        // Arrange
        var seams = new Dictionary<string, IReadOnlyList<ProductionSeam>> { [Program] = [Payments], [Extensions] = [Db with { SourcePath = Extensions }] };
        var phase = new ExtractPhase(Blobs, Extractor(seams: seams));

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(startup: [ProgramFile, new RepoFile(Extensions, "sha-ext", 10)]), CancellationToken.None);

        // Assert
        endpoints.Seams!.Select(s => s.Name).Should().Equal("bookstore-db", "payment-gateway");
    }

    [Fact]
    public async Task Extract_NoStartupFile_SeamsNull()
    {
        // Arrange
        var phase = new ExtractPhase(Blobs, Extractor());

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(startup: []), CancellationToken.None);

        // Assert
        endpoints.Seams.Should().BeNull();
    }

    [Fact]
    public async Task Extract_NoStartupFile_AddsUnverifiedReason()
    {
        // Arrange
        var phase = new ExtractPhase(Blobs, Extractor());

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(startup: []), CancellationToken.None);

        // Assert
        endpoints.UnverifiedReasons.Should().Equal(SeamsNotDetected);
    }

    [Fact]
    public async Task Extract_StartupFileWithoutRegistrations_SeamsEmptyNotNull()
    {
        // Arrange
        var phase = new ExtractPhase(Blobs, Extractor());

        // Act
        var endpoints = await phase.ExtractAsync(Run, Inventory(), CancellationToken.None);

        // Assert
        new { endpoints.Seams, endpoints.UnverifiedReasons }.Should().BeEquivalentTo(new { Seams = Array.Empty<ProductionSeam>(), UnverifiedReasons = Array.Empty<string>() });
    }

    [Fact]
    public async Task Extract_StartupFileWithKebabCaseConvention_AppliesToControllersInItsProject()
    {
        // Arrange
        var blobs = new FakeBlobReader(new Dictionary<string, string> { ["sha-books"] = "b", ["sha-orders"] = "o", ["sha-program"] = "KEBAB" });
        var extractor = Extractor();
        var inventory = Inventory(startup: [ProgramFile]);

        // Act
        await new ExtractPhase(blobs, extractor).ExtractAsync(Run, inventory, CancellationToken.None);

        // Assert
        extractor.KebabCaseByPath.Should().BeEquivalentTo(new Dictionary<string, bool> { [Books] = true, [Orders] = true });
    }

    [Fact]
    public async Task Extract_StartupFileWithKebabCaseConvention_DoesNotApplyToOtherProjects()
    {
        // Arrange
        var blobs = new FakeBlobReader(new Dictionary<string, string> { ["sha-books"] = "b", ["sha-orders"] = "o", ["sha-program"] = "KEBAB" });
        var extractor = Extractor();
        var inventory = Inventory(startup: [new RepoFile("src/Web/Program.cs", "sha-program", 10)]);

        // Act
        await new ExtractPhase(blobs, extractor).ExtractAsync(Run, inventory, CancellationToken.None);

        // Assert
        extractor.KebabCaseByPath.Should().BeEquivalentTo(new Dictionary<string, bool> { [Books] = false, [Orders] = false });
    }
}
