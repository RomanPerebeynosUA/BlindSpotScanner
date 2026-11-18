using BlindSpotScanner.Application.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlindSpotScanner.Infrastructure.Extraction;

/// <summary>
/// Test-file signals from C# syntax. Text is rebuilt from tokens, so whitespace and comments drop
/// out while string literal contents stay intact.
/// </summary>
public sealed class CSharpTestSyntaxReader : ITestSyntaxReader
{
    public TestFileFacts Read(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        var methods = classes.SelectMany(c => c.Members.OfType<MethodDeclarationSyntax>().Select(m => (Class: c, Method: m))).ToList();

        var attributes = classes.SelectMany(c => c.AttributeLists)
            .Concat(methods.SelectMany(m => m.Method.AttributeLists))
            .SelectMany(l => l.Attributes)
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var baseTypes = classes.SelectMany(c => c.BaseList?.Types ?? default).Select(t => Tokens(t.Type)).ToList();
        var testMethods = methods
            .Where(m => SyntaxNames.IsTestMethod(m.Method))
            .Select(m => $"{SyntaxNames.QualifiedName(m.Class)}.{m.Method.Identifier.ValueText}")
            .ToList();

        var allMethods = methods.Select(m => $"{SyntaxNames.QualifiedName(m.Class)}.{m.Method.Identifier.ValueText}").ToList();
        return new TestFileFacts(attributes, baseTypes, testMethods, allMethods);
    }

    private static string Normalize(AttributeSyntax attribute) =>
        SyntaxNames.Of(attribute) + (attribute.ArgumentList is { } arguments ? Tokens(arguments) : string.Empty);

    private static string Tokens(SyntaxNode node) => string.Concat(node.DescendantTokens().Select(t => t.Text));
}
