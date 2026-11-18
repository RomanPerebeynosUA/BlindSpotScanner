using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>P6's language strategy for assertion depth (D27): per-test facts of one test source file.</summary>
public interface ITestAssertionReader
{
    IReadOnlyList<TestAssertionFacts> Read(string source);
}

/// <summary>One test method's assertion facts, in the reader's test-method order.</summary>
/// <param name="TestMethod">As <c>Class.Method</c>, the same form as <see cref="TestFileFacts.TestMethods"/>.</param>
/// <param name="Level">0–5, or null when an assertion helper the reader can't see is called.</param>
/// <param name="ErrorPath">Some status assertion expects a 4xx status.</param>
/// <param name="FieldsResolved">False when a request payload was built somewhere the reader couldn't follow.</param>
public sealed record TestAssertionFacts(
    string TestMethod, int? Level, ReadBack? ReadBack, bool ErrorPath,
    IReadOnlyList<string> SentFields, IReadOnlyList<string> CheckedFields, bool FieldsResolved);
