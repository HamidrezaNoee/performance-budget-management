using System.Data;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class CashPlanningService(
    PbmDbContext db,
    IUserContext user,
    IBudgetService budgetService) : ICashPlanningService
{
    private static readonly string[] CashMeasureCodes = ["OPENING_CASH", "CASH_INFLOW", "CASH_OUTFLOW", "MINIMUM_CASH_BUFFER"];

    public async Task<CashPlanSetupDto> GetSetupAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        EnsureCompanyRead(companyId);
        await EnsureFiscalYearAsync(companyId, fiscalYearId, requireOpen: false, cancellationToken);
        var infrastructure = await GetInfrastructureAsync(cancellationToken);
        if (infrastructure is null)
        {
            if (!CanWriteCompany(companyId))
                throw new InvalidOperationException("Cash planning has not been initialized for this tenant. A user with company write access must initialize it first.");
            infrastructure = await EnsureInfrastructureAsync(cancellationToken);
        }
        return await BuildSetupAsync(companyId, fiscalYearId, infrastructure.Value, cancellationToken);
    }

    public async Task<CashPlanSetupDto> EnsurePlanAsync(EnsureCashPlanRequest request, CancellationToken cancellationToken = default)
    {
        EnsureCompanyWrite(request.CompanyId);
        await EnsureFiscalYearAsync(request.CompanyId, request.FiscalYearId, requireOpen: true, cancellationToken);
        var infrastructure = await EnsureInfrastructureAsync(cancellationToken);

        var plan = await db.BudgetPlans.Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId
                && x.FiscalYearId == request.FiscalYearId
                && x.BudgetModelId == infrastructure.Model.Id, cancellationToken);
        if (plan is null)
        {
            var scenario = await db.BudgetScenarios.SingleOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == "BASE" && x.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("The active BASE scenario is required before a cash plan can be created.");
            plan = new BudgetPlan
            {
                CompanyId = request.CompanyId,
                FiscalYearId = request.FiscalYearId,
                BudgetModelId = infrastructure.Model.Id,
                Name = "برنامه نقدینگی و خزانه‌داری"
            };
            plan.Versions.Add(new BudgetVersion
            {
                BudgetPlanId = plan.Id,
                ScenarioId = scenario.Id,
                VersionNumber = 1,
                Name = "نسخه اولیه نقدینگی"
            });
            db.BudgetPlans.Add(plan);
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = user.TenantId,
                UserId = user.UserId == Guid.Empty ? null : user.UserId,
                EntityType = "CashPlan",
                EntityId = plan.Id.ToString(),
                Action = "CREATE",
                NewValueJson = System.Text.Json.JsonSerializer.Serialize(new { plan.CompanyId, plan.FiscalYearId, plan.BudgetModelId, ScenarioId = scenario.Id })
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        return await BuildSetupAsync(request.CompanyId, request.FiscalYearId, infrastructure, cancellationToken);
    }

    public async Task<CashPlanSummaryDto> GetSummaryAsync(Guid versionId, string? currencyCode = null, CancellationToken cancellationToken = default)
    {
        var context = await GetVersionContextAsync(versionId, cancellationToken);
        EnsureCompanyRead(context.CompanyId);
        var infrastructure = await RequireInfrastructureAsync(cancellationToken);
        if (context.ModelId != infrastructure.Model.Id)
            throw new ArgumentException("Selected budget version is not a cash planning version.");

        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == context.FiscalYearId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);
        var measureIds = infrastructure.Measures.ToDictionary(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var allowedMeasureIds = measureIds.Values.ToHashSet();
        var facts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == versionId && allowedMeasureIds.Contains(x.MeasureId))
            .ToListAsync(cancellationToken);

        var baseCurrency = await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsBaseCurrency)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "IRR";
        var normalizedRequestedCurrency = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();
        var currencyCodes = normalizedRequestedCurrency is not null
            ? new[] { normalizedRequestedCurrency }
            : facts.Select(x => string.IsNullOrWhiteSpace(x.CurrencyCode) ? baseCurrency : x.CurrencyCode!.ToUpperInvariant())
                .Append(baseCurrency)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToArray();

        var summaries = currencyCodes.Select(code => BuildCurrencySummary(code, baseCurrency, periods, facts, measureIds)).ToList();
        return new CashPlanSummaryDto(versionId, context.CompanyId, context.FiscalYearId, summaries);
    }

    public async Task<IReadOnlyList<CashPlanEntryDto>> GetEntriesAsync(
        Guid versionId,
        string? currencyCode = null,
        Guid? periodId = null,
        CancellationToken cancellationToken = default)
    {
        var context = await GetVersionContextAsync(versionId, cancellationToken);
        EnsureCompanyRead(context.CompanyId);
        var infrastructure = await RequireInfrastructureAsync(cancellationToken);
        if (context.ModelId != infrastructure.Model.Id)
            throw new ArgumentException("Selected budget version is not a cash planning version.");

        var measureIds = infrastructure.Measures.Select(x => x.Id).ToHashSet();
        var query = db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
            .Where(x => x.VersionId == versionId && measureIds.Contains(x.MeasureId));
        if (periodId.HasValue) query = query.Where(x => x.PeriodId == periodId.Value);
        if (!string.IsNullOrWhiteSpace(currencyCode))
        {
            var code = currencyCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.CurrencyCode == code);
        }
        var facts = await query.OrderBy(x => x.Period!.Sequence).ThenBy(x => x.Measure!.DisplayOrder).ToListAsync(cancellationToken);
        var itemIds = facts.SelectMany(x => x.Dimensions)
            .Where(x => x.DimensionId == infrastructure.ItemDimension.Id)
            .Select(x => x.MemberId)
            .Distinct()
            .ToArray();
        var items = await db.DimensionMembers.AsNoTracking()
            .Where(x => itemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var measures = infrastructure.Measures.ToDictionary(x => x.Id);
        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == context.FiscalYearId).ToDictionaryAsync(x => x.Id, cancellationToken);
        var baseCurrency = await db.Currencies.AsNoTracking().Where(x => x.TenantId == user.TenantId && x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync(cancellationToken) ?? "IRR";

        var result = new List<CashPlanEntryDto>();
        foreach (var fact in facts)
        {
            var itemId = fact.Dimensions.Where(x => x.DimensionId == infrastructure.ItemDimension.Id).Select(x => (Guid?)x.MemberId).SingleOrDefault();
            if (!itemId.HasValue || !items.TryGetValue(itemId.Value, out var item) || !measures.TryGetValue(fact.MeasureId, out var measure) || !periods.TryGetValue(fact.PeriodId, out var period)) continue;
            result.Add(new CashPlanEntryDto(
                fact.Id, fact.VersionId, fact.PeriodId, period.Name, period.Sequence,
                item.Id, item.Code, item.Name, measure.Code, measure.Name, fact.ValueKind, fact.Value,
                string.IsNullOrWhiteSpace(fact.CurrencyCode) ? baseCurrency : fact.CurrencyCode!, fact.Note, fact.UpdatedAtUtc));
        }
        return result.OrderBy(x => x.PeriodSequence).ThenBy(x => x.ItemName).ThenBy(x => x.MeasureCode).ThenBy(x => x.ValueKind).ToList();
    }

    public async Task<Guid> UpsertEntryAsync(UpsertCashPlanEntryRequest request, CancellationToken cancellationToken = default)
    {
        var context = await GetVersionContextAsync(request.VersionId, cancellationToken);
        EnsureCompanyWrite(context.CompanyId);
        var infrastructure = await RequireInfrastructureAsync(cancellationToken);
        if (context.ModelId != infrastructure.Model.Id)
            throw new ArgumentException("Selected budget version is not a cash planning version.");
        var measureCode = (request.MeasureCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!CashMeasureCodes.Contains(measureCode, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unsupported cash planning measure.");
        if (request.Value < 0m) throw new ArgumentException("Cash planning values cannot be negative; use inflow/outflow direction instead of negative amounts.");
        if (request.ValueKind == ValueKind.Commitment && measureCode is "OPENING_CASH" or "MINIMUM_CASH_BUFFER")
            throw new ArgumentException("Commitment is only valid for cash inflow/outflow measures.");

        var measure = infrastructure.Measures.Single(x => x.Code == measureCode);
        var item = await db.DimensionMembers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.ItemMemberId && x.DimensionId == infrastructure.ItemDimension.Id && x.IsActive, cancellationToken)
            ?? throw new ArgumentException("Cash flow item is invalid.");
        if (!await db.FiscalPeriods.AnyAsync(x => x.Id == request.PeriodId && x.FiscalYearId == context.FiscalYearId, cancellationToken))
            throw new ArgumentException("Period does not belong to the cash plan fiscal year.");
        var currency = (request.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!await db.Currencies.AnyAsync(x => x.TenantId == user.TenantId && x.Code == currency, cancellationToken))
            throw new ArgumentException("Currency is not defined for the current tenant.");

        return await budgetService.UpsertFactAsync(new UpsertBudgetFactRequest(
            request.VersionId,
            request.PeriodId,
            measure.Id,
            request.ValueKind,
            request.Value,
            currency,
            [new DimensionSelection(infrastructure.ItemDimension.Id, item.Id)],
            "CashPlanning",
            string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()), cancellationToken);
    }

    private async Task<(BudgetModel Model, DimensionDefinition ItemDimension, List<MeasureDefinition> Measures)?> GetInfrastructureAsync(CancellationToken ct)
    {
        var model = await db.BudgetModels.Include(x => x.Dimensions).Include(x => x.Measures)
            .SingleOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == "CASHFLOW", ct);
        var dimension = await db.Dimensions.SingleOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == "CASHFLOW_ITEM", ct);
        if (model is null || dimension is null) return null;
        var measures = model.Measures.Where(x => CashMeasureCodes.Contains(x.Code, StringComparer.OrdinalIgnoreCase)).ToList();
        if (measures.Count != CashMeasureCodes.Length) return null;
        return (model, dimension, measures);
    }

    private async Task<(BudgetModel Model, DimensionDefinition ItemDimension, List<MeasureDefinition> Measures)> RequireInfrastructureAsync(CancellationToken ct) =>
        await GetInfrastructureAsync(ct) ?? throw new InvalidOperationException("Cash planning infrastructure has not been initialized.");

    private async Task<(BudgetModel Model, DimensionDefinition ItemDimension, List<MeasureDefinition> Measures)> EnsureInfrastructureAsync(CancellationToken ct)
    {
        var existing = await GetInfrastructureAsync(ct);
        if (existing.HasValue) return existing.Value;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var itemDimension = await db.Dimensions.SingleOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == "CASHFLOW_ITEM", ct);
        if (itemDimension is null)
        {
            itemDimension = new DimensionDefinition
            {
                TenantId = user.TenantId,
                Code = "CASHFLOW_ITEM",
                Name = "آیتم جریان نقدی",
                IsSystem = true,
                IsHierarchical = true
            };
            db.Dimensions.Add(itemDimension);
            await db.SaveChangesAsync(ct);
        }

        var standardItems = new[]
        {
            ("OPENING_BALANCE", "مانده افتتاحیه"),
            ("CUSTOMER_COLLECTIONS", "وصول از مشتریان"),
            ("OTHER_OPERATING_INFLOW", "سایر دریافت‌های عملیاتی"),
            ("SUPPLIER_PAYMENTS", "پرداخت به تامین‌کنندگان"),
            ("PAYROLL", "حقوق و مزایا"),
            ("TAX_AND_DUTY", "مالیات، عوارض و حقوق دولتی"),
            ("OTHER_OPERATING_OUTFLOW", "سایر پرداخت‌های عملیاتی"),
            ("CAPEX_PAYMENTS", "پرداخت پروژه‌های سرمایه‌ای"),
            ("LOAN_DRAWDOWN", "دریافت تسهیلات"),
            ("LOAN_REPAYMENT", "بازپرداخت اصل تسهیلات"),
            ("FINANCE_COST", "سود و هزینه تامین مالی"),
            ("OTHER_FINANCING", "سایر جریان‌های تامین مالی"),
            ("LIQUIDITY_BUFFER", "حداقل ذخیره نقدینگی")
        };
        var existingItemCodes = await db.DimensionMembers.Where(x => x.DimensionId == itemDimension.Id).Select(x => x.Code).ToHashSetAsync(ct);
        foreach (var (code, name) in standardItems)
            if (!existingItemCodes.Contains(code))
                db.DimensionMembers.Add(new DimensionMember { DimensionId = itemDimension.Id, CompanyId = null, Code = code, Name = name });

        var model = await db.BudgetModels.Include(x => x.Dimensions).Include(x => x.Measures)
            .SingleOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == "CASHFLOW", ct);
        if (model is null)
        {
            model = new BudgetModel
            {
                TenantId = user.TenantId,
                Code = "CASHFLOW",
                Name = "برنامه نقدینگی و خزانه‌داری",
                Description = "برنامه ماهانه دریافت، پرداخت، مانده نقد و حداقل ذخیره نقدینگی"
            };
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = itemDimension.Id, Sequence = 1, IsRequired = true });
            var optionalCodes = new[] { "DEPARTMENT", "COSTCENTER" };
            var optional = await db.Dimensions.Where(x => x.TenantId == user.TenantId && optionalCodes.Contains(x.Code)).ToListAsync(ct);
            var sequence = 2;
            foreach (var dimension in optional.OrderBy(x => Array.IndexOf(optionalCodes, x.Code)))
                model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = dimension.Id, Sequence = sequence++, IsRequired = false });
            model.Measures.Add(new MeasureDefinition { BudgetModelId = model.Id, Code = "OPENING_CASH", Name = "مانده نقد ابتدای دوره", Unit = "ارز", ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Sum, DisplayOrder = 1 });
            model.Measures.Add(new MeasureDefinition { BudgetModelId = model.Id, Code = "CASH_INFLOW", Name = "دریافت نقدی", Unit = "ارز", ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Sum, DisplayOrder = 2 });
            model.Measures.Add(new MeasureDefinition { BudgetModelId = model.Id, Code = "CASH_OUTFLOW", Name = "پرداخت نقدی", Unit = "ارز", ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Sum, DisplayOrder = 3 });
            model.Measures.Add(new MeasureDefinition { BudgetModelId = model.Id, Code = "MINIMUM_CASH_BUFFER", Name = "حداقل ذخیره نقدینگی", Unit = "ارز", ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Max, DisplayOrder = 4 });
            db.BudgetModels.Add(model);
        }
        else
        {
            if (!model.Dimensions.Any(x => x.DimensionId == itemDimension.Id))
                model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = itemDimension.Id, Sequence = 1, IsRequired = true });
            var existingMeasureCodes = model.Measures.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!existingMeasureCodes.Contains("OPENING_CASH")) model.Measures.Add(new MeasureDefinition { BudgetModelId = model.Id, Code = "OPENING_CASH", Name = "مانده نقد ابتدای دوره", Unit = "ارز", ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Sum, DisplayOrder = 1 });
            if (!existingMeasureCodes.Contains("CASH_INFLOW")) model.Measures.Add(new MeasureDefinition { BudgetModelId = model.Id, Code = "CASH_INFLOW", Name = "دریافت نقدی", Unit = "ارز", ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Sum, DisplayOrder = 2 });
            if (!existingMeasureCodes.Contains("CASH_OUTFLOW")) model.Measures.Add(new MeasureDefinition { BudgetModelId = model.Id, Code = "CASH_OUTFLOW", Name = "پرداخت نقدی", Unit = "ارز", ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Sum, DisplayOrder = 3 });
            if (!existingMeasureCodes.Contains("MINIMUM_CASH_BUFFER")) model.Measures.Add(new MeasureDefinition { BudgetModelId = model.Id, Code = "MINIMUM_CASH_BUFFER", Name = "حداقل ذخیره نقدینگی", Unit = "ارز", ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Max, DisplayOrder = 4 });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await RequireInfrastructureAsync(ct);
    }

    private async Task<CashPlanSetupDto> BuildSetupAsync(
        Guid companyId,
        Guid fiscalYearId,
        (BudgetModel Model, DimensionDefinition ItemDimension, List<MeasureDefinition> Measures) infrastructure,
        CancellationToken ct)
    {
        var plan = await db.BudgetPlans.AsNoTracking().Where(x => x.CompanyId == companyId && x.FiscalYearId == fiscalYearId && x.BudgetModelId == infrastructure.Model.Id)
            .Select(x => new
            {
                x.Id,
                Versions = x.Versions.OrderBy(v => v.VersionNumber).Select(v => new CashPlanVersionDto(v.Id, v.ScenarioId, v.VersionNumber, v.Name, v.Status, v.IsLocked)).ToList()
            })
            .SingleOrDefaultAsync(ct);
        var items = await db.DimensionMembers.AsNoTracking().Where(x => x.DimensionId == infrastructure.ItemDimension.Id && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .OrderBy(x => x.Name).Select(x => new CashFlowItemDto(x.Id, x.Code, x.Name)).ToListAsync(ct);
        var measureByCode = infrastructure.Measures.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        return new CashPlanSetupDto(
            infrastructure.Model.Id,
            infrastructure.ItemDimension.Id,
            measureByCode["OPENING_CASH"].Id,
            measureByCode["CASH_INFLOW"].Id,
            measureByCode["CASH_OUTFLOW"].Id,
            measureByCode["MINIMUM_CASH_BUFFER"].Id,
            plan?.Id,
            plan?.Versions ?? [],
            items);
    }

    private CashPlanCurrencySummaryDto BuildCurrencySummary(
        string currencyCode,
        string baseCurrency,
        IReadOnlyList<FiscalPeriod> periods,
        IReadOnlyList<BudgetFact> facts,
        IReadOnlyDictionary<string, Guid> measureIds)
    {
        var currencyFacts = facts.Where(x => string.Equals(string.IsNullOrWhiteSpace(x.CurrencyCode) ? baseCurrency : x.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase)).ToList();
        decimal previousBudgetClosing = 0m;
        decimal previousActualClosing = 0m;
        decimal previousForecastClosing = 0m;
        var monthly = new List<CashPlanMonthlyDto>(periods.Count);

        foreach (var period in periods)
        {
            var budgetOpening = Read(period.Id, measureIds["OPENING_CASH"], ValueKind.Budget);
            var actualOpening = Read(period.Id, measureIds["OPENING_CASH"], ValueKind.Actual);
            var forecastOpening = Read(period.Id, measureIds["OPENING_CASH"], ValueKind.Forecast);
            var budgetIn = Read(period.Id, measureIds["CASH_INFLOW"], ValueKind.Budget);
            var budgetOut = Read(period.Id, measureIds["CASH_OUTFLOW"], ValueKind.Budget);
            var actualIn = Read(period.Id, measureIds["CASH_INFLOW"], ValueKind.Actual);
            var actualOut = Read(period.Id, measureIds["CASH_OUTFLOW"], ValueKind.Actual);
            var forecastIn = Read(period.Id, measureIds["CASH_INFLOW"], ValueKind.Forecast);
            var forecastOut = Read(period.Id, measureIds["CASH_OUTFLOW"], ValueKind.Forecast);
            var commitmentOut = Read(period.Id, measureIds["CASH_OUTFLOW"], ValueKind.Commitment);
            var buffer = Read(period.Id, measureIds["MINIMUM_CASH_BUFFER"], ValueKind.Budget);

            var openingBudget = budgetOpening.HasValue ? budgetOpening.Value : previousBudgetClosing;
            var closingBudget = openingBudget + budgetIn.Value - budgetOut.Value;
            var openingActual = actualOpening.HasValue ? actualOpening.Value : previousActualClosing;
            var closingActual = openingActual + actualIn.Value - actualOut.Value;
            var openingForecast = forecastOpening.HasValue ? forecastOpening.Value : previousForecastClosing;
            if (!forecastOpening.HasValue && monthly.Count == 0 && actualOpening.HasValue) openingForecast = actualOpening.Value;
            if (!forecastOpening.HasValue && monthly.Count == 0 && !actualOpening.HasValue && budgetOpening.HasValue) openingForecast = budgetOpening.Value;
            var inflowForecast = forecastIn.HasValue ? forecastIn.Value : budgetIn.Value;
            var outflowForecast = forecastOut.HasValue ? forecastOut.Value : budgetOut.Value;
            var closingForecast = openingForecast + inflowForecast - outflowForecast;
            var projectedAvailable = closingForecast - commitmentOut.Value;
            var minimumBuffer = buffer.HasValue ? buffer.Value : 0m;
            var liquidityGap = projectedAvailable - minimumBuffer;

            monthly.Add(new CashPlanMonthlyDto(
                period.Id, period.Name, period.Sequence,
                openingBudget, budgetIn.Value, budgetOut.Value, closingBudget,
                openingActual, actualIn.Value, actualOut.Value, closingActual,
                openingForecast, inflowForecast, outflowForecast, closingForecast,
                commitmentOut.Value, projectedAvailable, minimumBuffer, liquidityGap));
            previousBudgetClosing = closingBudget;
            previousActualClosing = closingActual;
            previousForecastClosing = closingForecast;
        }

        var ending = monthly.LastOrDefault();
        var minimumProjected = monthly.Count == 0 ? 0m : monthly.Min(x => x.ProjectedAvailable);
        var maximumShortfall = monthly.Count == 0 ? 0m : Math.Max(0m, -monthly.Min(x => x.LiquidityGap));
        return new CashPlanCurrencySummaryDto(
            currencyCode,
            monthly.Sum(x => x.BudgetInflow), monthly.Sum(x => x.BudgetOutflow),
            monthly.Sum(x => x.ActualInflow), monthly.Sum(x => x.ActualOutflow),
            monthly.Sum(x => x.ForecastInflow), monthly.Sum(x => x.ForecastOutflow),
            monthly.Sum(x => x.CommitmentOutflow),
            ending?.BudgetClosing ?? 0m, ending?.ActualClosing ?? 0m, ending?.ForecastClosing ?? 0m,
            ending?.ProjectedAvailable ?? 0m, minimumProjected, maximumShortfall,
            monthly.Count(x => x.LiquidityGap < 0m), monthly);

        MeasureRead Read(Guid periodId, Guid measureId, ValueKind kind)
        {
            var matching = currencyFacts.Where(x => x.PeriodId == periodId && x.MeasureId == measureId && x.ValueKind == kind).ToList();
            return new MeasureRead(matching.Count > 0, matching.Sum(x => x.Value));
        }
    }

    private async Task<(Guid CompanyId, Guid FiscalYearId, Guid ModelId)> GetVersionContextAsync(Guid versionId, CancellationToken ct)
    {
        return await db.BudgetVersions.AsNoTracking().Where(x => x.Id == versionId)
            .Select(x => new ValueTuple<Guid, Guid, Guid>(x.BudgetPlan!.CompanyId, x.BudgetPlan.FiscalYearId, x.BudgetPlan.BudgetModelId))
            .SingleOrDefaultAsync(ct) is var value && value != default
            ? value
            : throw new KeyNotFoundException("Budget version was not found.");
    }

    private async Task EnsureFiscalYearAsync(Guid companyId, Guid fiscalYearId, bool requireOpen, CancellationToken ct)
    {
        var year = await db.FiscalYears.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, ct)
            ?? throw new ArgumentException("Fiscal year does not belong to the selected company.");
        if (requireOpen && year.IsClosed) throw new InvalidOperationException("Closed fiscal years cannot accept a cash plan.");
    }

    private bool CanWriteCompany(Guid companyId) => user.IsInRole("SUPERADMIN") || user.CanWriteCompany(companyId);
    private void EnsureCompanyRead(Guid companyId) { if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company."); }
    private void EnsureCompanyWrite(Guid companyId) { if (!CanWriteCompany(companyId)) throw new UnauthorizedAccessException("You do not have write access to this company."); }

    private readonly record struct MeasureRead(bool HasValue, decimal Value);
}
