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

        var budgetSales = await sales.QueryAsync(new SalesPlanningQueryRequest(fixture.VersionId, salesDimensions, ValueKind.Budget));
        Assert.Equal(100_000m, SalesValue(budgetSales, "GROSS_SALES", periodId));
        Assert.Equal(92_000m, SalesValue(budgetSales, "NET_SALES", periodId));
        Assert.Equal(60_500m, SalesValue(budgetSales, "SALES_COGS_TOTAL", periodId));
        Assert.Equal(34_500m, SalesValue(budgetSales, "SALES_GROSS_MARGIN", periodId));

        var forecastSales = await sales.QueryAsync(new SalesPlanningQueryRequest(fixture.VersionId, salesDimensions, ValueKind.Forecast));
        Assert.Equal(132_000m, SalesValue(forecastSales, "GROSS_SALES", periodId));
        Assert.Equal(120_000m, SalesValue(forecastSales, "NET_SALES", periodId));
        Assert.Equal(81_000m, SalesValue(forecastSales, "SALES_COGS_TOTAL", periodId));
        Assert.Equal(44_000m, SalesValue(forecastSales, "SALES_GROSS_MARGIN", periodId));

        var salesDash = await salesDashboard.GetAsync(fixture.CompanyId, fixture.FiscalYearId);
        Assert.NotNull(salesDash);
        var salesMonth = salesDash!.Monthly.Single(x => x.PeriodId == periodId);
        Assert.Equal(100m, salesMonth.BudgetQuantity);
        Assert.Equal(120m, salesMonth.ForecastQuantity);
        Assert.Equal(6_000m, salesMonth.BudgetDiscount);
        Assert.Equal(8_000m, salesMonth.ForecastDiscount);
        Assert.Equal(92_000m, salesMonth.BudgetNetSales);
        Assert.Equal(120_000m, salesMonth.ForecastNetSales);
        Assert.Equal(60_500m, salesMonth.BudgetCogs);
        Assert.Equal(81_000m, salesMonth.ForecastCogs);
        Assert.Equal(34_500m, salesMonth.BudgetGrossProfit);
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
        await PutExpense("OTHER_NON_OPERATING_INCOME", "NON_OPERATING_INCOME", 400m, ValueKind.Budget);
        await PutExpense("OTHER_NON_OPERATING_EXPENSE", "NON_OPERATING_EXPENSE", 100m, ValueKind.Budget);
        await PutExpense("TAX", "INCOME_TAX", 1_000m, ValueKind.Budget);

        var expenseDash = await expenseDashboard.GetAsync(fixture.CompanyId, fixture.FiscalYearId);
        Assert.NotNull(expenseDash);
        var expenseMonth = expenseDash!.Monthly.Single(x => x.PeriodId == periodId);
        Assert.Equal(13_600m, expenseMonth.BudgetExpense);
        Assert.Equal(1_400m, expenseMonth.BudgetIncome);
        Assert.Equal(12_200m, expenseMonth.BudgetNetCost);
        Assert.Contains(expenseDash.Classes, x => x.Code == "PERSONNEL" && x.BudgetAmount == 10_000m && x.ForecastAmount == 12_000m);

        // --- Workbook-style P&L: cash + in-kind discount and normal + in-kind COGS are reconciled.
        var pnl = await financial.GetAsync(
            fixture.CompanyId,
            fixture.FiscalYearId,
            FinancialReportType.ProfitLoss,
            ValueKind.Budget);

        AssertPnl("GROSS_SALES", 100_000m);
        AssertPnl("SALES_DISCOUNT", 6_000m);
        AssertPnl("CASH_SALES_DISCOUNT", 5_000m);
        AssertPnl("FREE_SALES_DISCOUNT", 1_000m);
        AssertPnl("SALES_RETURN", 2_000m);
        AssertPnl("NET_SALES", 92_000m);
        AssertPnl("COGS", 60_500m);
        AssertPnl("PURCHASE_COMPANY_DISCOUNT", 3_000m);
        AssertPnl("TOTAL_COGS", 57_500m);
        AssertPnl("GROSS_PROFIT", 34_500m);
        AssertPnl("ADMIN_EXPENSE", 10_000m);
        AssertPnl("OTHER_OPERATING_NET", 500m);
        AssertPnl("OPERATING_PROFIT", 25_000m);
        AssertPnl("FINANCE_COST", 2_000m);
        AssertPnl("OTHER_NON_OPERATING_NET", 300m);
        AssertPnl("PROFIT_BEFORE_TAX", 23_300m);
        AssertPnl("TAX", 1_000m);
        AssertPnl("NET_PROFIT", 22_300m);

        async Task PutSales(string code, decimal value, ValueKind kind) =>
            await sales.UpsertCellAsync(new UpsertSalesPlanningCellRequest(
                fixture.VersionId, periodId, code, value, salesDimensions, kind));

        async Task PutExpense(string classCode, string itemCode, decimal value, ValueKind kind)
        {
            var dimensions = RequiredExpenseSelections(expenseSetup.Dimensions, classCode, itemCode);
            await expenses.UpsertCellAsync(new UpsertExpensePlanningCellRequest(
                expenseVersionId!.Value, periodId, value, dimensions, kind));
        }

        void AssertPnl(string code, decimal expected)
        {
            var row = pnl.Rows.Single(x => x.Code == code);
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
