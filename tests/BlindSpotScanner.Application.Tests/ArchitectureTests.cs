using System.Reflection;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests;

public class ArchitectureTests
{
    [Fact]
    public void ApplicationAssembly_References_NoInfrastructureOrHost()
    {
        // Arrange
        var application = Assembly.Load("BlindSpotScanner.Application");

        // Act
        var projectReferences = application.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name!.StartsWith("BlindSpotScanner.", StringComparison.Ordinal));

        // Assert
        projectReferences.Should().NotContain(new[]
        {
            "BlindSpotScanner.Infrastructure",
            "BlindSpotScanner.Api",
            "BlindSpotScanner.Worker",
        });
    }
}
