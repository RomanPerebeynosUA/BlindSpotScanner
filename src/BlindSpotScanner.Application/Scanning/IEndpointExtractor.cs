using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>P3's language strategy (doc 03): reads one production source file for its endpoints or its seams.</summary>
public interface IEndpointExtractor
{
    /// <summary>Whether a startup file (<c>Program.cs</c>, <c>Startup.cs</c>) makes route tokens kebab-case (D35).</summary>
    bool UsesKebabCaseRouteTokens(string path, string source);

    /// <param name="kebabCaseRouteTokens">Write [controller], [action] and [area] values in kebab-case, as a slugify token transformer does.</param>
    ExtractionResult Extract(string path, string source, bool kebabCaseRouteTokens);

    /// <summary>The seams a startup file registers, distinct by kind and name, in ordinal name order (D34).</summary>
    IReadOnlyList<ProductionSeam> DetectSeams(string path, string source);
}

/// <param name="Warnings">Endpoints that could not be read; each becomes an unverified reason (principle 1).</param>
public sealed record ExtractionResult(IReadOnlyList<ExtractedController> Controllers, IReadOnlyList<string> Warnings);

/// <param name="RoutePrefix">The controller's route template, or null when it has none (and for minimal APIs).</param>
public sealed record ExtractedController(string Controller, string SourcePath, string? RoutePrefix, IReadOnlyList<ExtractedEndpoint> Endpoints);

/// <param name="Route">The full route template without a leading slash, e.g. <c>api/books/{id}</c>.</param>
public sealed record ExtractedEndpoint(EndpointMethod Method, string Route);
