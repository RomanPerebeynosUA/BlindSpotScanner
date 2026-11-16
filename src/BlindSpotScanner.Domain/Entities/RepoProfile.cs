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
}
