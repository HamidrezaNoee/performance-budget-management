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
    public required string SourceSystem { get; set; }
    public required string ExternalDocumentId { get; set; }
    public required string ExternalLineId { get; set; }
    public required string PayloadHash { get; set; }
    public DateTime PostingDate { get; set; }
    public decimal Amount { get; set; }
    public required string CurrencyCode { get; set; }
    public required string CoordinateHash { get; set; }
    public required string CoordinatesJson { get; set; }
    public string? Note { get; set; }
    public string? ReversalReason { get; set; }

    public Company? Company { get; set; }
    public BudgetVersion? Version { get; set; }
    public FiscalPeriod? Period { get; set; }
    public MeasureDefinition? Measure { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public ActualLedgerEntry? OriginalEntry { get; set; }
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
