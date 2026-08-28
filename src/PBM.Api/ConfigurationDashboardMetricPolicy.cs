using PBM.Application;

namespace PBM.Api;

public sealed class ConfigurationDashboardMetricPolicy : IDashboardMetricPolicy
{
    private static readonly string[] Defaults = ["NET_SALES", "GROSS_SALES", "EXPENSE_AMOUNT", "STATEMENT_AMOUNT", "FINANCE_AMOUNT"];

    public ConfigurationDashboardMetricPolicy(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Dashboard:PreferredAmountMeasureCodes").Get<string[]>() ?? [];
        PreferredAmountMeasureCodes = configured
            .Select(x => x?.Trim().ToUpperInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (PreferredAmountMeasureCodes.Count == 0)
            PreferredAmountMeasureCodes = Defaults;
    }

    public IReadOnlyList<string> PreferredAmountMeasureCodes { get; private set; }
}
