using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Infrastructure.Llm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlindSpotScanner.Infrastructure.Remediation;

/// <summary>
/// Doc 09 step 3 (D25): test-project paths only, C# files only (so no project or package edits),
/// create/modify matching the scanned commit, size limits, no secrets, and C# that parses. Each
/// file reports its first violation.
/// </summary>
public sealed class FixGuard : IFixGuard
{
    public const int MaxFiles = 5;
    public const int MaxChars = 100_000;

    public IReadOnlyList<string> Check(IReadOnlyList<FixFile> files, string testProjectFolder, IReadOnlySet<string> existingPaths)
    {
        if (files.Count > MaxFiles)
        {
            return [$"too many files ({files.Count}; at most {MaxFiles})"];
        }

        return [.. files.Select(f => Violation(f, testProjectFolder, existingPaths)).OfType<string>()];
    }

    private static string? Violation(FixFile file, string folder, IReadOnlySet<string> existing)
    {
        var path = file.Path;
        if (!IsNormalised(path))
        {
            return $"{path}: path must be relative and normalised";
        }

        if (!path.StartsWith(folder + "/", StringComparison.Ordinal))
        {
            return $"{path}: outside the test project {folder}";
        }

        if (!path.EndsWith(".cs", StringComparison.Ordinal))
        {
            return $"{path}: only C# files can be changed";
        }

        // Case-insensitive: Foo.cs beside foo.cs collides on Windows and macOS checkouts.
        if (file.Action == FixAction.Create && existing.Any(e => string.Equals(e, path, StringComparison.OrdinalIgnoreCase)))
        {
            return $"{path}: already exists; use modify";
        }

        if (file.Action == FixAction.Modify && !existing.Contains(path))
        {
            return $"{path}: doesn't exist at the scanned commit; use create";
        }

        if (file.Content.Length > MaxChars)
        {
            return $"{path}: larger than {MaxChars} characters";
        }

        if (SecretScrubber.Scrub(file.Content) != file.Content)
        {
            return $"{path}: contains something that looks like a secret";
        }

        // The LLM saw secrets scrubbed; a modify that echoes the marker would commit it in place of the value.
        if (file.Content.Contains(SecretScrubber.Redacted, StringComparison.Ordinal))
        {
            return $"{path}: contains a redacted value; restore it before opening the PR";
        }

        var error = CSharpSyntaxTree.ParseText(file.Content).GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        return error is null
            ? null
            : $"{path}: C# syntax error at line {error.Location.GetLineSpan().StartLinePosition.Line + 1}: {error.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public IReadOnlyList<string> CheckMessage(string title, string body, string branch)
    {
        var errors = new List<string>();
        if (SecretScrubber.Scrub(title) != title)
        {
            errors.Add("title: contains something that looks like a secret");
        }

        if (SecretScrubber.Scrub(body) != body)
        {
            errors.Add("body: contains something that looks like a secret");
        }

        if (!IsBranchName(branch))
        {
            errors.Add("branch: not a valid git branch name");
        }

        return errors;
    }

    /// <summary>A conservative subset of <c>git check-ref-format --branch</c>.</summary>
    private static bool IsBranchName(string branch) =>
        branch.Length is > 0 and <= 200
        && branch.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/')
        && !branch.Contains("..", StringComparison.Ordinal)
        && !branch.Contains("//", StringComparison.Ordinal)
        && !branch.StartsWith('/') && !branch.EndsWith('/')
        && !branch.StartsWith('.') && !branch.EndsWith('.')
        && !branch.EndsWith(".lock", StringComparison.Ordinal)
        && !branch.Split('/').Any(s => s.StartsWith('.'));

    private static bool IsNormalised(string path) =>
        path.Length > 0 && !path.Contains('\\') && !path.StartsWith('/') && path.Split('/').All(s => s.Length > 0 && s != "." && s != "..");
}
