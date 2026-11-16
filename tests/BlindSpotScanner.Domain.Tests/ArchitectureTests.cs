using System.Reflection;
using FluentAssertions;

namespace BlindSpotScanner.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void DomainAssembly_References_NoOtherBlindSpotScannerAssembly()
    {
        // Arrange
        var domain = Assembly.Load("BlindSpotScanner.Domain");

        // Act
        var projectReferences = domain.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name!.StartsWith("BlindSpotScanner.", StringComparison.Ordinal));

        // Assert
        projectReferences.Should().BeEmpty();
    }
}
