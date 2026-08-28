using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Infrastructure;

namespace PBM.Api;

public static class SystemInfoEndpoints
{
    public static IEndpointRouteBuilder MapSystemInfoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/system/info", async (
            PbmDbContext db,
            IUserContext user,
            IHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (!user.IsInRole("SUPERADMIN")
                && !user.IsInRole("ADMIN")
                && !user.IsInRole("AUDITOR"))
                throw new UnauthorizedAccessException("Administrator or auditor role is required to view system diagnostics.");

            var assembly = typeof(Program).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            var applicationVersion = informationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";

            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            var lastApplied = applied.LastOrDefault();

            return Results.Ok(new
            {
                application = new
                {
                    name = "Performance Budget Management",
                    version = applicationVersion,
                    environment = environment.EnvironmentName
                },
                database = new
                {
                    provider = db.Database.ProviderName,
                    canConnect = await db.Database.CanConnectAsync(cancellationToken),
                    migrationManaged = applied.Length > 0,
                    appliedMigrationCount = applied.Length,
                    lastAppliedMigration = lastApplied,
                    pendingMigrationCount = pending.Length,
                    pendingMigrations = pending
                },
                utc = DateTime.UtcNow
            });
        });

        return endpoints;
    }
}
