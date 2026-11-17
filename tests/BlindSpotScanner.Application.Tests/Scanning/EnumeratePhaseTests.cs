using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class EnumeratePhaseTests
{
    private const string Sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    private static readonly RunContext Run = new(FakeGitHubClient.Owner, FakeGitHubClient.Name, "main", Sha, "https://github.com/example-org/bookstore-api");

    private static readonly RepoProfile Profile = new()
    {
        RepositoryId = Guid.NewGuid(),
        Version = 1,
        Markdown = "# Profile",
        IntegrationMappedTo = IntegrationMappedTo.RealSeam,
        ControllerGlobs = ["**/Controllers/*Controller.cs"],
        TestGlobs = ["tests/**/*.cs"],
    };

    private static FakeGitHubClient Repo(params string[] files) =>
        new("main", new Dictionary<string, string> { ["main"] = Sha }, files);

    private static readonly string[] Bookstore =
    [
        "README.md",
        "src/Api/Controllers/BooksController.cs",
        "src/Api/Controllers/OrdersController.cs",
        "src/Api/Program.cs",
        "tests/Api.IntegrationTests/BooksApiTests.cs",
        "tests/Api.UnitTests/PriceTests.cs",
    ];

    [Fact]
    public async Task Enumerate_ProfileGlobs_SplitControllersAndTests()
    {
        // Arrange
        var phase = new EnumeratePhase(Repo(Bookstore));

        // Act
        var inventory = await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert
        new { Controllers = inventory.ControllerFiles.Select(f => f.Path), Tests = inventory.TestFiles.Select(f => f.Path) }
            .Should().BeEquivalentTo(new
            {
                Controllers = new[] { "src/Api/Controllers/BooksController.cs", "src/Api/Controllers/OrdersController.cs" },
                Tests = new[] { "tests/Api.IntegrationTests/BooksApiTests.cs", "tests/Api.UnitTests/PriceTests.cs" },
            }, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Enumerate_ControllerUnderTestFolder_IsNotDenominator()
    {
        // Arrange
        var phase = new EnumeratePhase(Repo([.. Bookstore, "tests/Api.IntegrationTests/Controllers/FakeGatewayController.cs"]));

        // Act
        var inventory = await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert
        new { inventory.ControllerFileCount, inventory.TestFileCount }.Should().BeEquivalentTo(new { ControllerFileCount = 2, TestFileCount = 3 });
    }

    [Fact]
    public async Task Enumerate_BinObjAndGenerated_AreExcluded()
    {
        // Arrange
        var phase = new EnumeratePhase(Repo(
        [
            .. Bookstore,
            "src/Api/obj/Debug/Controllers/TempController.cs",
            "src/Api/bin/Controllers/OldController.cs",
            "src/Api/Generated/Controllers/ClientController.cs",
            "tests/Api.UnitTests/bin/Debug/Copied.cs",
            "tests/Api.UnitTests/Form.Designer.cs",
            "tests/node_modules/x/index.cs",
        ]));

        // Act
        var inventory = await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert
        new { inventory.ControllerFileCount, inventory.TestFileCount }.Should().BeEquivalentTo(new { ControllerFileCount = 2, TestFileCount = 2 });
    }

    [Fact]
    public async Task Enumerate_LowercaseGeneratedThirdPartyDistAndWwwrootLib_AreExcluded()
    {
        // Arrange
        var phase = new EnumeratePhase(Repo(
        [
            .. Bookstore,
            "src/Api/generated/Controllers/ClientController.cs",
            "src/third_party/Controllers/VendorController.cs",
            "src/Web/dist/Controllers/BundleController.cs",
            "src/Web/wwwroot/lib/Controllers/JsController.cs",
        ]));

        // Act
        var inventory = await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert
        inventory.ControllerFileCount.Should().Be(2);
    }

    [Fact]
    public async Task Enumerate_Symlink_IsNotCounted()
    {
        // Arrange
        var repo = Repo(Bookstore);
        repo.Symlinks.Add("src/Api/Controllers/AliasController.cs");
        var phase = new EnumeratePhase(repo);

        // Act
        var inventory = await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert
        inventory.ControllerFileCount.Should().Be(2);
    }

    [Fact]
    public async Task Enumerate_TruncatedRecursiveTree_WalksSubtrees()
    {
        // Arrange
        var repo = Repo(Bookstore);
        repo.TruncatedRecursive.Add(string.Empty);
        var phase = new EnumeratePhase(repo);

        // Act
        var inventory = await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert
        new { inventory.ControllerFileCount, inventory.TestFileCount, inventory.UnverifiedReasons }
            .Should().BeEquivalentTo(new { ControllerFileCount = 2, TestFileCount = 2, UnverifiedReasons = Array.Empty<string>() });
    }

    [Fact]
    public async Task Enumerate_TruncatedRoot_ReadsEachTopLevelSubtreeRecursively()
    {
        // Arrange
        var repo = Repo(Bookstore);
        repo.TruncatedRecursive.Add(string.Empty);
        var phase = new EnumeratePhase(repo);

        // Act
        await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert (root recursive + root flat + one recursive read each for src/ and tests/)
        repo.TreeRequests.Should().Be(4);
    }

    [Fact]
    public async Task Enumerate_Submodule_RecordsUnverifiedReason()
    {
        // Arrange
        var repo = Repo(Bookstore);
        repo.Submodules.Add("libs/payments");
        var phase = new EnumeratePhase(repo);

        // Act
        var inventory = await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert
        inventory.UnverifiedReasons.Should().Equal("submodule at 'libs/payments' was not enumerated");
    }

    [Fact]
    public async Task Enumerate_SubtreeStillTruncated_RecordsUnverifiedReason()
    {
        // Arrange (every read on the path down to tests/Api.UnitTests is truncated)
        var repo = Repo(Bookstore);
        repo.TruncatedRecursive.UnionWith([string.Empty, "tests", "tests/Api.UnitTests"]);
        repo.TruncatedFlat.Add("tests/Api.UnitTests");
        var phase = new EnumeratePhase(repo);

        // Act
        var inventory = await phase.EnumerateAsync(Run, Profile, CancellationToken.None);

        // Assert
        inventory.UnverifiedReasons.Should().Equal("tree truncated at 'tests/Api.UnitTests'; files below it were not enumerated");
    }
}
