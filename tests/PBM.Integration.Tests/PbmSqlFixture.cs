using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Integration.Tests;

public sealed class PbmSqlFixture : IAsyncLifetime
{
    private readonly string _connectionString;

    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid MeasureId { get; private set; }
    public IReadOnlyList<DimensionSelection> Dimensions { get; private set; } = [];
    public IReadOnlyDictionary<int, Guid> PeriodIds { get; private set; } = new Dictionary<int, Guid>();

    public PbmSqlFixture()
    {
        _connectionString = Environment.GetEnvironmentVariable("PBM_INTEGRATION_SQL")
            ?? "Server=localhost,1433;Database=PBM_Integration_Tests;User Id=sa;Password=PbmIntegration_2026!Strong;Encrypt=False;TrustServerCertificate=True";
        EnsureSafeDatabaseName(_connectionString);
    }

    public async ValueTask InitializeAsync()
    {
        await WaitForSqlAndCreateDatabaseAsync();
        await SeedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }

    public PbmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PbmDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new PbmDbContext(options);
    }

    public async Task<DateTime> PostingDateForPeriodAsync(Guid periodId)
    {
        await using var db = CreateContext();
        var period = await db.FiscalPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
        return period.StartDate.Date.AddDays(1);
    }

    private async Task WaitForSqlAndCreateDatabaseAsync()
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                await using var db = CreateContext();
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();
                return;
            }
            catch (Exception ex) when (attempt < 30)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException("SQL Server integration-test database could not be initialized.", lastError);
    }

    private async Task SeedAsync()
    {
        await using var db = CreateContext();
        await SeedData.InitializeAsync(db);
        await EnterpriseSeedData.InitializeAsync(db);
        await PlanningSeedData.InitializeAsync(db);
        await AssumptionSeedData.InitializeAsync(db);
        await SecuritySeedData.InitializeAsync(db);

        var tenant = await db.Tenants.SingleAsync();
        var company = await db.Companies.SingleAsync(x => x.TenantId == tenant.Id);
        var fiscalYear = await db.FiscalYears.SingleAsync(x => x.CompanyId == company.Id);
        var version = await db.BudgetVersions
            .Include(x => x.BudgetPlan)
            .SingleAsync(x => x.BudgetPlan!.CompanyId == company.Id && x.BudgetPlan.FiscalYearId == fiscalYear.Id);
        var measure = await db.Measures.SingleAsync(x =>
            x.BudgetModelId == version.BudgetPlan!.BudgetModelId && x.Code == "BANK_FEE");
        var integrationRole = await db.Roles.SingleAsync(x => x.TenantId == tenant.Id && x.Code == "INTEGRATION");

        var testUser = new AppUser
        {
            TenantId = tenant.Id,
            UserName = "integration-test",
            DisplayName = "Integration Test Service Account",
            PasswordHash = "not-used",
            IsActive = true
        };
        testUser.UserRoles.Add(new UserRole { UserId = testUser.Id, RoleId = integrationRole.Id });
        testUser.CompanyAccess.Add(new UserCompanyAccess
        {
            UserId = testUser.Id,
            CompanyId = company.Id,
            CanRead = true,
            CanWrite = true
        });
        db.Users.Add(testUser);
        await db.SaveChangesAsync();

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == version.BudgetPlan!.BudgetModelId)
            .OrderBy(x => x.Sequence)
            .ToListAsync();
        var selections = new List<DimensionSelection>();
        foreach (var modelDimension in modelDimensions)
        {
            var memberId = await db.DimensionMembers.AsNoTracking()
                .Where(x => x.DimensionId == modelDimension.DimensionId
                    && x.IsActive
                    && (x.CompanyId == null || x.CompanyId == company.Id))
                .OrderBy(x => x.Code)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync();
            if (memberId.HasValue)
            {
                selections.Add(new DimensionSelection(modelDimension.DimensionId, memberId.Value));
                continue;
            }

            if (modelDimension.IsRequired)
                throw new InvalidOperationException($"Required integration-test dimension {modelDimension.DimensionId} has no active member.");
        }

        TenantId = tenant.Id;
        CompanyId = company.Id;
        FiscalYearId = fiscalYear.Id;
        UserId = testUser.Id;
        VersionId = version.Id;
        MeasureId = measure.Id;
        Dimensions = selections;
        PeriodIds = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == fiscalYear.Id)
            .ToDictionaryAsync(x => x.Sequence, x => x.Id);
    }

    private static void EnsureSafeDatabaseName(string value)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = value };
        object? databaseValue = null;
        if (!builder.TryGetValue("Database", out databaseValue))
            builder.TryGetValue("Initial Catalog", out databaseValue);
        var databaseName = Convert.ToString(databaseValue)?.Trim();
        if (string.IsNullOrWhiteSpace(databaseName)
            || !databaseName.StartsWith("PBM_Integration", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "PBM_INTEGRATION_SQL must target a disposable database whose name starts with 'PBM_Integration'.");
    }
}
