using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>A real dependency registered in a production startup file (D34).</summary>
/// <param name="Name">A kebab-case key, e.g. <c>payment-gateway</c>; an <c>UncoveredSeam</c> finding's subject must equal it.</param>
/// <param name="SourcePath">The startup file that registers it.</param>
public sealed record ProductionSeam(SeamKind Kind, string Name, string SourcePath);
