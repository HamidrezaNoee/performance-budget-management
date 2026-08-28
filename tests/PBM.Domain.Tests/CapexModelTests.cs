using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class CapexModelTests
{
    private static PbmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PbmDbContext>()
            .UseSqlServer("Server=localhost;Database=PBM_Model_Test;User Id=sa;Password=NotUsed_123!;TrustServerCertificate=True")
            .Options;
        return new PbmDbContext(options);
    }

    [Fact]
    public void Capex_project_code_is_unique_inside_company()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(CapexProject));
        Assert.NotNull(entity);

        var index = entity!.GetIndexes().Single(x =>
            x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual([nameof(CapexProject.CompanyId), nameof(CapexProject.Code)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Capex_money_and_progress_have_explicit_precision()
    {
        using var db = CreateContext();
        var project = db.Model.FindEntityType(typeof(CapexProject))!;
        var milestone = db.Model.FindEntityType(typeof(CapexMilestone))!;

        Assert.Equal(28, project.FindProperty(nameof(CapexProject.RequestedBudget))!.GetPrecision());
        Assert.Equal(8, project.FindProperty(nameof(CapexProject.RequestedBudget))!.GetScale());
        Assert.Equal(28, project.FindProperty(nameof(CapexProject.ApprovedBudgetLimit))!.GetPrecision());
        Assert.Equal(8, project.FindProperty(nameof(CapexProject.ApprovedBudgetLimit))!.GetScale());
        Assert.Equal(9, project.FindProperty(nameof(CapexProject.CompletionPercent))!.GetPrecision());
        Assert.Equal(4, project.FindProperty(nameof(CapexProject.CompletionPercent))!.GetScale());
        Assert.Equal(9, milestone.FindProperty(nameof(CapexMilestone.Weight))!.GetPrecision());
        Assert.Equal(4, milestone.FindProperty(nameof(CapexMilestone.Weight))!.GetScale());
    }

    [Fact]
    public void Capex_milestones_are_unique_per_project_and_cascade_with_project()
    {
        using var db = CreateContext();
        var milestone = db.Model.FindEntityType(typeof(CapexMilestone))!;
        var uniqueIndex = milestone.GetIndexes().Single(x =>
            x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual([nameof(CapexMilestone.ProjectId), nameof(CapexMilestone.Code)]));
        var projectFk = milestone.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(CapexProject));

        Assert.True(uniqueIndex.IsUnique);
        Assert.Equal(DeleteBehavior.Cascade, projectFk.DeleteBehavior);
    }
}
