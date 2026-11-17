using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class ClassifyPhaseTests
{
    private static readonly RunContext Run = new("example-org", "bookstore-api", "main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "https://github.com/example-org/bookstore-api");

    private static readonly RepoProfile Profile = new()
    {
        RepositoryId = Guid.NewGuid(),
        Version = 1,
        Markdown = "# Profile",
        IntegrationMappedTo = IntegrationMappedTo.RealSeam,
        ControllerGlobs = ["**/Controllers/*Controller.cs"],
        TestGlobs = ["tests/**/*.cs"],
        IntegrationGlobs = ["tests/*.IntegrationTests/**"],
        IntegrationAttributes = ["[Trait(\"Category\", \"Integration\")]", "Collection"],
        IntegrationBaseTypes = ["WebApplicationFactory"],
        NotIntegrationGlobs = ["**/*Fixture.cs"],
    };

    private static readonly string[] OneTest = ["BooksApiTests.A"];

    /// <summary>Classifies one file whose syntax reader returns the given facts.</summary>
    private static async Task<ClassifiedTestFile> ClassifyOne(string path, string[]? attributes = null, string[]? baseTypes = null, string[]? tests = null)
    {
        var facts = new TestFileFacts(attributes ?? [], baseTypes ?? [], tests ?? OneTest);
        var phase = new ClassifyPhase(
            new FakeBlobReader(new Dictionary<string, string> { ["sha"] = "source" }),
            new FakeTestSyntaxReader(new Dictionary<string, TestFileFacts> { ["source"] = facts }));
        var inventory = new RepositoryInventory([], [new RepoFile(path, "sha", 10)], []);
        var classification = await phase.ClassifyAsync(Run, inventory, Profile, CancellationToken.None);
        return classification.Files.Single();
    }

    [Fact]
    public async Task Classify_NotIntegrationGlob_IsOtherEvenInIntegrationFolder()
    {
        // Act
        var file = await ClassifyOne("tests/Api.IntegrationTests/DbFixture.cs");

        // Assert
        new { file.Kind, file.Reason }.Should().BeEquivalentTo(new { Kind = TestFileKind.Other, Reason = "matches not-integration glob '**/*Fixture.cs'" });
    }

    [Fact]
    public async Task Classify_NoTestMethods_IsOther()
    {
        // Act
        var file = await ClassifyOne("tests/Api.IntegrationTests/ApiFactory.cs", baseTypes: ["WebApplicationFactory<Program>"], tests: []);

        // Assert
        new { file.Kind, file.Reason }.Should().BeEquivalentTo(new { Kind = TestFileKind.Other, Reason = "no test methods" });
    }

    [Fact]
    public async Task Classify_IntegrationFolderGlob_IsIntegration()
    {
        // Act
        var file = await ClassifyOne("tests/Api.IntegrationTests/BooksApiTests.cs");

        // Assert
        new { file.Kind, file.Reason }.Should().BeEquivalentTo(new { Kind = TestFileKind.Integration, Reason = "matches integration glob 'tests/*.IntegrationTests/**'" });
    }

    [Fact]
    public async Task Classify_IntegrationAttribute_IsIntegration()
    {
        // Act
        var file = await ClassifyOne("tests/Api.UnitTests/StockTests.cs", attributes: ["Fact", "Trait(\"Category\",\"Integration\")"]);

        // Assert
        new { file.Kind, file.Reason }.Should().BeEquivalentTo(new { Kind = TestFileKind.Integration, Reason = "has attribute [Trait(\"Category\",\"Integration\")]" });
    }

    [Fact]
    public async Task Classify_AttributeNameOnlyEntry_MatchesAnyArguments()
    {
        // Act
        var file = await ClassifyOne("tests/Api.UnitTests/StockTests.cs", attributes: ["Collection(\"Database\")", "Fact"]);

        // Assert
        new { file.Kind, file.Reason }.Should().BeEquivalentTo(new { Kind = TestFileKind.Integration, Reason = "has attribute [Collection]" });
    }

    [Fact]
    public async Task Classify_BaseTypeWithoutGenerics_MatchesGenericBase()
    {
        // Act
        var file = await ClassifyOne("tests/Api.UnitTests/StockTests.cs", baseTypes: ["WebApplicationFactory<Program>"]);

        // Assert
        new { file.Kind, file.Reason }.Should().BeEquivalentTo(new { Kind = TestFileKind.Integration, Reason = "derives from WebApplicationFactory" });
    }

    [Fact]
    public async Task Classify_NoRuleMatched_IsAmbiguous()
    {
        // Act
        var file = await ClassifyOne("tests/Api.UnitTests/PriceTests.cs", attributes: ["Fact"]);

        // Assert
        new { file.Kind, file.Reason }.Should().BeEquivalentTo(new { Kind = TestFileKind.Ambiguous, Reason = "no profile rule matched" });
    }

    [Fact]
    public async Task Classify_IntegrationTests_CarryTestMethods()
    {
        // Act
        var file = await ClassifyOne("tests/Api.IntegrationTests/BooksApiTests.cs");

        // Assert
        file.TestMethods.Should().Equal("BooksApiTests.A");
    }
}
