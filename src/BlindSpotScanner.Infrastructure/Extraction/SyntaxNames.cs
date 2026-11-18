using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlindSpotScanner.Infrastructure.Extraction;

internal static class SyntaxNames
{
    private static readonly HashSet<string> TestAttributes = new(StringComparer.Ordinal)
    {
        "TestCase", "TestCaseSource", "TestMethod", "DataTestMethod",
    };

    /// <summary>Derived test attributes keep their base name as a suffix ([SkippableFact], [IntegrationTheory], [Test]).</summary>
    private static readonly string[] TestAttributeSuffixes = ["Fact", "Theory", "Test"];

    public static bool IsTestMethod(MethodDeclarationSyntax method) =>
        method.AttributeLists.SelectMany(l => l.Attributes).Any(a => IsTestAttribute(Of(a)));

    /// <summary>Nested classes keep their outer classes, so two nested classes with one name stay distinct.</summary>
    public static string QualifiedName(ClassDeclarationSyntax type) =>
        string.Join('.', type.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().Reverse().Select(c => c.Identifier.ValueText));

    private static bool IsTestAttribute(string name) =>
        TestAttributes.Contains(name) || TestAttributeSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>The attribute's simple name without namespace or "Attribute" suffix.</summary>
    public static string Of(AttributeSyntax attribute)
    {
        var name = Simple(attribute.Name);
        return name.Length > "Attribute".Length && name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }

    public static string Simple(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => type.ToString(),
    };
}
