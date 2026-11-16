using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BlindSpotScanner.Domain.Reports;

/// <summary>
/// Report timestamps: read only with an explicit offset ("Z" or "±hh:mm"), always written as UTC
/// ("…Z"), so canonical bytes never depend on the machine's time zone.
/// </summary>
internal sealed partial class UtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null || !ExplicitOffset().IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new JsonException($"'{text}' is not an RFC 3339 date-time with an explicit offset.");
        }

        return value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture));

    [GeneratedRegex(@"(Z|z|[+-]\d{2}:\d{2})$")]
    private static partial Regex ExplicitOffset();
}
