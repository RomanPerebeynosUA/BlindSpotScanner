using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Extraction;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Extraction;

public class AspNetCoreControllerExtractionTests
{
    private const string Path = "src/Api/Controllers/X.cs";

    private static ExtractionResult Extract(string source) => new AspNetCoreEndpointExtractor().Extract(Path, source);

    private static IEnumerable<string> Routes(ExtractionResult result) =>
        result.Controllers.SelectMany(c => c.Endpoints).Select(e => $"{e.Method} {e.Route}");

    [Fact]
    public void Extract_RoutePrefixAndVerbs_YieldsEndpoints()
    {
        // Arrange
        const string source = """
            [ApiController]
            [Route("api/books")]
            public class BooksController : ControllerBase
            {
                [HttpGet] public IActionResult List() => Ok();
                [HttpGet("{id}")] public IActionResult Get(int id) => Ok();
                [HttpPost] public IActionResult Create(Book book) => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        result.Controllers.Should().BeEquivalentTo(new[]
        {
            new ExtractedController("BooksController", Path, "api/books",
            [
                new ExtractedEndpoint(EndpointMethod.Get, "api/books"),
                new ExtractedEndpoint(EndpointMethod.Get, "api/books/{id}"),
                new ExtractedEndpoint(EndpointMethod.Post, "api/books"),
            ]),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Extract_ControllerToken_UsesClassNameWithoutSuffix()
    {
        // Arrange
        const string source = """
            [Route("api/[controller]")]
            public class OrdersController : ControllerBase
            {
                [HttpPost("{id}/cancel")] public IActionResult Cancel(int id) => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Post api/Orders/{id}/cancel");
    }

    [Fact]
    public void Extract_AbsoluteTemplate_ReplacesPrefix()
    {
        // Arrange
        const string source = """
            [Route("api/x")]
            public class StatusController : ControllerBase
            {
                [HttpGet("/health")] public IActionResult Health() => Ok();
                [HttpGet("~/ping")] public IActionResult Ping() => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Get health", "Get ping");
    }

    [Fact]
    public void Extract_MethodRouteAttribute_IsTemplate()
    {
        // Arrange
        const string source = """
            [Route("api/books")]
            public class BooksController : ControllerBase
            {
                [HttpGet]
                [Route("latest")]
                public IActionResult Latest() => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Get api/books/latest");
    }

    [Fact]
    public void Extract_TwoVerbAttributes_YieldTwoEndpoints()
    {
        // Arrange
        const string source = """
            [Route("api/books")]
            public class BooksController : ControllerBase
            {
                [HttpPut("{id}")]
                [HttpPatch("{id}")]
                public IActionResult Update(int id) => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Put api/books/{id}", "Patch api/books/{id}");
    }

    [Fact]
    public void Extract_CommentedOutAttribute_IsIgnored()
    {
        // Arrange
        const string source = """
            [Route("api/books")]
            public class BooksController : ControllerBase
            {
                [HttpGet] public IActionResult List() => Ok();

                // [HttpDelete("{id}")]
                public IActionResult Delete(int id) => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Get api/books");
    }

    [Fact]
    public void Extract_NonActionAndPrivate_AreSkipped()
    {
        // Arrange
        const string source = """
            [Route("api/books")]
            public class BooksController : ControllerBase
            {
                [HttpGet] public IActionResult List() => Ok();
                [NonAction][HttpGet("a")] public IActionResult A() => Ok();
                [HttpGet("b")] private IActionResult B() => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Get api/books");
    }

    [Fact]
    public void Extract_ConstantRoute_IsWarnedNotDropped()
    {
        // Arrange
        const string source = """
            [Route("api/books")]
            public class BooksController : ControllerBase
            {
                [HttpGet] public IActionResult List() => Ok();
                [HttpGet(Routes.Latest)] public IActionResult Latest() => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        result.Warnings.Should().Equal("src/Api/Controllers/X.cs: route of BooksController.Latest is not a string literal; endpoint not extracted");
    }

    [Fact]
    public void Extract_AbstractBaseController_YieldsNothing()
    {
        // Arrange
        const string source = """
            [ApiController]
            public abstract class ApiControllerBase : ControllerBase
            {
                [HttpGet("version")] public IActionResult Version() => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        new { result.Controllers, result.Warnings }.Should().BeEquivalentTo(new
        {
            Controllers = Array.Empty<ExtractedController>(),
            Warnings = Array.Empty<string>(),
        });
    }

    [Fact]
    public void Extract_ControllerWithoutRoutedActions_IsWarned()
    {
        // Arrange
        const string source = """
            public class LegacyController : Controller
            {
                public IActionResult Index() => View();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        new { result.Controllers, result.Warnings }.Should().BeEquivalentTo(new
        {
            Controllers = Array.Empty<ExtractedController>(),
            Warnings = new[] { "src/Api/Controllers/X.cs: LegacyController has no attribute-routed actions" },
        });
    }

    [Fact]
    public void Extract_QualifiedAttributeNames_AreRecognized()
    {
        // Arrange
        const string source = """
            [Microsoft.AspNetCore.Mvc.RouteAttribute("api/books")]
            public class BooksController : ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGetAttribute("x")] public IActionResult X() => Ok();
            }
            """;

        // Act
        var result = Extract(source);

        // Assert
        Routes(result).Should().Equal("Get api/books/x");
    }
}
