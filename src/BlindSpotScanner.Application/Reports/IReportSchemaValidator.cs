namespace BlindSpotScanner.Application.Reports;

/// <summary>Checks report JSON against contracts/integration-report.schema.json (doc 07 checklist item 1).</summary>
public interface IReportSchemaValidator
{
    /// <returns>An empty list when the report conforms; otherwise one message per violation.</returns>
    IReadOnlyList<string> Validate(string reportJson);
}
