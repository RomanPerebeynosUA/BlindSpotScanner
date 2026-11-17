using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlindSpotScanner.Infrastructure.Extraction;

internal static class SyntaxNames
{
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
