namespace BlindSpotScanner.Application.Scanning;

/// <summary>P4's language strategy: the syntactic signals of one test source file.</summary>
public interface ITestSyntaxReader
{
    TestFileFacts Read(string source);
}

/// <param name="Attributes">Attributes on classes and methods, whitespace removed, without brackets, namespace or "Attribute" suffix, e.g. <c>Trait("Category","Integration")</c>. Distinct, ordinal order.</param>
/// <param name="BaseTypes">Base-list entries of every class, whitespace removed, e.g. <c>IClassFixture&lt;ApiFactory&gt;</c>.</param>
/// <param name="TestMethods">Test methods as <c>Class.Method</c>, in source order.</param>
/// <param name="Methods">Every method as <c>Class.Method</c>, in source order (citations may name fixtures and helpers).</param>
public sealed record TestFileFacts(IReadOnlyList<string> Attributes, IReadOnlyList<string> BaseTypes, IReadOnlyList<string> TestMethods, IReadOnlyList<string> Methods);
