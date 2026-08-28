namespace PBM.Application;

public sealed record CashRollForwardPeriodInput(
    Guid PeriodId,
    string PeriodName,
    int Sequence,
    decimal? BudgetOpening,
    decimal BudgetInflow,
    decimal BudgetOutflow,
    decimal? ActualOpening,
    decimal ActualInflow,
    decimal ActualOutflow,
    decimal? ForecastOpening,
    decimal? ForecastInflow,
    decimal? ForecastOutflow,
    decimal CommitmentOutflow,
    decimal? MinimumCashBuffer);

public static class CashRollForwardCalculator
{
    public static CashPlanCurrencySummaryDto Calculate(
        string currencyCode,
        IReadOnlyList<CashRollForwardPeriodInput> periods)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));

        decimal previousBudgetClosing = 0m;
        decimal previousActualClosing = 0m;
        decimal previousForecastClosing = 0m;
        var monthly = new List<CashPlanMonthlyDto>(periods.Count);

        foreach (var period in periods.OrderBy(x => x.Sequence))
        {
            var budgetOpening = period.BudgetOpening ?? previousBudgetClosing;
            var budgetClosing = budgetOpening + period.BudgetInflow - period.BudgetOutflow;

            var actualOpening = period.ActualOpening ?? previousActualClosing;
            var actualClosing = actualOpening + period.ActualInflow - period.ActualOutflow;

            var forecastOpening = period.ForecastOpening ?? previousForecastClosing;
            if (!period.ForecastOpening.HasValue && monthly.Count == 0)
                forecastOpening = period.ActualOpening ?? period.BudgetOpening ?? 0m;
            var forecastInflow = period.ForecastInflow ?? period.BudgetInflow;
            var forecastOutflow = period.ForecastOutflow ?? period.BudgetOutflow;
            var forecastClosing = forecastOpening + forecastInflow - forecastOutflow;

            var projectedAvailable = forecastClosing - period.CommitmentOutflow;
            var minimumBuffer = period.MinimumCashBuffer ?? 0m;
            var liquidityGap = projectedAvailable - minimumBuffer;

            monthly.Add(new CashPlanMonthlyDto(
                period.PeriodId,
                period.PeriodName,
                period.Sequence,
                budgetOpening,
                period.BudgetInflow,
                period.BudgetOutflow,
                budgetClosing,
                actualOpening,
                period.ActualInflow,
                period.ActualOutflow,
                actualClosing,
                forecastOpening,
                forecastInflow,
                forecastOutflow,
                forecastClosing,
                period.CommitmentOutflow,
                projectedAvailable,
                minimumBuffer,
                liquidityGap));

            previousBudgetClosing = budgetClosing;
            previousActualClosing = actualClosing;
            previousForecastClosing = forecastClosing;
        }

        var ending = monthly.LastOrDefault();
        var minimumProjected = monthly.Count == 0 ? 0m : monthly.Min(x => x.ProjectedAvailable);
        var maximumShortfall = monthly.Count == 0 ? 0m : Math.Max(0m, -monthly.Min(x => x.LiquidityGap));

        return new CashPlanCurrencySummaryDto(
            currencyCode.Trim().ToUpperInvariant(),
            monthly.Sum(x => x.BudgetInflow),
            monthly.Sum(x => x.BudgetOutflow),
            monthly.Sum(x => x.ActualInflow),
            monthly.Sum(x => x.ActualOutflow),
            monthly.Sum(x => x.ForecastInflow),
            monthly.Sum(x => x.ForecastOutflow),
            monthly.Sum(x => x.CommitmentOutflow),
            ending?.BudgetClosing ?? 0m,
            ending?.ActualClosing ?? 0m,
            ending?.ForecastClosing ?? 0m,
            ending?.ProjectedAvailable ?? 0m,
            minimumProjected,
            maximumShortfall,
            monthly.Count(x => x.LiquidityGap < 0m),
            monthly);
    }
}
