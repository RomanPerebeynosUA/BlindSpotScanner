using BlindSpotScanner.Application.Scanning;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlindSpotScanner.Infrastructure.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddCore_ResolvesOrchestratorAndQueue()
    {
        // Arrange (resolving never opens a connection)
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = "Host=localhost;Database=x;Username=u;Password=p" })
            .Build();
        using var provider = new ServiceCollection().AddLogging().AddBlindSpotScannerCore(configuration).BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        // Act
        var resolved = new
        {
            Orchestrator = scope.ServiceProvider.GetService<ScanOrchestrator>() is not null,
            Queue = scope.ServiceProvider.GetService<IScanQueue>() is not null,
            Events = scope.ServiceProvider.GetService<IScanEvents>() is not null,
        };

        // Assert
        resolved.Should().BeEquivalentTo(new { Orchestrator = true, Queue = true, Events = true });
    }

    [Fact]
    public void AddCore_MissingConnectionString_Throws()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => new ServiceCollection().AddBlindSpotScannerCore(configuration);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("ConnectionStrings:Postgres is not configured.");
    }
}
