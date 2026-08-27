namespace PBM.Application;

public enum ForecastMethod
{
    LinearTrend = 0,
    MovingAverage3 = 1
}

public sealed record ForecastPointDto(Guid PeriodId, string PeriodName, int Sequence, decimal? Actual, decimal Predicted, bool IsFuture);
public sealed record ForecastResultDto(Guid CompanyId, Guid FiscalYearId, Guid MeasureId, string MeasureName, ForecastMethod Method, decimal? Slope, decimal? Intercept, decimal? RSquared, IReadOnlyList<ForecastPointDto> Points);

public interface IForecastService
{
    Task<ForecastResultDto> GenerateAsync(Guid companyId, Guid fiscalYearId, Guid measureId, ForecastMethod method, CancellationToken cancellationToken = default);
}
