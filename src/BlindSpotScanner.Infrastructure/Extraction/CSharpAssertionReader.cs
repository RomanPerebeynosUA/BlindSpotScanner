using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlindSpotScanner.Infrastructure.Extraction;

/// <summary>
/// D27: the assertion level (L0–L5) of every test method in a C# file, from syntax only. Helpers are
/// followed one level within the same class; a call to an assertion-like helper the reader can't see
/// makes the test unknown instead of guessing (principle 5).
/// </summary>
public sealed class CSharpAssertionReader : ITestAssertionReader
{
    private static readonly HashSet<string> AssertClasses = new(StringComparer.Ordinal) { "Assert", "ClassicAssert", "CollectionAssert", "StringAssert" };

    /// <summary>Classic assertions whose actual value is the second argument.</summary>
    private static readonly HashSet<string> ActualSecond = new(StringComparer.Ordinal)
    {
        "Equal", "NotEqual", "StrictEqual", "Same", "NotSame", "Contains", "DoesNotContain", "Matches", "AreEqual", "AreNotEqual", "AreSame",
    };

    /// <summary>Classic wrappers and outcomes that check nothing themselves (their lambdas are searched like any other code).</summary>
    private static readonly HashSet<string> NotChecks = new(StringComparer.Ordinal) { "Multiple", "All", "Collection", "Fail", "Pass", "Ignore", "Inconclusive" };

    private static readonly HashSet<string> HttpCalls = new(StringComparer.Ordinal)
    {
        "GetAsync", "PostAsync", "PutAsync", "PatchAsync", "DeleteAsync", "SendAsync", "PostAsJsonAsync", "PutAsJsonAsync", "PatchAsJsonAsync",
    };

    internal static readonly HashSet<string> Writes = new(StringComparer.Ordinal)
    {
        "PostAsync", "PostAsJsonAsync", "PutAsync", "PutAsJsonAsync", "PatchAsync", "PatchAsJsonAsync", "DeleteAsync",
    };

    private static readonly HashSet<string> Reads = new(StringComparer.Ordinal) { "GetAsync", "GetFromJsonAsync", "GetStringAsync" };

    private static readonly HashSet<string> ShapeMatchers = new(StringComparer.Ordinal)
    {
        "NotNull", "Null", "Empty", "NotEmpty", "Single", "IsType", "IsNotType", "IsAssignableFrom", "Throws", "ThrowsAsync",
        "IsNotNull", "IsNull", "IsEmpty", "IsNotEmpty", "NotBeNull", "BeNull", "BeEmpty", "NotBeEmpty", "ContainSingle", "HaveCount",
        "BeOfType", "BeAssignableTo", "Throw", "ThrowAsync", "ShouldNotBeNull", "ShouldBeNull", "ShouldBeEmpty", "ShouldNotBeEmpty", "ShouldBeOfType",
    };

    private static readonly string[] ShapeConstraints = ["Is.Null", "Is.Not.Null", "Is.Empty", "Is.Not.Empty", "Has.Count", "Has.Length"];

    private static readonly string[] ShapeSuffixes = [".Count", ".Length", ".Count()", ".Any()", ".LongCount()", ".CountAsync()", ".AnyAsync()"];

    private static readonly HashSet<string> SideEffectMatchers = new(StringComparer.Ordinal) { "HaveReceivedACall", "HaveReceivedNoCalls" };

    private static readonly HashSet<string> ClientErrors = new(StringComparer.Ordinal)
    {
        "BadRequest", "Unauthorized", "Forbidden", "NotFound", "MethodNotAllowed", "Conflict", "Gone", "PreconditionFailed",
        "UnsupportedMediaType", "UnprocessableEntity", "UnprocessableContent", "TooManyRequests",
    };

    private static readonly HashSet<string> ClientErrorMatchers = new(StringComparer.Ordinal) { "BeBadRequest", "BeNotFound", "BeUnauthorized", "BeForbidden", "BeConflict", "HaveClientError" };

    private static readonly string[] HelperPrefixes = ["Assert", "Verify", "Expect", "Check", "Should"];

    public IReadOnlyList<TestAssertionFacts> Read(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var facts = new List<TestAssertionFacts>();
        foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var methods = type.Members.OfType<MethodDeclarationSyntax>().ToList();
            foreach (var test in methods.Where(SyntaxNames.IsTestMethod))
            {
                facts.Add(Analyze(test, methods, $"{SyntaxNames.QualifiedName(type)}.{test.Identifier.ValueText}"));
            }
        }

        return facts;
    }

    private static TestAssertionFacts Analyze(MethodDeclarationSyntax test, IReadOnlyList<MethodDeclarationSyntax> classMethods, string name)
    {
        var helpers = classMethods.Where(m => m != test).GroupBy(m => m.Identifier.ValueText).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var scope = new List<SyntaxNode> { test };
        var unresolved = false;
        foreach (var call in test.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (LocalCallName(call) is { } local && helpers.TryGetValue(local, out var helper))
            {
                scope.Add(helper);
            }
        }

        var firstWrite = test.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c => Writes.Contains(Name(c))).Select(c => (int?)c.SpanStart).Min();
        var locals = scope.SelectMany(s => s.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            .Where(v => v.Initializer is not null)
            .GroupBy(v => v.Identifier.ValueText)
            .ToDictionary(g => g.Key, g => g.First().Initializer!.Value, StringComparer.Ordinal);

        var levels = new List<(int Level, ReadBack? ReadBack)>();
        var errorPath = false;
        foreach (var call in scope.SelectMany(s => s.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            if (Assertion(call) is { } assertion)
            {
                var classified = Classify(assertion, locals, firstWrite);
                levels.Add(classified);
                errorPath |= classified.Level == 1 && IsClientError(call, assertion.Matcher);
            }
            else if (IsUnresolvedHelper(call, helpers))
            {
                unresolved = true;
            }
        }

        int? level = unresolved ? null : levels.Count == 0 ? 0 : levels.Max(l => l.Level);
        var readBacks = levels.Where(l => l.ReadBack is not null).Select(l => l.ReadBack!.Value).ToList();
        ReadBack? readBack = level is null || readBacks.Count == 0 ? null : readBacks.Contains(ReadBack.Db) ? ReadBack.Db : ReadBack.Api;
        return new TestAssertionFacts(name, level, readBack, errorPath, [], [], FieldsResolved: true);
    }

    /// <param name="Actual">The value under test (null for EnsureSuccessStatusCode, which checks its receiver's status).</param>
    /// <param name="Constraint">NUnit's constraint argument of Assert.That.</param>
    private sealed record AssertionCall(ExpressionSyntax? Actual, string Matcher, string? Constraint, bool Fluent, bool Status);

    private static AssertionCall? Assertion(InvocationExpressionSyntax call)
    {
        if (call.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var name = member.Name.Identifier.ValueText;
        var args = call.ArgumentList.Arguments;
        if (member.Expression is IdentifierNameSyntax { Identifier.ValueText: var type } && AssertClasses.Contains(type))
        {
            if (NotChecks.Contains(name) || args.Count == 0)
            {
                return null;
            }

            var actual = name != "That" && ActualSecond.Contains(name) && args.Count > 1 ? args[1].Expression : args[0].Expression;
            var constraint = name == "That" && args.Count > 1 ? Tokens(args[1].Expression) : null;
            return new AssertionCall(actual, name, constraint, Fluent: false, Status: false);
        }

        if (member.Expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Should" } should, ArgumentList.Arguments.Count: 0 })
        {
            return new AssertionCall(should.Expression, name, null, Fluent: true, Status: false);
        }

        if (name.StartsWith("Should", StringComparison.Ordinal) && name != "Should")
        {
            return new AssertionCall(member.Expression, name, null, Fluent: false, Status: false);
        }

        return name == "EnsureSuccessStatusCode" ? new AssertionCall(null, name, null, Fluent: false, Status: true) : null;
    }

    private static (int Level, ReadBack? ReadBack) Classify(AssertionCall assertion, IReadOnlyDictionary<string, ExpressionSyntax> locals, int? firstWrite)
    {
        if (assertion.Status || assertion.Actual is null)
        {
            return (1, null);
        }

        var actual = Unwrap(assertion.Actual);
        var text = Tokens(actual);
        if (text.Contains("StatusCode", StringComparison.Ordinal) || text.Contains("IsSuccessStatusCode", StringComparison.Ordinal)
            || (assertion.Fluent && actual is IdentifierNameSyntax id && locals.TryGetValue(id.Identifier.ValueText, out var init) && Unwrap(init) is InvocationExpressionSyntax http && HttpCalls.Contains(Name(http))))
        {
            return (1, null);
        }

        if (text.Contains("LogEntries", StringComparison.Ordinal) || SideEffectMatchers.Contains(assertion.Matcher))
        {
            return (5, null);
        }

        if (ShapeMatchers.Contains(assertion.Matcher)
            || (assertion.Constraint is { } constraint && ShapeConstraints.Any(c => constraint.StartsWith(c, StringComparison.Ordinal)))
            || ShapeSuffixes.Any(s => text.EndsWith(s, StringComparison.Ordinal)))
        {
            return (2, null);
        }

        // The actual and the initializers of the locals it names (one level).
        var sources = new List<ExpressionSyntax> { actual };
        sources.AddRange(actual.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Select(n => locals.GetValueOrDefault(n.Identifier.ValueText))
            .OfType<ExpressionSyntax>());
        var nodes = sources.SelectMany(s => s.DescendantNodesAndSelf()).ToList();
        if (nodes.OfType<MemberAccessExpressionSyntax>().Any(IsDatabaseAccess) || nodes.OfType<GenericNameSyntax>().Any(g => g.Identifier.ValueText == "Set"))
        {
            return (4, ReadBack.Db);
        }

        if (firstWrite is { } write && nodes.OfType<InvocationExpressionSyntax>().Any(c => Reads.Contains(Name(c)) && c.SpanStart > write))
        {
            return (4, ReadBack.Api);
        }

        return (3, null);
    }

    private static bool IsDatabaseAccess(MemberAccessExpressionSyntax member) =>
        member.Expression is IdentifierNameSyntax { Identifier.ValueText: var name }
        && (name.EndsWith("db", StringComparison.OrdinalIgnoreCase) || name.EndsWith("context", StringComparison.OrdinalIgnoreCase));

    private static bool IsClientError(InvocationExpressionSyntax call, string matcher) =>
        ClientErrorMatchers.Contains(matcher)
        || call.ArgumentList.Arguments.Any(a =>
            (a.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "HttpStatusCode" } } code && ClientErrors.Contains(code.Name.Identifier.ValueText))
            || (a.Expression is LiteralExpressionSyntax literal && literal.Token.Value is int status && status is >= 400 and <= 499));

    private static bool IsUnresolvedHelper(InvocationExpressionSyntax call, IReadOnlyDictionary<string, MethodDeclarationSyntax> helpers)
    {
        var name = Name(call);
        if (name == "Should" || !HelperPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
        {
            return false;
        }

        return LocalCallName(call) is not { } local || !helpers.ContainsKey(local);
    }

    /// <summary>The name of a call to a method of the same class (<c>Foo()</c> or <c>this.Foo()</c>).</summary>
    private static string? LocalCallName(InvocationExpressionSyntax call) => call.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } member => member.Name.Identifier.ValueText,
        _ => null,
    };

    internal static string Name(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => string.Empty,
    };

    /// <summary>Strips parentheses, <c>await</c> and the null-forgiving <c>!</c>.</summary>
    internal static ExpressionSyntax Unwrap(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax p => Unwrap(p.Expression),
        AwaitExpressionSyntax a => Unwrap(a.Expression),
        PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } s => Unwrap(s.Operand),
        _ => expression,
    };

    private static string Tokens(SyntaxNode node) => string.Concat(node.DescendantTokens().Select(t => t.Text));
}
