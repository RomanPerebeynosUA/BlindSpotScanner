using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>The real OIDC + cookie sign-in (D26), against a fake provider: API calls get status codes, and sign-in uses the public URL.</summary>
public class OidcAuthApiTests(OidcApiFactory factory) : IClassFixture<OidcApiFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Me_NoSession_Returns401NotARedirect()
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await client.GetAsync("/api/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Hub_NoSession_Returns401NotARedirect()
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await client.PostAsync("/hubs/scans/negotiate?negotiateVersion=1", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ProviderWithEndSession_RedirectsToIt()
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await client.PostAsync("/api/auth/logout", null);

        // Assert
        new { response.StatusCode, ToProvider = response.Headers.Location?.AbsolutePath == "/logout" }.Should().Be(new { StatusCode = HttpStatusCode.Redirect, ToProvider = true });
    }

    [Fact]
    public async Task Login_RedirectsToProviderWithPublicCallbackUrl()
    {
        // Arrange (behind nginx or a TLS proxy the request host isn't the address users reach)
        using var client = Client();

        // Act
        using var response = await client.GetAsync("/api/auth/login?returnUrl=/repos");

        // Assert
        var location = response.Headers.Location!.ToString();
        new { response.StatusCode, Callback = location.Contains("redirect_uri=" + Uri.EscapeDataString(OidcApiFactory.PublicUrl + "/api/auth/callback"), StringComparison.Ordinal) }
            .Should().Be(new { StatusCode = HttpStatusCode.Redirect, Callback = true });
    }
}

public class OidcNoEndSessionAuthApiTests(OidcNoEndSessionApiFactory factory) : IClassFixture<OidcNoEndSessionApiFactory>
{
    [Fact]
    public async Task Logout_ProviderWithoutEndSession_SignsOutHereAndGoesHome()
    {
        // Arrange
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        using var response = await client.PostAsync("/api/auth/logout", null);

        // Assert
        new { response.StatusCode, Location = response.Headers.Location?.ToString() }.Should().Be(new { StatusCode = HttpStatusCode.Redirect, Location = (string?)"/" });
    }
}
