using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Infrastructure.Extraction;
using BlindSpotScanner.Infrastructure.GitHub;
using BlindSpotScanner.Infrastructure.Jira;
using BlindSpotScanner.Infrastructure.Llm;
using BlindSpotScanner.Infrastructure.Persistence;
using BlindSpotScanner.Infrastructure.Remediation;
using BlindSpotScanner.Infrastructure.Reports;
using BlindSpotScanner.Infrastructure.Repositories;
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
    /// <remarks>Configuration is read when services are resolved, not here, so a host (or a test factory) can still add sources after registration.</remarks>
    public static IServiceCollection AddBlindSpotScannerCore(this IServiceCollection services)
    {
        services.AddDbContext<BlindSpotScannerDbContext>((sp, o) => o.UseNpgsql(ConnectionString(sp)).UseSnakeCaseNamingConvention());
        services.AddSingleton(sp => new ScanEventListener(ConnectionString(sp)));
        services.AddOptions<GitHubOptions>().BindConfiguration("GitHub");
        services.AddOptions<LlmOptions>().BindConfiguration("Llm");
        services.AddOptions<ScanOptions>().BindConfiguration("Scan");
        services.AddOptions<JiraOptions>().BindConfiguration("Jira");
        services.TryAddSingleton(TimeProvider.System);

        // The typed client owns its HttpClient; the rate-limit handler sits in its handler chain (D16).
        services.AddTransient<GitHubRateLimitHandler>();
        services.AddHttpClient<IGitHubClient, GitHubClient>().AddHttpMessageHandler<GitHubRateLimitHandler>();
        services.AddHttpClient<IGitHubPullRequests, GitHubClient>().AddHttpMessageHandler<GitHubRateLimitHandler>();
        services.AddScoped<IBlobReader, CachingBlobReader>();
        // A short timeout: a hung Jira must not hold the report's actions list (D24).
        services.AddHttpClient<IJiraClient, JiraClient>().ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IEndpointExtractor, AspNetCoreEndpointExtractor>();
        services.AddSingleton<ITestSyntaxReader, CSharpTestSyntaxReader>();
        services.AddSingleton<IFixGuard, FixGuard>();

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
        services.AddScoped<IScanRequestStore, EfScanRequestStore>();
        services.AddScoped<IRepositoryStore, EfRepositoryStore>();
        services.AddScoped<RepositoryService>();
        services.AddScoped<RunComparison>();
        services.AddScoped<IRemediationStore, EfRemediationStore>();
        services.AddScoped<FixContextBuilder>();
        services.AddScoped(sp => new FixPullRequests(
            sp.GetRequiredService<IScanRequestStore>(), sp.GetRequiredService<IRemediationStore>(), sp.GetRequiredService<IRepositoryStore>(), sp.GetRequiredService<FixContextBuilder>(),
            sp.GetRequiredService<IFixGuard>(), sp.GetRequiredService<ILlmClient>(), sp.GetRequiredService<IGitHubClient>(),
            sp.GetRequiredService<IGitHubPullRequests>(), sp.GetRequiredService<TimeProvider>(), FixDefaultsFrom(sp)));
        services.AddScoped<RemediationActions>();
        services.AddScoped(sp =>
        {
            var jira = sp.GetRequiredService<IOptions<JiraOptions>>().Value;
            var publicUrl = PublicUrl(sp.GetRequiredService<IConfiguration>());
            return new JiraTickets(
                sp.GetRequiredService<IScanRequestStore>(), sp.GetRequiredService<IRemediationStore>(), sp.GetRequiredService<IJiraClient>(), sp.GetRequiredService<TimeProvider>(),
                new JiraDefaults(jira.ProjectKey, jira.IssueType, jira.Labels, publicUrl));
        });
        services.AddScoped<IProfileStore, EfProfileStore>();
        services.AddScoped<ProfileService>();
        services.AddScoped(sp => new ProfileDrafter(
            sp.GetRequiredService<IRepositoryStore>(), sp.GetRequiredService<IGitHubClient>(), sp.GetRequiredService<ILlmClient>(),
            sp.GetRequiredService<IOptions<ScanOptions>>().Value.MaxFileBytes));
        services.AddScoped(sp =>
        {
            var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            return new ScanRequests(
                sp.GetRequiredService<IScanRequestStore>(), sp.GetRequiredService<IScanQueue>(), sp.GetRequiredService<IScanEvents>(), sp.GetRequiredService<TimeProvider>(),
                new ScanRequestOptions(llm.Model, llm.PromptSetVersion, llm.MaxCostUsdPerScan));
        });
        services.AddScoped(sp => new ScanOrchestrator(
            sp.GetRequiredService<IScanStore>(),
            sp.GetRequiredService<IScanEvents>(),
            sp.GetRequiredService<ILlmClient>,
            sp.GetRequiredService<ScanPipelineFactory>(),
            sp.GetRequiredService<CitationVerifier>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    /// <summary>Read-only unless <c>Features:PullRequests</c> (default true) and a GitHub token are both set (D25).</summary>
    private static FixDefaults FixDefaultsFrom(IServiceProvider sp)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var enabled = configuration.GetValue("Features:PullRequests", true)
            && !string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<GitHubOptions>>().Value.Token);
        return new FixDefaults(enabled, PublicUrl(configuration));
    }

    private static string PublicUrl(IConfiguration configuration) => configuration["App:PublicUrl"] is { Length: > 0 } url ? url : "http://localhost:8080";

    private static string ConnectionString(IServiceProvider sp) =>
        sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
}
