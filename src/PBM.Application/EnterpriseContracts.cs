using PBM.Domain;

namespace PBM.Application;

public sealed record CurrencyDto(Guid Id, string Code, string Name, string? Symbol, bool IsBaseCurrency, bool IsActive);
public sealed record FxRateSourceDto(Guid Id, string Code, string Name, bool IsActive);
public sealed record FxRateDto(Guid Id, Guid SourceId, string SourceName, Guid FromCurrencyId, string FromCurrencyCode, Guid ToCurrencyId, string ToCurrencyCode, DateTime RateDate, decimal Rate, string? Note);
public sealed record UpsertFxRateRequest(Guid? Id, Guid SourceId, Guid FromCurrencyId, Guid ToCurrencyId, DateTime RateDate, decimal Rate, string? Note);
public sealed record KpiDto(Guid Id, string Code, string Name, string? Description, string? Unit, decimal Weight, decimal? Minimum, decimal? Maximum, string Frequency, string? FormulaExpression);
public sealed record CreateKpiRequest(string Code, string Name, string? Description, string? Unit, decimal Weight, decimal? Minimum, decimal? Maximum, string Frequency, string? FormulaExpression);
public sealed record KpiValueDto(Guid Id, Guid KpiId, Guid CompanyId, Guid PeriodId, decimal Target, decimal Actual, decimal? Score, decimal AchievementPercent);
public sealed record UpsertKpiValueRequest(Guid KpiId, Guid CompanyId, Guid PeriodId, decimal Target, decimal Actual);
public sealed record AuditLogDto(Guid Id, Guid? UserId, string EntityType, string EntityId, string Action, string? OldValueJson, string? NewValueJson, string? IpAddress, DateTime CreatedAtUtc);

public interface IReferenceDataService
{
    Task<IReadOnlyList<CurrencyDto>> GetCurrenciesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FxRateSourceDto>> GetFxRateSourcesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FxRateDto>> GetFxRatesAsync(DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default);
    Task<FxRateDto> UpsertFxRateAsync(UpsertFxRateRequest request, CancellationToken cancellationToken = default);
    Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, DateTime rateDate, Guid? sourceId = null, CancellationToken cancellationToken = default);
}

public interface IKpiService
{
    Task<IReadOnlyList<KpiDto>> GetKpisAsync(CancellationToken cancellationToken = default);
    Task<KpiDto> CreateKpiAsync(CreateKpiRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KpiValueDto>> GetValuesAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default);
    Task<KpiValueDto> UpsertValueAsync(UpsertKpiValueRequest request, CancellationToken cancellationToken = default);
}

public interface IAuditService
{
    Task<IReadOnlyList<AuditLogDto>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default);
}
