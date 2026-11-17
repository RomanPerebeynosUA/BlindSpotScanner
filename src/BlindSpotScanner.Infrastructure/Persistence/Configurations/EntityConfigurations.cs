using BlindSpotScanner.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlindSpotScanner.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> builder) => builder.HasKey(r => r.Id);
}

internal sealed class RepoProfileConfiguration : IEntityTypeConfiguration<RepoProfile>
{
    public void Configure(EntityTypeBuilder<RepoProfile> builder)
    {
        builder.HasKey(p => new { p.RepositoryId, p.Version });
        builder.HasOne<Repository>().WithMany().HasForeignKey(p => p.RepositoryId);
    }
}

internal sealed class ScanRunConfiguration : IEntityTypeConfiguration<ScanRun>
{
    public void Configure(EntityTypeBuilder<ScanRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasOne<Repository>().WithMany().HasForeignKey(r => r.RepositoryId);
        builder.Property(r => r.ReportJson).HasColumnType("jsonb");
        // The scan queue (D21) takes the oldest queued run, or an expired running one.
        builder.HasIndex(r => new { r.Status, r.QueuedAt });
        // A save only applies if nobody else changed the status or took the lease since it was read:
        // a stale worker or a cancel racing a dequeue fails with a concurrency error instead of overwriting.
        builder.Property(r => r.Status).IsConcurrencyToken();
        builder.Property(r => r.LeaseOwner).IsConcurrencyToken();
    }
}

internal sealed class ScanStepConfiguration : IEntityTypeConfiguration<ScanStep>
{
    public void Configure(EntityTypeBuilder<ScanStep> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasOne<ScanRun>().WithMany().HasForeignKey(s => s.ScanRunId);
    }
}

internal sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.HasKey(f => f.Id);
        builder.HasOne<ScanRun>().WithMany().HasForeignKey(f => f.ScanRunId);
    }
}

internal sealed class RemediationActionConfiguration : IEntityTypeConfiguration<RemediationAction>
{
    public void Configure(EntityTypeBuilder<RemediationAction> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasOne<Repository>().WithMany().HasForeignKey(a => a.RepositoryId);
        // Actions outlive runs (doc 03): deleting a finding must never silently delete its ticket or PR.
        builder.HasOne<Finding>().WithMany().HasForeignKey(a => a.CreatedFromFindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.RepositoryId, a.StableKey });
    }
}

internal sealed class FileCacheEntryConfiguration : IEntityTypeConfiguration<FileCacheEntry>
{
    public void Configure(EntityTypeBuilder<FileCacheEntry> builder) => builder.HasKey(f => f.BlobSha);
}

internal sealed class LlmCacheEntryConfiguration : IEntityTypeConfiguration<LlmCacheEntry>
{
    public void Configure(EntityTypeBuilder<LlmCacheEntry> builder)
    {
        builder.HasKey(l => l.Key);
        builder.Property(l => l.ResponseJson).HasColumnType("jsonb");
    }
}
