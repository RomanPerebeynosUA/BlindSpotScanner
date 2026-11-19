using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// P3 (doc 05): read each production file at the pinned commit and extract its endpoints. Endpoints
/// the extractor can't read are carried as unverified reasons after P2's (principle 1). It also reads
/// the startup files for seams (D34); without any, seams are unverified.
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

        var seams = await DetectSeamsAsync(run, inventory, ct);
        if (seams is null)
        {
            reasons.Add(EndpointInventory.SeamsNotDetected);
        }

        return new EndpointInventory(
            controllers.OrderBy(c => c.SourcePath, StringComparer.Ordinal).ThenBy(c => c.Controller, StringComparer.Ordinal).ToList(),
            reasons,
            seams);
    }

    /// <summary>Seams of every startup file, the first file (ordinal path) naming a seam kept as its source.</summary>
    private async Task<IReadOnlyList<ProductionSeam>?> DetectSeamsAsync(RunContext run, RepositoryInventory inventory, CancellationToken ct)
    {
        if (inventory.StartupFiles.Count == 0)
        {
            return null;
        }

        var seams = new List<ProductionSeam>();
        foreach (var file in inventory.StartupFiles.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            var source = await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct);
            seams.AddRange(extractor.DetectSeams(file.Path, source));
        }

        return seams.DistinctBy(s => (s.Kind, s.Name)).OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Kind).ToList();
    }
}
