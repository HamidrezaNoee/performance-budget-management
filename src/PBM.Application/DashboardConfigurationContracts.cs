namespace PBM.Application;

public interface IDashboardMetricPolicy
{
    IReadOnlyList<string> PreferredAmountMeasureCodes { get; }
}
