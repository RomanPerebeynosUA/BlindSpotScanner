using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlindSpotScanner.Domain.Reports;

/// <summary>
/// The single owner of the report's canonical JSON form. Every write of a report goes through
/// <see cref="Serialize"/>, so the same report always produces the same bytes.
/// </summary>
public static class ReportJsonSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(IntegrationReport report) =>
        JsonSerializer.Serialize(report, Options);

    /// <exception cref="JsonException">On unknown members, missing required members or malformed JSON.</exception>
    public static IntegrationReport Deserialize(string json) =>
        JsonSerializer.Deserialize<IntegrationReport>(json, Options)
            ?? throw new JsonException("The report JSON is null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // Reports are stored, not rendered as HTML, so keep characters such as → literal.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new UtcTimestampConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
