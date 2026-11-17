using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>Builds a pipeline per scan, because each scan gets its own budgeted LLM client.</summary>
public sealed class ScanPipelineFactory(
    IGitHubClient github,
    IBlobReader blobs,
    IEndpointExtractor extractor,
    ITestSyntaxReader reader,
    ReportValidator validator,
    TimeProvider time,
    ScanPipelineOptions options)
{
    public ScanPipeline Create(ILlmClient llm) => new(github, blobs, extractor, reader, llm, validator, time, options);
}
