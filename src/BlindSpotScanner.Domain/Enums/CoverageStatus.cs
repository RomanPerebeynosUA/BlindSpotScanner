using System.Text.Json.Serialization;

namespace BlindSpotScanner.Domain.Enums;

public enum CoverageStatus
{
    [JsonStringEnumMemberName("covered")] Covered,
    [JsonStringEnumMemberName("weak")] Weak,
    [JsonStringEnumMemberName("skipped")] Skipped,
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("unverified")] Unverified,
}
