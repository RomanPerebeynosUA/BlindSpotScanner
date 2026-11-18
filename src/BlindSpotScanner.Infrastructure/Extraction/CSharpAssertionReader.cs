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

    private static readonly HashSet<string> JsonWrites = new(StringComparer.Ordinal) { "PostAsJsonAsync", "PutAsJsonAsync", "PatchAsJsonAsync" };

    private static readonly HashSet<string> ContentWrites = new(StringComparer.Ordinal) { "PostAsync", "PutAsync", "PatchAsync" };

    private static readonly HashSet<string> NotFields = new(StringComparer.Ordinal) { "Count", "Length" };

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

        var (sent, payloads, resolved) = SentFields(scope, locals);
        var checkedFields = new List<string>();
        var levels = new List<(int Level, ReadBack? ReadBack)>();
        var errorPath = false;
        foreach (var call in scope.SelectMany(s => s.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            if (Assertion(call) is { } assertion)
            {
                var classified = Classify(assertion, locals, firstWrite);
                levels.Add(classified);
                errorPath |= classified.Level == 1 && IsClientError(call, assertion.Matcher);
                if (classified.Level != 1)
                {
                    checkedFields.AddRange(CheckedNames(assertion, call, payloads));
                }
            }
            else if (IsUnresolvedHelper(call, helpers))
            {
                unresolved = true;
            }
        }

        int? level = unresolved ? null : levels.Count == 0 ? 0 : levels.Max(l => l.Level);
        var readBacks = levels.Where(l => l.ReadBack is not null).Select(l => l.ReadBack!.Value).ToList();
        ReadBack? readBack = level is null || readBacks.Count == 0 ? null : readBacks.Contains(ReadBack.Db) ? ReadBack.Db : ReadBack.Api;
        return resolved
            ? new TestAssertionFacts(name, level, readBack, errorPath, sent, checkedFields.Distinct(StringComparer.Ordinal).ToList(), FieldsResolved: true)
            : new TestAssertionFacts(name, level, readBack, errorPath, [], [], FieldsResolved: false);
    }

    /// <summary>
    /// The leaf paths of every write's body, and each body local's fields (for equivalence checks).
    /// A body the reader can't follow (a builder, a field, a method result) makes the fields unresolved.
    /// </summary>
    private static (List<string> Sent, Dictionary<string, List<string>> Payloads, bool Resolved) SentFields(
        IReadOnlyList<SyntaxNode> scope, IReadOnlyDictionary<string, ExpressionSyntax> locals)
    {
        var sent = new List<string>();
        var payloads = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var call in scope.SelectMany(s => s.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            var name = Name(call);
            var args = call.ArgumentList.Arguments;
            ExpressionSyntax? body = (JsonWrites.Contains(name) || ContentWrites.Contains(name)) && args.Count > 1 ? Unwrap(args[1].Expression) : null;
            if (body is null)
            {
                continue;
            }

            if (ContentWrites.Contains(name))
            {
                body = body is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "JsonContent" }, Name.Identifier.ValueText: "Create" } } create
                    && create.ArgumentList.Arguments.Count > 0
                    ? Unwrap(create.ArgumentList.Arguments[0].Expression)
                    : null;
            }

            var local = body is IdentifierNameSyntax id ? id.Identifier.ValueText : null;
            if (local is not null)
            {
                body = locals.TryGetValue(local, out var init) ? Unwrap(init) : null;
            }

            if (body is null || !IsObject(body))
            {
                return ([], payloads, false);
            }

            var fields = new List<string>();
            CollectLeaves(body, prefix: string.Empty, fields);
            if (local is not null)
            {
                payloads[local] = fields;
            }

            sent.AddRange(fields);
        }

        return (sent.Distinct(StringComparer.Ordinal).ToList(), payloads, true);
    }

    private static bool IsObject(ExpressionSyntax expression) =>
        expression is AnonymousObjectCreationExpressionSyntax
        || (expression is BaseObjectCreationExpressionSyntax { Initializer: { } init } && init.IsKind(SyntaxKind.ObjectInitializerExpression));

    private static void CollectLeaves(ExpressionSyntax value, string prefix, List<string> leaves)
    {
        foreach (var (name, member) in Members(value))
        {
            var path = prefix + name;
            var inner = Unwrap(member);
            if (IsObject(inner))
            {
                CollectLeaves(inner, path + ".", leaves);
            }
            else if (Elements(inner) is { } elements && elements.Any(IsObject))
            {
                foreach (var element in elements.Where(IsObject))
                {
                    CollectLeaves(element, path + "[].", leaves);
                }
            }
            else if (!leaves.Contains(path))
            {
                leaves.Add(path);
            }
        }
    }

    private static IEnumerable<(string Name, ExpressionSyntax Value)> Members(ExpressionSyntax value) => value switch
    {
        AnonymousObjectCreationExpressionSyntax anonymous => anonymous.Initializers.Select(m => (m.NameEquals?.Name.Identifier.ValueText ?? InferredName(m.Expression), m.Expression)),
        BaseObjectCreationExpressionSyntax { Initializer: { } init } => init.Expressions.OfType<AssignmentExpressionSyntax>()
            .Where(a => a.Left is IdentifierNameSyntax)
            .Select(a => (((IdentifierNameSyntax)a.Left).Identifier.ValueText, a.Right)),
        _ => [],
    };

    private static string InferredName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => Tokens(expression),
    };

    private static List<ExpressionSyntax>? Elements(ExpressionSyntax value) => value switch
    {
        ImplicitArrayCreationExpressionSyntax array => [.. array.Initializer.Expressions],
        ArrayCreationExpressionSyntax { Initializer: { } init } => [.. init.Expressions],
        CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression).ToList(),
        BaseObjectCreationExpressionSyntax { Initializer: { } init } when init.IsKind(SyntaxKind.CollectionInitializerExpression) => [.. init.Expressions],
        _ => null,
    };

    /// <summary>
    /// The members read along the actual's access chain (<c>order!.Lines.Count</c> → Lines), plus the
    /// members of an anonymous expectation, plus every field of a body local compared by equivalence.
    /// </summary>
    private static IEnumerable<string> CheckedNames(AssertionCall assertion, InvocationExpressionSyntax call, IReadOnlyDictionary<string, List<string>> payloads)
    {
        var names = new List<string>();
        var node = assertion.Actual is null ? null : Unwrap(assertion.Actual);
        while (node is not null)
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax member:
                    names.Add(member.Name.Identifier.ValueText);
                    node = Unwrap(member.Expression);
                    break;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax method }:
                    node = Unwrap(method.Expression);
                    break;
                case ElementAccessExpressionSyntax element:
                    node = Unwrap(element.Expression);
                    break;
                default:
                    node = null;
                    break;
            }
        }

        names.Reverse();
        foreach (var argument in call.ArgumentList.Arguments.Select(a => Unwrap(a.Expression)))
        {
            if (argument is AnonymousObjectCreationExpressionSyntax expectation)
            {
                names.AddRange(Members(expectation).Select(m => m.Name));
            }
            else if (argument is IdentifierNameSyntax id && payloads.TryGetValue(id.Identifier.ValueText, out var fields))
            {
                names.AddRange(fields);
            }
        }

        return names.Where(n => !NotFields.Contains(n));
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
