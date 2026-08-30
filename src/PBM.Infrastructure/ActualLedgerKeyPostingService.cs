using Microsoft.EntityFrameworkCore;
using PBM.Application;

namespace PBM.Infrastructure;

public sealed class ActualLedgerKeyPostingService(
    PbmDbContext db,
    IUserContext user,
    IActualLedgerService ledger) : IActualLedgerKeyPostingService
{
    public async Task<ActualLedgerPostResult> PostAsync(
        PostActualLedgerByKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        var versionContext = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.Id == request.VersionId)
            .Select(x => new
            {
                x.Id,
                CompanyId = x.BudgetPlan!.CompanyId,
                FiscalYearId = x.BudgetPlan.FiscalYearId,
                BudgetModelId = x.BudgetPlan.BudgetModelId,
                TenantId = x.BudgetPlan.Company!.TenantId
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Budget version was not found.");
        if (versionContext.TenantId != user.TenantId)
            throw new UnauthorizedAccessException("Budget version is outside the current tenant.");

        var periodCode = NormalizeRequired(request.PeriodCode, 80, "Period code");
        var measureCode = NormalizeRequired(request.MeasureCode, 80, "Measure code");
        var periodId = await db.FiscalPeriods.AsNoTracking()
            .Where(x => x.FiscalYearId == versionContext.FiscalYearId && x.Code == periodCode)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException($"Fiscal period code '{periodCode}' was not found in the selected budget version fiscal year.");
        var measureId = await db.Measures.AsNoTracking()
            .Where(x => x.BudgetModelId == versionContext.BudgetModelId && x.Code == measureCode)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException($"Measure code '{measureCode}' was not found in the selected budget model.");

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == versionContext.BudgetModelId)
            .Select(x => new
            {
                x.DimensionId,
                x.IsRequired,
                Code = x.Dimension!.Code,
                Name = x.Dimension.Name
            })
            .ToListAsync(cancellationToken);
        var byCode = modelDimensions.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var selections = new List<DimensionSelection>(request.Dimensions.Count);
        var seenDimensionIds = new HashSet<Guid>();

        foreach (var pair in request.Dimensions)
        {
            var dimensionCode = NormalizeRequired(pair.Key, 80, "Dimension code");
            var memberKey = NormalizeRequired(pair.Value, 200, $"Member key for dimension {dimensionCode}");
            if (!byCode.TryGetValue(dimensionCode, out var dimension))
                throw new ArgumentException($"Dimension '{dimensionCode}' does not belong to the selected budget model.");
            if (!seenDimensionIds.Add(dimension.DimensionId))
                throw new ArgumentException($"Dimension '{dimensionCode}' was supplied more than once.");

            var candidates = await db.DimensionMembers.AsNoTracking()
                .Where(x => x.DimensionId == dimension.DimensionId
                    && x.IsActive
                    && (x.CompanyId == null || x.CompanyId == versionContext.CompanyId)
                    && (x.ExternalKey == memberKey || x.Code == memberKey))
                .Select(x => new { x.Id, x.Code, x.ExternalKey, x.CompanyId })
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
                throw new ArgumentException($"Member '{memberKey}' was not found for dimension '{dimensionCode}'. Configure DimensionMember.ExternalKey or use the PBM member code.");

            var exactExternal = candidates.Where(x => string.Equals(x.ExternalKey, memberKey, StringComparison.OrdinalIgnoreCase)).ToList();
            var preferred = exactExternal.Count == 1
                ? exactExternal[0]
                : candidates.Count == 1
                    ? candidates[0]
                    : candidates.Where(x => x.CompanyId == versionContext.CompanyId).SingleOrDefault();
            if (preferred is null)
                throw new InvalidOperationException($"Member key '{memberKey}' is ambiguous for dimension '{dimensionCode}'. Use a company-specific unique ExternalKey.");

            selections.Add(new DimensionSelection(dimension.DimensionId, preferred.Id));
        }

        var missingRequired = modelDimensions
            .Where(x => x.IsRequired && !seenDimensionIds.Contains(x.DimensionId))
            .Select(x => x.Code)
            .ToArray();
        if (missingRequired.Length > 0)
            throw new ArgumentException($"Required dimension(s) are missing: {string.Join(", ", missingRequired)}.");

        return await ledger.PostAsync(new PostActualLedgerRequest(
            request.VersionId,
            periodId,
            measureId,
            request.PostingDate,
            request.Amount,
            request.CurrencyCode,
            selections,
            request.SourceSystem,
            request.ExternalDocumentId,
            request.ExternalLineId,
            request.Note), cancellationToken);
    }

    private static string NormalizeRequired(string? value, int maxLength, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException($"{field} is required.");
        if (normalized.Length > maxLength) throw new ArgumentException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }
}
