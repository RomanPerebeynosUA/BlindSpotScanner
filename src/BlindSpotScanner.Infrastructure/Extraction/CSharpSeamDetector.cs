using System.Text.RegularExpressions;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlindSpotScanner.Infrastructure.Extraction;

/// <summary>
/// D34: the seams a C# startup file registers, read from syntax (comments are trivia, so a
/// commented-out registration never counts). A seam's name is a kebab-case key: the registered
/// type without its <c>Context</c>/<c>Client</c> suffix, a named client's name, or the technology.
/// </summary>
internal static partial class CSharpSeamDetector
{
    private static readonly Dictionary<string, SeamKind> DbContextRegistrations = new(StringComparer.Ordinal)
    {
        ["AddDbContext"] = SeamKind.Database,
        ["AddDbContextPool"] = SeamKind.Database,
        ["AddDbContextFactory"] = SeamKind.Database,
    };

    /// <summary>Registrations whose seam is the technology itself.</summary>
    private static readonly Dictionary<string, (SeamKind Kind, string Name)> FixedInvocations = new(StringComparer.Ordinal)
    {
        ["AddMassTransit"] = (SeamKind.MessageBroker, "masstransit"),
        ["AddStackExchangeRedisCache"] = (SeamKind.Cache, "redis"),
    };

    private static readonly Dictionary<string, (SeamKind Kind, string Name)> FixedCreations = new(StringComparer.Ordinal)
    {
        ["ConnectionFactory"] = (SeamKind.MessageBroker, "rabbitmq"),
        ["ProducerBuilder"] = (SeamKind.MessageBroker, "kafka"),
        ["ConsumerBuilder"] = (SeamKind.MessageBroker, "kafka"),
        ["ServiceBusClient"] = (SeamKind.MessageBroker, "azure-service-bus"),
        ["BlobServiceClient"] = (SeamKind.BlobStorage, "azure-blob-storage"),
        ["AmazonS3Client"] = (SeamKind.BlobStorage, "s3"),
    };

    public static IReadOnlyList<ProductionSeam> Detect(string path, string source)
    {
        if (!path.EndsWith(".cs", StringComparison.Ordinal))
        {
            return [];
        }

        var root = CSharpSyntaxTree.ParseText(source, path: path).GetRoot();
        var found = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(FromInvocation)
            .Concat(root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Select(c => FromCreation(c.Type)))
            .OfType<(SeamKind Kind, string Name)>();

        return found.Distinct()
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Kind)
            .Select(s => new ProductionSeam(s.Kind, s.Name, path))
            .ToList();
    }

    private static (SeamKind Kind, string Name)? FromInvocation(InvocationExpressionSyntax invocation)
    {
        var (receiver, name) = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => (member.Expression.ToString(), member.Name),
            SimpleNameSyntax simple => (string.Empty, simple),
            _ => (string.Empty, (SimpleNameSyntax?)null),
        };

        if (name is null)
        {
            return null;
        }

        var method = name.Identifier.ValueText;
        var typeArguments = name is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments : default;

        if (DbContextRegistrations.TryGetValue(method, out var kind) && typeArguments.Count > 0)
        {
            return (kind, Kebab(TrimSuffix(SimpleTypeName(typeArguments[0]), "Context")));
        }

        if (method == "AddHttpClient")
        {
            if (typeArguments.Count > 0)
            {
                return (SeamKind.OutboundHttp, Kebab(TrimSuffix(SimpleTypeName(typeArguments[^1]), "Client")));
            }

            return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
                ? (SeamKind.OutboundHttp, Kebab(literal.Token.ValueText))
                : null;
        }

        if (method == "AddAWSService" && typeArguments.Count > 0 && SimpleTypeName(typeArguments[0]) == "IAmazonS3")
        {
            return (SeamKind.BlobStorage, "s3");
        }

        if (method is "Connect" or "ConnectAsync" && receiver == "ConnectionMultiplexer")
        {
            return (SeamKind.Cache, "redis");
        }

        return FixedInvocations.TryGetValue(method, out var seam) ? seam : null;
    }

    private static (SeamKind Kind, string Name)? FromCreation(TypeSyntax type) =>
        FixedCreations.TryGetValue(SimpleTypeName(type), out var seam) ? seam : null;

    /// <summary>The rightmost identifier: <c>Ns.Foo&lt;T&gt;</c> → <c>Foo</c>.</summary>
    private static string SimpleTypeName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        _ => type.ToString(),
    };

    private static string TrimSuffix(string name, string suffix) =>
        name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;

    /// <summary><c>BookstoreDb</c> → <c>bookstore-db</c>; acronym runs stay together (<c>HTTPGateway</c> → <c>http-gateway</c>).</summary>
    private static string Kebab(string name) =>
        string.Join('-', Words().Matches(name).Select(m => m.Value.ToLowerInvariant()));

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
    private static partial Regex Words();
}
