using System.Data;
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

        if (await db.Database.CanConnectAsync(cancellationToken))
        {
            var existingPbmTableCount = await CountPbmTablesAsync(db, cancellationToken);
            if (existingPbmTableCount > 0)
            {
                throw new InvalidOperationException(
                    $"The Development database already contains {existingPbmTableCount} table(s) in schema 'pbm', but this build has no EF Core migrations. " +
                    "EnsureCreated cannot upgrade an existing schema after the model changes. Reset the disposable Development database (for Docker: 'docker compose down -v') " +
                    "or generate/apply EF Core migrations. The database was left unchanged.");
            }
        }

        logger.LogWarning("No EF Core migrations are present. Creating a new Development schema with EnsureCreated because Database:AllowEnsureCreated=true.");
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    private static async Task<int> CountPbmTablesAsync(PbmDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = N'pbm';";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
