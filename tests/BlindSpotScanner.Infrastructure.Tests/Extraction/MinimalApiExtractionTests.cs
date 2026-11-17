using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Extraction;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Extraction;

public class MinimalApiExtractionTests
{
    private const string Path = "src/Api/Endpoints/HealthEndpoints.cs";

    private static ExtractionResult Extract(string source) => new AspNetCoreEndpointExtractor().Extract(Path, source);

    private static IEnumerable<string> Routes(ExtractionResult result) =>
        result.Controllers.SelectMany(c => c.Endpoints).Select(e => $"{e.Method} {e.Route}");

    [Fact]
    public void Extract_MapVerbs_YieldEndpoints()
    {
        // Arrange
        const string source = """
            public static class HealthEndpoints
            {
                public static void Map(WebApplication app)
                {
                    app.MapGet("/health", () => Results.Ok());
                    app.MapPost("api/ping", () => Results.Ok());
                }
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        result.Controllers.Should().BeEquivalentTo(new[]
        {
            new ExtractedController("HealthEndpoints", Path, null,
            [
                new ExtractedEndpoint(EndpointMethod.Get, "health"),
                new ExtractedEndpoint(EndpointMethod.Post, "api/ping"),
            ]),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Extract_MapGroupVariable_PrefixesRoutes()
    {
        // Arrange
        const string source = """
            var orders = app.MapGroup("api/orders").WithTags("Orders");
            orders.MapPost("/{id}/cancel", (int id) => Results.Ok());
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Post api/orders/{id}/cancel");
    }

    [Fact]
    public void Extract_NestedMapGroup_JoinsPrefixes()
    {
        // Arrange
        const string source = """
            app.MapGroup("api").MapGroup("v1").MapGet("books", () => Results.Ok());
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Get api/v1/books");
    }

    [Fact]
    public void Extract_InterpolatedMinimalRoute_IsWarned()
    {
        // Arrange
        const string source = """
            const string Version = "v1";

            app.MapGet($"api/{Version}/x", () => Results.Ok());
            """;

        // Act
        var result = Extract(source);

        // Assert
        result.Warnings.Should().Equal("src/Api/Endpoints/HealthEndpoints.cs: route of MapGet call at line 3 is not a string literal; endpoint not extracted");
    }

    [Fact]
    public void Extract_ControllersAndMinimalApisInOneFile_BothExtracted()
    {
        // Arrange
        const string source = """
            app.MapGet("/health", () => Results.Ok());

            [Route("api/books")]
            public class BooksController : ControllerBase
            {
                [HttpGet] public IActionResult List() => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        result.Controllers.Select(c => c.Controller).Should().Equal("BooksController", "HealthEndpoints");
    }
}
