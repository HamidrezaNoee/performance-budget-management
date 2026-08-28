using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class GovernedBudgetService(
    BudgetService inner,
    PbmDbContext db,
    IUserContext user,
    ICalculationService calculation) : IBudgetService
{
    public Task<IReadOnlyList<FiscalYearDto>> GetFiscalYearsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        inner.GetFiscalYearsAsync(companyId, cancellationToken);

    public Task<IReadOnlyList<FiscalPeriodDto>> GetPeriodsAsync(Guid fiscalYearId, CancellationToken cancellationToken = default) =>
        inner.GetPeriodsAsync(fiscalYearId, cancellationToken);

    public Task<IReadOnlyList<BudgetModelDto>> GetModelsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        inner.GetModelsAsync(companyId, cancellationToken);

    public Task<IReadOnlyList<DimensionDto>> GetDimensionsAsync(Guid modelId, CancellationToken cancellationToken = default) =>
        inner.GetDimensionsAsync(modelId, cancellationToken);

    public Task<IReadOnlyList<DimensionMemberDto>> GetDimensionMembersAsync(Guid dimensionId, Guid? companyId, CancellationToken cancellationToken = default) =>
        inner.GetDimensionMembersAsync(dimensionId, companyId, cancellationToken);

    public Task<IReadOnlyList<MeasureDto>> GetMeasuresAsync(Guid modelId, CancellationToken cancellationToken = default) =>
        inner.GetMeasuresAsync(modelId, cancellationToken);

    public Task<IReadOnlyList<BudgetPlanDto>> GetPlansAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default) =>
        inner.GetPlansAsync(companyId, fiscalYearId, cancellationToken);

    public Task<BudgetPlanDto> CreatePlanAsync(CreateBudgetPlanRequest request, CancellationToken cancellationToken = default) =>
        inner.CreatePlanAsync(request, cancellationToken);

    public Task<BudgetGridDto> GetGridAsync(BudgetGridQuery query, CancellationToken cancellationToken = default) =>
        inner.GetGridAsync(query, cancellationToken);

    public async Task<Guid> UpsertFactAsync(UpsertBudgetFactRequest request, CancellationToken cancellationToken = default)
    {
        var version = await db.BudgetVersions.AsNoTracking()
            .Include(x => x.BudgetPlan)
            .SingleOrDefaultAsync(x => x.Id == request.VersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget version was not found.");

        var plan = version.BudgetPlan ?? throw new InvalidOperationException("Budget version has no plan.");
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == plan.CompanyId && x.TenantId == user.TenantId, cancellationToken))
            throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        EnsureCompanyWrite(plan.CompanyId);

        var decision = BudgetFactWritePolicy.Evaluate(version.Status, version.IsLocked, request.ValueKind);
        if (!decision.IsAllowed)
            throw new InvalidOperationException(decision.DenialReason);

        // Preserve the existing budget-entry behavior for normal Draft versions.
        if (version.Status == BudgetStatus.Draft && !version.IsLocked)
            return await inner.UpsertFactAsync(request, cancellationToken);

        // The only non-Draft path permitted by the policy is execution data on an Approved version.
        return await UpsertApprovedExecutionFactAsync(version, plan, request, cancellationToken);
    }

    private async Task<Guid> UpsertApprovedExecutionFactAsync(
        BudgetVersion version,
        BudgetPlan plan,
        UpsertBudgetFactRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ValueKind is not (ValueKind.Actual or ValueKind.Commitment))
            throw new InvalidOperationException("Only Actual or Commitment execution facts can be posted to an approved budget version.");

        var period = await db.FiscalPeriods.AsNoTracking()
            .Include(x => x.FiscalYear)
            .SingleOrDefaultAsync(x => x.Id == request.PeriodId && x.FiscalYearId == plan.FiscalYearId, cancellationToken)
            ?? throw new ArgumentException("Period does not belong to the budget plan fiscal year.");
        if (period.IsClosed || period.FiscalYear!.IsClosed)
            throw new InvalidOperationException("Closed fiscal periods cannot accept Actual or Commitment execution data.");

        var measure = await db.Measures.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.MeasureId && x.BudgetModelId == plan.BudgetModelId, cancellationToken)
            ?? throw new ArgumentException("Measure does not belong to the budget model.");
        if (measure.IsCalculated)
            throw new InvalidOperationException("Calculated measures are read-only and are generated from their formula dependencies.");

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == plan.BudgetModelId)
            .ToListAsync(cancellationToken);
        var allowedDimensions = modelDimensions.Select(x => x.DimensionId).ToHashSet();
        var suppliedDimensions = request.Dimensions.Select(x => x.DimensionId).ToList();
        if (suppliedDimensions.Count != suppliedDimensions.Distinct().Count())
            throw new ArgumentException("A dimension can only be supplied once.");
        if (modelDimensions.Where(x => x.IsRequired).Any(x => !suppliedDimensions.Contains(x.DimensionId)))
            throw new ArgumentException("One or more required dimensions are missing.");
        if (suppliedDimensions.Any(x => !allowedDimensions.Contains(x)))
            throw new ArgumentException("A supplied dimension does not belong to the budget model.");

        foreach (var selection in request.Dimensions)
        {
            var valid = await db.DimensionMembers.AsNoTracking().AnyAsync(x =>
                x.Id == selection.MemberId
                && x.DimensionId == selection.DimensionId
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == plan.CompanyId), cancellationToken);
            if (!valid) throw new ArgumentException("Invalid dimension member selection.");
        }

        var currencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? null
            : request.CurrencyCode.Trim().ToUpperInvariant();
        if (currencyCode is not null && !await db.Currencies.AsNoTracking().AnyAsync(
                x => x.TenantId == user.TenantId && x.Code == currencyCode, cancellationToken))
            throw new ArgumentException("Currency is not defined for the current tenant.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var coordinateHash = BudgetCoordinateKey.Create(request.Dimensions);
        var coordinatesJson = JsonSerializer.Serialize(request.Dimensions.OrderBy(x => x.DimensionId));
        var fact = await db.BudgetFacts
            .Include(x => x.Dimensions)
            .SingleOrDefaultAsync(x =>
                x.VersionId == version.Id
                && x.PeriodId == request.PeriodId
                && x.MeasureId == request.MeasureId
                && x.ValueKind == request.ValueKind
                && x.CoordinateHash == coordinateHash, cancellationToken);

        var oldValue = fact?.Value;
        if (fact is null)
        {
            fact = new BudgetFact
            {
                VersionId = version.Id,
                PeriodId = request.PeriodId,
                MeasureId = request.MeasureId,
                ValueKind = request.ValueKind,
                CoordinateHash = coordinateHash,
                CoordinatesJson = coordinatesJson
            };
            db.BudgetFacts.Add(fact);
        }
        else
        {
            db.BudgetFactDimensions.RemoveRange(fact.Dimensions);
            fact.Dimensions.Clear();
        }

        fact.Value = request.Value;
        fact.CurrencyCode = currencyCode;
        fact.Source = string.IsNullOrWhiteSpace(request.Source) ? "Execution" : request.Source.Trim();
        fact.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        fact.UpdatedAtUtc = DateTime.UtcNow;
        foreach (var selection in request.Dimensions)
            fact.Dimensions.Add(new BudgetFactDimension
            {
                BudgetFactId = fact.Id,
                DimensionId = selection.DimensionId,
                MemberId = selection.MemberId
            });

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "BudgetFact",
            EntityId = fact.Id.ToString(),
            Action = oldValue.HasValue ? "EXECUTION_UPDATE" : "EXECUTION_CREATE",
            OldValueJson = oldValue.HasValue ? JsonSerializer.Serialize(new { Value = oldValue.Value }) : null,
            NewValueJson = JsonSerializer.Serialize(new
            {
                fact.Value,
                fact.ValueKind,
                fact.PeriodId,
                fact.MeasureId,
                fact.CurrencyCode,
                fact.CoordinateHash,
                VersionStatus = version.Status,
                fact.Source
            })
        });

        await db.SaveChangesAsync(cancellationToken);
        await calculation.RecalculateCoordinateAsync(
            version.Id,
            request.PeriodId,
            request.ValueKind,
            request.Dimensions,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return fact.Id;
    }

    private void EnsureCompanyWrite(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }
}
