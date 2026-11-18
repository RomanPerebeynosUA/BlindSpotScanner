using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>D27: the per-test findings that follow from assertion levels, derived by code.</summary>
public static class AssertionFindings
{
    public static IReadOnlyList<ReportFinding> From(IReadOnlyList<TestAssertion> tests)
    {
        var findings = new List<ReportFinding>();
        foreach (var test in tests)
        {
            // The method name keeps headlines (and Jira summaries) apart when several tests share a problem.
            var method = test.Test[(test.Test.LastIndexOf('.') + 1)..];
            if (test.Level == 0)
            {
                findings.Add(Finding(test, FindingKind.NoAssertion, Severity.High, "no-assertion", $"{method} checks nothing", [$"{test.Test} passes whatever the code does."]));
            }

            if (!test.ErrorPath && test.FieldsResolved && test.UncheckedFields.Count > 0)
            {
                findings.Add(Finding(test, FindingKind.UncheckedFields, Severity.Medium, "unchecked-fields", $"{method} sends fields it never checks",
                    [$"Sends: {string.Join(", ", test.SentFields)}", $"Not checked: {string.Join(", ", test.UncheckedFields)}"]));
            }

            if (test.ErrorPath && test.Level is 1 or 2)
            {
                findings.Add(Finding(test, FindingKind.ErrorWithoutStateCheck, Severity.Medium, "error-without-state-check", $"{method} checks only the error status",
                    ["Checks the error status but not the error body or that nothing was saved."]));
            }
        }

        return findings;
    }

    private static ReportFinding Finding(TestAssertion test, FindingKind kind, Severity severity, string key, string headline, IReadOnlyList<string> detail) => new()
    {
        Id = string.Empty,
        Kind = kind,
        Severity = severity,
        StableKey = $"{key}:{test.Test}",
        Headline = headline,
        Detail = detail,
        Evidence = [test.Test],
    };
}
