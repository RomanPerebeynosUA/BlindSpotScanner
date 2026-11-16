using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlindSpotScanner.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` to build the model for migrations; it never connects. Set
/// BLINDSPOT_DESIGN_CONNECTION to run commands that need a database (e.g. `database update`).
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BlindSpotScannerDbContext>
{
    public BlindSpotScannerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BLINDSPOT_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=blindspot";
        return new BlindSpotScannerDbContext(
            new DbContextOptionsBuilder<BlindSpotScannerDbContext>()
                .UseBlindSpotScannerNpgsql(connectionString)
                .Options);
    }
}
