using BlindSpotScanner.Application.Llm;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Llm;

public class LlmContentPolicyTests
{
    [Theory]
    [InlineData(".env")]
    [InlineData("src/.env.local")]
    [InlineData("certs/api.pem")]
    [InlineData("x.pfx")]
    [InlineData("src/appsettings.Development.json")]
    [InlineData("config/secrets.json")]
    public void IsDenied_SensitivePath_IsTrue(string path)
    {
        // Act
        var denied = LlmContentPolicy.IsDenied(path);

        // Assert
        denied.Should().BeTrue();
    }

    [Theory]
    [InlineData("src/appsettings.json")]
    [InlineData("tests/BooksApiTests.cs")]
    public void IsDenied_OrdinaryPath_IsFalse(string path)
    {
        // Act
        var denied = LlmContentPolicy.IsDenied(path);

        // Assert
        denied.Should().BeFalse();
    }
}
