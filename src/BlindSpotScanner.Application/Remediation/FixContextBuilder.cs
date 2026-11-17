using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Remediation;

public sealed record SourceFile(string Path, string Content);

/// <param name="TestProjectFolder">The folder of the nearest <c>.csproj</c> above the sibling tests, without a trailing slash.</param>
/// <param name="TargetFolder">Where a new test goes (the first sibling's folder), with a trailing slash.</param>
/// <param name="TargetTest">The test a WeakTest / FalsePositive / SkippedTest fix modifies.</param>
/// <param name="BlobShas">Every blob in the tree at the scanned commit, path → blob SHA (for the guard and the staleness check).</param>
public sealed record FixContext(
    SourceFile? Controller,
    IReadOnlyList<SourceFile> SiblingTests,
    IReadOnlyList<SourceFile> Fixtures,
    SourceFile TestProjectFile,
    string TestProjectFolder,
    string TargetFolder,
    SourceFile? TargetTest,
    IReadOnlyDictionary<string, string> BlobShas);

/// <summary>
/// Doc 09 step 1, at the run's commit: the target controller, 1–3 sibling integration tests (they show
/// the conventions), the test project file and its shared fixtures. Null when no test project is found.
/// </summary>
public sealed class FixContextBuilder(IGitHubClient github)
{
    private const int MaxSiblings = 3;
    private const int MaxFixtures = 3;

    public async Task<FixContext?> BuildAsync(IntegrationReport report, ReportFinding finding, CancellationToken ct)
    {
        var (owner, name, sha) = (report.Meta.Repo.Owner, report.Meta.Repo.Name, report.Meta.CommitSha);
        var tree = await github.GetTreeAsync(owner, name, sha, recursive: true, ct);
        var blobs = tree.Entries.Where(e => e.Type == GitEntryType.Blob).ToDictionary(e => e.Path, e => e.Sha, StringComparer.Ordinal);

        var anchor = finding.Kind == FindingKind.UncoveredEndpoint ? null : finding.Evidence.Select(PathOf).FirstOrDefault(blobs.ContainsKey);
        var controller = finding.Kind == FindingKind.UncoveredEndpoint ? ControllerOf(report, finding) : null;

        var siblingPaths = new[] { anchor }
            .Concat(controller?.Endpoints.SelectMany(e => e.Evidence).Select(PathOf) ?? [])
            .Concat(report.EndpointCoverage.SelectMany(c => c.Endpoints).SelectMany(e => e.Evidence).Select(PathOf))
            .OfType<string>()
            .Where(blobs.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSiblings)
            .ToList();
        if (siblingPaths.Count == 0 || ProjectFileAbove(Folder(siblingPaths[0]), blobs.Keys) is not { } projectPath)
        {
            return null;
        }

        var projectFolder = Folder(projectPath);
        var fixturePaths = blobs.Keys
            .Where(p => p.StartsWith(projectFolder + "/", StringComparison.Ordinal) && p.EndsWith(".cs", StringComparison.Ordinal) && !siblingPaths.Contains(p))
            .Where(p => FileName(p).Contains("Fixture", StringComparison.Ordinal) || FileName(p).Contains("Factory", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Take(MaxFixtures);

        async Task<SourceFile> ReadAsync(string path) => new(path, await github.GetBlobTextAsync(owner, name, blobs[path], ct));

        var siblings = new List<SourceFile>();
        foreach (var path in siblingPaths)
        {
            siblings.Add(await ReadAsync(path));
        }

        var fixtures = new List<SourceFile>();
        foreach (var path in fixturePaths)
        {
            fixtures.Add(await ReadAsync(path));
        }

        var controllerSource = controller is not null && blobs.ContainsKey(controller.SourcePath) ? await ReadAsync(controller.SourcePath) : null;
        return new FixContext(
            controllerSource,
            siblings,
            fixtures,
            await ReadAsync(projectPath),
            projectFolder,
            Folder(siblingPaths[0]) + "/",
            anchor is null ? null : siblings.Single(s => s.Path == anchor),
            blobs);
    }

    private static ControllerCoverage? ControllerOf(IntegrationReport report, ReportFinding finding) =>
        finding.Detail is [var endpoint, ..]
            ? report.EndpointCoverage.FirstOrDefault(c => c.Endpoints.Any(e => EndpointKey.Display(e.Method, e.Route) == endpoint))
            : null;

    /// <summary>The nearest folder at or above <paramref name="folder"/> holding a <c>.csproj</c>.</summary>
    private static string? ProjectFileAbove(string folder, IEnumerable<string> paths)
    {
        var projects = paths.Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToList();
        for (var current = folder; ; current = Folder(current))
        {
            if (projects.Where(p => Folder(p) == current).Order(StringComparer.Ordinal).FirstOrDefault() is { } found)
            {
                return found;
            }

            if (current.Length == 0)
            {
                return null;
            }
        }
    }

    private static string PathOf(string citation) => citation.Split("::")[0];

    private static string Folder(string path) => path.LastIndexOf('/') is var i and >= 0 ? path[..i] : string.Empty;

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];
}
