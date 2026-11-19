namespace BlindSpotScanner.Domain.Enums;

/// <summary>The kind of a real dependency boundary ("seam") registered in production startup code (D34).</summary>
public enum SeamKind
{
    Database,
    OutboundHttp,
    MessageBroker,
    BlobStorage,
    Cache,
}
