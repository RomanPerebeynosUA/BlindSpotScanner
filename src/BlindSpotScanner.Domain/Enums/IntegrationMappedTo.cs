using System.Text.Json.Serialization;

namespace BlindSpotScanner.Domain.Enums;

public enum IntegrationMappedTo
{
    [JsonStringEnumMemberName("real-seam")] RealSeam,
    [JsonStringEnumMemberName("e2e-mocked-data")] E2eMockedData,
    [JsonStringEnumMemberName("e2e-real-backend")] E2eRealBackend,
    [JsonStringEnumMemberName("contract")] Contract,
    [JsonStringEnumMemberName("other")] Other,
}
