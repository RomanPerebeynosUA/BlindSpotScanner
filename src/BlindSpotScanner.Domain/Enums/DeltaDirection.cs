using System.Text.Json.Serialization;

namespace BlindSpotScanner.Domain.Enums;

public enum DeltaDirection
{
    [JsonStringEnumMemberName("improved")] Improved,
    [JsonStringEnumMemberName("regressed")] Regressed,
    [JsonStringEnumMemberName("unchanged")] Unchanged,
}
