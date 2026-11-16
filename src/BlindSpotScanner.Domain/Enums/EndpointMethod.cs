using System.Text.Json.Serialization;

namespace BlindSpotScanner.Domain.Enums;

public enum EndpointMethod
{
    [JsonStringEnumMemberName("GET")] Get,
    [JsonStringEnumMemberName("POST")] Post,
    [JsonStringEnumMemberName("PUT")] Put,
    [JsonStringEnumMemberName("PATCH")] Patch,
    [JsonStringEnumMemberName("DELETE")] Delete,
    [JsonStringEnumMemberName("HEAD")] Head,
    [JsonStringEnumMemberName("OPTIONS")] Options,
}
