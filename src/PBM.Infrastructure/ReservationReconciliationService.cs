using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ReservationReconciliationService(
    PbmDbContext db,
    IUserContext user) : IReservationReconciliationService
{
    public async Task<ReservationReconciliationSummaryDto> GetAsync(
        Guid companyId,
        Guid? fiscalYearId = null,
        int graceDays = 2,
        decimal tolerancePercent = 0.1m,
        CancellationToken cancellationToken = default)
    {
        EnsureCompanyRead(companyId);
        if (graceDays is < 0 or > 365) throw new ArgumentOutOfRangeException(nameof(graceDays), "Grace days must be between 0 and 365.");
        if (tolerancePercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(tolerancePercent), "Tolerance percent must be between 0 and 100.");
        if (fiscalYearId.HasValue && !await db.FiscalYears.AsNoTracking().AnyAsync(
                x => x.Id == fiscalYearId.Value && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var reservationQuery = db.BudgetReservations.AsNoTracking()
            .Include(x => x.Version).ThenInclude(x => x!.BudgetPlan)
            .Include(x => x.Period)
            .Include(x => x.Measure)
            .Include(x => x.Dimensions)
            .Where(x => x.TenantId == user.TenantId
                && x.CompanyId == companyId
                && x.Status == BudgetReservationStatus.Consumed
                && x.ConsumedAtUtc != null);
        if (fiscalYearId.HasValue)
            reservationQuery = reservationQuery.Where(x => x.Version!.BudgetPlan!.FiscalYearId == fiscalYearId.Value);

        var reservations = await reservationQuery.ToListAsync(cancellationToken);
        if (reservations.Count == 0)
            return Empty(companyId, fiscalYearId, graceDays, tolerancePercent);

        var versionIds = reservations.Select(x => x.VersionId).Distinct().ToArray();
        var actualFacts = await db.BudgetFacts.AsNoTracking()
            .Where(x => versionIds.Contains(x.VersionId) && x.ValueKind == ValueKind.Actual)
            .ToListAsync(cancellationToken);
        var actualByCoordinate = actualFacts.ToDictionary(
            x => (x.VersionId, x.PeriodId, x.MeasureId, x.CoordinateHash),
            x => x);

        var baseCurrency = await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsBaseCurrency)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "IRR";
        var now = DateTime.UtcNow;

        var items = new List<ReservationReconciliationItemDto>();
        foreach (var group in reservations.GroupBy(x => (x.VersionId, x.PeriodId, x.MeasureId, x.CoordinateHash)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = group.OrderBy(x => x.ConsumedAtUtc).First();
            var firstConsumed = group.Min(x => x.ConsumedAtUtc)!.Value;
            var lastConsumed = group.Max(x => x.ConsumedAtUtc)!.Value;
            var days = Math.Max(0, (now.Date - firstConsumed.Date).Days);
            var consumed = group.Sum(x => x.Amount);
            actualByCoordinate.TryGetValue(group.Key, out var actual);

            var reservationCurrencies = group
                .Select(x => NormalizeCurrency(x.CurrencyCode, baseCurrency))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var displayCurrency = reservationCurrencies.Length == 1 ? reservationCurrencies[0] : "MIXED";
            var actualCurrency = actual is null ? null : NormalizeCurrency(actual.CurrencyCode, baseCurrency);
            var currencyConflict = reservationCurrencies.Length > 1
                || (actualCurrency is not null
                    && reservationCurrencies.Length == 1
                    && !string.Equals(reservationCurrencies[0], actualCurrency, StringComparison.OrdinalIgnoreCase));

            var decision = ReservationReconciliationPolicy.Evaluate(
                consumed,
                actual?.Value,
                days,
                graceDays,
                tolerancePercent,
                currencyConflict);

            var dimensions = first.Dimensions
                .OrderBy(x => x.DimensionId)
                .Select(x => new DimensionSelection(x.DimensionId, x.MemberId))
                .ToList();
            var externalReferences = group.Select(x => x.ExternalReference?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            items.Add(new ReservationReconciliationItemDto(
                companyId,
                first.Version!.BudgetPlan!.FiscalYearId,
                first.VersionId,
                first.Version.VersionNumber,
                first.PeriodId,
                first.Period?.Name ?? "-",
                first.MeasureId,
                first.Measure?.Code ?? "-",
                first.Measure?.Name ?? "-",
                first.CoordinateHash,
                displayCurrency,
                group.Select(x => x.ReservationNo).OrderBy(x => x).ToList(),
                externalReferences,
                group.Count(),
                consumed,
                decision.ActualAmount,
                decision.Variance,
                decision.AllowedTolerance,
                firstConsumed,
                lastConsumed,
                days,
                decision.Status,
                actual?.Id,
                actual?.Source,
                actual?.UpdatedAtUtc,
                dimensions));
        }

        items = items
            .OrderBy(x => StatusPriority(x.Status))
            .ThenByDescending(x => x.DaysSinceFirstConsumption)
            .ThenBy(x => x.PeriodName)
            .ThenBy(x => x.MeasureName)
            .ToList();

        var currencySummaries = items.GroupBy(x => x.CurrencyCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key)
            .Select(group => new ReservationReconciliationCurrencySummaryDto(
                group.Key,
                group.Count(),
                group.Count(x => x.Status == ReservationReconciliationStatus.Reconciled),
                group.Count(x => x.Status != ReservationReconciliationStatus.Reconciled),
                group.Sum(x => x.ConsumedAmount),
                group.Sum(x => x.ActualAmount),
                group.Sum(x => x.Variance)))
            .ToList();

        return new ReservationReconciliationSummaryDto(
            companyId,
            fiscalYearId,
            graceDays,
            tolerancePercent,
            items.Count,
            items.Count(x => x.Status == ReservationReconciliationStatus.Reconciled),
            items.Count(x => x.Status == ReservationReconciliationStatus.AwaitingActual),
            items.Count(x => x.Status == ReservationReconciliationStatus.MissingActual),
            items.Count(x => x.Status == ReservationReconciliationStatus.UnderPosted),
            items.Count(x => x.Status == ReservationReconciliationStatus.OverPosted),
            items.Count(x => x.Status == ReservationReconciliationStatus.CurrencyConflict),
            currencySummaries,
            items);
    }

    private static ReservationReconciliationSummaryDto Empty(Guid companyId, Guid? fiscalYearId, int graceDays, decimal tolerancePercent) =>
        new(companyId, fiscalYearId, graceDays, tolerancePercent, 0, 0, 0, 0, 0, 0, 0, [], []);

    private static string NormalizeCurrency(string? code, string baseCurrency) =>
        string.IsNullOrWhiteSpace(code) ? baseCurrency : code.Trim().ToUpperInvariant();

    private static int StatusPriority(ReservationReconciliationStatus status) => status switch
    {
        ReservationReconciliationStatus.CurrencyConflict => 0,
        ReservationReconciliationStatus.MissingActual => 1,
        ReservationReconciliationStatus.UnderPosted => 2,
        ReservationReconciliationStatus.OverPosted => 3,
        ReservationReconciliationStatus.AwaitingActual => 4,
        ReservationReconciliationStatus.Reconciled => 5,
        _ => 9
    };

    private void EnsureCompanyRead(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }
}
