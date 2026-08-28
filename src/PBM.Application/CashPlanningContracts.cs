using PBM.Domain;

namespace PBM.Application;

public sealed record CashFlowItemDto(Guid Id, string Code, string Name);
public sealed record CashPlanVersionDto(Guid Id, Guid ScenarioId, int VersionNumber, string Name, BudgetStatus Status, bool IsLocked);

public sealed record CashPlanSetupDto(
    Guid BudgetModelId,
    Guid CashFlowItemDimensionId,
    Guid OpeningCashMeasureId,
    Guid CashInflowMeasureId,
    Guid CashOutflowMeasureId,
    Guid MinimumCashBufferMeasureId,
    Guid? BudgetPlanId,
    IReadOnlyList<CashPlanVersionDto> Versions,
    IReadOnlyList<CashFlowItemDto> Items);

public sealed record EnsureCashPlanRequest(Guid CompanyId, Guid FiscalYearId);

public sealed record CashPlanEntryDto(
    Guid FactId,
    Guid VersionId,
    Guid PeriodId,
    string PeriodName,
    int PeriodSequence,
    Guid ItemMemberId,
    string ItemCode,
    string ItemName,
    string MeasureCode,
    string MeasureName,
    ValueKind ValueKind,
    decimal Value,
    string CurrencyCode,
    string? Note,
    DateTime UpdatedAtUtc);

public sealed record UpsertCashPlanEntryRequest(
    Guid VersionId,
    Guid PeriodId,
    Guid ItemMemberId,
    string MeasureCode,
    ValueKind ValueKind,
    decimal Value,
    string CurrencyCode,
    string? Note);

public sealed record CashPlanMonthlyDto(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal BudgetOpening,
    decimal BudgetInflow,
    decimal BudgetOutflow,
    decimal BudgetClosing,
    decimal ActualOpening,
    decimal ActualInflow,
    decimal ActualOutflow,
    decimal ActualClosing,
    decimal ForecastOpening,
    decimal ForecastInflow,
    decimal ForecastOutflow,
    decimal ForecastClosing,
    decimal CommitmentOutflow,
    decimal ProjectedAvailable,
    decimal MinimumCashBuffer,
    decimal LiquidityGap);

public sealed record CashPlanCurrencySummaryDto(
    string CurrencyCode,
    decimal BudgetInflow,
    decimal BudgetOutflow,
    decimal ActualInflow,
    decimal ActualOutflow,
    decimal ForecastInflow,
    decimal ForecastOutflow,
    decimal CommitmentOutflow,
    decimal BudgetEndingCash,
    decimal ActualEndingCash,
    decimal ForecastEndingCash,
    decimal ProjectedAvailableEndingCash,
    decimal MinimumProjectedAvailableCash,
    decimal MaximumLiquidityShortfall,
    int MonthsBelowBuffer,
    IReadOnlyList<CashPlanMonthlyDto> Monthly);

public sealed record CashPlanSummaryDto(
    Guid VersionId,
    Guid CompanyId,
    Guid FiscalYearId,
    IReadOnlyList<CashPlanCurrencySummaryDto> Currencies);

public interface ICashPlanningService
{
    Task<CashPlanSetupDto> GetSetupAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default);
    Task<CashPlanSetupDto> EnsurePlanAsync(EnsureCashPlanRequest request, CancellationToken cancellationToken = default);
    Task<CashPlanSummaryDto> GetSummaryAsync(Guid versionId, string? currencyCode = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashPlanEntryDto>> GetEntriesAsync(Guid versionId, string? currencyCode = null, Guid? periodId = null, CancellationToken cancellationToken = default);
    Task<Guid> UpsertEntryAsync(UpsertCashPlanEntryRequest request, CancellationToken cancellationToken = default);
}
