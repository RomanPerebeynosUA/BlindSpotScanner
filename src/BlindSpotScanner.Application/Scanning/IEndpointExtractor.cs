using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>P3's language strategy (doc 03): reads one production source file and returns its endpoints.</summary>
public interface IEndpointExtractor
{
    ExtractionResult Extract(string path, string source);
}

/// <param name="Warnings">Endpoints that could not be read; each becomes an unverified reason (principle 1).</param>
public sealed record ExtractionResult(IReadOnlyList<ExtractedController> Controllers, IReadOnlyList<string> Warnings);

/// <param name="RoutePrefix">The controller's route template, or null when it has none (and for minimal APIs).</param>
public sealed record ExtractedController(string Controller, string SourcePath, string? RoutePrefix, IReadOnlyList<ExtractedEndpoint> Endpoints);

/// <param name="Route">The full route template without a leading slash, e.g. <c>api/books/{id}</c>.</param>
public sealed record ExtractedEndpoint(EndpointMethod Method, string Route);
