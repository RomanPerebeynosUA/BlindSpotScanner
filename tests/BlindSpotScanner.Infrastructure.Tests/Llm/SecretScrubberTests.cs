using BlindSpotScanner.Infrastructure.Llm;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Llm;

/// <summary>Fake secrets are assembled from parts so the source never holds a token-shaped literal.</summary>
public class SecretScrubberTests
{
    private const string Redacted = "«redacted»";

    public static TheoryData<string> Secrets => new()
    {
        "AKIA" + "ABCDEFGHIJKLMNOP",
        "ghp_" + new string('a', 36),
        "github_pat_" + new string('b', 30),
        "eyJ" + "hbGciOiJIUzI1NiJ9" + ".eyJ" + "zdWIiOiIxIn0" + ".c2lnbmF0dXJl",
        "-----BEGIN RSA " + "PRIVATE KEY-----\nMIIEow\nIBAAK\n-----END RSA " + "PRIVATE KEY-----",
    };

    [Theory]
    [MemberData(nameof(Secrets))]
    public void Scrub_KnownSecret_IsRedacted(string secret)
    {
        // Act
        var scrubbed = SecretScrubber.Scrub($"var key = \"{secret}\";");

        // Assert
        scrubbed.Should().Be($"var key = \"{Redacted}\";");
    }

    [Theory]
    [InlineData("Host=db;Password=s3cret;Port=5432", "Host=db;Password=«redacted»;Port=5432")]
    [InlineData("Server=x;pwd=hunter2\"", "Server=x;pwd=«redacted»\"")]
    [InlineData("PASSWORD=abc", "PASSWORD=«redacted»")]
    public void Scrub_ConnectionString_KeepsKeyRedactsValue(string input, string expected)
    {
        // Act
        var scrubbed = SecretScrubber.Scrub(input);

        // Assert
        scrubbed.Should().Be(expected);
    }

    [Fact]
    public void Scrub_OrdinaryCode_IsUnchanged()
    {
        // Arrange
        const string code = "public async Task Create_persists_book() { var password = form.Password; Assert.Equal(201, (int)response.StatusCode); }";

        // Act
        var scrubbed = SecretScrubber.Scrub(code);

        // Assert
        scrubbed.Should().Be(code);
    }
}
