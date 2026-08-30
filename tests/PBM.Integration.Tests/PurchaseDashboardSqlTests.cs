using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Integration.Tests;

[Collection("PBM SQL Integration")]
public sealed class PurchaseDashboardSqlTests(PbmSqlFixture fixture)
{
    [Fact]
    public async Task Purchase_budget_actual_and_forecast_roll_up_by_month_cost_and_product_dimension()
    {
        if (!fixture.IsEnabled) return;

        await using var db = fixture.CreateContext();
        var user = fixture.CreateUserContext();
        var calculation = new CalculationService(db, user, new FormulaEngine());
        var budget = new BudgetService(db, user, calculation);
        var planning = new PurchaseForecastService(db, user, budget);
        var dashboard = new PurchaseDashboardService(db, user);

        var setup = await planning.GetSetupAsync(fixture.CompanyId);
        var productDimension = setup.Dimensions.Single(x => x.Code == "PRODUCT");
        var product = new DimensionMember
        {
            DimensionId = productDimension.Id,
            CompanyId = fixture.CompanyId,
            Code = "PURCHASE_DASHBOARD_PRODUCT_TEST",
            Name = "کالای تست داشبورد خرید"
        };
        db.DimensionMembers.Add(product);
        await db.SaveChangesAsync();

        var costType = await planning.CreateCostTypeAsync(new CreatePurchaseCostTypeRequest(
            fixture.CompanyId,
            "PURCHASE_DASHBOARD_COST_TEST",
            "هزینه تست داشبورد خرید"));
        var dimensions = new List<DimensionSelection> { new(productDimension.Id, product.Id) };
        var periodId = fixture.PeriodIds[3];

        await planning.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_FORECAST_QTY", 100m, dimensions,
            ValueKind: ValueKind.Budget));
        await planning.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_FORECAST_AMOUNT", 1_000_000m, dimensions,
            ValueKind: ValueKind.Budget));
        await planning.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_COST_RATE", 10m, dimensions, costType.Id,
            ValueKind: ValueKind.Budget));

        await planning.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_FORECAST_QTY", 120m, dimensions,
            ValueKind: ValueKind.Forecast));
        await planning.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_FORECAST_AMOUNT", 1_200_000m, dimensions,
            ValueKind: ValueKind.Forecast));
        await planning.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_COST_RATE", 12m, dimensions, costType.Id,
            ValueKind: ValueKind.Forecast));

        // Emulate controlled Actual Ledger / ERP projection into the same purchase coordinates.
        await budget.UpsertFactAsync(new UpsertBudgetFactRequest(
            fixture.VersionId, periodId, setup.QuantityMeasure.Id, ValueKind.Actual, 110m, null,
            dimensions, "IntegrationActual", "Actual purchase quantity"));
        await budget.UpsertFactAsync(new UpsertBudgetFactRequest(
            fixture.VersionId, periodId, setup.AmountMeasure.Id, ValueKind.Actual, 1_100_000m, setup.BaseCurrencyCode,
            dimensions, "IntegrationActual", "Actual purchase amount"));
        var purchaseCostDimensionId = await db.Dimensions.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.Code == "PURCHASECOST")
            .Select(x => x.Id).SingleAsync();
        var costDimensions = dimensions.Concat([new DimensionSelection(purchaseCostDimensionId, costType.Id)]).ToList();
        await budget.UpsertFactAsync(new UpsertBudgetFactRequest(
            fixture.VersionId, periodId, setup.CostAmountMeasure.Id, ValueKind.Actual, 121_000m, setup.BaseCurrencyCode,
            costDimensions, "IntegrationActual", "Actual purchase landed cost component"));

        await Assert.ThrowsAsync<ArgumentException>(() => planning.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_FORECAST_QTY", 999m, dimensions,
            ValueKind: ValueKind.Actual)));

        var budgetData = await planning.QueryAsync(new PurchaseForecastQueryRequest(
            fixture.VersionId, dimensions, ValueKind.Budget));
        var forecastData = await planning.QueryAsync(new PurchaseForecastQueryRequest(
            fixture.VersionId, dimensions, ValueKind.Forecast));
        var result = await dashboard.GetAsync(fixture.CompanyId, fixture.FiscalYearId, productDimension.Id, 50);

        Assert.NotNull(result);
        Assert.Equal(100m, budgetData.Quantity.Single(x => x.PeriodId == periodId).Value);
        Assert.Equal(1_000_000m, budgetData.Amount.Single(x => x.PeriodId == periodId).Value);
        Assert.Equal(100_000m, budgetData.Costs.Single(x => x.CostTypeId == costType.Id).Amounts.Single(x => x.PeriodId == periodId).Value);
        Assert.Equal(120m, forecastData.Quantity.Single(x => x.PeriodId == periodId).Value);
        Assert.Equal(1_200_000m, forecastData.Amount.Single(x => x.PeriodId == periodId).Value);
        Assert.Equal(144_000m, forecastData.Costs.Single(x => x.CostTypeId == costType.Id).Amounts.Single(x => x.PeriodId == periodId).Value);

        var monthly = result!.Monthly.Single(x => x.PeriodId == periodId);
        Assert.Equal(100m, monthly.BudgetQuantity);
        Assert.Equal(110m, monthly.ActualQuantity);
        Assert.Equal(120m, monthly.ForecastQuantity);
        Assert.Equal(1_000_000m, monthly.BudgetPurchaseAmount);
        Assert.Equal(1_100_000m, monthly.ActualPurchaseAmount);
        Assert.Equal(1_200_000m, monthly.ForecastPurchaseAmount);
        Assert.Equal(100_000m, monthly.BudgetCostAmount);
        Assert.Equal(121_000m, monthly.ActualCostAmount);
        Assert.Equal(144_000m, monthly.ForecastCostAmount);
        Assert.Equal(1_100_000m, monthly.BudgetTotalAmount);
        Assert.Equal(1_221_000m, monthly.ActualTotalAmount);
        Assert.Equal(1_344_000m, monthly.ForecastTotalAmount);

        var cost = result.Costs.Single(x => x.CostTypeId == costType.Id);
        Assert.Equal(100_000m, cost.BudgetAmount);
        Assert.Equal(121_000m, cost.ActualAmount);
        Assert.Equal(144_000m, cost.ForecastAmount);
        Assert.Equal(21_000m, cost.ActualVarianceAmount);
        Assert.Equal(44_000m, cost.ForecastVarianceAmount);

        Assert.Equal(productDimension.Id, result.SelectedDimensionId);
        var row = result.Drilldown.Single(x => x.MemberId == product.Id);
        Assert.Equal(100m, row.BudgetQuantity);
        Assert.Equal(110m, row.ActualQuantity);
        Assert.Equal(120m, row.ForecastQuantity);
        Assert.Equal(1_000_000m, row.BudgetPurchaseAmount);
        Assert.Equal(1_100_000m, row.ActualPurchaseAmount);
        Assert.Equal(1_200_000m, row.ForecastPurchaseAmount);
        Assert.Equal(100_000m, row.BudgetCostAmount);
        Assert.Equal(121_000m, row.ActualCostAmount);
        Assert.Equal(144_000m, row.ForecastCostAmount);
        Assert.Equal(1_100_000m, row.BudgetTotalAmount);
        Assert.Equal(1_221_000m, row.ActualTotalAmount);
        Assert.Equal(1_344_000m, row.ForecastTotalAmount);
        Assert.Equal(121_000m, row.ActualVarianceAmount);
        Assert.Equal(244_000m, row.ForecastVarianceAmount);
    }
}
