using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Entities;

/// <summary>One version of a repository's scan profile; keyed by (RepositoryId, Version) to keep history.</summary>
public class RepoProfile
{
    public required Guid RepositoryId { get; init; }
    public required int Version { get; init; }
    public required string Markdown { get; init; }
    public required IntegrationMappedTo IntegrationMappedTo { get; init; }
    public required string[] TestGlobs { get; init; }
    public required string[] ControllerGlobs { get; init; }

    /// <summary>Files with minimal API route registrations; part of the denominator source with the controllers.</summary>
    public string[] MinimalApiGlobs { get; init; } = [];

    /// <summary>P4 rules (D18): integration folders and file naming.</summary>
    public string[] IntegrationGlobs { get; init; } = [];

    /// <summary>P4 rules: attributes that mark integration tests, without brackets, e.g. <c>Trait("Category","Integration")</c>.</summary>
    public string[] IntegrationAttributes { get; init; } = [];

    /// <summary>P4 rules: base types of integration tests; an entry without <c>&lt;</c> matches any generic arity.</summary>
    public string[] IntegrationBaseTypes { get; init; } = [];

    /// <summary>P4 rules: files that are never integration tests, even inside an integration folder.</summary>
    public string[] NotIntegrationGlobs { get; init; } = [];
}
