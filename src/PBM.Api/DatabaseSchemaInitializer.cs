using Microsoft.EntityFrameworkCore;
using PBM.Infrastructure;

namespace PBM.Api;

public static class DatabaseSchemaInitializer
{
    public static async Task InitializeAsync(
        PbmDbContext db,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var migrations = db.Database.GetMigrations().ToArray();
        if (migrations.Length > 0)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            if (pending.Length == 0)
            {
                logger.LogInformation("PBM database schema is current. Applied migration model contains {MigrationCount} migration(s).", migrations.Length);
                return;
            }

            if (!configuration.GetValue<bool>("Database:AutoMigrate"))
            {
                throw new InvalidOperationException(
                    $"PBM database has {pending.Length} pending EF Core migration(s). " +
                    "Apply migrations during deployment or explicitly enable Database:AutoMigrate.");
            }

            logger.LogInformation("Applying {PendingMigrationCount} pending PBM database migration(s).", pending.Length);
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("PBM database migrations applied successfully.");
            return;
        }

        var allowEnsureCreated = configuration.GetValue<bool>("Database:AllowEnsureCreated");
        if (!environment.IsDevelopment() || !allowEnsureCreated)
        {
            throw new InvalidOperationException(
                "No EF Core migrations are present for PBM. Production startup will not use EnsureCreated. " +
                "Generate and commit an initial migration, or use Database:AllowEnsureCreated=true only in Development.");
        }

        logger.LogWarning("No EF Core migrations are present. Creating the schema with EnsureCreated because this is Development and Database:AllowEnsureCreated=true.");
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
