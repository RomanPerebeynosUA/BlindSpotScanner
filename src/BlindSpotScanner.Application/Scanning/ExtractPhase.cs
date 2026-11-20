using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// P3 (doc 05): read each production file at the pinned commit and extract its endpoints. Endpoints
/// the extractor can't read are carried as unverified reasons after P2's (principle 1). It also reads
/// the startup files once, for seams (D34) and a kebab-case route convention (D35); without any
/// startup file, seams are unverified.
/// </summary>
public sealed class ExtractPhase(IBlobReader blobs, IEndpointExtractor extractor)
{
    public async Task<EndpointInventory> ExtractAsync(RunContext run, RepositoryInventory inventory, CancellationToken ct)
    {
        var startup = new List<(string Path, string Source)>();
        foreach (var file in inventory.StartupFiles.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            startup.Add((file.Path, await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct)));
        }

        var kebabCaseProjects = startup
            .Where(s => extractor.UsesKebabCaseRouteTokens(s.Path, s.Source))
            .Select(s => s.Path[..(s.Path.LastIndexOf('/') + 1)])
            .ToList();

        var controllers = new List<ExtractedController>();
        var reasons = new List<string>(inventory.UnverifiedReasons);
        foreach (var file in inventory.ControllerFiles.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            var source = await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct);
            var kebabCase = kebabCaseProjects.Exists(project => file.Path.StartsWith(project, StringComparison.Ordinal));
            var result = extractor.Extract(file.Path, source, kebabCase);
            controllers.AddRange(result.Controllers);
            reasons.AddRange(result.Warnings);
        }

        var seams = DetectSeams(startup);
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
    private List<ProductionSeam>? DetectSeams(List<(string Path, string Source)> startup) =>
        startup.Count == 0
            ? null
            : startup.SelectMany(s => extractor.DetectSeams(s.Path, s.Source))
                .DistinctBy(s => (s.Kind, s.Name)).OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Kind).ToList();
}
