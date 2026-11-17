namespace BlindSpotScanner.Application.GitHub;

/// <summary>Reads file content by Git blob SHA. Blobs are content-addressed, so a SHA's content never changes.</summary>
public interface IBlobReader
{
    Task<string> ReadAsync(string owner, string name, string blobSha, CancellationToken ct);
}
