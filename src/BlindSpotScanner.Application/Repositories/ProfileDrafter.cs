using System.Text.Json;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Scanning;

namespace BlindSpotScanner.Application.Repositories;

public sealed record ProfileDraft(string Markdown, IReadOnlyList<string> OpenQuestions);

public enum DraftProfileStatus
{
    Drafted,
    RepositoryNotFound,
    GitHubFailed,
    LlmFailed,
}

public sealed record DraftProfileResult(DraftProfileStatus Status, ProfileDraft? Draft = null, IReadOnlyList<string>? Errors = null);

/// <summary>
/// Drafts a profile's Markdown with the LLM (<c>prompts/draft-profile.md</c>) from the default branch
/// head. The draft is only a preview: nothing is saved until the user saves it (D22).
/// </summary>
public sealed class ProfileDrafter(IRepositoryStore repositories, IGitHubClient github, ILlmClient llm, int maxFileBytes)
{
    public const string TruncatedQuestion = "The repository tree was truncated by GitHub; some folders may be missing from this draft.";

    public const string CappedQuestion = "The repository has more than 3000 files, so only the first 3000 paths (alphabetically) were sent; some folders may be missing from this draft.";

    private const int MaxTreePaths = 3000;
    private const int MaxBuildFiles = 20;
    private const int MaxSampleTests = 5;

    private static readonly string[] BuildFileNames = ["Directory.Build.props", "Directory.Packages.props", "global.json"];
    private static readonly string[] BuildFileExtensions = [".csproj", ".sln", ".slnx"];

    public async Task<DraftProfileResult> DraftAsync(Guid repositoryId, CancellationToken ct)
    {
        if (await repositories.FindAsync(repositoryId, ct) is not { } repository)
        {
            return new DraftProfileResult(DraftProfileStatus.RepositoryNotFound);
        }

        GitTree tree;
        var capped = false;
        string owner = repository.Owner, name = repository.Name;
        List<object> buildFiles, sampleTests;
        List<string> paths;
        try
        {
            var head = await github.GetBranchHeadShaAsync(owner, name, repository.DefaultBranch, ct);
            tree = await github.GetTreeAsync(owner, name, head, recursive: true, ct);
            var blobs = tree.Entries
                .Where(e => e.Type == GitEntryType.Blob && !EnumeratePhase.IsExcluded(e.Path) && !LlmContentPolicy.IsDenied(e.Path))
                .OrderBy(e => e.Path, StringComparer.Ordinal)
                .ToList();
            paths = blobs.Take(MaxTreePaths).Select(e => e.Path).ToList();
            capped = blobs.Count > MaxTreePaths;
            var readable = blobs.Where(e => e.Size is null || e.Size <= maxFileBytes).ToList();
            buildFiles = await ReadAsync(readable.Where(e => IsBuildFile(e.Path)).Take(MaxBuildFiles), ct);
            sampleTests = await ReadAsync(
                readable.Where(e => e.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && e.Path.Contains("Test", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.Path.Contains("Integration", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(e => e.Path, StringComparer.Ordinal)
                    .Take(MaxSampleTests),
                ct);
        }
        catch (Exception ex) when (ex is GitHubNotFoundException or GitHubAuthenticationException or GitHubRateLimitException)
        {
            return new DraftProfileResult(DraftProfileStatus.GitHubFailed, Errors: [ex.Message]);
        }

        var input = JsonSerializer.Serialize(new { rootTree = paths, buildFiles, sampleTests });
        var result = await llm.CompleteAsync(new LlmRequest(Prompts.DraftProfile, input, LlmSchemas.Get(Prompts.DraftProfile), Check), ct);
        if (result.Output is not { } output)
        {
            return new DraftProfileResult(DraftProfileStatus.LlmFailed, Errors: result.Errors);
        }

        var questions = output.GetProperty("openQuestions").EnumerateArray().Select(q => q.GetString()!).ToList();
        if (capped)
        {
            questions.Insert(0, CappedQuestion);
        }

        if (tree.Truncated)
        {
            questions.Insert(0, TruncatedQuestion);
        }

        return new DraftProfileResult(DraftProfileStatus.Drafted, new ProfileDraft(output.GetProperty("markdown").GetString()!, questions));

        async Task<List<object>> ReadAsync(IEnumerable<GitTreeEntry> entries, CancellationToken token)
        {
            var files = new List<object>();
            foreach (var entry in entries)
            {
                files.Add(new { path = entry.Path, content = await github.GetBlobTextAsync(owner, name, entry.Sha, token) });
            }

            return files;
        }
    }

    private static bool IsBuildFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return BuildFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
            || BuildFileExtensions.Any(x => fileName.EndsWith(x, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> Check(JsonElement answer) =>
        string.IsNullOrWhiteSpace(answer.GetProperty("markdown").GetString()) ? ["markdown must not be empty."] : [];
}
