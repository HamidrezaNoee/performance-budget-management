using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class SalesPlanningService(
    PbmDbContext db,
    IUserContext user,
    BudgetService budgetService,
    CommercialPlanningProvisioner provisioner) : ISalesPlanningService
{
    private const string ModelCode = "TRADE";
    private const string ProductDimensionCode = "PRODUCT";
    private const string PurchaseCostDimensionCode = "PURCHASECOST";

    private static readonly string[] SeriesCodes =
    [
        "SALES_QTY", "FREE_SALES_QTY", "SALES_PRICE", "GROSS_SALES", "SALES_DISCOUNT", "SALES_RETURN",
        "FOC_SALES_AMOUNT", "NET_SALES", "COGS_AMOUNT", "PURCHASE_COMPANY_DISCOUNT", "SALES_GROSS_MARGIN"
    ];

    private static readonly HashSet<string> EditableCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SALES_QTY", "FREE_SALES_QTY", "SALES_PRICE", "SALES_DISCOUNT", "SALES_RETURN",
        "FOC_SALES_AMOUNT", "COGS_AMOUNT", "PURCHASE_COMPANY_DISCOUNT"
    };

    public async Task<SalesPlanningSetupDto> GetSetupAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureCompany(companyId);
        var tenantId = await GetCompanyTenantAsync(companyId, cancellationToken);
        await provisioner.EnsureSalesAsync(tenantId, cancellationToken);

        var model = await db.BudgetModels.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenantId && x.Code == ModelCode && x.IsActive, cancellationToken);
        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == model.Id && x.Dimension!.IsActive && x.Dimension.Code != PurchaseCostDimensionCode)
            .OrderBy(x => x.Sequence)
            .Select(x => new { x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence, x.IsRequired })
            .ToListAsync(cancellationToken);
        var dimensionIds = modelDimensions.Select(x => x.DimensionId).ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => dimensionIds.Contains(x.DimensionId) && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .OrderBy(x => x.Name)
            .Select(x => new SalesPlanningMemberDto(x.Id, x.DimensionId, x.Code, x.Name))
            .ToListAsync(cancellationToken);
        var groupedMembers = members.GroupBy(x => x.DimensionId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<SalesPlanningMemberDto>)x.ToList());
        var dimensions = modelDimensions.Select(x => new SalesPlanningDimensionDto(
            x.DimensionId, x.Code, x.Name, x.Sequence, x.IsRequired,
            groupedMembers.GetValueOrDefault(x.DimensionId, []))).ToList();

        if (!dimensions.Any(x => x.Code == ProductDimensionCode))
            throw new InvalidOperationException("TRADE model requires PRODUCT for sales planning.");

        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == model.Id && SeriesCodes.Contains(x.Code))
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SalesPlanningMeasureDto(x.Id, x.Code, x.Name, x.Unit, x.ValueType, x.IsCalculated))
            .ToListAsync(cancellationToken);
        if (SeriesCodes.Any(code => measures.All(x => !x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Sales planning measures are not fully initialized.");

        return new SalesPlanningSetupDto(model.Id, model.Name, await GetBaseCurrencyAsync(tenantId, cancellationToken), dimensions, measures);
    }

    public async Task<SalesPlanningDataDto> QueryAsync(SalesPlanningQueryRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePlanningKind(request.ValueKind);
        var context = await ResolveContextAsync(request.VersionId, cancellationToken);
        EnsureCompany(context.CompanyId);
        await provisioner.EnsureSalesAsync(context.TenantId, cancellationToken);
        var dimensions = await ValidateDimensionsAsync(context.ModelId, context.CompanyId, request.Dimensions, cancellationToken);
        var hash = BudgetCoordinateKey.Create(dimensions);

        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == context.FiscalYearId)
            .OrderBy(x => x.Sequence)
            .Select(x => new FiscalPeriodDto(x.Id, x.Sequence, x.Code, x.Name, x.JalaliMonth, x.StartDate, x.EndDate, x.IsClosed))
            .ToListAsync(cancellationToken);
        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == context.ModelId && SeriesCodes.Contains(x.Code))
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);
        var measureIds = measures.Select(x => x.Id).ToArray();
        var facts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == request.VersionId
                && x.ValueKind == request.ValueKind
                && x.CoordinateHash == hash
                && measureIds.Contains(x.MeasureId))
            .ToListAsync(cancellationToken);

        var orderedMeasures = SeriesCodes
            .Select(code => measures.Single(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var series = orderedMeasures.Select(measure => new SalesPlanningSeriesDto(
            measure.Code,
            measure.Name,
            measure.Unit,
            measure.IsCalculated,
            BuildSeries(periods, facts, measure.Id))).ToList();
        return new SalesPlanningDataDto(periods, series);
    }

    public async Task<Guid> UpsertCellAsync(UpsertSalesPlanningCellRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePlanningKind(request.ValueKind);
        var context = await ResolveContextAsync(request.VersionId, cancellationToken);
        EnsureCompanyWrite(context.CompanyId);
        await provisioner.EnsureSalesAsync(context.TenantId, cancellationToken);
        var dimensions = await ValidateDimensionsAsync(context.ModelId, context.CompanyId, request.Dimensions, cancellationToken);
        var code = (request.MeasureCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!EditableCodes.Contains(code)) throw new ArgumentException("Selected sales measure is calculated or not editable.");
        if (request.Value < 0) throw new ArgumentOutOfRangeException(nameof(request.Value), "Sales planning values cannot be negative.");

        var measure = await db.Measures.AsNoTracking()
            .SingleAsync(x => x.BudgetModelId == context.ModelId && x.Code == code, cancellationToken);
        var currency = measure.ValueType == MeasureValueType.Amount
            ? await GetBaseCurrencyAsync(context.TenantId, cancellationToken)
            : null;
        return await budgetService.UpsertFactAsync(new UpsertBudgetFactRequest(
            request.VersionId,
            request.PeriodId,
            measure.Id,
            request.ValueKind,
            request.Value,
            currency,
            dimensions,
            request.ValueKind == ValueKind.Budget ? "SalesBudgetPlanner" : "SalesForecastPlanner",
            request.Note), cancellationToken);
    }

    private async Task<PlanningContext> ResolveContextAsync(Guid versionId, CancellationToken ct)
    {
        var context = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new PlanningContext(
                x.Id,
                x.BudgetPlan!.Company!.TenantId,
                x.BudgetPlan.CompanyId,
                x.BudgetPlan.FiscalYearId,
                x.BudgetPlan.BudgetModelId,
                x.BudgetPlan.BudgetModel!.Code))
            .SingleAsync(ct);
        if (context.TenantId != user.TenantId) throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        if (!context.ModelCode.Equals(ModelCode, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Sales planning can only use the TRADE budget model.");
        return context;
    }

    private async Task<IReadOnlyList<DimensionSelection>> ValidateDimensionsAsync(
        Guid modelId,
        Guid companyId,
        IReadOnlyList<DimensionSelection> selections,
        CancellationToken ct)
    {
        if (selections is null || selections.Count == 0) throw new ArgumentException("At least PRODUCT must be selected.");
        if (selections.Select(x => x.DimensionId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A dimension can only be selected once.");
        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == modelId && x.Dimension!.Code != PurchaseCostDimensionCode)
            .Select(x => new { x.DimensionId, x.IsRequired, x.Dimension!.Code })
            .ToListAsync(ct);
        var allowed = modelDimensions.ToDictionary(x => x.DimensionId);
        var supplied = selections.Select(x => x.DimensionId).ToHashSet();
        if (selections.Any(x => !allowed.ContainsKey(x.DimensionId))) throw new ArgumentException("A selected dimension does not belong to sales planning.");
        if (modelDimensions.Any(x => x.IsRequired && !supplied.Contains(x.DimensionId))) throw new ArgumentException("One or more required TRADE dimensions are missing.");
        if (modelDimensions.Any(x => x.Code == ProductDimensionCode && !supplied.Contains(x.DimensionId))) throw new ArgumentException("PRODUCT is required for sales planning.");

        var dimensionIds = selections.Select(x => x.DimensionId).ToArray();
        var memberIds = selections.Select(x => x.MemberId).ToArray();
        var pairs = await db.DimensionMembers.AsNoTracking()
            .Where(x => dimensionIds.Contains(x.DimensionId) && memberIds.Contains(x.Id) && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.DimensionId, x.Id }).ToListAsync(ct);
        if (selections.Any(s => !pairs.Any(x => x.DimensionId == s.DimensionId && x.Id == s.MemberId)))
            throw new ArgumentException("One or more sales dimension members are invalid for this company.");
        return selections.OrderBy(x => x.DimensionId).ToList();
    }

    private static IReadOnlyList<SalesPlanningPeriodValueDto> BuildSeries(
        IReadOnlyList<FiscalPeriodDto> periods,
        IReadOnlyList<BudgetFact> facts,
        Guid measureId)
    {
        var values = facts.Where(x => x.MeasureId == measureId).ToDictionary(x => x.PeriodId);
        return periods.Select(period => values.TryGetValue(period.Id, out var fact)
            ? new SalesPlanningPeriodValueDto(period.Id, period.Name, period.Sequence, fact.Value, fact.Id)
            : new SalesPlanningPeriodValueDto(period.Id, period.Name, period.Sequence, 0m)).ToList();
    }

    private async Task<Guid> GetCompanyTenantAsync(Guid companyId, CancellationToken ct)
    {
        var tenantId = await db.Companies.AsNoTracking().Where(x => x.Id == companyId && x.IsActive).Select(x => (Guid?)x.TenantId).SingleOrDefaultAsync(ct)
            ?? throw new ArgumentException("Company was not found or is inactive.");
        if (tenantId != user.TenantId) throw new UnauthorizedAccessException("Company is outside the current tenant.");
        return tenantId;
    }

    private async Task<string> GetBaseCurrencyAsync(Guid tenantId, CancellationToken ct) =>
        await db.Currencies.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync(ct) ?? "IRR";

    private static void ValidatePlanningKind(ValueKind valueKind)
    {
        if (valueKind is not (ValueKind.Budget or ValueKind.Forecast))
            throw new ArgumentException("Sales planner supports Budget and Forecast values only.");
    }

    private void EnsureCompany(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private void EnsureCompanyWrite(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId)) throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private sealed record PlanningContext(Guid VersionId, Guid TenantId, Guid CompanyId, Guid FiscalYearId, Guid ModelId, string ModelCode);
}
