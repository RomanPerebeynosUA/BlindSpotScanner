using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>
/// Claude's Messages API, served by WireMock with canned answers for the bookstore fixture (the same
/// answers as the M5 canned client). The answer is picked by text in the request body: the task
/// prompt's title, the controller name, and the marker <see cref="ForgeMarker"/>, which a test puts in
/// its profile Markdown to make grading cite a test that does not exist.
/// </summary>
public sealed class FakeClaudeServer : IDisposable
{
    public const string ForgeMarker = "FORGE-CITATION";

    /// <summary>A profile marker that delays P4b's answer, so a test can cancel the scan while it runs.</summary>
    public const string SlowMarker = "SLOW-SCAN";

    public static readonly TimeSpan SlowDelay = TimeSpan.FromSeconds(4);

    private const string Books = "tests/Bookstore.IntegrationTests/BooksApiTests.cs";
    private const string Orders = "tests/Bookstore.IntegrationTests/OrdersApiTests.cs";

    private readonly WireMockServer _server = WireMockServer.Start();

    public FakeClaudeServer()
    {
        // Lower priority numbers win; the forged and Books answers must beat the generic ones they overlap.
        Answer(1, ["grade integration-test quality", ForgeMarker], Grade.Replace("Create_persists_book", "Deletes_book", StringComparison.Ordinal));
        Answer(2, ["grade integration-test quality"], Grade);
        Answer(3, ["map integration tests", "BooksController"], BooksMap);
        Answer(4, ["map integration tests"], OrdersMap);
        Answer(5, ["classify ambiguous test files"], Classify);
        Answer(6, ["draft a repo profile"], Draft);
        Answer(0, ["classify ambiguous test files", SlowMarker], Classify, SlowDelay);
    }

    public string Url => _server.Url!;

    public void Dispose() => _server.Stop();

    /// <summary>WireMock rejects a zero delay, so a delay is added only when one is asked for.</summary>
    private static IResponseBuilder Delayed(IResponseBuilder response, TimeSpan? delay) => delay is { } d ? response.WithDelay(d) : response;

    private void Answer(int priority, string[] bodyContains, string answerJson, TimeSpan? delay = null) =>
        _server.Given(Request.Create().WithPath("/v1/messages").UsingPost()
                .WithBody(new RegexMatcher("(?s)^" + string.Concat(bodyContains.Select(t => $"(?=.*{System.Text.RegularExpressions.Regex.Escape(t)})")))))
            .AtPriority(priority)
            .RespondWith(Delayed(Response.Create().WithStatusCode(200), delay).WithBodyAsJson(new
            {
                id = "msg_test",
                type = "message",
                role = "assistant",
                model = "claude-sonnet-5-5",
                content = new[] { new { type = "text", text = answerJson } },
                stop_reason = "end_turn",
                stop_sequence = (string?)null,
                usage = new { input_tokens = 1000, output_tokens = 200 },
            }));

    private const string Classify = """
        {"files":[{"path":"tests/Bookstore.UnitTests/PriceTests.cs","kind":"unit","confidence":"High","reason":"pure arithmetic, no host or database","evidenceKind":"observed"}]}
        """;

    public const string Draft = """
        {"markdown":"# Repo profile — example-org/bookstore (drafted)","openQuestions":["Is the payment gateway a real seam?"]}
        """;

    private const string BooksMap = $$"""
        {"endpoints":[
          {"id":"e1","status":"covered","evidence":["{{Books}}::List_returns_seeded_books"],"falsePositive":false,"notes":"Asserts the seeded count."},
          {"id":"e2","status":"weak","evidence":["{{Books}}::Get_by_id_returns_ok"],"falsePositive":true,"notes":"Asserts only 200 OK."},
          {"id":"e3","status":"covered","evidence":["{{Books}}::Create_persists_book"],"falsePositive":false,"notes":"Reads the created book back."}]}
        """;

    private const string OrdersMap = $$"""
        {"endpoints":[
          {"id":"e1","status":"covered","evidence":["{{Orders}}::Place_order_persists_lines_and_total"],"falsePositive":false,"notes":"Asserts lines and total."},
          {"id":"e2","status":"none","evidence":[],"falsePositive":false,"notes":"No test calls cancel."}]}
        """;

    private static string Dimension(int id) =>
        $$"""{"id":{{id}},"score":{{id % 4 + 4}},"scorable":true,"confidence":"Med","justificationBullets":["What is tested: the main book and order flows","What is missing: failure paths","Evidence: {{Books}}::Create_persists_book","Why this score: 5-6 band"],"evidence":["{{Books}}::Create_persists_book"]}""";

    private static readonly string Grade = $$"""
        {"scorecard":[{{string.Join(',', Enumerable.Range(1, 7).Select(Dimension))}}],
         "findings":[{"kind":"FalsePositive","severity":"Medium","subject":"{{Books}}::Get_by_id_returns_ok","headline":"A book lookup test only checks that the request succeeded","detail":["Asserts 200 OK only."],"evidence":["{{Books}}::Get_by_id_returns_ok"]}],
         "topRisks":[{"headline":"Some tests pass even when the wrong data comes back","whyItMatters":"They give a false sense of safety for the book details page.","severity":"Medium","detail":["Get_by_id_returns_ok asserts only the HTTP status."],"evidence":["{{Books}}::Get_by_id_returns_ok"]}]}
        """;
}
