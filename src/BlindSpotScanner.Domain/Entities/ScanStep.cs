using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Entities;

/// <summary>One pipeline phase of a scan run (doc 05).</summary>
public class ScanStep
{
    public required Guid Id { get; init; }
    public required Guid ScanRunId { get; init; }
    public required string Phase { get; init; }
    public required RunStatus Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal CostUsd { get; init; }
    public int ReadInFull { get; init; }
    public int Sampled { get; init; }
    public string? Notes { get; init; }
}
