using System.Text.Json;
using BlindSpotScanner.Application.Reports;
using Json.Schema;

namespace BlindSpotScanner.Infrastructure.Reports;

/// <summary>Validates report JSON against contracts/integration-report.schema.json (draft 2020-12).</summary>
public sealed class ReportSchemaValidator : IReportSchemaValidator
{
    private const string SchemaResourceName = "integration-report.schema.json";

    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(ReadEmbeddedSchema()));

    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        // Draft 2020-12 treats "format" as an annotation by default; uuid/date-time/uri must be enforced.
        RequireFormatValidation = true,
    };

    /// <returns>An empty list when the report is valid; otherwise "&lt;instance location&gt;: &lt;message&gt;" per error.</returns>
    public IReadOnlyList<string> Validate(string reportJson)
    {
        using var document = JsonDocument.Parse(reportJson);
        var results = Schema.Value.Evaluate(document.RootElement, Options);
        if (results.IsValid)
        {
            return [];
        }

        return (results.Details ?? [])
            .Prepend(results)
            .Where(detail => detail.Errors is not null)
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}"))
            .ToList();
    }

    public static string ReadEmbeddedSchema()
    {
        using var stream = typeof(ReportSchemaValidator).Assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{SchemaResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
