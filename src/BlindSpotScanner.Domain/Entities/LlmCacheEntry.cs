namespace BlindSpotScanner.Domain.Entities;

/// <summary>A cached structured LLM response, keyed by a hash of the request.</summary>
public class LlmCacheEntry
{
    public required string Key { get; init; }
    public required string ResponseJson { get; init; }
}
