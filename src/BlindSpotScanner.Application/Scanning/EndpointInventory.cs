namespace BlindSpotScanner.Application.Scanning;

/// <summary>The output of P3: the endpoint denominator (principle 3) and why parts of it may be incomplete.</summary>
public sealed record EndpointInventory(IReadOnlyList<ExtractedController> Controllers, IReadOnlyList<string> UnverifiedReasons)
{
    public int EndpointCount => Controllers.Sum(c => c.Endpoints.Count);
}
