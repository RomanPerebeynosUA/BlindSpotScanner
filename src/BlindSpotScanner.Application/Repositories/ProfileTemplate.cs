using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Repositories;

/// <summary>The blueprint's <c>profiles/_template.md</c> (embedded) and the default profile a new repository gets (D22).</summary>
public static class ProfileTemplate
{
    private const string DisplayNamePlaceholder = "<repo display name>";

    public static string Markdown { get; } = Read();

    /// <summary>Profile v1: the template's Markdown and the globs the M6 end-to-end test proved on the bookstore fixture.</summary>
    public static RepoProfile Default(Guid repositoryId, string displayName) => new()
    {
        RepositoryId = repositoryId,
        Version = 1,
        Markdown = Markdown.Replace(DisplayNamePlaceholder, displayName, StringComparison.Ordinal),
        IntegrationMappedTo = IntegrationMappedTo.RealSeam,
        ControllerGlobs = ["src/**/Controllers/**/*Controller.cs"],
        MinimalApiGlobs = ["src/**/Endpoints/**/*.cs"],
        TestGlobs = ["tests/**/*.cs"],
        IntegrationGlobs = ["tests/*.IntegrationTests/**"],
        IntegrationAttributes = ["Trait(\"Category\",\"Integration\")"],
        IntegrationBaseTypes = ["WebApplicationFactory"],
        NotIntegrationGlobs = ["**/*Fixture.cs"],
    };

    private static string Read()
    {
        using var stream = typeof(ProfileTemplate).Assembly.GetManifestResourceStream("profile-template.md")
            ?? throw new InvalidOperationException("Missing embedded resource profile-template.md.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
