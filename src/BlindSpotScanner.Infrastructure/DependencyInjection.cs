using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Infrastructure.Extraction;
using BlindSpotScanner.Infrastructure.GitHub;
using BlindSpotScanner.Infrastructure.Llm;
using BlindSpotScanner.Infrastructure.Persistence;
using BlindSpotScanner.Infrastructure.Reports;
using BlindSpotScanner.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Everything the api and the worker share: persistence, GitHub, the LLM, the pipeline, the queue and events.</summary>
    public static IServiceCollection AddBlindSpotScannerCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<BlindSpotScannerDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddSingleton(new ScanEventListener(connectionString));
        services.Configure<GitHubOptions>(configuration.GetSection("GitHub"));
        services.Configure<LlmOptions>(configuration.GetSection("Llm"));
        services.Configure<ScanOptions>(configuration.GetSection("Scan"));
        services.TryAddSingleton(TimeProvider.System);

        // The typed client owns its HttpClient; the rate-limit handler sits in its handler chain (D16).
        services.AddTransient<GitHubRateLimitHandler>();
        services.AddHttpClient<IGitHubClient, GitHubClient>().AddHttpMessageHandler<GitHubRateLimitHandler>();
        services.AddScoped<IBlobReader, CachingBlobReader>();
        services.AddSingleton<IEndpointExtractor, AspNetCoreEndpointExtractor>();
        services.AddSingleton<ITestSyntaxReader, CSharpTestSyntaxReader>();

        services.AddSingleton<PromptLibrary>();
        services.AddSingleton<ClaudeLlmClient>();
        services.AddScoped<ILlmClient>(sp => new CachingLlmClient(
            sp.GetRequiredService<BlindSpotScannerDbContext>(),
            sp.GetRequiredService<ClaudeLlmClient>(),
            sp.GetRequiredService<PromptLibrary>(),
            sp.GetRequiredService<IOptions<LlmOptions>>()));

        services.AddSingleton<IReportSchemaValidator, ReportSchemaValidator>();
        services.AddSingleton<ReportValidator>();
        services.AddScoped<CitationVerifier>();
        services.AddScoped(sp =>
        {
            var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            var scan = sp.GetRequiredService<IOptions<ScanOptions>>().Value;
            return new ScanPipelineFactory(
                sp.GetRequiredService<IGitHubClient>(),
                sp.GetRequiredService<IBlobReader>(),
                sp.GetRequiredService<IEndpointExtractor>(),
                sp.GetRequiredService<ITestSyntaxReader>(),
                sp.GetRequiredService<ReportValidator>(),
                sp.GetRequiredService<TimeProvider>(),
                new ScanPipelineOptions(llm.Model, llm.PromptSetVersion, scan.MaxFileBytes));
        });

        services.AddScoped<IScanStore, EfScanStore>();
        services.AddScoped<IScanQueue, PostgresScanQueue>();
        services.AddScoped<IScanEvents, PostgresScanEvents>();
        services.AddScoped(sp => new ScanOrchestrator(
            sp.GetRequiredService<IScanStore>(),
            sp.GetRequiredService<IScanEvents>(),
            sp.GetRequiredService<ILlmClient>,
            sp.GetRequiredService<ScanPipelineFactory>(),
            sp.GetRequiredService<CitationVerifier>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
