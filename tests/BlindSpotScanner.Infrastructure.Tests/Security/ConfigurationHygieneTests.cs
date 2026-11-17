using System.Text.Json.Nodes;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Security;

/// <summary>Doc 10: never commit appsettings or .env files with real secret values.</summary>
public class ConfigurationHygieneTests
{
    private static readonly string[] SecretNames = ["Token", "ApiKey", "ApiToken", "ClientSecret", "Password", "Secret"];

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BlindSpotScanner.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> SecretValues(JsonNode? node, string path)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    var here = $"{path}:{key}";
                    if (value is JsonValue v && SecretNames.Any(s => key.EndsWith(s, StringComparison.OrdinalIgnoreCase)) && v.ToString().Length > 0)
                    {
                        yield return here;
                    }

                    foreach (var nested in SecretValues(value, here))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    foreach (var nested in SecretValues(item, path))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    [Fact]
    public void CommittedAppSettings_HoldNoSecretValues()
    {
        // Arrange
        var files = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        // Act
        var leaks = files.SelectMany(f => SecretValues(JsonNode.Parse(File.ReadAllText(f)), Path.GetFileName(f))).ToList();

        // Assert
        new { Checked = files.Count >= 5, Leaks = string.Join(", ", leaks) }.Should().Be(new { Checked = true, Leaks = string.Empty });
    }

    [Fact]
    public void EnvExample_HoldsNoSecretValues()
    {
        // Arrange (only the documented placeholder is allowed)
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".env.example"))
            .Where(l => !l.TrimStart().StartsWith('#') && l.Contains('='))
            .Select(l => (Key: l[..l.IndexOf('=')].Trim(), Value: l[(l.IndexOf('=') + 1)..].Trim()));

        // Act
        var leaks = lines
            .Where(l => new[] { "TOKEN", "KEY", "SECRET", "PASSWORD" }.Any(s => l.Key.Contains(s, StringComparison.Ordinal)))
            .Where(l => l.Value.Length > 0 && l.Value != "change-me")
            .Select(l => l.Key)
            .ToList();

        // Assert
        leaks.Should().BeEmpty();
    }
}
