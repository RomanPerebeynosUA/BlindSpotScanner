using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Infrastructure.Remediation;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Remediation;

public class FixGuardTests
{
    private const string Folder = "tests/Bookstore.IntegrationTests";
    private const string ValidTest = "namespace Bookstore.IntegrationTests;\n\npublic class CancelTests\n{\n    [Fact]\n    public void Cancels() { }\n}\n";

    private static readonly HashSet<string> Existing =
    [
        "src/Bookstore.Api/Controllers/OrdersController.cs",
        "tests/Bookstore.IntegrationTests/OrdersApiTests.cs",
        "tests/Bookstore.IntegrationTests/Bookstore.IntegrationTests.csproj",
    ];

    private static IReadOnlyList<string> Check(params FixFile[] files) => new FixGuard().Check(files, Folder, Existing);

    private static FixFile New(string path, string content = ValidTest) => new(path, FixAction.Create, content);

    [Fact]
    public void Check_ProductionCodeEdit_Rejected()
    {
        // Act
        var errors = Check(new FixFile("src/Bookstore.Api/Controllers/OrdersController.cs", FixAction.Modify, ValidTest));

        // Assert
        errors.Should().Equal("src/Bookstore.Api/Controllers/OrdersController.cs: outside the test project tests/Bookstore.IntegrationTests");
    }

    [Fact]
    public void Check_ValidNewTest_Passes()
    {
        // Act
        var errors = Check(New("tests/Bookstore.IntegrationTests/CancelOrderTests.cs"));

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_DotDotEscape_Rejected()
    {
        // Act
        var errors = Check(New("tests/Bookstore.IntegrationTests/../../src/Bookstore.Api/X.cs"));

        // Assert
        errors.Should().Equal("tests/Bookstore.IntegrationTests/../../src/Bookstore.Api/X.cs: path must be relative and normalised");
    }

    [Fact]
    public void Check_Backslashes_Rejected()
    {
        // Act
        var errors = Check(New(@"tests\Bookstore.IntegrationTests\X.cs"));

        // Assert
        errors.Should().Equal(@"tests\Bookstore.IntegrationTests\X.cs: path must be relative and normalised");
    }

    [Fact]
    public void Check_ProjectFileEdit_Rejected()
    {
        // Act
        var errors = Check(new FixFile("tests/Bookstore.IntegrationTests/Bookstore.IntegrationTests.csproj", FixAction.Modify, "<Project />"));

        // Assert
        errors.Should().Equal("tests/Bookstore.IntegrationTests/Bookstore.IntegrationTests.csproj: only C# files can be changed");
    }

    [Fact]
    public void Check_CreateExisting_Rejected()
    {
        // Act
        var errors = Check(New("tests/Bookstore.IntegrationTests/OrdersApiTests.cs"));

        // Assert
        errors.Should().Equal("tests/Bookstore.IntegrationTests/OrdersApiTests.cs: already exists; use modify");
    }

    [Fact]
    public void Check_ModifyMissing_Rejected()
    {
        // Act
        var errors = Check(new FixFile("tests/Bookstore.IntegrationTests/Missing.cs", FixAction.Modify, ValidTest));

        // Assert
        errors.Should().Equal("tests/Bookstore.IntegrationTests/Missing.cs: doesn't exist at the scanned commit; use create");
    }

    [Fact]
    public void Check_TooLarge_Rejected()
    {
        // Act
        var errors = Check(New("tests/Bookstore.IntegrationTests/Big.cs", ValidTest + "//" + new string('x', 100_000)));

        // Assert
        errors.Should().Equal("tests/Bookstore.IntegrationTests/Big.cs: larger than 100000 characters");
    }

    [Fact]
    public void Check_Secret_Rejected()
    {
        // Act
        var errors = Check(New("tests/Bookstore.IntegrationTests/Leak.cs", ValidTest + "// ghp_" + new string('a', 36) + "\n"));

        // Assert
        errors.Should().Equal("tests/Bookstore.IntegrationTests/Leak.cs: contains something that looks like a secret");
    }

    [Fact]
    public void Check_SyntaxError_Rejected()
    {
        // Act
        var errors = Check(New("tests/Bookstore.IntegrationTests/Broken.cs", "public class Broken {\n    void M( {\n}\n"));

        // Assert
        errors.Should().ContainSingle().Which.Should().StartWith("tests/Bookstore.IntegrationTests/Broken.cs: C# syntax error at line 2: ");
    }

    [Fact]
    public void Check_SixFiles_Rejected()
    {
        // Act
        var errors = Check([.. Enumerable.Range(1, 6).Select(i => New($"tests/Bookstore.IntegrationTests/T{i}.cs"))]);

        // Assert
        errors.Should().Equal("too many files (6; at most 5)");
    }
}
