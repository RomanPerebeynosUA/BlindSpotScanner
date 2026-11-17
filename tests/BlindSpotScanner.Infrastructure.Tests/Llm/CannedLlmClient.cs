using System.Text.Json;
using BlindSpotScanner.Application.Llm;

namespace BlindSpotScanner.Infrastructure.Tests.Llm;

/// <summary>
/// Canned LLM answers for the bookstore fixture (mirroring examples/sample-report.json): P4 calls
/// PriceTests a unit test; P5 grades Books and Orders; P6 scores every dimension and returns one
/// false positive and one risk. Each answer still goes through the request's rule check.
/// </summary>
/// <param name="forgeCitation">P6 cites a test that does not exist (the M6 citation-verifier scenario).</param>
internal sealed class CannedLlmClient(bool forgeCitation = false) : ILlmClient
{
    private const string Books = "tests/Bookstore.IntegrationTests/BooksApiTests.cs";
    private const string Orders = "tests/Bookstore.IntegrationTests/OrdersApiTests.cs";

    public static readonly LlmUsage CallUsage = new(1000, 200, 0.004m);

    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var json = request.Prompt switch
        {
            Prompts.ClassifyTests => Classify(request.InputJson),
            Prompts.MapCoverage => Map(request.InputJson),
            Prompts.GradeQuality => forgeCitation ? Grade.Replace("Create_persists_book", "Deletes_book", StringComparison.Ordinal) : Grade,
            _ => throw new InvalidOperationException($"No canned answer for {request.Prompt}."),
        };

        var output = JsonDocument.Parse(json).RootElement.Clone();
        var errors = request.Validate?.Invoke(output) ?? [];
        return Task.FromResult(errors.Count == 0
            ? new LlmResult(output, [], CallUsage, FromCache: false)
            : new LlmResult(null, errors, CallUsage, FromCache: false));
    }

    private static string Classify(string input)
    {
        var path = JsonDocument.Parse(input).RootElement.GetProperty("files")[0].GetProperty("path").GetString();
        return $$"""{"files":[{"path":"{{path}}","kind":"unit","confidence":"High","reason":"pure arithmetic, no host or database","evidenceKind":"observed"}]}""";
    }

    private static string Map(string input) => JsonDocument.Parse(input).RootElement.GetProperty("controller").GetString() switch
    {
        "BooksController" => $$"""
            {"endpoints":[
              {"id":"e1","status":"covered","evidence":["{{Books}}::List_returns_seeded_books"],"falsePositive":false,"notes":"Asserts the seeded count."},
              {"id":"e2","status":"weak","evidence":["{{Books}}::Get_by_id_returns_ok"],"falsePositive":true,"notes":"Asserts only 200 OK."},
              {"id":"e3","status":"covered","evidence":["{{Books}}::Create_persists_book"],"falsePositive":false,"notes":"Reads the created book back."}]}
            """,
        "OrdersController" => $$"""
            {"endpoints":[
              {"id":"e1","status":"covered","evidence":["{{Orders}}::Place_order_persists_lines_and_total"],"falsePositive":false,"notes":"Asserts lines and total."},
              {"id":"e2","status":"none","evidence":[],"falsePositive":false,"notes":"No test calls cancel."}]}
            """,
        var other => throw new InvalidOperationException($"No canned mapping for {other}."),
    };

    private static string Dimension(int id) =>
        $$"""{"id":{{id}},"score":{{id % 4 + 4}},"scorable":true,"confidence":"Med","justificationBullets":["What is tested: the main book and order flows","What is missing: failure paths","Evidence: {{Books}}::Create_persists_book","Why this score: 5-6 band"],"evidence":["{{Books}}::Create_persists_book"]}""";

    private static readonly string Grade = $$"""
        {"scorecard":[{{string.Join(',', Enumerable.Range(1, 7).Select(Dimension))}}],
         "findings":[{"kind":"FalsePositive","severity":"Medium","subject":"{{Books}}::Get_by_id_returns_ok","headline":"A book lookup test only checks that the request succeeded","detail":["Asserts 200 OK only."],"evidence":["{{Books}}::Get_by_id_returns_ok"]}],
         "topRisks":[{"headline":"Some tests pass even when the wrong data comes back","whyItMatters":"They give a false sense of safety for the book details page.","severity":"Medium","detail":["Get_by_id_returns_ok asserts only the HTTP status."],"evidence":["{{Books}}::Get_by_id_returns_ok"]}]}
        """;
}
