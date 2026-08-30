using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PBM.Infrastructure;

public sealed class PbmDbContextFactory : IDesignTimeDbContextFactory<PbmDbContext>
{
    public PbmDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PBM_DESIGNTIME_CONNECTION")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PBM_DESIGNTIME_CONNECTION is required for EF Core design-time operations. " +
                "Provide a disposable SQL Server connection string before running dotnet ef commands.");
        }

        var options = new DbContextOptionsBuilder<PbmDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new PbmDbContext(options);
    }
}
