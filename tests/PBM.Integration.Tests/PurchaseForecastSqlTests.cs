using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Integration.Tests;

[Collection("PBM SQL Integration")]
public sealed class PurchaseForecastSqlTests(PbmSqlFixture fixture)
{
    [Fact]
    public async Task Forecast_quantity_amount_and_percentage_cost_are_saved_at_exact_dimensions()
    {
        if (!fixture.IsEnabled) return;

        await using var db = fixture.CreateContext();
        var user = fixture.CreateUserContext();
        var calculation = new CalculationService(db, user, new FormulaEngine());
        var budget = new BudgetService(db, user, calculation);
        var service = new PurchaseForecastService(db, user, budget);

        var setup = await service.GetSetupAsync(fixture.CompanyId);
        Assert.Contains(setup.Dimensions, x => x.Code == "PRODUCT" && x.IsRequired);
        Assert.Contains(setup.Dimensions, x => x.Code == "SUPPLIER");
        Assert.Contains(setup.Dimensions, x => x.Code == "BRAND");
        Assert.Contains(setup.Dimensions, x => x.Code == "CONTRACT");
        Assert.Contains(setup.Dimensions, x => x.Code == "PROJECT");
        Assert.Contains(setup.CostTypes, x => x.Code == "FREIGHT");

        var product = setup.Dimensions.Single(x => x.Code == "PRODUCT");
        var productMember = Assert.Single(product.Members.Take(1));
        var dimensions = new List<DimensionSelection>
        {
            new(product.Id, productMember.Id)
        };

        var supplier = setup.Dimensions.SingleOrDefault(x => x.Code == "SUPPLIER");
        if (supplier?.Members.FirstOrDefault() is { } supplierMember)
            dimensions.Add(new DimensionSelection(supplier.Id, supplierMember.Id));

        var customCost = await service.CreateCostTypeAsync(new CreatePurchaseCostTypeRequest(
            fixture.CompanyId,
            "ORIGIN_INSPECTION_TEST",
            "هزینه بازرسی مبدا تست"));

        var periodId = fixture.PeriodIds[1];
        await service.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId,
            periodId,
            "PURCHASE_FORECAST_QTY",
            120m,
            dimensions));
        await service.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId,
            periodId,
            "PURCHASE_FORECAST_AMOUNT",
            1_000_000m,
            dimensions));
        await service.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId,
            periodId,
            "PURCHASE_COST_RATE",
            2.5m,
            dimensions,
            customCost.Id));

        var result = await service.QueryAsync(new PurchaseForecastQueryRequest(fixture.VersionId, dimensions));
        Assert.Equal(120m, result.Quantity.Single(x => x.PeriodId == periodId).Value);
        Assert.Equal(1_000_000m, result.Amount.Single(x => x.PeriodId == periodId).Value);

        var customSeries = result.Costs.Single(x => x.CostTypeId == customCost.Id);
        Assert.Equal(2.5m, customSeries.Rates.Single(x => x.PeriodId == periodId).Value);
        Assert.Equal(25_000m, customSeries.Amounts.Single(x => x.PeriodId == periodId).Value);

        var forecastFacts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == fixture.VersionId
                && x.PeriodId == periodId
                && x.ValueKind == PBM.Domain.ValueKind.Forecast)
            .ToListAsync();
        Assert.Contains(forecastFacts, x => x.Value == 120m);
        Assert.Contains(forecastFacts, x => x.Value == 1_000_000m);
        Assert.Contains(forecastFacts, x => x.Value == 25_000m);
    }

    [Fact]
    public async Task Changing_purchase_amount_recalculates_rate_driven_cost_amount()
    {
        if (!fixture.IsEnabled) return;

        await using var db = fixture.CreateContext();
        var user = fixture.CreateUserContext();
        var calculation = new CalculationService(db, user, new FormulaEngine());
        var budget = new BudgetService(db, user, calculation);
        var service = new PurchaseForecastService(db, user, budget);
        var setup = await service.GetSetupAsync(fixture.CompanyId);
        var product = setup.Dimensions.Single(x => x.Code == "PRODUCT");
        var dimensions = new List<DimensionSelection> { new(product.Id, product.Members.First().Id) };
        var cost = await service.CreateCostTypeAsync(new CreatePurchaseCostTypeRequest(
            fixture.CompanyId,
            "BANK_SPECIAL_TEST",
            "کارمزد ویژه تست"));
        var periodId = fixture.PeriodIds[2];

        await service.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_FORECAST_AMOUNT", 2_000_000m, dimensions));
        await service.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_COST_RATE", 3m, dimensions, cost.Id));
        await service.UpsertCellAsync(new UpsertPurchaseForecastCellRequest(
            fixture.VersionId, periodId, "PURCHASE_FORECAST_AMOUNT", 3_000_000m, dimensions));

        var result = await service.QueryAsync(new PurchaseForecastQueryRequest(fixture.VersionId, dimensions));
        var series = result.Costs.Single(x => x.CostTypeId == cost.Id);
        Assert.Equal(90_000m, series.Amounts.Single(x => x.PeriodId == periodId).Value);
    }
}
