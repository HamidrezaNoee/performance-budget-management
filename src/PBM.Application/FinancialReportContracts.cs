using PBM.Domain;

namespace PBM.Application;

public enum FinancialReportType
{
    ProfitLoss = 0,
    BalanceSheet = 1,
    CashFlow = 2
}

public sealed record FinancialReportCellDto(Guid PeriodId, string PeriodName, int Sequence, decimal Value);
public sealed record FinancialReportRowDto(string Code, string Name, int DisplayOrder, IReadOnlyList<FinancialReportCellDto> Periods, decimal Total);
public sealed record FinancialReportDto(
    FinancialReportType Type,
    Guid CompanyId,
    Guid FiscalYearId,
    Guid? VersionId,
    string? VersionName,
    ValueKind ValueKind,
    IReadOnlyList<FinancialReportRowDto> Rows);

public interface IFinancialReportService
{
    Task<FinancialReportDto> GetAsync(Guid companyId, Guid fiscalYearId, FinancialReportType type, ValueKind valueKind = ValueKind.Budget, Guid? versionId = null, CancellationToken cancellationToken = default);
}
