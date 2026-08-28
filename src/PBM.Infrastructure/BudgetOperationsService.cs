using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class BudgetOperationsService(PbmDbContext db, IUserContext user, ICalculationService calculation) : IBudgetOperationsService
{
    public async Task<BudgetBulkOperationResultDto> CopyPriorYearActualAsync(
        CopyPriorYearActualRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.GrowthPercent is < -100m or > 1000m)
            throw new ArgumentException("Growth percent must be between -100 and 1000.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var target = await GetEditableVersionAsync(request.TargetVersionId, cancellationToken);
        var targetYear = target.BudgetPlan!.FiscalYear!;
        var previousYear = await db.FiscalYears.AsNoTracking()
            .Where(x => x.CompanyId == target.BudgetPlan.CompanyId && x.StartDate < targetYear.StartDate)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Previous fiscal year was not found for the selected company.");

        var sourcePlan = await db.BudgetPlans.AsNoTracking()
            .Where(x => x.CompanyId == target.BudgetPlan.CompanyId
                && x.FiscalYearId == previousYear.Id
                && x.BudgetModelId == target.BudgetPlan.BudgetModelId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("No prior-year plan exists for the selected budget model.");

        var sourceVersion = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.BudgetPlanId == sourcePlan.Id && x.Status != BudgetStatus.Rejected)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("No usable prior-year budget version was found.");

        var targetPeriods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == targetYear.Id)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);
        var targetPeriodBySequence = targetPeriods.ToDictionary(x => x.Sequence);

        var sourceFacts = await db.BudgetFacts.AsNoTracking()
            .Include(x => x.Dimensions)
            .Include(x => x.Period)
            .Where(x => x.VersionId == sourceVersion.Id
                && x.ValueKind == ValueKind.Actual
                && !x.Measure!.IsCalculated)
            .ToListAsync(cancellationToken);
        if (sourceFacts.Count == 0)
            throw new InvalidOperationException("The prior-year version has no Actual facts to use as a baseline.");

        var existing = await db.BudgetFacts
            .Where(x => x.VersionId == target.Id && x.ValueKind == ValueKind.Budget)
            .ToDictionaryAsync(x => (x.PeriodId, x.MeasureId, x.CoordinateHash), cancellationToken);

        var multiplier = 1m + request.GrowthPercent / 100m;
        var warnings = new List<string>();
        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var source in sourceFacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Period is null || !targetPeriodBySequence.TryGetValue(source.Period.Sequence, out var targetPeriod))
            {
                skipped++;
                AddWarning(warnings, $"No target period matches source sequence {source.Period?.Sequence.ToString() ?? "?"}.");
                continue;
            }
            if (targetPeriod.IsClosed)
            {
                skipped++;
                AddWarning(warnings, $"Target period '{targetPeriod.Name}' is closed and was skipped.");
                continue;
            }

            var key = (targetPeriod.Id, source.MeasureId, source.CoordinateHash);
            var value = Math.Round(source.Value * multiplier, 8, MidpointRounding.AwayFromZero);
            if (existing.TryGetValue(key, out var fact))
            {
                if (!request.ReplaceExisting)
                {
                    skipped++;
                    continue;
                }
                fact.Value = value;
                fact.CurrencyCode = source.CurrencyCode;
                fact.Source = $"PriorYearActual:{previousYear.Code}";
                fact.Note = $"Copied from prior-year actual with {request.GrowthPercent:0.####}% adjustment.";
                fact.UpdatedAtUtc = DateTime.UtcNow;
                updated++;
            }
            else
            {
                fact = new BudgetFact
                {
                    VersionId = target.Id,
                    PeriodId = targetPeriod.Id,
                    MeasureId = source.MeasureId,
                    ValueKind = ValueKind.Budget,
                    Value = value,
                    CurrencyCode = source.CurrencyCode,
                    CoordinateHash = source.CoordinateHash,
                    CoordinatesJson = source.CoordinatesJson,
                    Source = $"PriorYearActual:{previousYear.Code}",
                    Note = $"Copied from prior-year actual with {request.GrowthPercent:0.####}% adjustment."
                };
                foreach (var dimension in source.Dimensions)
                    fact.Dimensions.Add(new BudgetFactDimension
                    {
                        BudgetFactId = fact.Id,
                        DimensionId = dimension.DimensionId,
                        MemberId = dimension.MemberId
                    });
                db.BudgetFacts.Add(fact);
                existing[key] = fact;
                created++;
            }
        }

        AddAudit("BudgetVersion", target.Id, "COPY_PRIOR_YEAR_ACTUAL", new
        {
            SourceFiscalYearId = previousYear.Id,
            SourceFiscalYearCode = previousYear.Code,
            SourceVersionId = sourceVersion.Id,
            request.GrowthPercent,
            request.ReplaceExisting,
            created,
            updated,
            skipped
        });
        await db.SaveChangesAsync(cancellationToken);
        var calculationResult = await calculation.RecalculateVersionAsync(target.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var error in calculationResult.Errors) AddWarning(warnings, error);
        return new BudgetBulkOperationResultDto(created, updated, skipped, calculationResult.CoordinatesProcessed, warnings);
    }

    public async Task<BudgetBulkOperationResultDto> SpreadAsync(
        SpreadBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        var target = await GetEditableVersionAsync(request.VersionId, cancellationToken);
        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == target.BudgetPlan!.FiscalYearId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);
        var openPeriods = periods.Where(x => !x.IsClosed).ToList();
        if (openPeriods.Count == 0) throw new InvalidOperationException("All periods in the fiscal year are closed.");

        IReadOnlyList<decimal> weights;
        if (request.Method == BudgetSpreadMethod.Weighted)
        {
            if (request.Weights is null || request.Weights.Count != openPeriods.Count)
                throw new ArgumentException("Weighted spread requires one weight for each open fiscal period.");
            if (request.Weights.Any(x => x < 0) || request.Weights.Sum() <= 0)
                throw new ArgumentException("Spread weights must be non-negative and their sum must be greater than zero.");
            weights = request.Weights;
        }
        else
        {
            weights = Enumerable.Repeat(1m, openPeriods.Count).ToArray();
        }

        var totalWeight = weights.Sum();
        var cells = new List<BulkBudgetCellInput>(openPeriods.Count);
        decimal allocated = 0;
        for (var index = 0; index < openPeriods.Count; index++)
        {
            var value = index == openPeriods.Count - 1
                ? request.Total - allocated
                : Math.Round(request.Total * weights[index] / totalWeight, 8, MidpointRounding.AwayFromZero);
            allocated += value;
            cells.Add(new BulkBudgetCellInput(openPeriods[index].Id, value));
        }

        var result = await BulkPasteAsync(new BulkBudgetPasteRequest(
            request.VersionId,
            request.MeasureId,
            request.ValueKind,
            request.RowDimensionId,
            request.Filters,
            [new BulkBudgetRowInput(request.RowMemberId, cells)],
            request.CurrencyCode,
            request.Note ?? $"Spread:{request.Method}"), cancellationToken);

        AddAudit("BudgetVersion", request.VersionId, "SPREAD", new
        {
            request.MeasureId,
            request.ValueKind,
            request.RowDimensionId,
            request.RowMemberId,
            request.Total,
            request.Method,
            OpenPeriods = openPeriods.Count
        });
        await db.SaveChangesAsync(cancellationToken);

        var warnings = result.Warnings.ToList();
        if (openPeriods.Count != periods.Count)
            AddWarning(warnings, $"{periods.Count - openPeriods.Count} closed period(s) were excluded from the spread.");
        return result with { Warnings = warnings };
    }

    public async Task<BudgetBulkOperationResultDto> BulkPasteAsync(
        BulkBudgetPasteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0) throw new ArgumentException("At least one row is required for bulk paste.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var target = await GetEditableVersionAsync(request.VersionId, cancellationToken);
        var plan = target.BudgetPlan!;

        var measure = await db.Measures.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.MeasureId && x.BudgetModelId == plan.BudgetModelId, cancellationToken)
            ?? throw new ArgumentException("Measure does not belong to the budget model.");
        if (measure.IsCalculated) throw new InvalidOperationException("Calculated measures are read-only.");

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == plan.BudgetModelId)
            .ToListAsync(cancellationToken);
        var allowedDimensionIds = modelDimensions.Select(x => x.DimensionId).ToHashSet();
        if (!allowedDimensionIds.Contains(request.RowDimensionId)) throw new ArgumentException("Row dimension does not belong to the budget model.");
        if (request.Filters.Any(x => x.DimensionId == request.RowDimensionId)) throw new ArgumentException("Row dimension cannot also be supplied as a fixed filter.");
        if (request.Filters.Select(x => x.DimensionId).Distinct().Count() != request.Filters.Count) throw new ArgumentException("A fixed dimension filter can only be supplied once.");
        if (request.Filters.Any(x => !allowedDimensionIds.Contains(x.DimensionId))) throw new ArgumentException("A fixed dimension does not belong to the budget model.");

        var suppliedDimensions = request.Filters.Select(x => x.DimensionId).Append(request.RowDimensionId).ToHashSet();
        if (modelDimensions.Where(x => x.IsRequired).Any(x => !suppliedDimensions.Contains(x.DimensionId)))
            throw new ArgumentException("One or more required dimensions are missing from the row/filter coordinate.");

        var memberIds = request.Filters.Select(x => x.MemberId).Concat(request.Rows.Select(x => x.RowMemberId)).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (members.Count != memberIds.Length) throw new ArgumentException("One or more dimension members are invalid.");
        foreach (var filter in request.Filters)
            ValidateMember(members[filter.MemberId], filter.DimensionId, plan.CompanyId);
        foreach (var row in request.Rows)
            ValidateMember(members[row.RowMemberId], request.RowDimensionId, plan.CompanyId);

        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == plan.FiscalYearId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var requestedPeriodIds = request.Rows.SelectMany(x => x.Cells).Select(x => x.PeriodId).Distinct().ToArray();
        if (requestedPeriodIds.Any(id => !periods.ContainsKey(id))) throw new ArgumentException("One or more periods do not belong to the budget fiscal year.");

        var existing = await db.BudgetFacts
            .Where(x => x.VersionId == target.Id && x.MeasureId == request.MeasureId && x.ValueKind == request.ValueKind)
            .ToDictionaryAsync(x => (x.PeriodId, x.CoordinateHash), cancellationToken);

        var warnings = new List<string>();
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var touched = new HashSet<(Guid PeriodId, string Hash)>();

        foreach (var row in request.Rows)
        {
            if (row.Cells.Select(x => x.PeriodId).Distinct().Count() != row.Cells.Count)
                throw new ArgumentException("A row contains the same fiscal period more than once.");

            var coordinate = new[] { new DimensionSelection(request.RowDimensionId, row.RowMemberId) }
                .Concat(request.Filters)
                .OrderBy(x => x.DimensionId)
                .ToArray();
            var hash = BudgetCoordinateKey.Create(coordinate);
            var coordinatesJson = JsonSerializer.Serialize(coordinate);

            foreach (var cell in row.Cells)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var period = periods[cell.PeriodId];
                if (period.IsClosed)
                {
                    skipped++;
                    AddWarning(warnings, $"Period '{period.Name}' is closed and was skipped.");
                    continue;
                }

                var key = (cell.PeriodId, hash);
                if (existing.TryGetValue(key, out var fact))
                {
                    fact.Value = cell.Value;
                    fact.CurrencyCode = request.CurrencyCode;
                    fact.Source = "BulkPaste";
                    fact.Note = request.Note;
                    fact.UpdatedAtUtc = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    fact = new BudgetFact
                    {
                        VersionId = target.Id,
                        PeriodId = cell.PeriodId,
                        MeasureId = request.MeasureId,
                        ValueKind = request.ValueKind,
                        Value = cell.Value,
                        CurrencyCode = request.CurrencyCode,
                        CoordinateHash = hash,
                        CoordinatesJson = coordinatesJson,
                        Source = "BulkPaste",
                        Note = request.Note
                    };
                    foreach (var dimension in coordinate)
                        fact.Dimensions.Add(new BudgetFactDimension
                        {
                            BudgetFactId = fact.Id,
                            DimensionId = dimension.DimensionId,
                            MemberId = dimension.MemberId
                        });
                    db.BudgetFacts.Add(fact);
                    existing[key] = fact;
                    created++;
                }
                touched.Add((cell.PeriodId, hash));
            }
        }

        AddAudit("BudgetVersion", target.Id, "BULK_PASTE", new
        {
            request.MeasureId,
            request.ValueKind,
            request.RowDimensionId,
            Rows = request.Rows.Count,
            Cells = request.Rows.Sum(x => x.Cells.Count),
            created,
            updated,
            skipped
        });
        await db.SaveChangesAsync(cancellationToken);
        var calculationResult = await calculation.RecalculateVersionAsync(target.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var error in calculationResult.Errors) AddWarning(warnings, error);
        return new BudgetBulkOperationResultDto(created, updated, skipped, calculationResult.CoordinatesProcessed, warnings);
    }

    public async Task<BudgetVersionComparisonDto> CompareVersionsAsync(
        BudgetVersionComparisonQuery query,
        CancellationToken cancellationToken = default)
    {
        var versions = await db.BudgetVersions.AsNoTracking().Include(x => x.BudgetPlan)
            .Where(x => x.Id == query.LeftVersionId || x.Id == query.RightVersionId)
            .ToListAsync(cancellationToken);
        if (versions.Count != 2) throw new KeyNotFoundException("One or both budget versions were not found.");
        var left = versions.Single(x => x.Id == query.LeftVersionId);
        var right = versions.Single(x => x.Id == query.RightVersionId);
        if (left.BudgetPlanId != right.BudgetPlanId) throw new ArgumentException("Only versions of the same budget plan can be compared.");
        var plan = left.BudgetPlan!;
        EnsureCompanyRead(plan.CompanyId);

        var measure = await db.Measures.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == query.MeasureId && x.BudgetModelId == plan.BudgetModelId, cancellationToken)
            ?? throw new ArgumentException("Measure does not belong to the budget model.");
        var rowDimension = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == plan.BudgetModelId && x.DimensionId == query.RowDimensionId)
            .Select(x => new DimensionDto(x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence, x.IsRequired))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Row dimension does not belong to the budget model.");

        if (query.Filters.Any(x => x.DimensionId == query.RowDimensionId)) throw new ArgumentException("Row dimension cannot also be used as a fixed filter.");
        if (query.Filters.Select(x => x.DimensionId).Distinct().Count() != query.Filters.Count) throw new ArgumentException("A fixed filter dimension can only be supplied once.");

        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == plan.FiscalYearId).OrderBy(x => x.Sequence)
            .Select(x => new FiscalPeriodDto(x.Id, x.Sequence, x.Code, x.Name, x.JalaliMonth, x.StartDate, x.EndDate, x.IsClosed))
            .ToListAsync(cancellationToken);
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => x.DimensionId == query.RowDimensionId && x.IsActive && (x.CompanyId == null || x.CompanyId == plan.CompanyId))
            .OrderBy(x => x.Name)
            .Select(x => new DimensionMemberDto(x.Id, x.DimensionId, x.ParentId, x.CompanyId, x.Code, x.Name))
            .ToListAsync(cancellationToken);

        var factQuery = db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
            .Where(x => (x.VersionId == left.Id || x.VersionId == right.Id) && x.MeasureId == query.MeasureId && x.ValueKind == query.ValueKind);
        foreach (var filter in query.Filters)
            factQuery = factQuery.Where(x => x.Dimensions.Any(d => d.DimensionId == filter.DimensionId && d.MemberId == filter.MemberId));
        var facts = await factQuery.ToListAsync(cancellationToken);

        var leftValues = BuildAggregatedValues(facts.Where(x => x.VersionId == left.Id), query.RowDimensionId, measure.Aggregation);
        var rightValues = BuildAggregatedValues(facts.Where(x => x.VersionId == right.Id), query.RowDimensionId, measure.Aggregation);
        var rows = members.Select(member => new BudgetVersionComparisonRowDto(
            member.Id,
            member.Code,
            member.Name,
            periods.Select(period =>
            {
                var leftValue = leftValues.GetValueOrDefault((member.Id, period.Id));
                var rightValue = rightValues.GetValueOrDefault((member.Id, period.Id));
                var variance = rightValue - leftValue;
                var variancePercent = leftValue == 0m ? (decimal?)null : Math.Round(variance / Math.Abs(leftValue) * 100m, 2);
                return new BudgetVersionComparisonCellDto(period.Id, leftValue, rightValue, variance, variancePercent);
            }).ToList())).ToList();

        return new BudgetVersionComparisonDto(
            left.Id,
            right.Id,
            periods,
            new MeasureDto(measure.Id, measure.Code, measure.Name, measure.Unit, measure.ValueType, measure.Aggregation, measure.IsCalculated, measure.FormulaExpression, measure.DisplayOrder),
            rowDimension,
            rows);
    }

    private async Task<BudgetVersion> GetEditableVersionAsync(Guid versionId, CancellationToken ct)
    {
        var version = await db.BudgetVersions
            .Include(x => x.BudgetPlan).ThenInclude(x => x!.FiscalYear)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct)
            ?? throw new KeyNotFoundException("Budget version was not found.");
        var plan = version.BudgetPlan ?? throw new InvalidOperationException("Budget version has no plan.");
        EnsureCompanyWrite(plan.CompanyId);
        if (version.IsLocked || version.Status != BudgetStatus.Draft)
            throw new InvalidOperationException("Only an unlocked draft budget version can be changed.");
        if (plan.FiscalYear?.IsClosed == true)
            throw new InvalidOperationException("Fiscal year is closed and cannot be changed.");
        return version;
    }

    private void EnsureCompanyRead(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private void EnsureCompanyWrite(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private static void ValidateMember(DimensionMember member, Guid expectedDimensionId, Guid companyId)
    {
        if (member.DimensionId != expectedDimensionId || !member.IsActive || (member.CompanyId.HasValue && member.CompanyId != companyId))
            throw new ArgumentException("A dimension member is invalid for the selected company/model coordinate.");
    }

    private static Dictionary<(Guid MemberId, Guid PeriodId), decimal> BuildAggregatedValues(
        IEnumerable<BudgetFact> facts,
        Guid rowDimensionId,
        MeasureAggregation aggregation)
    {
        return facts.Select(x => new
            {
                Fact = x,
                RowMemberId = x.Dimensions.Where(d => d.DimensionId == rowDimensionId).Select(d => (Guid?)d.MemberId).SingleOrDefault()
            })
            .Where(x => x.RowMemberId.HasValue)
            .GroupBy(x => (x.RowMemberId!.Value, x.Fact.PeriodId))
            .ToDictionary(x => x.Key, x => Aggregate(x.Select(y => y.Fact), aggregation));
    }

    private static decimal Aggregate(IEnumerable<BudgetFact> facts, MeasureAggregation aggregation)
    {
        var list = facts.ToList();
        if (list.Count == 0) return 0m;
        return aggregation switch
        {
            MeasureAggregation.Average => list.Average(x => x.Value),
            MeasureAggregation.Min => list.Min(x => x.Value),
            MeasureAggregation.Max => list.Max(x => x.Value),
            MeasureAggregation.LastNonEmpty => list.OrderByDescending(x => x.UpdatedAtUtc).First().Value,
            MeasureAggregation.None => list.OrderByDescending(x => x.UpdatedAtUtc).First().Value,
            _ => list.Sum(x => x.Value)
        };
    }

    private void AddAudit(string entityType, Guid entityId, string action, object newValue) => db.AuditLogs.Add(new AuditLog
    {
        TenantId = user.TenantId,
        UserId = user.UserId == Guid.Empty ? null : user.UserId,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        Action = action,
        NewValueJson = JsonSerializer.Serialize(newValue)
    });

    private static void AddWarning(ICollection<string> warnings, string warning)
    {
        if (warnings.Count < 200 && !warnings.Contains(warning)) warnings.Add(warning);
    }
}
