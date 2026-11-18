using System.Globalization;
using System.Net;
using System.Text;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Reports;

/// <summary>
/// The printable report export (doc 11 M11, D26): one self-contained HTML page with inline CSS and
/// no scripts. Every report string is HTML-encoded (finding text is LLM-authored), and status is
/// written as text, never shown by colour alone.
/// </summary>
public static class ReportHtmlRenderer
{
    private const string Css = """
        body{font:14px/1.45 system-ui,sans-serif;color:#1b1f24;margin:24px;max-width:1100px}
        h1{font-size:22px;margin:0 0 4px}h2{font-size:17px;margin:24px 0 8px;border-bottom:1px solid #d0d7de}
        .meta{color:#57606a}.kpis{display:flex;flex-wrap:wrap;gap:12px}.kpi{border:1px solid #d0d7de;border-radius:6px;padding:8px 12px}
        .kpi b{display:block;font-size:20px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #d0d7de;padding:4px 8px;text-align:left;vertical-align:top}
        .mono{font-family:ui-monospace,monospace}.s-covered{background:#dafbe1}.s-weak{background:#fff8c5}.s-none{background:#ffebe9}.s-skipped,.s-unverified{background:#f6f8fa}
        footer{margin-top:32px;color:#57606a}@media print{body{margin:0}h2{break-after:avoid}tr{break-inside:avoid}}
        """;

    public static string Render(IntegrationReport report)
    {
        var meta = report.Meta;
        var summary = report.Summary;
        var repo = $"{meta.Repo.Owner}/{meta.Repo.Name}";
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>")
            .Append(E(repo)).Append(" · ").Append(E(Short(meta.CommitSha))).Append("</title><style>").Append(Css).Append("</style></head><body>");

        html.Append("<header><h1>").Append(E(repo)).Append("</h1><div class=\"meta\">")
            .Append(E(meta.Branch)).Append(" · <span class=\"mono\">").Append(E(Short(meta.CommitSha))).Append("</span> · ")
            .Append(E(meta.GeneratedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture))).Append(" · model ").Append(E(meta.ModelId))
            .Append(" · prompts v").Append(E(meta.PromptSetVersion)).Append(" · integration = ").Append(E(summary.IntegrationMappedTo)).Append("</div></header>");

        var e = summary.Endpoints;
        html.Append("<section class=\"kpis\">")
            .Append(Kpi("Quality score", summary.QualityScore is { } score ? $"{score.ToString("0.0", CultureInfo.InvariantCulture)}/10" : "N/A",
                $"{summary.WeightAssessablePct.ToString("0", CultureInfo.InvariantCulture)}% of weight assessable · {summary.ScoreLabel}"))
            .Append(Kpi("Endpoints covered", $"{e.Covered}/{e.Total}", null)).Append(Kpi("Weak", e.Weak.ToString(CultureInfo.InvariantCulture), null))
            .Append(Kpi("Untested", e.None.ToString(CultureInfo.InvariantCulture), null)).Append(Kpi("Unverified", e.Unverified.ToString(CultureInfo.InvariantCulture), null))
            .Append("</section>");

        html.Append("<h2>Top risks</h2><ol>");
        foreach (var risk in report.TopRisks.OrderBy(r => r.Rank))
        {
            html.Append("<li><b>").Append(E(risk.Headline)).Append("</b> (").Append(E(risk.Severity.ToString())).Append(")<br>").Append(E(risk.WhyItMatters)).Append(List(risk.Detail)).Append("</li>");
        }

        html.Append("</ol><h2>Endpoint coverage</h2><table><thead><tr><th>Controller</th><th>Method</th><th>Route</th><th>Status</th><th>Evidence</th></tr></thead><tbody>");
        foreach (var controller in report.EndpointCoverage)
        {
            foreach (var endpoint in controller.Endpoints)
            {
                var status = endpoint.Status.ToString().ToLowerInvariant();
                html.Append("<tr><td>").Append(E(controller.Controller)).Append("</td><td class=\"mono\">").Append(E(endpoint.Method.ToString().ToUpperInvariant()))
                    .Append("</td><td class=\"mono\">").Append(E(endpoint.Route)).Append("</td><td class=\"status s-").Append(status).Append("\">").Append(status)
                    .Append("</td><td class=\"mono\">").Append(string.Join("<br>", endpoint.Evidence.Select(E))).Append("</td></tr>");
            }
        }

        html.Append("</tbody></table><h2>Quality scorecard</h2><table><thead><tr><th>Dimension</th><th>Weight</th><th>Score</th><th>Confidence</th><th>Why</th></tr></thead><tbody>");
        foreach (var dimension in report.Scorecard)
        {
            html.Append("<tr><td>").Append(E(dimension.Dimension)).Append("</td><td>").Append(E(dimension.Weight.ToString("0.##", CultureInfo.InvariantCulture)))
                .Append("</td><td>").Append(dimension is { Scorable: true, Score: { } s } ? E($"{s}/10") : "N/A")
                .Append("</td><td>").Append(E(dimension.Confidence?.ToString() ?? "")).Append("</td><td>").Append(List(dimension.JustificationBullets)).Append("</td></tr>");
        }

        html.Append("</tbody></table><h2>Findings</h2><ul>");
        foreach (var finding in report.Findings)
        {
            html.Append("<li><b>").Append(E(finding.Headline)).Append("</b> (").Append(E(finding.Kind.ToString())).Append(", ").Append(E(finding.Severity.ToString())).Append(')')
                .Append(List(finding.Detail ?? [])).Append(List(finding.Evidence)).Append("</li>");
        }

        html.Append("</ul><footer>Generated by BlindSpotScanner · run <span class=\"mono\">").Append(E(meta.RunId.ToString())).Append("</span></footer></body></html>");
        return html.ToString();
    }

    private static string Kpi(string label, string value, string? sub) =>
        $"<div class=\"kpi\">{E(label)}<b>{E(value)}</b>{(sub is null ? "" : $"<span class=\"meta\">{E(sub)}</span>")}</div>";

    private static string List(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? string.Empty : "<ul>" + string.Concat(list.Select(i => $"<li>{E(i)}</li>")) + "</ul>";
    }

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
