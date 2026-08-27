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
    public DbSet<KpiDefinition> Kpis => Set<KpiDefinition>();
    public DbSet<KpiValue> KpiValues => Set<KpiValue>();
    public DbSet<BudgetComment> BudgetComments => Set<BudgetComment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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
        modelBuilder.Entity<AppUser>().HasIndex(x => new { x.TenantId, x.UserName }).IsUnique();
        modelBuilder.Entity<Role>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

        modelBuilder.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        modelBuilder.Entity<UserCompanyAccess>().HasKey(x => new { x.UserId, x.CompanyId });
        modelBuilder.Entity<BudgetModelDimension>().HasKey(x => new { x.BudgetModelId, x.DimensionId });
        modelBuilder.Entity<BudgetFactDimension>().HasKey(x => new { x.BudgetFactId, x.DimensionId });

        modelBuilder.Entity<BudgetFact>()
            .HasIndex(x => new { x.VersionId, x.PeriodId, x.MeasureId, x.ValueKind, x.CoordinateHash })
            .IsUnique();
        modelBuilder.Entity<BudgetFact>().Property(x => x.Value).HasPrecision(28, 8);
        modelBuilder.Entity<KpiDefinition>().Property(x => x.Weight).HasPrecision(9, 4);
        modelBuilder.Entity<KpiDefinition>().Property(x => x.Minimum).HasPrecision(28, 8);
        modelBuilder.Entity<KpiDefinition>().Property(x => x.Maximum).HasPrecision(28, 8);
        modelBuilder.Entity<KpiValue>().Property(x => x.Target).HasPrecision(28, 8);
        modelBuilder.Entity<KpiValue>().Property(x => x.Actual).HasPrecision(28, 8);
        modelBuilder.Entity<KpiValue>().Property(x => x.Score).HasPrecision(28, 8);

        modelBuilder.Entity<OrganizationUnit>().HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DimensionMember>().HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DimensionMember>().HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetModelDimension>().HasOne(x => x.BudgetModel).WithMany(x => x.Dimensions).HasForeignKey(x => x.BudgetModelId);
        modelBuilder.Entity<BudgetModelDimension>().HasOne(x => x.Dimension).WithMany().HasForeignKey(x => x.DimensionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetFactDimension>().HasOne(x => x.BudgetFact).WithMany(x => x.Dimensions).HasForeignKey(x => x.BudgetFactId);
        modelBuilder.Entity<BudgetFactDimension>().HasOne(x => x.Dimension).WithMany().HasForeignKey(x => x.DimensionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetFactDimension>().HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserCompanyAccess>().HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

        base.OnModelCreating(modelBuilder);
    }
}
