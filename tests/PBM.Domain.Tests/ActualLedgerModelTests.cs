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
    public void Reversal_relationship_uses_original_entry_id()
    {
        using var db = CreateContext();
        var entry = db.Model.FindEntityType(typeof(ActualLedgerEntry))!;

        var selfForeignKey = entry.GetForeignKeys().Single(x =>
            x.PrincipalEntityType.ClrType == typeof(ActualLedgerEntry)
            && x.Properties.Select(p => p.Name).SequenceEqual([nameof(ActualLedgerEntry.OriginalEntryId)]));

        Assert.False(selfForeignKey.IsRequired);
    }

    [Fact]
    public void Ledger_dimension_has_its_own_key_and_entry_relationship()
    {
        using var db = CreateContext();
        var dimension = db.Model.FindEntityType(typeof(ActualLedgerDimension))!;

        Assert.Equal([nameof(ActualLedgerDimension.Id)], dimension.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(dimension.GetForeignKeys(), x =>
            x.PrincipalEntityType.ClrType == typeof(ActualLedgerEntry)
            && x.Properties.Select(p => p.Name).SequenceEqual([nameof(ActualLedgerDimension.EntryId)]));
    }

    [Fact]
    public void Ledger_amount_and_external_keys_use_financial_safe_storage_metadata()
    {
        using var db = CreateContext();
        var entry = db.Model.FindEntityType(typeof(ActualLedgerEntry))!;

        Assert.Equal("decimal(28,8)", entry.FindProperty(nameof(ActualLedgerEntry.Amount))!.GetColumnType());
        Assert.Equal(80, entry.FindProperty(nameof(ActualLedgerEntry.SourceSystem))!.GetMaxLength());
        Assert.Equal(160, entry.FindProperty(nameof(ActualLedgerEntry.ExternalDocumentId))!.GetMaxLength());
        Assert.Equal(160, entry.FindProperty(nameof(ActualLedgerEntry.ExternalLineId))!.GetMaxLength());
        Assert.Equal(64, entry.FindProperty(nameof(ActualLedgerEntry.PayloadHash))!.GetMaxLength());
        Assert.Equal(128, entry.FindProperty(nameof(ActualLedgerEntry.CoordinateHash))!.GetMaxLength());
    }
}
