namespace BlindSpotScanner.Domain.Entities;

/// <summary>File content cached by Git blob SHA.</summary>
public class FileCacheEntry
{
    public required string BlobSha { get; init; }
    public required string Content { get; init; }
}
