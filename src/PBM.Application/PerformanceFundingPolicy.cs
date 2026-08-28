namespace PBM.Application;

public sealed record PerformanceFundingCurrencySignal(
    string CurrencyCode,
    decimal AnnualBudget,
    decimal AnnualActual,
    decimal AnnualCommitment,
    decimal AnnualForecast,
    decimal YtdBudget,
    decimal YtdActual,
    decimal YtdCommitment,
    decimal YtdForecast);

public sealed record PerformanceFundingDecision(
    PerformanceFundingRecommendation Recommendation,
    IReadOnlyList<string> Reasons);

public static class PerformanceFundingPolicy
{
    public static PerformanceFundingDecision Evaluate(
        decimal dataCoveragePercent,
        decimal? weightedKpiScore,
        IReadOnlyList<PerformanceFundingCurrencySignal> currencies)
    {
        var reasons = new List<string>();
        if (!weightedKpiScore.HasValue || dataCoveragePercent < 50m)
        {
            reasons.Add(weightedKpiScore.HasValue
                ? $"KPI data coverage is only {dataCoveragePercent:0.##}%; at least 50% is required for a funding recommendation."
                : "No scored KPI observations are available for this fiscal year.");
            return new PerformanceFundingDecision(PerformanceFundingRecommendation.InsufficientData, reasons);
        }

        foreach (var currency in currencies)
        {
            if (currency.YtdBudget <= 0m && (currency.YtdActual > 0m || currency.YtdCommitment > 0m))
            {
                reasons.Add($"{currency.CurrencyCode}: YTD actual/commitment exists without YTD budget.");
                return new PerformanceFundingDecision(PerformanceFundingRecommendation.CorrectiveAction, reasons);
            }

            if (currency.AnnualBudget <= 0m && currency.AnnualForecast > 0m)
            {
                reasons.Add($"{currency.CurrencyCode}: forecast exists without annual budget.");
                return new PerformanceFundingDecision(PerformanceFundingRecommendation.CorrectiveAction, reasons);
            }

            var ytdExposure = Percent(currency.YtdActual + currency.YtdCommitment, currency.YtdBudget);
            if (ytdExposure.HasValue && ytdExposure.Value > 115m)
            {
                reasons.Add($"{currency.CurrencyCode}: YTD actual plus commitments are {ytdExposure.Value:0.##}% of YTD budget.");
                return new PerformanceFundingDecision(PerformanceFundingRecommendation.CorrectiveAction, reasons);
            }

            var forecast = Percent(currency.AnnualForecast, currency.AnnualBudget);
            if (forecast.HasValue && forecast.Value > 110m)
            {
                reasons.Add($"{currency.CurrencyCode}: annual forecast is {forecast.Value:0.##}% of annual budget.");
                return new PerformanceFundingDecision(PerformanceFundingRecommendation.CorrectiveAction, reasons);
            }
        }

        var score = weightedKpiScore.Value;
        if (score < 70m)
        {
            reasons.Add($"Weighted KPI score is {score:0.##}%, below the 70% funding-review threshold.");
            return new PerformanceFundingDecision(PerformanceFundingRecommendation.ReviewFunding, reasons);
        }

        if (score >= 110m && currencies.All(IsWithinPriorityFundingExposure))
        {
            reasons.Add($"Weighted KPI score is {score:0.##}% and no material budget overrun is visible.");
            return new PerformanceFundingDecision(PerformanceFundingRecommendation.PriorityForIncrement, reasons);
        }

        if (score >= 90m)
        {
            reasons.Add($"Weighted KPI score is {score:0.##}% and spending exposure is within control thresholds.");
            return new PerformanceFundingDecision(PerformanceFundingRecommendation.MaintainFunding, reasons);
        }

        reasons.Add($"Weighted KPI score is {score:0.##}%; performance should be monitored before changing funding.");
        return new PerformanceFundingDecision(PerformanceFundingRecommendation.MonitorClosely, reasons);
    }

    public static decimal? Percent(decimal numerator, decimal denominator) =>
        denominator == 0m
            ? null
            : Math.Round(numerator / denominator * 100m, 2, MidpointRounding.AwayFromZero);

    private static bool IsWithinPriorityFundingExposure(PerformanceFundingCurrencySignal currency)
    {
        var ytdExposure = Percent(currency.YtdActual + currency.YtdCommitment, currency.YtdBudget);
        var forecast = Percent(currency.AnnualForecast, currency.AnnualBudget);
        return (!ytdExposure.HasValue || ytdExposure.Value <= 105m)
            && (!forecast.HasValue || forecast.Value <= 105m);
    }
}
