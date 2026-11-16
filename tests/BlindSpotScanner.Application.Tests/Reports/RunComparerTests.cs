using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Reports;

public class RunComparerTests
{
    [Fact]
    public void Compare_PreviousAndCurrentExamples_ReproducesEmbeddedDelta()
    {
        // Arrange
        var previous = Fixtures.Previous;
        var current = Fixtures.Current;

        // Act
        var delta = RunComparer.Compare(previous, current);

        // Assert
        Fixtures.Canonical(delta).Should().Be(Fixtures.Canonical(current.Delta));
    }

    [Fact]
    public void Compare_RouteParameterRenamed_IsSameEndpoint()
    {
        // Arrange
        var current = Fixtures.Current.WithEndpoint("GET", "api/books/{id}", e => e with { Route = "api/books/{bookId}" });

        // Act
        var delta = RunComparer.Compare(Fixtures.Previous, current);

        // Assert
        new { delta.Endpoints.Added, delta.Endpoints.Removed }.Should().BeEquivalentTo(new { Added = Array.Empty<string>(), Removed = Array.Empty<string>() });
    }

    [Fact]
    public void Compare_RoutesDifferingOnlyByConstraint_AreDistinctEndpoints()
    {
        // Arrange
        var previous = Fixtures.Previous.WithExtraEndpoints(("api/items/{id:int}", CoverageStatus.None), ("api/items/{slug}", CoverageStatus.None));
        var current = Fixtures.Current.WithExtraEndpoints(("api/items/{id:int}", CoverageStatus.Covered), ("api/items/{slug}", CoverageStatus.None));

        // Act
        var delta = RunComparer.Compare(previous, current);

        // Assert
        delta.Endpoints.NewlyCovered.Should().Equal("POST api/orders", "GET api/items/{id:int}");
    }

    [Fact]
    public void Compare_SameRouteInTwoControllers_DoesNotThrow()
    {
        // Arrange
        var previous = Fixtures.Previous.WithExtraEndpoints(("api/books", CoverageStatus.Covered));
        var current = Fixtures.Current.WithExtraEndpoints(("api/books", CoverageStatus.Covered));

        // Act
        var act = () => RunComparer.Compare(previous, current);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Compare_CoveredBecomesUnverified_IsConfidenceChangedNotRegressed()
    {
        // Arrange
        var current = Fixtures.Current.WithEndpoint("GET", "api/books", e => e with { Status = CoverageStatus.Unverified });

        // Act
        var delta = RunComparer.Compare(Fixtures.Previous, current);

        // Assert
        new { delta.Endpoints.ConfidenceChanged, delta.Endpoints.Regressed }
            .Should().BeEquivalentTo(new { ConfidenceChanged = new[] { "GET api/books" }, Regressed = Array.Empty<string>() });
    }

    [Fact]
    public void Compare_CoveredBecomesNone_IsRegressed()
    {
        // Arrange
        var current = Fixtures.Current.WithEndpoint("GET", "api/books", e => e with { Status = CoverageStatus.None });

        // Act
        var delta = RunComparer.Compare(Fixtures.Previous, current);

        // Assert
        delta.Endpoints.Regressed.Should().Equal("GET api/books");
    }

    [Fact]
    public void Compare_RewordedRisk_IsDroppedAndNew()
    {
        // Arrange
        var original = Fixtures.Current;
        var risks = original.TopRisks.ToList();
        risks[1] = risks[1] with { Headline = "Some tests pass even when wrong data comes back" };
        var current = original with { TopRisks = risks };

        // Act
        var delta = RunComparer.Compare(Fixtures.Previous, current);

        // Assert
        new { delta.Risks.Dropped, delta.Risks.New }.Should().BeEquivalentTo(new
        {
            Dropped = new[] { "Nothing tests that orders are really placed and charged", "Some tests pass even when the wrong data comes back" },
            New = new[] { "Nothing tests a real charge with the payment provider", "Some tests pass even when wrong data comes back", "Order cancellation is not tested at all" },
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Compare_DifferentIntegrationMappedTo_IsNotComparable()
    {
        // Arrange
        var original = Fixtures.Current;
        var current = original with { Meta = original.Meta with { IntegrationMappedTo = IntegrationMappedTo.E2eMockedData } };

        // Act
        var delta = RunComparer.Compare(Fixtures.Previous, current);

        // Assert
        new { delta.Comparable, delta.Comparability }
            .Should().BeEquivalentTo(new { Comparable = false, Comparability = new[] { "integrationMappedTo changed" } });
    }

    [Fact]
    public void Compare_DenominatorMovedMoreThan20Percent_IsNotComparable()
    {
        // Arrange
        var original = Fixtures.Current;
        var current = original with { Inventory = original.Inventory with { EndpointsTotal = 7 } };

        // Act
        var delta = RunComparer.Compare(Fixtures.Previous, current);

        // Assert
        new { delta.Comparable, delta.Comparability }
            .Should().BeEquivalentTo(new { Comparable = false, Comparability = new[] { "endpoint denominator changed: 5 → 7" } });
    }

    [Fact]
    public void Compare_DenominatorMoved20PercentExactly_IsComparable()
    {
        // Arrange
        var original = Fixtures.Current;
        var current = original with { Inventory = original.Inventory with { EndpointsTotal = 6 } };

        // Act
        var delta = RunComparer.Compare(Fixtures.Previous, current);

        // Assert
        new { delta.Comparable, delta.Comparability }
            .Should().BeEquivalentTo(new { Comparable = true, Comparability = new[] { "endpoint denominator changed: 5 → 6" } });
    }
}
