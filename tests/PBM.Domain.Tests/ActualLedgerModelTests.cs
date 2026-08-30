using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class ActualLedgerModelTests
{
    private static PbmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PbmDbContext>()
            .UseSqlServer("Server=localhost;Database=PBM_Model_Test;User Id=sa;Password=NotUsed_123!;TrustServerCertificate=True")
            .Options;
        return new PbmDbContext(options);
    }

    [Fact]
    public void Actual_ledger_entities_are_discovered_by_the_EF_model()
    {
        using var db = CreateContext();

        Assert.NotNull(db.Model.FindEntityType(typeof(ActualLedgerEntry)));
        Assert.NotNull(db.Model.FindEntityType(typeof(ActualLedgerDimension)));
    }

    [Fact]
    public void Posting_business_key_is_unique_per_tenant_company_and_source_line()
    {
        using var db = CreateContext();
        var entry = db.Model.FindEntityType(typeof(ActualLedgerEntry))!;
        var index = entry.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual([
                nameof(ActualLedgerEntry.TenantId),
                nameof(ActualLedgerEntry.CompanyId),
                nameof(ActualLedgerEntry.SourceSystem),
                nameof(ActualLedgerEntry.ExternalDocumentId),
                nameof(ActualLedgerEntry.ExternalLineId),
                nameof(ActualLedgerEntry.EntryType)
            ]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Only_one_reversal_can_reference_each_posting()
    {
        using var db = CreateContext();
        var entry = db.Model.FindEntityType(typeof(ActualLedgerEntry))!;
        var index = entry.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual([nameof(ActualLedgerEntry.OriginalEntryId)]));

        Assert.True(index.IsUnique);
        Assert.Equal("[OriginalEntryId] IS NOT NULL", index.GetFilter());

        var selfForeignKey = entry.GetForeignKeys().Single(x =>
            x.PrincipalEntityType.ClrType == typeof(ActualLedgerEntry)
            && x.Properties.Select(p => p.Name).SequenceEqual([nameof(ActualLedgerEntry.OriginalEntryId)]));
        Assert.False(selfForeignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, selfForeignKey.DeleteBehavior);
    }

    [Fact]
    public void Ledger_has_coordinate_and_recent_activity_indexes_for_projection_and_operations()
    {
        using var db = CreateContext();
        var entry = db.Model.FindEntityType(typeof(ActualLedgerEntry))!;
        var indexShapes = entry.GetIndexes()
            .Select(x => x.Properties.Select(p => p.Name).ToArray())
            .ToList();

        Assert.Contains(indexShapes, x => x.SequenceEqual([
            nameof(ActualLedgerEntry.VersionId),
            nameof(ActualLedgerEntry.PeriodId),
            nameof(ActualLedgerEntry.MeasureId),
            nameof(ActualLedgerEntry.CoordinateHash)
        ]));
        Assert.Contains(indexShapes, x => x.SequenceEqual([
            nameof(ActualLedgerEntry.TenantId),
            nameof(ActualLedgerEntry.CompanyId),
            nameof(ActualLedgerEntry.CreatedAtUtc)
        ]));
    }

    [Fact]
    public void Ledger_dimension_is_unique_per_entry_and_dimension_and_cascades_only_with_entry()
    {
        using var db = CreateContext();
        var dimension = db.Model.FindEntityType(typeof(ActualLedgerDimension))!;

        Assert.Equal([nameof(ActualLedgerDimension.Id)], dimension.FindPrimaryKey()!.Properties.Select(x => x.Name));
        var uniqueIndex = dimension.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual([
                nameof(ActualLedgerDimension.EntryId),
                nameof(ActualLedgerDimension.DimensionId)
            ]));
        Assert.True(uniqueIndex.IsUnique);

        var entryFk = dimension.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(ActualLedgerEntry));
        var dimensionFk = dimension.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(DimensionDefinition));
        var memberFk = dimension.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(DimensionMember));
        Assert.Equal(DeleteBehavior.Cascade, entryFk.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, dimensionFk.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, memberFk.DeleteBehavior);
    }

    [Fact]
    public void Ledger_parent_foreign_keys_are_restrict_to_avoid_multiple_cascade_paths()
    {
        using var db = CreateContext();
        var entry = db.Model.FindEntityType(typeof(ActualLedgerEntry))!;
        var restrictedPrincipalTypes = new[]
        {
            typeof(Company),
            typeof(BudgetVersion),
            typeof(FiscalPeriod),
            typeof(MeasureDefinition),
            typeof(AppUser),
            typeof(ActualLedgerEntry)
        };

        foreach (var principalType in restrictedPrincipalTypes)
        {
            var foreignKey = entry.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == principalType);
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        }
    }

    [Fact]
    public void Ledger_amount_and_external_keys_use_financial_safe_storage_metadata()
    {
        using var db = CreateContext();
        var entry = db.Model.FindEntityType(typeof(ActualLedgerEntry))!;

        Assert.Equal(28, entry.FindProperty(nameof(ActualLedgerEntry.Amount))!.GetPrecision());
        Assert.Equal(8, entry.FindProperty(nameof(ActualLedgerEntry.Amount))!.GetScale());
        Assert.Equal(80, entry.FindProperty(nameof(ActualLedgerEntry.SourceSystem))!.GetMaxLength());
        Assert.Equal(160, entry.FindProperty(nameof(ActualLedgerEntry.ExternalDocumentId))!.GetMaxLength());
        Assert.Equal(160, entry.FindProperty(nameof(ActualLedgerEntry.ExternalLineId))!.GetMaxLength());
        Assert.Equal(64, entry.FindProperty(nameof(ActualLedgerEntry.PayloadHash))!.GetMaxLength());
        Assert.Equal(12, entry.FindProperty(nameof(ActualLedgerEntry.CurrencyCode))!.GetMaxLength());
        Assert.Equal(128, entry.FindProperty(nameof(ActualLedgerEntry.CoordinateHash))!.GetMaxLength());
        Assert.Equal(1000, entry.FindProperty(nameof(ActualLedgerEntry.Note))!.GetMaxLength());
        Assert.Equal(1000, entry.FindProperty(nameof(ActualLedgerEntry.ReversalReason))!.GetMaxLength());
    }
}
