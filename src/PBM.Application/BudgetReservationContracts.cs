using PBM.Domain;

namespace PBM.Application;

public sealed record CreateBudgetReservationRequest(
    Guid CompanyId,
    Guid VersionId,
    Guid PeriodId,
    Guid MeasureId,
    decimal Amount,
    string? CurrencyCode,
    string Description,
    IReadOnlyList<DimensionSelection> Dimensions,
    string? ExternalReference = null);

public sealed record BudgetReservationDecisionRequest(string? Comment);
public sealed record ConsumeBudgetReservationRequest(string? ExternalReference, string? Comment);

public sealed record BudgetReservationDto(
    Guid Id,
    string ReservationNo,
    Guid CompanyId,
    Guid VersionId,
    int VersionNumber,
    Guid PeriodId,
    string PeriodName,
    Guid MeasureId,
    string MeasureName,
    decimal Amount,
    string? CurrencyCode,
    BudgetReservationStatus Status,
    string Description,
    string? ExternalReference,
    Guid RequestedByUserId,
    string RequestedByDisplayName,
    Guid? DecidedByUserId,
    string? DecidedByDisplayName,
    string? DecisionComment,
    DateTime CreatedAtUtc,
    DateTime? DecidedAtUtc,
    DateTime? ReleasedAtUtc,
    DateTime? ConsumedAtUtc,
    IReadOnlyList<DimensionSelection> Dimensions);

public sealed record BudgetAvailabilityDto(
    decimal Budget,
    decimal Actual,
    decimal Commitment,
    decimal Available);

public interface IBudgetReservationService
{
    Task<IReadOnlyList<BudgetReservationDto>> GetAsync(Guid companyId, BudgetReservationStatus? status = null, int take = 100, CancellationToken cancellationToken = default);
    Task<BudgetAvailabilityDto> GetAvailabilityAsync(Guid versionId, Guid periodId, Guid measureId, IReadOnlyList<DimensionSelection> dimensions, CancellationToken cancellationToken = default);
    Task<BudgetReservationDto> CreateAsync(CreateBudgetReservationRequest request, CancellationToken cancellationToken = default);
    Task<BudgetReservationDto> ApproveAsync(Guid reservationId, BudgetReservationDecisionRequest request, CancellationToken cancellationToken = default);
    Task<BudgetReservationDto> RejectAsync(Guid reservationId, BudgetReservationDecisionRequest request, CancellationToken cancellationToken = default);
    Task<BudgetReservationDto> ReleaseAsync(Guid reservationId, BudgetReservationDecisionRequest request, CancellationToken cancellationToken = default);
    Task<BudgetReservationDto> ConsumeAsync(Guid reservationId, ConsumeBudgetReservationRequest request, CancellationToken cancellationToken = default);
}
