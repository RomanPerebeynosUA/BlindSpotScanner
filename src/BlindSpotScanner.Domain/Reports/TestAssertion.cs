using System.Text.Json.Serialization;

namespace BlindSpotScanner.Domain.Reports;

/// <summary>Who decided a test's assertion level (D27).</summary>
public enum LevelSource
{
    [JsonStringEnumMemberName("code")] Code,
    [JsonStringEnumMemberName("llm")] Llm,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

/// <summary>How an L4 test read the saved state back.</summary>
public enum ReadBack
{
    [JsonStringEnumMemberName("api")] Api,
    [JsonStringEnumMemberName("db")] Db,
}

/// <summary>
/// One integration test's assertion depth (D27): the highest level it reaches on the L0–L5 ladder
/// and, for write tests, the fields it sends against the fields it checks.
/// </summary>
public sealed record TestAssertion
{
    /// <summary>The test as <c>path::Class.Method</c>.</summary>
    public required string Test { get; init; }

    /// <summary>0–5, or null when neither code nor the LLM could decide (principle 5).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required int? Level { get; init; }

    public required LevelSource LevelSource { get; init; }
    public ReadBack? ReadBack { get; init; }
    public required bool ErrorPath { get; init; }
    public required IReadOnlyList<string> SentFields { get; init; }
    public required IReadOnlyList<string> CheckedFields { get; init; }
    public required IReadOnlyList<string> UncheckedFields { get; init; }

    /// <summary>False when the request payload was built somewhere the reader couldn't follow; the field lists are then empty.</summary>
    public required bool FieldsResolved { get; init; }
}

/// <summary>Integration tests per assertion level; computed by ReportAssembler from <c>testAssertions</c>.</summary>
public sealed record AssertionLevels
{
    public required int L0 { get; init; }
    public required int L1 { get; init; }
    public required int L2 { get; init; }
    public required int L3 { get; init; }
    public required int L4 { get; init; }
    public required int L5 { get; init; }
    public required int Unknown { get; init; }
}
