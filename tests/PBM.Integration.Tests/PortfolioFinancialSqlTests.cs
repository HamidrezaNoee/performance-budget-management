using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Integration.Tests;

[Collection("PBM SQL Integration")]
public sealed class PortfolioFinancialSqlTests(PbmSqlFixture fixture)
{
    [Fact]
    public async Task Portfolio_aligns_companies_by_jalali_year_and_ranks_the_same_profit_loss_semantics()
    {
        if (!fixture.IsEnabled) return;

        await using var db = fixture.CreateContext();
        var secondCompany = new Company
        {
            TenantId = fixture.TenantId,
            Code = "PORTFOLIO-02",
            Name = "شرکت دوم تست پرتفوی",
            Industry = "Integration Test"
        };
        var secondYear = new FiscalYear
        {
            CompanyId = secondCompany.Id,
            Code = "1405",
            Name = "سال مالی 1405 - شرکت دوم",
            JalaliYear = 1405,
            StartDate = new DateTime(2026, 3, 21),
            EndDate = new DateTime(2027, 3, 20)
        };
        var secondPeriod = new FiscalPeriod
        {
            FiscalYearId = secondYear.Id,
            Sequence = 1,
            Code = "1405-01",
            Name = "فروردین",
            JalaliMonth = 1,
            StartDate = new DateTime(2026, 3, 21),
            EndDate = new DateTime(2026, 4, 20)
        };
        secondYear.Periods.Add(secondPeriod);
        db.Companies.Add(secondCompany);
        db.FiscalYears.Add(secondYear);
        await db.SaveChangesAsync();

        var superUser = new TestUserContext(
            fixture.UserId,
            fixture.TenantId,
            new HashSet<Guid> { fixture.CompanyId, secondCompany.Id },
            new HashSet<Guid> { fixture.CompanyId, secondCompany.Id },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SUPERADMIN" });
        var calculation = new CalculationService(db, superUser, new FormulaEngine());
        var budget = new BudgetService(db, superUser, calculation);
        var provisioner = new CommercialPlanningProvisioner(db);
        await provisioner.EnsureSalesAsync(fixture.TenantId);
        await provisioner.EnsureExpenseAsync(fixture.TenantId);

        var tradeModelId = await db.BudgetModels.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.Code == "TRADE")
            .Select(x => x.Id).SingleAsync();
        var expenseModelId = await db.BudgetModels.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.Code == "EXPENSE")
            .Select(x => x.Id).SingleAsync();
        var baseScenarioId = await db.BudgetScenarios.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.Code == "BASE")
            .Select(x => x.Id).SingleAsync();

        var tradePlan = new BudgetPlan
        {
            CompanyId = secondCompany.Id,
            FiscalYearId = secondYear.Id,
            BudgetModelId = tradeModelId,
            Name = "Portfolio trade plan"
        };
        var tradeVersion = new BudgetVersion
        {
            BudgetPlanId = tradePlan.Id,
            ScenarioId = baseScenarioId,
            Name = "Portfolio trade V1",
            VersionNumber = 1
        };
        tradePlan.Versions.Add(tradeVersion);
        var expensePlan = new BudgetPlan
        {
            CompanyId = secondCompany.Id,
            FiscalYearId = secondYear.Id,
            BudgetModelId = expenseModelId,
            Name = "Portfolio expense plan"
        };
        var expenseVersion = new BudgetVersion
        {
            BudgetPlanId = expensePlan.Id,
            ScenarioId = baseScenarioId,
            Name = "Portfolio expense V1",
            VersionNumber = 1
        };
        expensePlan.Versions.Add(expenseVersion);
        db.BudgetPlans.AddRange(tradePlan, expensePlan);
        await db.SaveChangesAsync();

        var productDimensionId = await db.Dimensions.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.Code == "PRODUCT")
            .Select(x => x.Id).SingleAsync();
        var supplierDimensionId = await db.Dimensions.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.Code == "SUPPLIER")
            .Select(x => x.Id).SingleAsync();
        var product = new DimensionMember
        {
            DimensionId = productDimensionId,
            CompanyId = secondCompany.Id,
            Code = "PORTFOLIO-PRODUCT",
            Name = "کالای شرکت دوم"
        };
        var supplier = new DimensionMember
        {
            DimensionId = supplierDimensionId,
            CompanyId = secondCompany.Id,
            Code = "PORTFOLIO-SUPPLIER",
            Name = "تامین‌کننده شرکت دوم"
        };
        db.DimensionMembers.AddRange(product, supplier);
        await db.SaveChangesAsync();

        var sales = new SalesPlanningService(db, superUser, budget, provisioner);
        var salesSetup = await sales.GetSetupAsync(secondCompany.Id);
        var salesDimensions = salesSetup.Dimensions
            .Where(x => x.IsRequired || x.Code == "PRODUCT")
            .Select(x => new DimensionSelection(
                x.Id,
                x.Code switch
                {
                    "PRODUCT" => product.Id,
                    "SUPPLIER" => supplier.Id,
                    _ => x.Members.First().Id
                }))
            .ToList();

        await PutSales("SALES_QTY", 100m, ValueKind.Budget);
        await PutSales("SALES_PRICE", 1_000m, ValueKind.Budget);
        await PutSales("COGS_AMOUNT", 60_000m, ValueKind.Budget);
        await PutSales("SALES_QTY", 130m, ValueKind.Forecast);
        await PutSales("SALES_PRICE", 1_000m, ValueKind.Forecast);
        await PutSales("COGS_AMOUNT", 75_000m, ValueKind.Forecast);
        await PutActualSales("SALES_QTY", 120m);
        await PutActualSales("SALES_PRICE", 1_000m);
        await PutActualSales("COGS_AMOUNT", 70_000m);

        var expenses = new ExpensePlanningService(db, superUser, budget, provisioner);
        var expenseSetup = await expenses.GetSetupAsync(secondCompany.Id);
        var expenseDimensions = ExpenseSelections(expenseSetup.Dimensions, "PERSONNEL", "SALARY_BASE");
        var costCenter = expenseSetup.Dimensions.Single(x => x.Code == "COSTCENTER");
        var financeCostCenter = costCenter.Members.Single(x => x.Code == "CC_FINANCE");
        expenseDimensions.Add(new DimensionSelection(costCenter.Id, financeCostCenter.Id));

        await expenses.UpsertCellAsync(new UpsertExpensePlanningCellRequest(
            expenseVersion.Id, secondPeriod.Id, 10_000m, expenseDimensions, ValueKind.Budget));
        await expenses.UpsertCellAsync(new UpsertExpensePlanningCellRequest(
            expenseVersion.Id, secondPeriod.Id, 11_000m, expenseDimensions, ValueKind.Forecast));
        await budget.UpsertFactAsync(new UpsertBudgetFactRequest(
            expenseVersion.Id,
            secondPeriod.Id,
            expenseSetup.MeasureId,
            ValueKind.Actual,
            12_000m,
            expenseSetup.BaseCurrencyCode,
            expenseDimensions,
            "IntegrationActual",
            "Portfolio actual expense"));

        var financial = new FinancialReportService(db, superUser, provisioner);
        var portfolio = new PortfolioFinancialService(db, superUser, financial);
        var result = await portfolio.GetAsync(secondCompany.Id, secondYear.Id);

        Assert.Equal(1405, result.JalaliYear);
        Assert.Equal(2, result.AccessibleCompanyCount);
        Assert.Equal(2, result.CompaniesWithFiscalYear);
        var row = result.Companies.Single(x => x.CompanyId == secondCompany.Id);
        Assert.Equal(100_000m, row.BudgetNetSales);
        Assert.Equal(120_000m, row.ActualNetSales);
        Assert.Equal(130_000m, row.ForecastNetSales);
        Assert.Equal(40_000m, row.BudgetGrossProfit);
        Assert.Equal(50_000m, row.ActualGrossProfit);
        Assert.Equal(55_000m, row.ForecastGrossProfit);
        Assert.Equal(30_000m, row.BudgetOperatingProfit);
        Assert.Equal(38_000m, row.ActualOperatingProfit);
        Assert.Equal(44_000m, row.ForecastOperatingProfit);
        Assert.Equal(30_000m, row.BudgetNetProfit);
        Assert.Equal(38_000m, row.ActualNetProfit);
        Assert.Equal(44_000m, row.ForecastNetProfit);
        Assert.Equal(20_000m, row.ActualNetSalesVariance);
        Assert.Equal(30_000m, row.ForecastNetSalesVariance);
        Assert.Equal(8_000m, row.ActualNetProfitVariance);
        Assert.Equal(14_000m, row.ForecastNetProfitVariance);
        Assert.Equal(120m, row.BudgetAchievementPercent);
        Assert.InRange(row.ActualNetMarginPercent, 31.6666m, 31.6667m);

        var salesDashboard = new SalesDashboardService(db, superUser, provisioner);
        var expenseDashboard = new ExpenseDashboardService(db, superUser, provisioner);
        var dimensions = new PortfolioDimensionService(db, superUser, provisioner, salesDashboard, expenseDashboard);

        var productRanking = await dimensions.GetSalesAsync(secondCompany.Id, secondYear.Id, "PRODUCT", 50);
        var productRow = productRanking.Rows.Single(x => x.MemberCode == product.Code);
        Assert.Equal(100_000m, productRow.BudgetNetSales);
        Assert.Equal(120_000m, productRow.ActualNetSales);
        Assert.Equal(130_000m, productRow.ForecastNetSales);
        Assert.Equal(20_000m, productRow.ActualNetSalesVariance);
        Assert.Equal(40_000m, productRow.BudgetGrossProfit);
        Assert.Equal(50_000m, productRow.ActualGrossProfit);
        Assert.Equal(55_000m, productRow.ForecastGrossProfit);
        Assert.Equal(1, productRow.CompanyCount);
        Assert.InRange(productRow.ActualContributionPercent, 0.0001m, 100m);

        var costCenterRanking = await dimensions.GetExpensesAsync(secondCompany.Id, secondYear.Id, "COSTCENTER", 50);
        var costCenterRow = costCenterRanking.Rows.Single(x => x.MemberCode == financeCostCenter.Code);
        Assert.Equal(10_000m, costCenterRow.BudgetNetCost);
        Assert.Equal(12_000m, costCenterRow.ActualNetCost);
        Assert.Equal(11_000m, costCenterRow.ForecastNetCost);
        Assert.Equal(2_000m, costCenterRow.ActualVarianceAmount);
        Assert.Equal(1_000m, costCenterRow.ForecastVarianceAmount);
        Assert.Equal(120m, costCenterRow.BudgetAchievementPercent);
        Assert.Equal(1, costCenterRow.CompanyCount);
        Assert.InRange(costCenterRow.ActualContributionPercent, 0.0001m, 100m);

        async Task PutSales(string code, decimal value, ValueKind kind) =>
            await sales.UpsertCellAsync(new UpsertSalesPlanningCellRequest(
                tradeVersion.Id, secondPeriod.Id, code, value, salesDimensions, kind));

        async Task PutActualSales(string code, decimal value)
        {
            var measure = salesSetup.Measures.Single(x => x.Code == code);
            var currency = code is "SALES_QTY" or "FREE_SALES_QTY" ? null : salesSetup.BaseCurrencyCode;
            await budget.UpsertFactAsync(new UpsertBudgetFactRequest(
                tradeVersion.Id,
                secondPeriod.Id,
                measure.Id,
                ValueKind.Actual,
                value,
                currency,
                salesDimensions,
                "IntegrationActual",
                "Portfolio actual sale"));
        }
    }

    private static List<DimensionSelection> ExpenseSelections(
        IReadOnlyList<ExpensePlanningDimensionDto> dimensions,
        string classCode,
        string itemCode)
    {
        var result = new List<DimensionSelection>();
        foreach (var dimension in dimensions.Where(x => x.IsRequired))
        {
            var member = dimension.Code switch
            {
                "EXPENSECLASS" => dimension.Members.FirstOrDefault(x => x.Code == classCode),
                "EXPENSEITEM" => dimension.Members.FirstOrDefault(x => x.Code == itemCode),
                "ACCOUNT" => dimension.Members.FirstOrDefault(x => x.Code == "EXPENSE_BUDGET") ?? dimension.Members.FirstOrDefault(),
                _ => dimension.Members.FirstOrDefault()
            };
            if (member is null) throw new InvalidOperationException($"Required expense dimension {dimension.Code} has no member.");
            result.Add(new DimensionSelection(dimension.Id, member.Id));
        }
        return result;
    }
}
