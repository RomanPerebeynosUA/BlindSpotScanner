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

    /// <summary>Receivers that are routing roots: a parameter of these types, or the static WebApplication factory.</summary>
    private static readonly HashSet<string> RootBuilders = new(StringComparer.Ordinal) { "WebApplication", "IEndpointRouteBuilder" };

    public ExtractionResult Extract(string path, string source)
    {
        if (!path.EndsWith(".cs", StringComparison.Ordinal))
        {
            return new ExtractionResult([], [$"{path}: not a C# file; endpoints not extracted"]);
        }

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

    /// <summary>Minimal API registrations this extractor doesn't read; each one is reported.</summary>
    private static readonly HashSet<string> UnsupportedMaps = new(StringComparer.Ordinal) { "Map", "MapMethods", "MapFallback" };

    /// <summary>ASP.NET Core's rule: public, not abstract or static, not [NonController].</summary>
    private static bool IsController(ClassDeclarationSyntax type) =>
        type.Modifiers.Any(SyntaxKind.PublicKeyword)
        && !type.Modifiers.Any(SyntaxKind.AbstractKeyword)
        && !type.Modifiers.Any(SyntaxKind.StaticKeyword)
        && !Attributes(type.AttributeLists).Any(a => Name(a) == "NonController")
        && (Attributes(type.AttributeLists).Any(a => Name(a) == "ApiController")
            || type.BaseList?.Types.Any(t => SimpleName(t.Type) is "Controller" or "ControllerBase") == true
            || type.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal));

    private static void ExtractController(string path, ClassDeclarationSyntax type, List<ExtractedController> controllers, List<string> warnings)
    {
        var name = type.Identifier.ValueText;
        var areaAttribute = Attributes(type.AttributeLists).FirstOrDefault(a => Name(a) == "Area");
        string? area = null;
        if (areaAttribute is not null && !TryTemplate(areaAttribute, out area))
        {
            warnings.Add($"{path}: area of {name} is not a string literal; endpoints not extracted");
            return;
        }

        var prefixes = new List<string?>();
        foreach (var route in Attributes(type.AttributeLists).Where(a => Name(a) == "Route"))
        {
            if (!TryTemplate(route, out var classTemplate))
            {
                warnings.Add($"{path}: route of {name} is not a string literal; endpoints not extracted");
                return;
            }

            if (area is null && classTemplate.Contains("[area]", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"{path}: {name} uses [area] without an [Area] attribute; endpoints not extracted");
                return;
            }

            prefixes.Add(Clean(ReplaceTokens(classTemplate, name, action: null, area)));
        }

        if (prefixes.Count == 0)
        {
            prefixes.Add(null);
        }

        if (type.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            warnings.Add($"{path}: {name} is partial; a [Route] or actions in its other files are not read");
        }

        if (type.BaseList?.Types.FirstOrDefault() is { } first
            && SimpleName(first.Type) is var baseName and not ("Controller" or "ControllerBase")
            && !IsInterfaceName(baseName))
        {
            warnings.Add($"{path}: {name} derives from {baseName}; an inherited [Route] or inherited actions are not read");
        }

        var endpoints = new List<ExtractedEndpoint>();
        var warningsBefore = warnings.Count;
        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>().Where(IsAction))
        {
            ExtractAction(path, name, area, method, prefixes, endpoints, warnings);
        }

        if (endpoints.Count > 0)
        {
            controllers.Add(new ExtractedController(name, path, prefixes[0], endpoints));
        }
        else if (warnings.Count == warningsBefore)
        {
            warnings.Add($"{path}: {name} has no attribute-routed actions");
        }
    }

    /// <summary>One endpoint per class route × verb template (a verb without a template takes each method [Route]).</summary>
    private static void ExtractAction(string path, string controller, string? area, MethodDeclarationSyntax method, List<string?> prefixes, List<ExtractedEndpoint> endpoints, List<string> warnings)
    {
        var action = method.Identifier.ValueText;
        var attributes = Attributes(method.AttributeLists).ToList();
        var actionName = ActionName(method, attributes);
        var verbs = attributes.Where(a => Verbs.ContainsKey(Name(a))).ToList();
        var methodRoutes = attributes.Where(a => Name(a) == "Route").ToList();
        if (verbs.Count == 0)
        {
            if (methodRoutes.Count > 0 || attributes.Any(a => Name(a) == "AcceptVerbs"))
            {
                warnings.Add($"{path}: {controller}.{action} is routed without an Http* verb attribute; endpoint not extracted");
            }

            return;
        }

        foreach (var verb in verbs)
        {
            var sources = TemplateArgument(verb) is not null || methodRoutes.Count == 0 ? [verb] : methodRoutes;
            var templates = new List<string>();
            foreach (var source in sources)
            {
                if (!TryTemplate(source, out var template))
                {
                    warnings.Add($"{path}: route of {controller}.{action} is not a string literal; endpoint not extracted");
                    return;
                }

                templates.Add(ReplaceTokens(template, controller, actionName, area));
            }

            endpoints.AddRange(
                from prefix in prefixes
                from template in templates
                select new ExtractedEndpoint(Verbs[Name(verb)], Combine(prefix, template).Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>The [action] value: [ActionName], else the method name without ASP.NET's trimmed "Async" suffix.</summary>
    private static string ActionName(MethodDeclarationSyntax method, List<AttributeSyntax> attributes)
    {
        if (attributes.FirstOrDefault(a => Name(a) == "ActionName") is { } attribute && TryTemplate(attribute, out var named) && named.Length > 0)
        {
            return named;
        }

        var name = method.Identifier.ValueText;
        return name.Length > "Async".Length && name.EndsWith("Async", StringComparison.Ordinal) ? name[..^"Async".Length] : name;
    }

    private static bool IsInterfaceName(string name) => name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);

    private static List<ExtractedEndpoint> ExtractMinimalApis(string path, SyntaxNode root, List<string> warnings)
    {
        var endpoints = new List<ExtractedEndpoint>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax access)
            {
                continue;
            }

            var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var callName = access.Name.Identifier.ValueText;
            if (UnsupportedMaps.Contains(callName)
                && (callName == "MapFallback" || call.ArgumentList.Arguments.FirstOrDefault() is { } first && TryLiteral(first.Expression, out _)))
            {
                warnings.Add($"{path}: {callName} call at line {line} is not supported; endpoint not extracted");
                continue;
            }

            if (!MapVerbs.TryGetValue(callName, out var method) || call.ArgumentList.Arguments.Count == 0)
            {
                continue;
            }

            if (!TryLiteral(call.ArgumentList.Arguments[0].Expression, out var template))
            {
                warnings.Add($"{path}: route of {access.Name.Identifier.ValueText} call at line {line} is not a string literal; endpoint not extracted");
                continue;
            }

            if (!TryGroupPrefix(access.Expression, MaxGroupDepth, out var prefix))
            {
                warnings.Add($"{path}: group prefix of {access.Name.Identifier.ValueText} call at line {line} cannot be resolved; endpoint not extracted");
                continue;
            }

            // In minimal APIs a leading "/" is normal and still sits under the group prefix.
            endpoints.Add(new ExtractedEndpoint(method, Clean($"{prefix}/{template}")));
        }

        return endpoints;
    }

    /// <summary>
    /// The MapGroup prefix of a route builder expression: MapGroup calls add their prefix, other calls
    /// (WithTags, RequireAuthorization, …) pass through, and a local variable is followed to its nearest
    /// earlier declaration in scope. A routing root (a WebApplication or IEndpointRouteBuilder parameter,
    /// or the static WebApplication factory) has no prefix. Anything else (a RouteGroupBuilder
    /// parameter, a field) gets its prefix from code this file doesn't show, so it can't be resolved.
    /// </summary>
    private static bool TryGroupPrefix(ExpressionSyntax builder, int depth, out string prefix)
    {
        prefix = string.Empty;
        if (depth == 0)
        {
            return false;
        }

        switch (builder)
        {
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call:
                if (!TryGroupPrefix(access.Expression, depth - 1, out var outer))
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
                if (NearestValue(identifier) is { } value)
                {
                    return TryGroupPrefix(value, depth - 1, out prefix);
                }

                var name = identifier.Identifier.ValueText;
                var parameter = identifier.Ancestors()
                    .SelectMany(a => a.ChildNodes().OfType<ParameterListSyntax>())
                    .SelectMany(list => list.Parameters)
                    .FirstOrDefault(p => p.Identifier.ValueText == name);
                return parameter is null
                    ? name == "WebApplication"
                    : parameter.Type is { } parameterType && RootBuilders.Contains(SimpleName(parameterType));
            default:
                return false;
        }
    }

    /// <summary>
    /// The value most recently given to this local before it: its initializer or a later plain
    /// assignment, from a statement whose scope contains the use.
    /// </summary>
    private static ExpressionSyntax? NearestValue(IdentifierNameSyntax identifier)
    {
        var name = identifier.Identifier.ValueText;
        var ancestors = identifier.Ancestors().ToHashSet();
        bool InScope(SyntaxNode? statement) =>
            statement is not null
            && (statement.Parent is GlobalStatementSyntax global ? global.Parent : statement.Parent) is { } scope
            && ancestors.Contains(scope);

        var nodes = identifier.SyntaxTree.GetRoot().DescendantNodes().Where(n => n.SpanStart < identifier.SpanStart).ToList();
        var initializers = nodes.OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Identifier.ValueText == name && v.Initializer is not null && v.Parent?.Parent is LocalDeclarationStatementSyntax && InScope(v.Parent.Parent))
            .Select(v => (v.SpanStart, Value: v.Initializer!.Value));
        var assignments = nodes.OfType<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && a.Left is IdentifierNameSyntax left && left.Identifier.ValueText == name
                && a.Parent is ExpressionStatementSyntax && InScope(a.Parent))
            .Select(a => (a.SpanStart, Value: a.Right));
        return initializers.Concat(assignments).OrderByDescending(c => c.SpanStart).Select(c => c.Value).FirstOrDefault();
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

    private static string ReplaceTokens(string template, string controller, string? action, string? area)
    {
        var name = controller.EndsWith("Controller", StringComparison.Ordinal) ? controller[..^"Controller".Length] : controller;
        var result = template.Replace("[controller]", name, StringComparison.OrdinalIgnoreCase);
        result = area is null ? result : result.Replace("[area]", area, StringComparison.OrdinalIgnoreCase);
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
