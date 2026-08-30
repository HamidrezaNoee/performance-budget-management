using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Integration.Tests;

[Collection("PBM SQL Integration")]
public sealed class CommercialPlanningSqlTests(PbmSqlFixture fixture)
{
    [Fact]
    public async Task Monthly_sales_expenses_dashboards_and_workbook_profit_loss_reconcile()
    {
        if (!fixture.IsEnabled) return;

        await using var db = fixture.CreateContext();
        var user = fixture.CreateUserContext();
        var calculation = new CalculationService(db, user, new FormulaEngine());
        var budget = new BudgetService(db, user, calculation);
        var provisioner = new CommercialPlanningProvisioner(db);
        var sales = new SalesPlanningService(db, user, budget, provisioner);
        var salesDashboard = new SalesDashboardService(db, user, provisioner);
        var expenses = new ExpensePlanningService(db, user, budget, provisioner);
        var expenseDashboard = new ExpenseDashboardService(db, user, provisioner);
        var financial = new FinancialReportService(db, user, provisioner);

        var periodId = fixture.PeriodIds[8]; // Seed sales only cover the first six periods.

        // --- Sales: same monthly quantity/amount structure as the workbook, including in-kind discount/cost.
        var salesSetup = await sales.GetSetupAsync(fixture.CompanyId);
        var salesDimensions = RequiredSelections(salesSetup.Dimensions);

        await PutSales("SALES_QTY", 100m, ValueKind.Budget);
        await PutSales("SALES_PRICE", 1_000m, ValueKind.Budget);
        await PutSales("SALES_DISCOUNT", 5_000m, ValueKind.Budget);
        await PutSales("FOC_SALES_AMOUNT", 1_000m, ValueKind.Budget);
        await PutSales("SALES_RETURN", 2_000m, ValueKind.Budget);
        await PutSales("COGS_AMOUNT", 60_000m, ValueKind.Budget);
        await PutSales("FOC_COST", 500m, ValueKind.Budget);
        await PutSales("PURCHASE_COMPANY_DISCOUNT", 3_000m, ValueKind.Budget);

        await PutSales("SALES_QTY", 120m, ValueKind.Forecast);
        await PutSales("SALES_PRICE", 1_100m, ValueKind.Forecast);
        await PutSales("SALES_DISCOUNT", 6_000m, ValueKind.Forecast);
        await PutSales("FOC_SALES_AMOUNT", 2_000m, ValueKind.Forecast);
        await PutSales("SALES_RETURN", 4_000m, ValueKind.Forecast);
        await PutSales("COGS_AMOUNT", 80_000m, ValueKind.Forecast);
        await PutSales("FOC_COST", 1_000m, ValueKind.Forecast);
        await PutSales("PURCHASE_COMPANY_DISCOUNT", 5_000m, ValueKind.Forecast);

        // Actual is written through the underlying governed fact path here to emulate an ERP/Ledger projection.
        // The planner itself must stay read-only for ValueKind.Actual.
        await PutActualSales("SALES_QTY", 90m);
        await PutActualSales("SALES_PRICE", 1_050m);
        await PutActualSales("SALES_DISCOUNT", 4_000m);
        await PutActualSales("FOC_SALES_AMOUNT", 500m);
        await PutActualSales("SALES_RETURN", 1_000m);
        await PutActualSales("COGS_AMOUNT", 55_000m);
        await PutActualSales("FOC_COST", 400m);
        await PutActualSales("PURCHASE_COMPANY_DISCOUNT", 2_500m);

        await Assert.ThrowsAsync<ArgumentException>(() => sales.UpsertCellAsync(new UpsertSalesPlanningCellRequest(
            fixture.VersionId, periodId, "SALES_QTY", 999m, salesDimensions, ValueKind.Actual)));

        var budgetSales = await sales.QueryAsync(new SalesPlanningQueryRequest(fixture.VersionId, salesDimensions, ValueKind.Budget));
        Assert.Equal(100_000m, SalesValue(budgetSales, "GROSS_SALES", periodId));
        Assert.Equal(92_000m, SalesValue(budgetSales, "NET_SALES", periodId));
        Assert.Equal(60_500m, SalesValue(budgetSales, "SALES_COGS_TOTAL", periodId));
        Assert.Equal(34_500m, SalesValue(budgetSales, "SALES_GROSS_MARGIN", periodId));

        var actualSales = await sales.QueryAsync(new SalesPlanningQueryRequest(fixture.VersionId, salesDimensions, ValueKind.Actual));
        Assert.Equal(94_500m, SalesValue(actualSales, "GROSS_SALES", periodId));
        Assert.Equal(89_000m, SalesValue(actualSales, "NET_SALES", periodId));
        Assert.Equal(55_400m, SalesValue(actualSales, "SALES_COGS_TOTAL", periodId));
        Assert.Equal(36_100m, SalesValue(actualSales, "SALES_GROSS_MARGIN", periodId));

        var forecastSales = await sales.QueryAsync(new SalesPlanningQueryRequest(fixture.VersionId, salesDimensions, ValueKind.Forecast));
        Assert.Equal(132_000m, SalesValue(forecastSales, "GROSS_SALES", periodId));
        Assert.Equal(120_000m, SalesValue(forecastSales, "NET_SALES", periodId));
        Assert.Equal(81_000m, SalesValue(forecastSales, "SALES_COGS_TOTAL", periodId));
        Assert.Equal(44_000m, SalesValue(forecastSales, "SALES_GROSS_MARGIN", periodId));

        var salesDash = await salesDashboard.GetAsync(fixture.CompanyId, fixture.FiscalYearId);
        Assert.NotNull(salesDash);
        var salesMonth = salesDash!.Monthly.Single(x => x.PeriodId == periodId);
        Assert.Equal(100m, salesMonth.BudgetQuantity);
        Assert.Equal(90m, salesMonth.ActualQuantity);
        Assert.Equal(120m, salesMonth.ForecastQuantity);
        Assert.Equal(6_000m, salesMonth.BudgetDiscount);
        Assert.Equal(4_500m, salesMonth.ActualDiscount);
        Assert.Equal(8_000m, salesMonth.ForecastDiscount);
        Assert.Equal(92_000m, salesMonth.BudgetNetSales);
        Assert.Equal(89_000m, salesMonth.ActualNetSales);
        Assert.Equal(120_000m, salesMonth.ForecastNetSales);
        Assert.Equal(60_500m, salesMonth.BudgetCogs);
        Assert.Equal(55_400m, salesMonth.ActualCogs);
        Assert.Equal(81_000m, salesMonth.ForecastCogs);
        Assert.Equal(34_500m, salesMonth.BudgetGrossProfit);
        Assert.Equal(36_100m, salesMonth.ActualGrossProfit);
        Assert.Equal(44_000m, salesMonth.ForecastGrossProfit);

        // --- Expenses: create/reuse an EXPENSE plan and post workbook categories at the same month.
        await provisioner.EnsureExpenseAsync(fixture.TenantId);
        var expenseModelId = await db.BudgetModels.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.Code == "EXPENSE")
            .Select(x => x.Id).SingleAsync();
        var expenseVersionId = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlan!.CompanyId == fixture.CompanyId
                && x.BudgetPlan.FiscalYearId == fixture.FiscalYearId
                && x.BudgetPlan.BudgetModelId == expenseModelId)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync();
        if (!expenseVersionId.HasValue)
        {
            var scenarioId = await db.BudgetScenarios.AsNoTracking()
                .Where(x => x.TenantId == fixture.TenantId && x.Code == "BASE")
                .Select(x => x.Id).SingleAsync();
            var plan = new BudgetPlan
            {
                CompanyId = fixture.CompanyId,
                FiscalYearId = fixture.FiscalYearId,
                BudgetModelId = expenseModelId,
                Name = "Integration expense plan"
            };
            var version = new BudgetVersion
            {
                BudgetPlanId = plan.Id,
                ScenarioId = scenarioId,
                VersionNumber = 1,
                Name = "Integration expense V1"
            };
            plan.Versions.Add(version);
            db.BudgetPlans.Add(plan);
            await db.SaveChangesAsync();
            expenseVersionId = version.Id;
        }

        var expenseSetup = await expenses.GetSetupAsync(fixture.CompanyId);
        await PutExpense("PERSONNEL", "SALARY_BASE", 10_000m, ValueKind.Budget);
        await PutExpense("PERSONNEL", "SALARY_BASE", 12_000m, ValueKind.Forecast);
        await PutExpense("OTHER_OPERATING_INCOME", "SCRAP_SALE", 1_000m, ValueKind.Budget);
        await PutExpense("OTHER_OPERATING_EXPENSE", "INVENTORY_SHORTAGE", 500m, ValueKind.Budget);
        await PutExpense("FINANCIAL_EXPENSE", "FINANCE_INTEREST", 2_000m, ValueKind.Budget);
        await PutExpense("OTHER_NON_OPERATING_INCOME", "ASSET_SALE_GAIN", 400m, ValueKind.Budget);
        await PutExpense("OTHER_NON_OPERATING_EXPENSE", "NONCURRENT_ASSET_SALE_LOSS", 100m, ValueKind.Budget);
        await PutExpense("TAX", "INCOME_TAX", 1_000m, ValueKind.Budget);

        await PutActualExpense("PERSONNEL", "SALARY_BASE", 11_000m);
        await PutActualExpense("OTHER_OPERATING_INCOME", "SCRAP_SALE", 900m);
        await PutActualExpense("OTHER_OPERATING_EXPENSE", "INVENTORY_SHORTAGE", 600m);
        await PutActualExpense("FINANCIAL_EXPENSE", "FINANCE_INTEREST", 1_800m);
        await PutActualExpense("OTHER_NON_OPERATING_INCOME", "ASSET_SALE_GAIN", 350m);
        await PutActualExpense("OTHER_NON_OPERATING_EXPENSE", "NONCURRENT_ASSET_SALE_LOSS", 120m);
        await PutActualExpense("TAX", "INCOME_TAX", 900m);

        var salaryDimensions = RequiredExpenseSelections(expenseSetup.Dimensions, "PERSONNEL", "SALARY_BASE");
        await Assert.ThrowsAsync<ArgumentException>(() => expenses.UpsertCellAsync(new UpsertExpensePlanningCellRequest(
            expenseVersionId!.Value, periodId, 99_999m, salaryDimensions, ValueKind.Actual)));
        var actualSalary = await expenses.QueryAsync(new ExpensePlanningQueryRequest(expenseVersionId!.Value, salaryDimensions, ValueKind.Actual));
        Assert.Equal(11_000m, actualSalary.Values.Single(x => x.PeriodId == periodId).Value);

        var expenseDash = await expenseDashboard.GetAsync(fixture.CompanyId, fixture.FiscalYearId);
        Assert.NotNull(expenseDash);
        var expenseMonth = expenseDash!.Monthly.Single(x => x.PeriodId == periodId);
        Assert.Equal(13_600m, expenseMonth.BudgetExpense);
        Assert.Equal(14_420m, expenseMonth.ActualExpense);
        Assert.Equal(1_400m, expenseMonth.BudgetIncome);
        Assert.Equal(1_250m, expenseMonth.ActualIncome);
        Assert.Equal(12_200m, expenseMonth.BudgetNetCost);
        Assert.Equal(13_170m, expenseMonth.ActualNetCost);
        Assert.Contains(expenseDash.Classes, x => x.Code == "PERSONNEL" && x.BudgetAmount == 10_000m && x.ActualAmount == 11_000m && x.ForecastAmount == 12_000m);

        // --- Workbook-style P&L: cash + in-kind discount and normal + in-kind COGS are reconciled.
        var pnl = await financial.GetAsync(
            fixture.CompanyId,
            fixture.FiscalYearId,
            FinancialReportType.ProfitLoss,
            ValueKind.Budget);

        AssertPnl(pnl, "GROSS_SALES", 100_000m);
        AssertPnl(pnl, "SALES_DISCOUNT", 6_000m);
        AssertPnl(pnl, "CASH_SALES_DISCOUNT", 5_000m);
        AssertPnl(pnl, "FREE_SALES_DISCOUNT", 1_000m);
        AssertPnl(pnl, "SALES_RETURN", 2_000m);
        AssertPnl(pnl, "NET_SALES", 92_000m);
        AssertPnl(pnl, "COGS", 60_500m);
        AssertPnl(pnl, "PURCHASE_COMPANY_DISCOUNT", 3_000m);
        AssertPnl(pnl, "TOTAL_COGS", 57_500m);
        AssertPnl(pnl, "GROSS_PROFIT", 34_500m);
        AssertPnl(pnl, "ADMIN_EXPENSE", 10_000m);
        AssertPnl(pnl, "OTHER_OPERATING_NET", 500m);
        AssertPnl(pnl, "OPERATING_PROFIT", 25_000m);
        AssertPnl(pnl, "FINANCE_COST", 2_000m);
        AssertPnl(pnl, "OTHER_NON_OPERATING_NET", 300m);
        AssertPnl(pnl, "PROFIT_BEFORE_TAX", 23_300m);
        AssertPnl(pnl, "TAX", 1_000m);
        AssertPnl(pnl, "NET_PROFIT", 22_300m);

        var actualPnl = await financial.GetAsync(
            fixture.CompanyId,
            fixture.FiscalYearId,
            FinancialReportType.ProfitLoss,
            ValueKind.Actual);
        AssertPnl(actualPnl, "GROSS_SALES", 94_500m);
        AssertPnl(actualPnl, "SALES_DISCOUNT", 4_500m);
        AssertPnl(actualPnl, "SALES_RETURN", 1_000m);
        AssertPnl(actualPnl, "NET_SALES", 89_000m);
        AssertPnl(actualPnl, "COGS", 55_400m);
        AssertPnl(actualPnl, "PURCHASE_COMPANY_DISCOUNT", 2_500m);
        AssertPnl(actualPnl, "TOTAL_COGS", 52_900m);
        AssertPnl(actualPnl, "GROSS_PROFIT", 36_100m);
        AssertPnl(actualPnl, "ADMIN_EXPENSE", 11_000m);
        AssertPnl(actualPnl, "OTHER_OPERATING_NET", 300m);
        AssertPnl(actualPnl, "OPERATING_PROFIT", 25_400m);
        AssertPnl(actualPnl, "FINANCE_COST", 1_800m);
        AssertPnl(actualPnl, "OTHER_NON_OPERATING_NET", 230m);
        AssertPnl(actualPnl, "PROFIT_BEFORE_TAX", 23_830m);
        AssertPnl(actualPnl, "TAX", 900m);
        AssertPnl(actualPnl, "NET_PROFIT", 22_930m);

        async Task PutSales(string code, decimal value, ValueKind kind) =>
            await sales.UpsertCellAsync(new UpsertSalesPlanningCellRequest(
                fixture.VersionId, periodId, code, value, salesDimensions, kind));

        async Task PutActualSales(string code, decimal value)
        {
            var measure = salesSetup.Measures.Single(x => x.Code == code);
            var currency = code is "SALES_QTY" or "FREE_SALES_QTY" ? null : salesSetup.BaseCurrencyCode;
            await budget.UpsertFactAsync(new UpsertBudgetFactRequest(
                fixture.VersionId, periodId, measure.Id, ValueKind.Actual, value, currency,
                salesDimensions, "IntegrationActual", "Emulates controlled Actual Ledger projection."));
        }

        async Task PutExpense(string classCode, string itemCode, decimal value, ValueKind kind)
        {
            var dimensions = RequiredExpenseSelections(expenseSetup.Dimensions, classCode, itemCode);
            await expenses.UpsertCellAsync(new UpsertExpensePlanningCellRequest(
                expenseVersionId!.Value, periodId, value, dimensions, kind));
        }

        async Task PutActualExpense(string classCode, string itemCode, decimal value)
        {
            var dimensions = RequiredExpenseSelections(expenseSetup.Dimensions, classCode, itemCode);
            await budget.UpsertFactAsync(new UpsertBudgetFactRequest(
                expenseVersionId!.Value, periodId, expenseSetup.MeasureId, ValueKind.Actual, value,
                expenseSetup.BaseCurrencyCode, dimensions, "IntegrationActual", "Emulates controlled Actual Ledger projection."));
        }

        void AssertPnl(FinancialReportDto report, string code, decimal expected)
        {
            var row = report.Rows.Single(x => x.Code == code);
            Assert.Equal(expected, row.Periods.Single(x => x.PeriodId == periodId).Value);
        }
    }

    private static List<DimensionSelection> RequiredSelections(IReadOnlyList<SalesPlanningDimensionDto> dimensions)
    {
        var selections = new List<DimensionSelection>();
        foreach (var dimension in dimensions.Where(x => x.IsRequired || x.Code == "PRODUCT"))
        {
            var member = dimension.Members.FirstOrDefault()
                ?? throw new InvalidOperationException($"Required sales dimension {dimension.Code} has no member.");
            selections.Add(new DimensionSelection(dimension.Id, member.Id));
        }
        return selections;
    }

    private static List<DimensionSelection> RequiredExpenseSelections(
        IReadOnlyList<ExpensePlanningDimensionDto> dimensions,
        string classCode,
        string itemCode)
    {
        var selections = new List<DimensionSelection>();
        foreach (var dimension in dimensions.Where(x => x.IsRequired))
        {
            ExpensePlanningMemberDto? member = dimension.Code switch
            {
                "EXPENSECLASS" => dimension.Members.FirstOrDefault(x => x.Code == classCode),
                "EXPENSEITEM" => dimension.Members.FirstOrDefault(x => x.Code == itemCode),
                "ACCOUNT" => dimension.Members.FirstOrDefault(x => x.Code == "EXPENSE_BUDGET") ?? dimension.Members.FirstOrDefault(),
                _ => dimension.Members.FirstOrDefault()
            };
            if (member is null)
                throw new InvalidOperationException($"Required expense dimension {dimension.Code} has no requested member.");
            selections.Add(new DimensionSelection(dimension.Id, member.Id));
        }
        return selections;
    }

    private static decimal SalesValue(SalesPlanningDataDto data, string code, Guid periodId) =>
        data.Series.Single(x => x.MeasureCode == code).Values.Single(x => x.PeriodId == periodId).Value;
}
