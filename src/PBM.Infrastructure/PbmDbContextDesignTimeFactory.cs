using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PBM.Infrastructure;

public sealed class PbmDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PbmDbContext>
{
    public PbmDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PbmDbContext>()
            .UseSqlServer("Server=localhost;Database=PBM_DesignTime;User Id=sa;Password=PBM_DesignTime_Only_123!;TrustServerCertificate=True;Encrypt=False")
            .Options;

        return new PbmDbContext(options);
    }
}
