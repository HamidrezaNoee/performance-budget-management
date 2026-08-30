namespace PBM.Application;

public sealed record FiscalPeriodDetailsDto(
    Guid Id,
    Guid FiscalYearId,
    int Sequence,
    string Code,
    string Name,
    int JalaliMonth,
    DateTime StartDate,
    DateTime EndDate,
    bool IsClosed);

public sealed record FiscalYearDetailsDto(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    int JalaliYear,
    DateTime StartDate,
    DateTime EndDate,
    bool IsClosed,
    IReadOnlyList<FiscalPeriodDetailsDto> Periods);

public sealed record CreateFiscalYearRequest(
    Guid CompanyId,
    string Code,
    string Name,
    int JalaliYear,
    int StartJalaliMonth = 1,
    int MonthCount = 12);

public sealed record CreateFiscalPeriodRequest(
    int Sequence,
    string Code,
    string Name,
    int JalaliMonth,
    DateTime StartDate,
    DateTime EndDate);

public sealed record SetFiscalPeriodClosedRequest(bool IsClosed);
public sealed record SetFiscalYearClosedRequest(bool IsClosed);

public interface IFiscalCalendarService
{
    Task<IReadOnlyList<FiscalYearDetailsDto>> GetYearsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FiscalYearDetailsDto> CreateYearAsync(CreateFiscalYearRequest request, CancellationToken cancellationToken = default);
    Task<FiscalPeriodDetailsDto> AddPeriodAsync(Guid fiscalYearId, CreateFiscalPeriodRequest request, CancellationToken cancellationToken = default);
    Task<FiscalPeriodDetailsDto> SetPeriodClosedAsync(Guid periodId, bool isClosed, CancellationToken cancellationToken = default);
    Task<FiscalYearDetailsDto> SetYearClosedAsync(Guid fiscalYearId, bool isClosed, CancellationToken cancellationToken = default);
}
