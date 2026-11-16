using System.Text.Json.Serialization;

namespace BlindSpotScanner.Domain.Enums;

public enum Verdict
{
    [JsonStringEnumMemberName("strong")] Strong,
    [JsonStringEnumMemberName("moderate")] Moderate,
    [JsonStringEnumMemberName("partial")] Partial,
    [JsonStringEnumMemberName("weak")] Weak,
}
