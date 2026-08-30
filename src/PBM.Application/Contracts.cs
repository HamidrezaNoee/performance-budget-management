using PBM.Domain;

namespace PBM.Application;

public sealed record CompanyDto(Guid Id, Guid TenantId, string Code, string Name, string? Industry);
public sealed record FiscalYearDto(Guid Id, string Code, string Name, int JalaliYear, DateTime StartDate, DateTime EndDate, bool IsClosed);
public sealed record FiscalPeriodDto(Guid Id, int Sequence, string Code, string Name, int JalaliMonth, DateTime StartDate, DateTime EndDate, bool IsClosed);
public sealed record BudgetModelDto(Guid Id, string Code, string Name, string? Description);
public sealed record DimensionDto(Guid Id, string Code, string Name, int Sequence, bool IsRequired);
public sealed record DimensionMemberDto(Guid Id, Guid DimensionId, Guid? ParentId, Guid? CompanyId, string Code, string Name);
public sealed record MeasureDto(Guid Id, string Code, string Name, string? Unit, MeasureValueType ValueType, MeasureAggregation Aggregation, bool IsCalculated, string? FormulaExpression, int DisplayOrder);
public sealed record BudgetPlanDto(Guid Id, Guid CompanyId, Guid FiscalYearId, Guid BudgetModelId, string Name, BudgetStatus Status, IReadOnlyList<BudgetVersionDto> Versions);
public sealed record BudgetVersionDto(Guid Id, Guid ScenarioId, int VersionNumber, string Name, BudgetStatus Status, bool IsLocked);
public sealed record DimensionSelection(Guid DimensionId, Guid MemberId);
public sealed record CreateBudgetPlanRequest(Guid CompanyId, Guid FiscalYearId, Guid BudgetModelId, string Name, Guid? ScenarioId = null);
public sealed record UpsertBudgetFactRequest(Guid VersionId, Guid PeriodId, Guid MeasureId, ValueKind ValueKind, decimal Value, string? CurrencyCode, IReadOnlyList<DimensionSelection> Dimensions, string? Source, string? Note);
public sealed record MonthlySeriesPointDto(Guid PeriodId, string PeriodName, int Sequence, decimal Budget, decimal Actual, decimal Commitment, decimal Forecast);
public sealed record DashboardSummaryDto(decimal Budget, decimal Actual, decimal Commitment, decimal Forecast, decimal Remaining, decimal Variance, decimal BudgetUtilizationPercent, IReadOnlyList<MonthlySeriesPointDto> Monthly);
public sealed record BudgetGridQuery(Guid VersionId, Guid RowDimensionId, Guid MeasureId, ValueKind ValueKind, IReadOnlyList<DimensionSelection> Filters);
public sealed record BudgetGridCellDto(Guid PeriodId, Guid? FactId, decimal Value);
public sealed record BudgetGridRowDto(Guid MemberId, string Code, string Name, IReadOnlyList<BudgetGridCellDto> Cells);
public sealed record BudgetGridDto(IReadOnlyList<FiscalPeriodDto> Periods, MeasureDto Measure, DimensionDto RowDimension, IReadOnlyList<BudgetGridRowDto> Rows);

public interface IFormulaEngine
{
    decimal Evaluate(string expression, IReadOnlyDictionary<string, decimal> variables);
}

public interface ICompanyService
{
    Task<IReadOnlyList<CompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default);
}

public interface IBudgetService
{
    Task<IReadOnlyList<FiscalYearDto>> GetFiscalYearsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FiscalPeriodDto>> GetPeriodsAsync(Guid fiscalYearId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BudgetModelDto>> GetModelsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DimensionDto>> GetDimensionsAsync(Guid modelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DimensionMemberDto>> GetDimensionMembersAsync(Guid dimensionId, Guid? companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MeasureDto>> GetMeasuresAsync(Guid modelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BudgetPlanDto>> GetPlansAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default);
    Task<BudgetPlanDto> CreatePlanAsync(CreateBudgetPlanRequest request, CancellationToken cancellationToken = default);
    Task<Guid> UpsertFactAsync(UpsertBudgetFactRequest request, CancellationToken cancellationToken = default);
    Task<BudgetGridDto> GetGridAsync(BudgetGridQuery query, CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default);
}
