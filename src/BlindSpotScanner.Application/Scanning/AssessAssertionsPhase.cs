using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// P6, code part (D27): the assertion level and fields of every integration test, read from its
/// C# source. A level the reader can't decide stays unknown; it is never guessed (principle 5).
/// </summary>
public sealed class AssessAssertionsPhase(IBlobReader blobs, ITestAssertionReader reader)
{
    public async Task<PhaseResult<IReadOnlyList<TestAssertion>>> AssessAsync(RunContext run, TestClassification tests, CancellationToken ct)
    {
        var rows = new List<TestAssertion>();
        var notes = new List<string>();
        foreach (var file in tests.IntegrationTests)
        {
            if (!file.Path.EndsWith(".cs", StringComparison.Ordinal))
            {
                notes.Add($"{file.Path}: assertion levels not assessed (C# only)");
                continue;
            }

            foreach (var facts in reader.Read(await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct)))
            {
                rows.Add(new TestAssertion
                {
                    Test = $"{file.Path}::{facts.TestMethod}",
                    Level = facts.Level,
                    LevelSource = facts.Level is null ? LevelSource.Unknown : LevelSource.Code,
                    ReadBack = facts.ReadBack,
                    ErrorPath = facts.ErrorPath,
                    SentFields = facts.SentFields,
                    CheckedFields = facts.CheckedFields,
                    UncheckedFields = facts.FieldsResolved ? TestAssertionRules.UncheckedFields(facts.SentFields, facts.CheckedFields) : [],
                    FieldsResolved = facts.FieldsResolved,
                });
            }
        }

        return new PhaseResult<IReadOnlyList<TestAssertion>>(rows, LlmUsage.None, notes);
    }
}
