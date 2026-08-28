using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class PbmDbContext(DbContextOptions<PbmDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<LicenseSubscription> LicenseSubscriptions => Set<LicenseSubscription>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserCompanyAccess> UserCompanyAccess => Set<UserCompanyAccess>();
    public DbSet<DimensionDefinition> Dimensions => Set<DimensionDefinition>();
    public DbSet<DimensionMember> DimensionMembers => Set<DimensionMember>();
    public DbSet<BudgetModel> BudgetModels => Set<BudgetModel>();
    public DbSet<BudgetModelDimension> BudgetModelDimensions => Set<BudgetModelDimension>();
    public DbSet<MeasureDefinition> Measures => Set<MeasureDefinition>();
    public DbSet<BudgetScenario> BudgetScenarios => Set<BudgetScenario>();
    public DbSet<BudgetPlan> BudgetPlans => Set<BudgetPlan>();
    public DbSet<BudgetVersion> BudgetVersions => Set<BudgetVersion>();
    public DbSet<BudgetFact> BudgetFacts => Set<BudgetFact>();
    public DbSet<BudgetFactDimension> BudgetFactDimensions => Set<BudgetFactDimension>();
    public DbSet<BudgetAttachment> BudgetAttachments => Set<BudgetAttachment>();
    public DbSet<KpiDefinition> Kpis => Set<KpiDefinition>();
    public DbSet<KpiValue> KpiValues => Set<KpiValue>();
    public DbSet<BudgetComment> BudgetComments => Set<BudgetComment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CurrencyDefinition> Currencies => Set<CurrencyDefinition>();
    public DbSet<FxRateSource> FxRateSources => Set<FxRateSource>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<StrategicObjective> StrategicObjectives => Set<StrategicObjective>();
    public DbSet<KpiObjectiveLink> KpiObjectiveLinks => Set<KpiObjectiveLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pbm");
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Company>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<OrganizationUnit>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        modelBuilder.Entity<FiscalYear>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        modelBuilder.Entity<FiscalPeriod>().HasIndex(x => new { x.FiscalYearId, x.Sequence }).IsUnique();
        modelBuilder.Entity<DimensionDefinition>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<DimensionMember>().HasIndex(x => new { x.DimensionId, x.CompanyId, x.Code }).IsUnique();
        modelBuilder.Entity<BudgetModel>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<MeasureDefinition>().HasIndex(x => new { x.BudgetModelId, x.Code }).IsUnique();
        modelBuilder.Entity<BudgetScenario>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<BudgetPlan>().HasIndex(x => new { x.CompanyId, x.FiscalYearId, x.BudgetModelId }).IsUnique();
        modelBuilder.Entity<BudgetVersion>().HasIndex(x => new { x.BudgetPlanId, x.VersionNumber }).IsUnique();
        modelBuilder.Entity<BudgetAttachment>().HasIndex(x => new { x.VersionId, x.CreatedAtUtc });
        modelBuilder.Entity<BudgetAttachment>().HasIndex(x => new { x.VersionId, x.Sha256 });
        modelBuilder.Entity<AppUser>().HasIndex(x => new { x.TenantId, x.UserName }).IsUnique();
        modelBuilder.Entity<Role>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<CurrencyDefinition>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<FxRateSource>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<FxRate>().HasIndex(x => new { x.SourceId, x.FromCurrencyId, x.ToCurrencyId, x.RateDate }).IsUnique();
        modelBuilder.Entity<StrategicObjective>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<KpiDefinition>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<KpiValue>().HasIndex(x => new { x.KpiId, x.CompanyId, x.PeriodId }).IsUnique();

        modelBuilder.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        modelBuilder.Entity<UserCompanyAccess>().HasKey(x => new { x.UserId, x.CompanyId });
        modelBuilder.Entity<BudgetModelDimension>().HasKey(x => new { x.BudgetModelId, x.DimensionId });
        modelBuilder.Entity<BudgetFactDimension>().HasKey(x => new { x.BudgetFactId, x.DimensionId });
        modelBuilder.Entity<KpiObjectiveLink>().HasKey(x => new { x.KpiId, x.ObjectiveId });

        modelBuilder.Entity<BudgetFact>().HasIndex(x => new { x.VersionId, x.PeriodId, x.MeasureId, x.ValueKind, x.CoordinateHash }).IsUnique();
        modelBuilder.Entity<BudgetFact>().Property(x => x.Value).HasPrecision(28, 8);
        modelBuilder.Entity<BudgetAttachment>().Property(x => x.FileName).HasMaxLength(240);
        modelBuilder.Entity<BudgetAttachment>().Property(x => x.ContentType).HasMaxLength(120);
        modelBuilder.Entity<BudgetAttachment>().Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
        modelBuilder.Entity<BudgetAttachment>().Property(x => x.Content).HasColumnType("varbinary(max)");
        modelBuilder.Entity<KpiDefinition>().Property(x => x.Weight).HasPrecision(9, 4);
        modelBuilder.Entity<KpiDefinition>().Property(x => x.Minimum).HasPrecision(28, 8);
        modelBuilder.Entity<KpiDefinition>().Property(x => x.Maximum).HasPrecision(28, 8);
        modelBuilder.Entity<KpiValue>().Property(x => x.Target).HasPrecision(28, 8);
        modelBuilder.Entity<KpiValue>().Property(x => x.Actual).HasPrecision(28, 8);
        modelBuilder.Entity<KpiValue>().Property(x => x.Score).HasPrecision(28, 8);
        modelBuilder.Entity<FxRate>().Property(x => x.Rate).HasPrecision(28, 10);
        modelBuilder.Entity<StrategicObjective>().Property(x => x.Weight).HasPrecision(9, 4);
        modelBuilder.Entity<KpiObjectiveLink>().Property(x => x.Weight).HasPrecision(9, 4);

        modelBuilder.Entity<OrganizationUnit>().HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DimensionMember>().HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DimensionMember>().HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetModelDimension>().HasOne(x => x.BudgetModel).WithMany(x => x.Dimensions).HasForeignKey(x => x.BudgetModelId);
        modelBuilder.Entity<BudgetModelDimension>().HasOne(x => x.Dimension).WithMany().HasForeignKey(x => x.DimensionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetFactDimension>().HasOne(x => x.BudgetFact).WithMany(x => x.Dimensions).HasForeignKey(x => x.BudgetFactId);
        modelBuilder.Entity<BudgetFactDimension>().HasOne(x => x.Dimension).WithMany().HasForeignKey(x => x.DimensionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetFactDimension>().HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetAttachment>().HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BudgetAttachment>().HasOne(x => x.Comment).WithMany().HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<BudgetAttachment>().HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserCompanyAccess>().HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StrategicObjective>().HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<KpiObjectiveLink>().HasOne(x => x.Kpi).WithMany().HasForeignKey(x => x.KpiId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<KpiObjectiveLink>().HasOne(x => x.Objective).WithMany().HasForeignKey(x => x.ObjectiveId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FxRate>().HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FxRate>().HasOne(x => x.FromCurrency).WithMany().HasForeignKey(x => x.FromCurrencyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FxRate>().HasOne(x => x.ToCurrency).WithMany().HasForeignKey(x => x.ToCurrencyId).OnDelete(DeleteBehavior.Restrict);
        base.OnModelCreating(modelBuilder);
    }
}
