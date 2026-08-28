using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PBM.Domain;

public enum ActualLedgerEntryType
{
    Posting,
    Reversal
}

public sealed class ActualLedgerEntry : Entity
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid VersionId { get; set; }
    public Guid PeriodId { get; set; }
    public Guid MeasureId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? OriginalEntryId { get; set; }
    public ActualLedgerEntryType EntryType { get; set; } = ActualLedgerEntryType.Posting;

    [MaxLength(80)]
    public required string SourceSystem { get; set; }

    [MaxLength(160)]
    public required string ExternalDocumentId { get; set; }

    [MaxLength(160)]
    public required string ExternalLineId { get; set; }

    [MaxLength(64)]
    public required string PayloadHash { get; set; }

    public DateTime PostingDate { get; set; }

    [Column(TypeName = "decimal(28,8)")]
    public decimal Amount { get; set; }

    [MaxLength(12)]
    public required string CurrencyCode { get; set; }

    [MaxLength(128)]
    public required string CoordinateHash { get; set; }

    public required string CoordinatesJson { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    [MaxLength(1000)]
    public string? ReversalReason { get; set; }

    public Company? Company { get; set; }
    public BudgetVersion? Version { get; set; }
    public FiscalPeriod? Period { get; set; }
    public MeasureDefinition? Measure { get; set; }
    public AppUser? CreatedByUser { get; set; }

    [ForeignKey(nameof(OriginalEntryId))]
    public ActualLedgerEntry? OriginalEntry { get; set; }

    [InverseProperty(nameof(OriginalEntry))]
    public ICollection<ActualLedgerEntry> Reversals { get; set; } = [];

    public ICollection<ActualLedgerDimension> Dimensions { get; set; } = [];
}

public sealed class ActualLedgerDimension : Entity
{
    public Guid EntryId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid MemberId { get; set; }
    public ActualLedgerEntry? Entry { get; set; }
    public DimensionDefinition? Dimension { get; set; }
    public DimensionMember? Member { get; set; }
}
