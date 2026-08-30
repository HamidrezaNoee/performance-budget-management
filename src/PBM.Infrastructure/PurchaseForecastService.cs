using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class PurchaseForecastService(
    PbmDbContext db,
    IUserContext user,
    BudgetService budgetService) : IPurchaseForecastService
{
    private const string TradeModelCode = "TRADE";
    private const string ProductDimensionCode = "PRODUCT";
    private const string CostDimensionCode = "PURCHASECOST";
    private const string QuantityMeasureCode = "PURCHASE_FORECAST_QTY";
    private const string AmountMeasureCode = "PURCHASE_FORECAST_AMOUNT";
    private const string CostAmountMeasureCode = "PURCHASE_COST_AMOUNT";
    private const string CostRateMeasureCode = "PURCHASE_COST_RATE";

    public async Task<PurchaseForecastSetupDto> GetSetupAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        EnsureCompany(companyId);
        var tenantId = await db.Companies.AsNoTracking()
            .Where(x => x.Id == companyId && x.IsActive)
            .Select(x => x.TenantId)
            .SingleAsync(cancellationToken);
        if (tenantId != user.TenantId)
            throw new UnauthorizedAccessException("Company is outside the current tenant.");

        var model = await db.BudgetModels.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Code == TradeModelCode && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("TRADE budget model is not available.");

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == model.Id && x.Dimension!.IsActive)
            .OrderBy(x => x.Sequence)
            .Select(x => new
            {
                x.DimensionId,
                x.Dimension!.Code,
                x.Dimension.Name,
                x.Sequence,
                x.IsRequired
            })
            .ToListAsync(cancellationToken);

        var costDimension = modelDimensions.SingleOrDefault(x => x.Code == CostDimensionCode)
            ?? throw new InvalidOperationException("Purchase forecast setup is not initialized. Restart the API so PlanningSeedData can provision PURCHASECOST.");
        if (!modelDimensions.Any(x => x.Code == ProductDimensionCode))
            throw new InvalidOperationException("TRADE model requires the PRODUCT dimension.");

        var dimensionIds = modelDimensions.Select(x => x.DimensionId).ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => dimensionIds.Contains(x.DimensionId)
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == companyId))
            .OrderBy(x => x.Name)
            .Select(x => new PurchaseForecastMemberDto(x.Id, x.DimensionId, x.Code, x.Name))
            .ToListAsync(cancellationToken);
        var membersByDimension = members.GroupBy(x => x.DimensionId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<PurchaseForecastMemberDto>)x.ToList());

        var dimensions = modelDimensions
            .Where(x => x.Code != CostDimensionCode)
            .Select(x => new PurchaseForecastDimensionDto(
                x.DimensionId,
                x.Code,
                x.Name,
                x.Sequence,
                x.IsRequired,
                membersByDimension.GetValueOrDefault(x.DimensionId, [])))
            .ToList();
        var costTypes = membersByDimension.GetValueOrDefault(costDimension.DimensionId, []);

        var measureCodes = new[]
        {
            QuantityMeasureCode, AmountMeasureCode, CostAmountMeasureCode, CostRateMeasureCode
        };
        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == model.Id && measureCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        foreach (var code in measureCodes)
            if (!measures.ContainsKey(code))
                throw new InvalidOperationException($"Purchase forecast measure '{code}' is not initialized. Restart the API to run PlanningSeedData.");

        return new PurchaseForecastSetupDto(
            model.Id,
            model.Name,
            await GetBaseCurrencyAsync(tenantId, cancellationToken),
            dimensions,
            costTypes,
            ToMeasureDto(measures[QuantityMeasureCode]),
            ToMeasureDto(measures[AmountMeasureCode]),
            ToMeasureDto(measures[CostAmountMeasureCode]),
            ToMeasureDto(measures[CostRateMeasureCode]));
    }

    public async Task<PurchaseForecastMemberDto> CreateCostTypeAsync(
        CreatePurchaseCostTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureCompanyWrite(request.CompanyId);
        var code = NormalizeCode(request.Code);
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ArgumentException("Cost type name is required and must be at most 200 characters.");

        var tenantId = await db.Companies.AsNoTracking()
            .Where(x => x.Id == request.CompanyId && x.IsActive)
            .Select(x => x.TenantId)
            .SingleAsync(cancellationToken);
        if (tenantId != user.TenantId)
            throw new UnauthorizedAccessException("Company is outside the current tenant.");

        var dimension = await db.Dimensions.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Code == CostDimensionCode && x.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("PURCHASECOST dimension is not initialized.");

        if (await db.DimensionMembers.AnyAsync(x =>
                x.DimensionId == dimension.Id
                && x.Code == code
                && (x.CompanyId == null || x.CompanyId == request.CompanyId), cancellationToken))
            throw new InvalidOperationException("A purchase cost type with the same code already exists for this company.");

        var member = new DimensionMember
        {
            DimensionId = dimension.Id,
            CompanyId = request.CompanyId,
            Code = code,
            Name = name
        };
        db.DimensionMembers.Add(member);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "PurchaseCostType",
            EntityId = member.Id.ToString(),
            Action = "CREATE",
            NewValueJson = JsonSerializer.Serialize(new { member.Code, member.Name, member.CompanyId })
        });
        await db.SaveChangesAsync(cancellationToken);
        return new PurchaseForecastMemberDto(member.Id, member.DimensionId, member.Code, member.Name);
    }

    public async Task<PurchaseForecastDataDto> QueryAsync(
        PurchaseForecastQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveContextAsync(request.VersionId, cancellationToken);
        EnsureCompany(context.CompanyId);
        var dimensions = await ValidateBaseDimensionsAsync(context.ModelId, context.CompanyId, request.Dimensions, cancellationToken);

        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == context.FiscalYearId)
            .OrderBy(x => x.Sequence)
            .Select(x => new FiscalPeriodDto(x.Id, x.Sequence, x.Code, x.Name, x.JalaliMonth, x.StartDate, x.EndDate, x.IsClosed))
            .ToListAsync(cancellationToken);

        var measureCodes = new[] { QuantityMeasureCode, AmountMeasureCode, CostAmountMeasureCode, CostRateMeasureCode };
        var measures = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == context.ModelId && measureCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (measures.Count != measureCodes.Length)
            throw new InvalidOperationException("Purchase forecast measures are not fully initialized.");

        var baseHash = BudgetCoordinateKey.Create(dimensions);
        var baseMeasureIds = new[] { measures[QuantityMeasureCode].Id, measures[AmountMeasureCode].Id };
        var baseFacts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == request.VersionId
                && x.ValueKind == ValueKind.Forecast
                && x.CoordinateHash == baseHash
                && baseMeasureIds.Contains(x.MeasureId))
            .ToListAsync(cancellationToken);

        var costDimension = await db.Dimensions.AsNoTracking()
            .SingleAsync(x => x.TenantId == context.TenantId && x.Code == CostDimensionCode && x.IsActive, cancellationToken);
        var costTypes = await db.DimensionMembers.AsNoTracking()
            .Where(x => x.DimensionId == costDimension.Id
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == context.CompanyId))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var costHashes = costTypes.ToDictionary(
            x => x.Id,
            x => BudgetCoordinateKey.Create([.. dimensions, new DimensionSelection(costDimension.Id, x.Id)]));
        var hashValues = costHashes.Values.Distinct().ToArray();
        var costMeasureIds = new[] { measures[CostAmountMeasureCode].Id, measures[CostRateMeasureCode].Id };
        List<BudgetFact> costFacts = hashValues.Length == 0
            ? []
            : await db.BudgetFacts.AsNoTracking()
                .Where(x => x.VersionId == request.VersionId
                    && x.ValueKind == ValueKind.Forecast
                    && hashValues.Contains(x.CoordinateHash)
                    && costMeasureIds.Contains(x.MeasureId))
                .ToListAsync(cancellationToken);

        var quantity = BuildSeries(periods, baseFacts, measures[QuantityMeasureCode].Id);
        var amount = BuildSeries(periods, baseFacts, measures[AmountMeasureCode].Id);
        var costs = costTypes.Select(costType =>
        {
            var hash = costHashes[costType.Id];
            var facts = costFacts.Where(x => x.CoordinateHash == hash).ToList();
            return new PurchaseForecastCostSeriesDto(
                costType.Id,
                costType.Code,
                costType.Name,
                BuildSeries(periods, facts, measures[CostAmountMeasureCode].Id),
                BuildSeries(periods, facts, measures[CostRateMeasureCode].Id));
        }).ToList();

        return new PurchaseForecastDataDto(periods, quantity, amount, costs);
    }

    public async Task<Guid> UpsertCellAsync(
        UpsertPurchaseForecastCellRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveContextAsync(request.VersionId, cancellationToken);
        EnsureCompanyWrite(context.CompanyId);
        var dimensions = await ValidateBaseDimensionsAsync(context.ModelId, context.CompanyId, request.Dimensions, cancellationToken);
        var code = (request.MeasureCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code is not (QuantityMeasureCode or AmountMeasureCode or CostAmountMeasureCode or CostRateMeasureCode))
            throw new ArgumentException("Unsupported purchase forecast measure code.");
        if (request.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(request.Value), "Purchase forecast values cannot be negative.");

        var measure = await db.Measures.AsNoTracking()
            .SingleAsync(x => x.BudgetModelId == context.ModelId && x.Code == code, cancellationToken);
        var finalDimensions = dimensions.ToList();
        DimensionDefinition? costDimension = null;

        if (code is CostAmountMeasureCode or CostRateMeasureCode)
        {
            if (!request.CostTypeId.HasValue)
                throw new ArgumentException("A purchase cost type is required for a cost forecast value.");
            costDimension = await db.Dimensions.AsNoTracking()
                .SingleAsync(x => x.TenantId == context.TenantId && x.Code == CostDimensionCode && x.IsActive, cancellationToken);
            var validCostType = await db.DimensionMembers.AsNoTracking().AnyAsync(x =>
                x.Id == request.CostTypeId.Value
                && x.DimensionId == costDimension.Id
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == context.CompanyId), cancellationToken);
            if (!validCostType) throw new ArgumentException("Invalid purchase cost type.");
            finalDimensions.Add(new DimensionSelection(costDimension.Id, request.CostTypeId.Value));
        }
        else if (request.CostTypeId.HasValue)
        {
            throw new ArgumentException("Cost type can only be supplied for purchase cost amount/rate measures.");
        }

        var baseCurrencyCode = measure.ValueType == MeasureValueType.Amount
            ? await GetBaseCurrencyAsync(context.TenantId, cancellationToken)
            : null;

        var id = await budgetService.UpsertFactAsync(new UpsertBudgetFactRequest(
            request.VersionId,
            request.PeriodId,
            measure.Id,
            ValueKind.Forecast,
            request.Value,
            baseCurrencyCode,
            finalDimensions,
            "PurchaseForecastPlanner",
            request.Note), cancellationToken);

        if (code == CostRateMeasureCode && request.CostTypeId.HasValue && costDimension is not null)
        {
            var purchaseAmount = await GetPurchaseAmountAsync(
                request.VersionId, request.PeriodId, context.ModelId, dimensions, cancellationToken);
            await UpsertRateDrivenCostAmountAsync(
                context,
                request.PeriodId,
                dimensions,
                costDimension.Id,
                request.CostTypeId.Value,
                purchaseAmount,
                request.Value,
                cancellationToken);
        }
        else if (code == AmountMeasureCode)
        {
            await RecalculateRateDrivenCostsAsync(
                context,
                request.PeriodId,
                dimensions,
                request.Value,
                cancellationToken);
        }

        return id;
    }

    private async Task RecalculateRateDrivenCostsAsync(
        ForecastContext context,
        Guid periodId,
        IReadOnlyList<DimensionSelection> baseDimensions,
        decimal purchaseAmount,
        CancellationToken ct)
    {
        var costDimension = await db.Dimensions.AsNoTracking()
            .SingleAsync(x => x.TenantId == context.TenantId && x.Code == CostDimensionCode && x.IsActive, ct);
        var rateMeasureId = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == context.ModelId && x.Code == CostRateMeasureCode)
            .Select(x => x.Id)
            .SingleAsync(ct);
        var costTypes = await db.DimensionMembers.AsNoTracking()
            .Where(x => x.DimensionId == costDimension.Id
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == context.CompanyId))
            .Select(x => x.Id)
            .ToListAsync(ct);

        foreach (var costTypeId in costTypes)
        {
            var dimensions = baseDimensions.Concat([new DimensionSelection(costDimension.Id, costTypeId)]).ToList();
            var hash = BudgetCoordinateKey.Create(dimensions);
            var rate = await db.BudgetFacts.AsNoTracking()
                .Where(x => x.VersionId == context.VersionId
                    && x.PeriodId == periodId
                    && x.MeasureId == rateMeasureId
                    && x.ValueKind == ValueKind.Forecast
                    && x.CoordinateHash == hash)
                .Select(x => (decimal?)x.Value)
                .SingleOrDefaultAsync(ct);
            if (rate.HasValue)
                await UpsertRateDrivenCostAmountAsync(
                    context, periodId, baseDimensions, costDimension.Id, costTypeId, purchaseAmount, rate.Value, ct);
        }
    }

    private async Task UpsertRateDrivenCostAmountAsync(
        ForecastContext context,
        Guid periodId,
        IReadOnlyList<DimensionSelection> baseDimensions,
        Guid costDimensionId,
        Guid costTypeId,
        decimal purchaseAmount,
        decimal rate,
        CancellationToken ct)
    {
        var amountMeasure = await db.Measures.AsNoTracking()
            .SingleAsync(x => x.BudgetModelId == context.ModelId && x.Code == CostAmountMeasureCode, ct);
        var dimensions = baseDimensions.Concat([new DimensionSelection(costDimensionId, costTypeId)]).ToList();
        var calculatedAmount = decimal.Round(purchaseAmount * rate / 100m, 2, MidpointRounding.AwayFromZero);
        await budgetService.UpsertFactAsync(new UpsertBudgetFactRequest(
            context.VersionId,
            periodId,
            amountMeasure.Id,
            ValueKind.Forecast,
            calculatedAmount,
            await GetBaseCurrencyAsync(context.TenantId, ct),
            dimensions,
            "PurchaseForecastRate",
            $"Calculated from purchase amount using {rate}% purchase cost rate."), ct);
    }

    private async Task<decimal> GetPurchaseAmountAsync(
        Guid versionId,
        Guid periodId,
        Guid modelId,
        IReadOnlyList<DimensionSelection> dimensions,
        CancellationToken ct)
    {
        var amountMeasureId = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == modelId && x.Code == AmountMeasureCode)
            .Select(x => x.Id)
            .SingleAsync(ct);
        var hash = BudgetCoordinateKey.Create(dimensions);
        return await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == versionId
                && x.PeriodId == periodId
                && x.MeasureId == amountMeasureId
                && x.ValueKind == ValueKind.Forecast
                && x.CoordinateHash == hash)
            .Select(x => (decimal?)x.Value)
            .SingleOrDefaultAsync(ct) ?? 0m;
    }

    private async Task<ForecastContext> ResolveContextAsync(Guid versionId, CancellationToken ct)
    {
        var context = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new ForecastContext(
                x.Id,
                x.BudgetPlan!.Company!.TenantId,
                x.BudgetPlan.CompanyId,
                x.BudgetPlan.FiscalYearId,
                x.BudgetPlan.BudgetModelId,
                x.BudgetPlan.BudgetModel!.Code))
            .SingleAsync(ct);
        if (context.TenantId != user.TenantId)
            throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        if (!string.Equals(context.ModelCode, TradeModelCode, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Purchase forecast can only be recorded in the TRADE budget model.");
        return context;
    }

    private async Task<IReadOnlyList<DimensionSelection>> ValidateBaseDimensionsAsync(
        Guid modelId,
        Guid companyId,
        IReadOnlyList<DimensionSelection> selections,
        CancellationToken ct)
    {
        if (selections is null || selections.Count == 0)
            throw new ArgumentException("At least the PRODUCT dimension must be selected.");
        if (selections.Select(x => x.DimensionId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A dimension can only be selected once.");

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == modelId)
            .Select(x => new { x.DimensionId, x.IsRequired, Code = x.Dimension!.Code })
            .ToListAsync(ct);
        var allowed = modelDimensions.ToDictionary(x => x.DimensionId);
        var suppliedIds = selections.Select(x => x.DimensionId).ToHashSet();
        if (selections.Any(x => !allowed.ContainsKey(x.DimensionId)))
            throw new ArgumentException("A selected dimension does not belong to the TRADE model.");
        if (modelDimensions.Any(x => x.IsRequired && !suppliedIds.Contains(x.DimensionId)))
            throw new ArgumentException("One or more required TRADE dimensions are missing.");
        if (modelDimensions.Any(x => x.Code == ProductDimensionCode && !suppliedIds.Contains(x.DimensionId)))
            throw new ArgumentException("PRODUCT is required for purchase forecasting.");
        var costDimensionId = modelDimensions.Where(x => x.Code == CostDimensionCode)
            .Select(x => (Guid?)x.DimensionId).SingleOrDefault();
        if (costDimensionId.HasValue && suppliedIds.Contains(costDimensionId.Value))
            throw new ArgumentException("PURCHASECOST is managed separately and must not be included in the base dimension selection.");

        var dimensionIds = selections.Select(x => x.DimensionId).ToArray();
        var memberIds = selections.Select(x => x.MemberId).ToArray();
        var validPairs = await db.DimensionMembers.AsNoTracking()
            .Where(x => dimensionIds.Contains(x.DimensionId)
                && memberIds.Contains(x.Id)
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.DimensionId, x.Id })
            .ToListAsync(ct);
        if (selections.Any(selection => !validPairs.Any(x => x.DimensionId == selection.DimensionId && x.Id == selection.MemberId)))
            throw new ArgumentException("One or more selected dimension members are invalid for this company.");

        return selections.OrderBy(x => x.DimensionId).ToList();
    }

    private async Task<string> GetBaseCurrencyAsync(Guid tenantId, CancellationToken ct) =>
        await db.Currencies.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && x.IsBaseCurrency)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(ct) ?? "IRR";

    private static IReadOnlyList<PurchaseForecastPeriodValueDto> BuildSeries(
        IReadOnlyList<FiscalPeriodDto> periods,
        IReadOnlyList<BudgetFact> facts,
        Guid measureId)
    {
        var values = facts.Where(x => x.MeasureId == measureId).ToDictionary(x => x.PeriodId);
        return periods.Select(period => values.TryGetValue(period.Id, out var fact)
            ? new PurchaseForecastPeriodValueDto(period.Id, period.Name, period.Sequence, fact.Value, fact.Id)
            : new PurchaseForecastPeriodValueDto(period.Id, period.Name, period.Sequence, 0)).ToList();
    }

    private static PurchaseForecastMeasureDto ToMeasureDto(MeasureDefinition measure) =>
        new(measure.Id, measure.Code, measure.Name, measure.Unit, measure.ValueType, measure.Aggregation);

    private static string NormalizeCode(string value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')))
            throw new ArgumentException("Cost type code must contain 2-64 letters, numbers, underscore, dash or dot characters.");
        return code;
    }

    private void EnsureCompany(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private void EnsureCompanyWrite(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private sealed record ForecastContext(
        Guid VersionId,
        Guid TenantId,
        Guid CompanyId,
        Guid FiscalYearId,
        Guid ModelId,
        string ModelCode);
}
