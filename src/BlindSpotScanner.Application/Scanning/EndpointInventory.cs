namespace BlindSpotScanner.Application.Scanning;

/// <summary>The output of P3: the endpoint denominator (principle 3) and why parts of it may be incomplete.</summary>
/// <param name="Seams">The seams detected in startup files (D34); null when no startup file exists, so seams are unverified, never "none".</param>
public sealed record EndpointInventory(IReadOnlyList<ExtractedController> Controllers, IReadOnlyList<string> UnverifiedReasons, IReadOnlyList<ProductionSeam>? Seams = null)
{
    public const string SeamsNotDetected = "seams not detected: no Program.cs, Startup.cs or *ServiceCollectionExtensions.cs found";

    public int EndpointCount => Controllers.Sum(c => c.Endpoints.Count);
}
