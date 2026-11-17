using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlindSpotScanner.Infrastructure.Extraction;

/// <summary>
/// ASP.NET Core endpoints from C# syntax (no compilation): attribute-routed controllers and minimal
/// APIs with MapGroup prefixes (D19). A file's minimal APIs form one controller named after the file.
/// Comments are trivia, so commented-out attributes never count. A route that is not a string
/// literal can't be read syntactically and is reported as a warning, never dropped silently.
/// </summary>
public sealed class AspNetCoreEndpointExtractor : IEndpointExtractor
{
    private static readonly Dictionary<string, EndpointMethod> Verbs = new(StringComparer.Ordinal)
    {
        ["HttpGet"] = EndpointMethod.Get,
        ["HttpPost"] = EndpointMethod.Post,
        ["HttpPut"] = EndpointMethod.Put,
        ["HttpPatch"] = EndpointMethod.Patch,
        ["HttpDelete"] = EndpointMethod.Delete,
        ["HttpHead"] = EndpointMethod.Head,
        ["HttpOptions"] = EndpointMethod.Options,
    };

    private static readonly Dictionary<string, EndpointMethod> MapVerbs = new(StringComparer.Ordinal)
    {
        ["MapGet"] = EndpointMethod.Get,
        ["MapPost"] = EndpointMethod.Post,
        ["MapPut"] = EndpointMethod.Put,
        ["MapPatch"] = EndpointMethod.Patch,
        ["MapDelete"] = EndpointMethod.Delete,
    };

    /// <summary>Bounds group-variable chasing (a variable defined from itself would loop).</summary>
    private const int MaxGroupDepth = 16;

    public ExtractionResult Extract(string path, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, path: path).GetRoot();
        var controllers = new List<ExtractedController>();
        var warnings = new List<string>();
        foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(IsController))
        {
            ExtractController(path, type, controllers, warnings);
        }

        var minimal = ExtractMinimalApis(path, root, warnings);
        if (minimal.Count > 0)
        {
            controllers.Add(new ExtractedController(System.IO.Path.GetFileNameWithoutExtension(path), path, RoutePrefix: null, minimal));
        }

        return new ExtractionResult(controllers, warnings);
    }

    private static bool IsController(ClassDeclarationSyntax type) =>
        !type.Modifiers.Any(SyntaxKind.AbstractKeyword)
        && (Attributes(type.AttributeLists).Any(a => Name(a) == "ApiController")
            || type.BaseList?.Types.Any(t => SimpleName(t.Type) is "Controller" or "ControllerBase") == true
            || type.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal));

    private static void ExtractController(string path, ClassDeclarationSyntax type, List<ExtractedController> controllers, List<string> warnings)
    {
        var name = type.Identifier.ValueText;
        var routeAttribute = Attributes(type.AttributeLists).FirstOrDefault(a => Name(a) == "Route");
        string? prefix = null;
        if (routeAttribute is not null)
        {
            if (!TryTemplate(routeAttribute, out var classTemplate))
            {
                warnings.Add($"{path}: route of {name} is not a string literal; endpoints not extracted");
                return;
            }

            prefix = Clean(ReplaceTokens(classTemplate, name, action: null));
        }

        var endpoints = new List<ExtractedEndpoint>();
        var warningsBefore = warnings.Count;
        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>().Where(IsAction))
        {
            var attributes = Attributes(method.AttributeLists).ToList();
            var methodRoute = attributes.FirstOrDefault(a => Name(a) == "Route");
            foreach (var verb in attributes.Where(a => Verbs.ContainsKey(Name(a))))
            {
                var template = string.Empty;
                var literal = TemplateArgument(verb) is not null
                    ? TryTemplate(verb, out template)
                    : methodRoute is null || TryTemplate(methodRoute, out template);
                if (!literal)
                {
                    warnings.Add($"{path}: route of {name}.{method.Identifier.ValueText} is not a string literal; endpoint not extracted");
                    continue;
                }

                endpoints.Add(new ExtractedEndpoint(Verbs[Name(verb)], Combine(prefix, ReplaceTokens(template, name, method.Identifier.ValueText))));
            }
        }

        if (endpoints.Count > 0)
        {
            controllers.Add(new ExtractedController(name, path, prefix, endpoints));
        }
        else if (warnings.Count == warningsBefore)
        {
            warnings.Add($"{path}: {name} has no attribute-routed actions");
        }
    }

    private static List<ExtractedEndpoint> ExtractMinimalApis(string path, SyntaxNode root, List<string> warnings)
    {
        var endpoints = new List<ExtractedEndpoint>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax access
                || !MapVerbs.TryGetValue(access.Name.Identifier.ValueText, out var method)
                || call.ArgumentList.Arguments.Count == 0)
            {
                continue;
            }

            if (!TryLiteral(call.ArgumentList.Arguments[0].Expression, out var template)
                || !TryGroupPrefix(access.Expression, root, MaxGroupDepth, out var prefix))
            {
                var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                warnings.Add($"{path}: route of {access.Name.Identifier.ValueText} call at line {line} is not a string literal; endpoint not extracted");
                continue;
            }

            // In minimal APIs a leading "/" is normal and still sits under the group prefix.
            endpoints.Add(new ExtractedEndpoint(method, Clean($"{prefix}/{template}")));
        }

        return endpoints;
    }

    /// <summary>
    /// The MapGroup prefix of a route builder expression: MapGroup calls add their prefix, other calls
    /// (WithTags, RequireAuthorization, …) pass through, and a local variable is followed to its initializer.
    /// </summary>
    private static bool TryGroupPrefix(ExpressionSyntax builder, SyntaxNode root, int depth, out string prefix)
    {
        prefix = string.Empty;
        if (depth == 0)
        {
            return false;
        }

        switch (builder)
        {
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call:
                if (!TryGroupPrefix(access.Expression, root, depth - 1, out var outer))
                {
                    return false;
                }

                if (access.Name.Identifier.ValueText != "MapGroup")
                {
                    prefix = outer;
                    return true;
                }

                if (call.ArgumentList.Arguments.Count == 0 || !TryLiteral(call.ArgumentList.Arguments[0].Expression, out var group))
                {
                    return false;
                }

                prefix = Clean($"{outer}/{group}");
                return true;
            case IdentifierNameSyntax identifier:
                var declaration = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                    .FirstOrDefault(v => v.Identifier.ValueText == identifier.Identifier.ValueText && v.Initializer is not null);
                return declaration is null || TryGroupPrefix(declaration.Initializer!.Value, root, depth - 1, out prefix);
            default:
                return true;
        }
    }

    private static bool IsAction(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.PublicKeyword)
        && !method.Modifiers.Any(SyntaxKind.StaticKeyword)
        && !Attributes(method.AttributeLists).Any(a => Name(a) == "NonAction");

    /// <summary>An absolute template ("/x" or "~/x") replaces the controller prefix.</summary>
    private static string Combine(string? prefix, string template) =>
        template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal)
            ? Clean(template.TrimStart('~'))
            : Clean($"{prefix}/{template}");

    private static string Clean(string route)
    {
        while (route.Contains("//", StringComparison.Ordinal))
        {
            route = route.Replace("//", "/", StringComparison.Ordinal);
        }

        return route.Trim('/');
    }

    private static string ReplaceTokens(string template, string controller, string? action)
    {
        var area = controller.EndsWith("Controller", StringComparison.Ordinal) ? controller[..^"Controller".Length] : controller;
        var result = template.Replace("[controller]", area, StringComparison.OrdinalIgnoreCase);
        return action is null ? result : result.Replace("[action]", action, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The first positional argument (or <c>template:</c>), if any.</summary>
    private static AttributeArgumentSyntax? TemplateArgument(AttributeSyntax attribute) =>
        attribute.ArgumentList?.Arguments.FirstOrDefault(a =>
            a.NameEquals is null && (a.NameColon is null || a.NameColon.Name.Identifier.ValueText == "template"));

    /// <summary>False when the template exists but is not a string literal.</summary>
    private static bool TryTemplate(AttributeSyntax attribute, out string template)
    {
        template = string.Empty;
        var argument = TemplateArgument(attribute);
        if (argument is null)
        {
            return true;
        }

        return TryLiteral(argument.Expression, out template);
    }

    private static bool TryLiteral(ExpressionSyntax expression, out string value)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            value = literal.Token.ValueText;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static IEnumerable<AttributeSyntax> Attributes(SyntaxList<AttributeListSyntax> lists) => lists.SelectMany(l => l.Attributes);

    private static string Name(AttributeSyntax attribute) => SyntaxNames.Of(attribute);

    private static string SimpleName(TypeSyntax type) => SyntaxNames.Simple(type);
}
