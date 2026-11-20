using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Extraction;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Extraction;

public class MinimalApiExtractionTests
{
    private const string Path = "src/Api/Endpoints/HealthEndpoints.cs";

    private static ExtractionResult Extract(string source) => new AspNetCoreEndpointExtractor().Extract(Path, source, kebabCaseRouteTokens: false);

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
            var app = WebApplication.CreateBuilder(args).Build();
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
            var app = WebApplication.Create();
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
            var app = WebApplication.Create();
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
            var app = WebApplication.Create();
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

    [Fact]
    public void Extract_ReusedGroupVariableName_UsesNearestDeclaration()
    {
        // Arrange
        const string source = """
            public static class Endpoints
            {
                public static void MapBooks(this WebApplication app)
                {
                    var group = app.MapGroup("/books");
                    group.MapGet("/", () => Results.Ok());
                }

                public static void MapOrders(this WebApplication app)
                {
                    var group = app.MapGroup("/orders");
                    group.MapGet("/{id}", (int id) => Results.Ok());
                }
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Get books", "Get orders/{id}");
    }

    [Fact]
    public void Extract_GroupBuilderParameter_IsWarned()
    {
        // Arrange (the group prefix is set by the caller, which this file does not show)
        const string source = """
            public static class BookEndpoints
            {
                public static void MapBookEndpoints(this RouteGroupBuilder group) =>
                    group.MapGet("/{id}", (int id) => Results.Ok());
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        result.Warnings.Should().Equal("src/Api/Endpoints/HealthEndpoints.cs: group prefix of MapGet call at line 4 cannot be resolved; endpoint not extracted");
    }

    [Fact]
    public void Extract_FieldReceiver_IsWarned()
    {
        // Arrange
        const string source = """
            public class BookModule
            {
                private RouteGroupBuilder _group = null!;

                public void Map() => _group.MapGet("/{id}", (int id) => Results.Ok());
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        result.Warnings.Should().Equal("src/Api/Endpoints/HealthEndpoints.cs: group prefix of MapGet call at line 5 cannot be resolved; endpoint not extracted");
    }

    [Fact]
    public void Extract_MapMethods_IsWarned()
    {
        // Arrange
        const string source = """
            public static class Legacy
            {
                public static void Map(WebApplication app) =>
                    app.MapMethods("/x", new[] { "GET" }, () => Results.Ok());
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        result.Warnings.Should().Equal("src/Api/Endpoints/HealthEndpoints.cs: MapMethods call at line 4 is not supported; endpoint not extracted");
    }

    [Fact]
    public void Extract_GroupAssignedAfterDeclaration_UsesAssignment()
    {
        // Arrange
        const string source = """
            public static class Endpoints
            {
                public static void Map(WebApplication app)
                {
                    RouteGroupBuilder group;
                    group = app.MapGroup("/books");
                    group.MapGet("/{id}", (int id) => Results.Ok());
                }
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Get books/{id}");
    }
}
