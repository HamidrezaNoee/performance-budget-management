using PBM.Domain;

namespace PBM.Application;

public sealed record BudgetTransferDimensionInput(
    Guid DimensionId,
    Guid SourceMemberId,
    Guid DestinationMemberId);

public sealed record CreateBudgetTransferRequest(
    Guid CompanyId,
    Guid VersionId,
    Guid MeasureId,
    Guid SourcePeriodId,
    Guid DestinationPeriodId,
    decimal Amount,
    string? CurrencyCode,
    string Description,
    IReadOnlyList<BudgetTransferDimensionInput> Dimensions,
    string? ExternalReference = null);

public sealed record BudgetTransferDecisionRequest(string? Comment);

public sealed record BudgetTransferDto(
    Guid Id,
    string TransferNo,
    Guid CompanyId,
    Guid VersionId,
    int VersionNumber,
    Guid MeasureId,
    string MeasureName,
    Guid SourcePeriodId,
    string SourcePeriodName,
    Guid DestinationPeriodId,
    string DestinationPeriodName,
    decimal Amount,
    string? CurrencyCode,
    BudgetTransferStatus Status,
    string Description,
    string? ExternalReference,
    Guid RequestedByUserId,
    string RequestedByDisplayName,
    Guid? DecidedByUserId,
    string? DecidedByDisplayName,
    string? DecisionComment,
    DateTime CreatedAtUtc,
    DateTime? DecidedAtUtc,
    IReadOnlyList<BudgetTransferDimensionInput> Dimensions);

public sealed record BudgetTransferAvailabilityDto(
    decimal SourceBudget,
    decimal SourceActual,
    decimal SourceCommitment,
    decimal SourceAvailable,
    decimal DestinationBudget);

public interface IBudgetTransferService
{
    Task<IReadOnlyList<BudgetTransferDto>> GetAsync(Guid companyId, Guid? fiscalYearId = null, BudgetTransferStatus? status = null, int take = 100, CancellationToken cancellationToken = default);
    Task<BudgetTransferAvailabilityDto> GetAvailabilityAsync(CreateBudgetTransferRequest request, CancellationToken cancellationToken = default);
    Task<BudgetTransferDto> CreateAsync(CreateBudgetTransferRequest request, CancellationToken cancellationToken = default);
    Task<BudgetTransferDto> ApproveAsync(Guid transferId, BudgetTransferDecisionRequest request, CancellationToken cancellationToken = default);
    Task<BudgetTransferDto> RejectAsync(Guid transferId, BudgetTransferDecisionRequest request, CancellationToken cancellationToken = default);
}
