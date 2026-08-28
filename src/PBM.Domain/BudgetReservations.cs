namespace PBM.Domain;

public enum BudgetReservationStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Released = 3,
    Consumed = 4
}

public sealed class BudgetReservation : Entity
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid VersionId { get; set; }
    public Guid PeriodId { get; set; }
    public Guid MeasureId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public required string ReservationNo { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public BudgetReservationStatus Status { get; set; } = BudgetReservationStatus.Requested;
    public required string CoordinateHash { get; set; }
    public required string CoordinatesJson { get; set; }
    public string? DecisionComment { get; set; }
    public string? ExternalReference { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }

    public Company? Company { get; set; }
    public BudgetVersion? Version { get; set; }
    public FiscalPeriod? Period { get; set; }
    public MeasureDefinition? Measure { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public AppUser? DecidedByUser { get; set; }
    public ICollection<BudgetReservationDimension> Dimensions { get; set; } = [];
}

public sealed class BudgetReservationDimension
{
    public Guid ReservationId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid MemberId { get; set; }

    public BudgetReservation? Reservation { get; set; }
    public DimensionDefinition? Dimension { get; set; }
    public DimensionMember? Member { get; set; }
}
