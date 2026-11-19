using BlindSpotScanner.Infrastructure.Extraction;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Extraction;

public class CSharpTestSyntaxReaderTests
{
    [Fact]
    public void Read_ClassAndMethodAttributes_AreNormalized()
    {
        // Arrange
        const string source = """
            [Trait( "Category" , "Integration" )]
            public class BooksApiTests
            {
                [Fact] public void A() { }
            }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.Attributes.Should().Equal("Fact", "Trait(\"Category\",\"Integration\")");
    }

    [Fact]
    public void Read_AttributeSuffixAndNamespace_AreDropped()
    {
        // Arrange
        const string source = """
            [Xunit.TraitAttribute("Category", "Integration")]
            public class BooksApiTests { }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.Attributes.Should().Equal("Trait(\"Category\",\"Integration\")");
    }

    [Fact]
    public void Read_BaseTypes_IncludeGenericArguments()
    {
        // Arrange
        const string source = """
            public class BooksApiTests : IClassFixture<WebApplicationFactory< Program >> { }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.BaseTypes.Should().Equal("IClassFixture<WebApplicationFactory<Program>>");
    }

    [Fact]
    public void Read_TestMethods_NamedByClass()
    {
        // Arrange
        const string source = """
            public class BooksApiTests
            {
                [Fact] public void A() { }
                [Theory, InlineData(1)] public void B(int x) { }
                private void C() { }
            }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.TestMethods.Should().Equal("BooksApiTests.A", "BooksApiTests.B");
    }


    [Fact]
    public void Read_Classes_ListsTopLevelAndNested()
    {
        // Arrange (eShopOnWeb's WebTestFixture.cs declares the fixture class TestApplication)
        const string source = """
            public class TestApplication : WebApplicationFactory<Program>
            {
                private sealed class Handler { }
            }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.Classes.Should().Equal("TestApplication", "TestApplication.Handler");
    }

    [Fact]
    public void Read_CommentedOutAttribute_IsIgnored()
    {
        // Arrange
        const string source = """
            // [Trait("Category","Integration")]
            public class PriceTests
            {
                [Fact] public void A() { }
            }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.Attributes.Should().Equal("Fact");
    }

    [Fact]
    public void Read_CustomTestAttributes_AreTestMethods()
    {
        // Arrange (derived xUnit attributes and NUnit sources still mark tests)
        const string source = """
            public class StockTests
            {
                [SkippableFact] public void A() { }
                [IntegrationTheory] public void B() { }
                [TestCaseSource(nameof(Cases))] public void C(int x) { }
                [Helper] public void D() { }
            }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.TestMethods.Should().Equal("StockTests.A", "StockTests.B", "StockTests.C");
    }

    [Fact]
    public void Read_NestedTestClass_NamedWithOuterClass()
    {
        // Arrange
        const string source = """
            public class BooksApiTests
            {
                public class WhenEmpty
                {
                    [Fact] public void Lists_nothing() { }
                }
            }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.TestMethods.Should().Equal("BooksApiTests.WhenEmpty.Lists_nothing");
    }

    [Fact]
    public void Read_Methods_IncludeNonTestMethods()
    {
        // Arrange
        const string source = """
            public class ApiFactory : WebApplicationFactory<Program>
            {
                public Task InitializeAsync() => Task.CompletedTask;
                [Fact] public void Smoke() { }
            }
            """;

        // Act
        var facts = new CSharpTestSyntaxReader().Read(source);

        // Assert
        facts.Methods.Should().Equal("ApiFactory.InitializeAsync", "ApiFactory.Smoke");
    }
}
