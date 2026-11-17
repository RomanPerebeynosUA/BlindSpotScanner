namespace BlindSpotScanner.Application.Scanning;

/// <summary>Doc 06's seven quality dimensions. Names and weights come from here, never from the LLM.</summary>
public static class Rubric
{
    public static readonly IReadOnlyList<(int Id, string Name, decimal Weight)> Dimensions =
    [
        (1, "Integration-point coverage", 0.20m),
        (2, "Critical-path coverage", 0.20m),
        (3, "Failure & edge-path coverage", 0.15m),
        (4, "Environmental fidelity", 0.15m),
        (5, "Assertion strength", 0.15m),
        (6, "Determinism / reliability", 0.10m),
        (7, "Diagnosability / maintainability", 0.05m),
    ];
}
