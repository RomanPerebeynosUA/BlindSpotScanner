using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Application.Tests.Scanning;

/// <summary>Blob text by SHA; records every read so tests can check reads stay at the pinned blobs.</summary>
internal sealed class FakeBlobReader(IReadOnlyDictionary<string, string> textBySha) : IBlobReader
{
    public List<string> Reads { get; } = [];

    public Task<string> ReadAsync(string owner, string name, string blobSha, CancellationToken ct)
    {
        Reads.Add(blobSha);
        return Task.FromResult(textBySha[blobSha]);
    }
}
