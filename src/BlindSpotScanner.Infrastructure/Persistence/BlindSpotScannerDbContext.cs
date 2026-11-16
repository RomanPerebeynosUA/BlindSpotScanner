using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Persistence;

public class BlindSpotScannerDbContext(DbContextOptions<BlindSpotScannerDbContext> options) : DbContext(options)
{
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<RepoProfile> RepoProfiles => Set<RepoProfile>();
    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();
    public DbSet<ScanStep> ScanSteps => Set<ScanStep>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<RemediationAction> RemediationActions => Set<RemediationAction>();
    public DbSet<FileCacheEntry> FileCache => Set<FileCacheEntry>();
    public DbSet<LlmCacheEntry> LlmCache => Set<LlmCacheEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by name; the immutability trigger compares status = 'Succeeded'.
        configurationBuilder.Properties<RunStatus>().HaveConversion<string>();
        configurationBuilder.Properties<IntegrationMappedTo>().HaveConversion<string>();
        configurationBuilder.Properties<FindingKind>().HaveConversion<string>();
        configurationBuilder.Properties<Severity>().HaveConversion<string>();
        configurationBuilder.Properties<ActionType>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BlindSpotScannerDbContext).Assembly);
}

public static class BlindSpotScannerDbContextOptions
{
    /// <summary>The one place the provider and naming convention are chosen (runtime, tests and design time).</summary>
    public static DbContextOptionsBuilder<BlindSpotScannerDbContext> UseBlindSpotScannerNpgsql(
        this DbContextOptionsBuilder<BlindSpotScannerDbContext> builder, string connectionString) =>
        builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}
