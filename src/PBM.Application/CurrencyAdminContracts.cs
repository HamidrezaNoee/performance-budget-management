namespace PBM.Application;

public sealed record SaveCurrencyRequest(
    Guid? Id,
    string Code,
    string Name,
    string? Symbol,
    bool IsBaseCurrency,
    bool IsActive);

public interface ICurrencyAdminService
{
    Task<CurrencyDto> SaveAsync(SaveCurrencyRequest request, CancellationToken cancellationToken = default);
}
