using PBM.Domain;

namespace PBM.Application;

public sealed record ExpensePlanningMemberDto(Guid Id, Guid DimensionId, string Code, string Name);
public sealed record ExpensePlanningDimensionDto(Guid Id, string Code, string Name, int Sequence, bool IsRequired, IReadOnlyList<ExpensePlanningMemberDto> Members);
public sealed record ExpensePlanningSetupDto(Guid ModelId, string ModelName, string BaseCurrencyCode, IReadOnlyList<ExpensePlanningDimensionDto> Dimensions, Guid MeasureId);
public sealed record ExpensePlanningPeriodValueDto(Guid PeriodId, string PeriodName, int Sequence, decimal Value, Guid? FactId = null);
public sealed record ExpensePlanningQueryRequest(Guid VersionId, IReadOnlyList<DimensionSelection> Dimensions, ValueKind ValueKind = ValueKind.Forecast);
public sealed record ExpensePlanningDataDto(IReadOnlyList<FiscalPeriodDto> Periods, IReadOnlyList<ExpensePlanningPeriodValueDto> Values);
public sealed record UpsertExpensePlanningCellRequest(Guid VersionId, Guid PeriodId, decimal Value, IReadOnlyList<DimensionSelection> Dimensions, ValueKind ValueKind = ValueKind.Forecast, string? Note = null);
public sealed record CreateExpenseItemRequest(Guid CompanyId, string Code, string Name);

public interface IExpensePlanningService
{
    Task<ExpensePlanningSetupDto> GetSetupAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<ExpensePlanningDataDto> QueryAsync(ExpensePlanningQueryRequest request, CancellationToken cancellationToken = default);
    Task<Guid> UpsertCellAsync(UpsertExpensePlanningCellRequest request, CancellationToken cancellationToken = default);
    Task<ExpensePlanningMemberDto> CreateItemAsync(CreateExpenseItemRequest request, CancellationToken cancellationToken = default);
}
