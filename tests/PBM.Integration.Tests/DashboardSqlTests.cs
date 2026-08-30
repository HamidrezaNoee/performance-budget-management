using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Integration.Tests;

[Collection("PBM SQL Integration")]
public sealed class DashboardSqlTests(PbmSqlFixture fixture)
{
    [Fact]
    public async Task Selectable_metric_summary_and_dimension_drilldown_use_the_same_measure()
    {
        if (!fixture.IsEnabled) return;

        await using var db = fixture.CreateContext();
        var user = fixture.CreateUserContext();
        var version = await db.BudgetVersions.Include(x => x.BudgetPlan).SingleAsync(x => x.Id == fixture.VersionId);
        var metric = new MeasureDefinition
        {
            BudgetModelId = version.BudgetPlan!.BudgetModelId,
            Code = "DASHBOARD_SQL_TEST",
            Name = "Dashboard SQL Test Amount",
            Unit = "IRR",
            ValueType = MeasureValueType.Amount,
            Aggregation = MeasureAggregation.Sum,
            DisplayOrder = 9_999
        };
        db.Measures.Add(metric);

        var baseCurrency = await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.IsBaseCurrency && x.IsActive)
            .Select(x => x.Code)
            .SingleAsync();
        var periodId = fixture.PeriodIds[1];
        const string coordinateHash = "DASHBOARD_SQL_TEST_COORDINATE";
        var coordinatesJson = JsonSerializer.Serialize(fixture.Dimensions.OrderBy(x => x.DimensionId));

        AddFact(ValueKind.Budget, 1_000_000m);
        AddFact(ValueKind.Actual, 400_000m);
        AddFact(ValueKind.Commitment, 100_000m);
        AddFact(ValueKind.Forecast, 1_200_000m);
        await db.SaveChangesAsync();

        var dashboard = new ExecutiveDashboardService(db, user, new TestDashboardMetricPolicy(metric.Code));

        var options = await dashboard.GetMetricOptionsAsync(fixture.CompanyId, fixture.FiscalYearId);
        var summary = await dashboard.GetSummaryForMeasureAsync(fixture.CompanyId, fixture.FiscalYearId, metric.Code);
        var dimensions = await dashboard.GetDrilldownDimensionsAsync(fixture.CompanyId, fixture.FiscalYearId, metric.Code);
        var rowDimension = dimensions.First(x => x.Id == fixture.Dimensions[0].DimensionId);
        var drilldown = await dashboard.GetDrilldownAsync(
            fixture.CompanyId,
            fixture.FiscalYearId,
            metric.Code,
            rowDimension.Id);
        var row = drilldown.Rows.Single(x => x.MemberId == fixture.Dimensions[0].MemberId);

        Assert.Contains(options, x => x.Code == metric.Code && x.CurrencyCode == baseCurrency);
        Assert.Equal(metric.Code, summary.MeasureCode);
        Assert.Equal(1_000_000m, summary.Summary.Budget);
        Assert.Equal(400_000m, summary.Summary.Actual);
        Assert.Equal(100_000m, summary.Summary.Commitment);
        Assert.Equal(1_200_000m, summary.Summary.Forecast);
        Assert.Equal(500_000m, summary.Summary.Remaining);
        Assert.Equal(-600_000m, summary.Summary.Variance);
        Assert.Equal(40m, summary.Summary.BudgetUtilizationPercent);

        Assert.Equal(metric.Code, drilldown.MeasureCode);
        Assert.Equal(baseCurrency, drilldown.CurrencyCode);
        Assert.Equal(1_000_000m, row.Budget);
        Assert.Equal(400_000m, row.Actual);
        Assert.Equal(100_000m, row.Commitment);
        Assert.Equal(1_200_000m, row.Forecast);
        Assert.Equal(500_000m, row.Remaining);
        Assert.Equal(-600_000m, row.Variance);
        Assert.Equal(40m, row.BudgetUtilizationPercent);

        void AddFact(ValueKind valueKind, decimal value)
        {
            var fact = new BudgetFact
            {
                VersionId = fixture.VersionId,
                PeriodId = periodId,
                MeasureId = metric.Id,
                ValueKind = valueKind,
                Value = value,
                CurrencyCode = baseCurrency,
                CoordinateHash = coordinateHash,
                CoordinatesJson = coordinatesJson,
                Source = "DASHBOARD_SQL_TEST"
            };
            foreach (var selection in fixture.Dimensions)
                fact.Dimensions.Add(new BudgetFactDimension
                {
                    BudgetFactId = fact.Id,
                    DimensionId = selection.DimensionId,
                    MemberId = selection.MemberId
                });
            db.BudgetFacts.Add(fact);
        }
    }

    private sealed class TestDashboardMetricPolicy : IDashboardMetricPolicy
    {
        public TestDashboardMetricPolicy(params string[] codes) => PreferredAmountMeasureCodes = codes;
        public IReadOnlyList<string> PreferredAmountMeasureCodes { get; }
    }
}
