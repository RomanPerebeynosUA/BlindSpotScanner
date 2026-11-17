using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlindSpotScanner.Infrastructure.Tests;

public class DependencyInjectionTests
{
    private static ServiceProvider Provider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddBlindSpotScannerCore()
            .BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void AddCore_ResolvesOrchestratorAndQueue()
    {
        // Arrange (resolving never opens a connection)
        using var provider = Provider(new() { ["ConnectionStrings:Postgres"] = "Host=localhost;Database=x;Username=u;Password=p" });
        using var scope = provider.CreateScope();

        // Act
        var resolved = new
        {
            Orchestrator = scope.ServiceProvider.GetService<ScanOrchestrator>() is not null,
            Queue = scope.ServiceProvider.GetService<IScanQueue>() is not null,
            Events = scope.ServiceProvider.GetService<IScanEvents>() is not null,
            Requests = scope.ServiceProvider.GetService<ScanRequests>() is not null,
        };

        // Assert
        resolved.Should().BeEquivalentTo(new { Orchestrator = true, Queue = true, Events = true, Requests = true });
    }

    [Fact]
    public void AddCore_JiraClient_TimesOutAfterTenSeconds()
    {
        // Arrange (a hung Jira must not hold a report page for HttpClient's default 100 s)
        using var provider = Provider([]);

        // Act
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IJiraClient));

        // Assert
        http.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void AddCore_MissingConnectionString_ThrowsOnFirstUse()
    {
        // Arrange
        using var provider = Provider([]);
        using var scope = provider.CreateScope();

        // Act
        var act = () => scope.ServiceProvider.GetRequiredService<BlindSpotScannerDbContext>().Model;

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("ConnectionStrings:Postgres is not configured.");
    }
}
