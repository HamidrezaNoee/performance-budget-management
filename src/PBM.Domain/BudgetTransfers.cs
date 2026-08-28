namespace PBM.Domain;

public enum BudgetTransferStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2
}

public sealed class BudgetTransfer : Entity
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid VersionId { get; set; }
    public Guid MeasureId { get; set; }
    public Guid SourcePeriodId { get; set; }
    public Guid DestinationPeriodId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public required string TransferNo { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public BudgetTransferStatus Status { get; set; } = BudgetTransferStatus.Requested;
    public required string SourceCoordinateHash { get; set; }
    public required string SourceCoordinatesJson { get; set; }
    public required string DestinationCoordinateHash { get; set; }
    public required string DestinationCoordinatesJson { get; set; }
    public string? DecisionComment { get; set; }
    public string? ExternalReference { get; set; }
    public DateTime? DecidedAtUtc { get; set; }

    public Company? Company { get; set; }
    public BudgetVersion? Version { get; set; }
    public MeasureDefinition? Measure { get; set; }
    public FiscalPeriod? SourcePeriod { get; set; }
    public FiscalPeriod? DestinationPeriod { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public AppUser? DecidedByUser { get; set; }
    public ICollection<BudgetTransferDimension> Dimensions { get; set; } = [];
}

public sealed class BudgetTransferDimension
{
    public Guid TransferId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid SourceMemberId { get; set; }
    public Guid DestinationMemberId { get; set; }

    public BudgetTransfer? Transfer { get; set; }
    public DimensionDefinition? Dimension { get; set; }
    public DimensionMember? SourceMember { get; set; }
    public DimensionMember? DestinationMember { get; set; }
}
