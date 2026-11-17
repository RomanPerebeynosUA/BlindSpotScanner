using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// P3 (doc 05): read each production file at the pinned commit and extract its endpoints. Endpoints
/// the extractor can't read are carried as unverified reasons after P2's (principle 1).
/// </summary>
public sealed class ExtractPhase(IBlobReader blobs, IEndpointExtractor extractor)
{
    public async Task<EndpointInventory> ExtractAsync(RunContext run, RepositoryInventory inventory, CancellationToken ct)
    {
        var controllers = new List<ExtractedController>();
        var reasons = new List<string>(inventory.UnverifiedReasons);
        foreach (var file in inventory.ControllerFiles.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            var source = await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct);
            var result = extractor.Extract(file.Path, source);
            controllers.AddRange(result.Controllers);
            reasons.AddRange(result.Warnings);
        }

        return new EndpointInventory(
            controllers.OrderBy(c => c.SourcePath, StringComparer.Ordinal).ThenBy(c => c.Controller, StringComparer.Ordinal).ToList(),
            reasons);
    }
}
