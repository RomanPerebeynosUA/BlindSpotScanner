using System.Text.Json;
using System.Text.Json.Serialization;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Repositories;

/// <param name="IntegrationMappedTo">One of <c>real-seam | e2e-mocked-data | e2e-real-backend | contract | other</c>.</param>
public sealed record ProfileInput(
    string Markdown, string IntegrationMappedTo, string[] TestGlobs, string[] ControllerGlobs, string[] MinimalApiGlobs,
    string[] IntegrationGlobs, string[] IntegrationAttributes, string[] IntegrationBaseTypes, string[] NotIntegrationGlobs);

public enum SaveProfileStatus
{
    Saved,
    RepositoryNotFound,
    Invalid,
    Conflict,
}

public sealed record SaveProfileResult(SaveProfileStatus Status, RepoProfile? Profile = null, IReadOnlyList<string>? Errors = null);

public interface IProfileStore
{
    /// <summary>The given version, or the latest when <paramref name="version"/> is null.</summary>
    Task<RepoProfile?> GetAsync(Guid repositoryId, int? version, CancellationToken ct);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<int>> VersionsAsync(Guid repositoryId, CancellationToken ct);

    /// <summary>False when that (RepositoryId, Version) already exists: another save came first.</summary>
    Task<bool> TryAddAsync(RepoProfile profile, CancellationToken ct);
}

/// <summary>Profiles are versioned and never edited in place: a save adds the next version (D22).</summary>
public sealed class ProfileService(IProfileStore profiles, IRepositoryStore repositories)
{
    private static readonly JsonSerializerOptions EnumNames = new() { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };

    /// <summary>The wire name of a mapping, e.g. <c>real-seam</c>.</summary>
    public static string MappingName(IntegrationMappedTo mapping) => JsonSerializer.Serialize(mapping, EnumNames).Trim('"');

    public Task<RepoProfile?> GetAsync(Guid repositoryId, int? version, CancellationToken ct) => profiles.GetAsync(repositoryId, version, ct);

    /// <summary>Null when the repository doesn't exist.</summary>
    public async Task<IReadOnlyList<int>?> VersionsAsync(Guid repositoryId, CancellationToken ct) =>
        await repositories.FindAsync(repositoryId, ct) is null ? null : await profiles.VersionsAsync(repositoryId, ct);

    /// <param name="baseVersion">The version the user edited; a save from an older one is a conflict.</param>
    public async Task<SaveProfileResult> SaveAsync(Guid repositoryId, int baseVersion, ProfileInput input, CancellationToken ct)
    {
        if (await repositories.FindAsync(repositoryId, ct) is null)
        {
            return new SaveProfileResult(SaveProfileStatus.RepositoryNotFound);
        }

        var testGlobs = Clean(input.TestGlobs);
        var controllerGlobs = Clean(input.ControllerGlobs);
        var minimalApiGlobs = Clean(input.MinimalApiGlobs);
        var errors = new List<string>();
        if (baseVersion < 0)
        {
            errors.Add("baseVersion must be 0 (no profile yet) or the version being edited.");
        }

        if (string.IsNullOrWhiteSpace(input.Markdown))
        {
            errors.Add("Markdown must not be empty.");
        }

        if (!TryParseMapping(input.IntegrationMappedTo, out var mapping))
        {
            errors.Add($"integrationMappedTo '{input.IntegrationMappedTo}' is not one of real-seam, e2e-mocked-data, e2e-real-backend, contract, other.");
        }

        if (testGlobs.Length == 0)
        {
            errors.Add("At least one test glob is required.");
        }

        if (controllerGlobs.Length == 0 && minimalApiGlobs.Length == 0)
        {
            errors.Add("At least one controller glob or minimal API file glob is required (the endpoint denominator).");
        }

        // D31: one glob per entry, so text pasted next to a pattern can't silently match nothing.
        errors.AddRange(ProfilePatterns.SyntaxErrors(new ProfileRules(
            input.IntegrationMappedTo, testGlobs, controllerGlobs, minimalApiGlobs, Clean(input.IntegrationGlobs), [], [], Clean(input.NotIntegrationGlobs))));

        if (errors.Count > 0)
        {
            return new SaveProfileResult(SaveProfileStatus.Invalid, Errors: errors);
        }

        // The base must be the latest version, or 0 when the repository has no profile yet.
        var latestVersion = (await profiles.GetAsync(repositoryId, null, ct))?.Version ?? 0;
        if (latestVersion != baseVersion)
        {
            return new SaveProfileResult(SaveProfileStatus.Conflict);
        }

        var profile = new RepoProfile
        {
            RepositoryId = repositoryId,
            Version = baseVersion + 1,
            Markdown = input.Markdown,
            IntegrationMappedTo = mapping,
            TestGlobs = testGlobs,
            ControllerGlobs = controllerGlobs,
            MinimalApiGlobs = minimalApiGlobs,
            IntegrationGlobs = Clean(input.IntegrationGlobs),
            IntegrationAttributes = Clean(input.IntegrationAttributes),
            IntegrationBaseTypes = Clean(input.IntegrationBaseTypes),
            NotIntegrationGlobs = Clean(input.NotIntegrationGlobs),
        };

        // Two saves from the same base race on the (RepositoryId, Version) key; the loser gets a conflict.
        return await profiles.TryAddAsync(profile, ct)
            ? new SaveProfileResult(SaveProfileStatus.Saved, profile)
            : new SaveProfileResult(SaveProfileStatus.Conflict);
    }

    private static string[] Clean(string[]? entries) =>
        (entries ?? []).Where(e => e is not null).Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    private static bool TryParseMapping(string? text, out IntegrationMappedTo mapping)
    {
        mapping = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            mapping = JsonSerializer.Deserialize<IntegrationMappedTo>(JsonSerializer.Serialize(text), EnumNames);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
