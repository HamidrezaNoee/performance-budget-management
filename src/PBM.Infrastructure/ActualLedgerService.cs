using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ActualLedgerService(
    PbmDbContext db,
    IUserContext user,
    ActualLedgerValidationService validation,
    ActualLedgerProjectionService projection,
    SqlApplicationLock applicationLock) : IActualLedgerService
{
    public async Task<ActualLedgerPostResult> PostAsync(
        PostActualLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        var posting = await validation.ValidatePostingAsync(request, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await applicationLock.AcquireAsync(
            $"actual-ext:{ActualLedgerValidationService.HashLockKey(
                $"{user.TenantId:N}|{posting.Context.CompanyId:N}|{posting.SourceSystem}|{posting.ExternalDocumentId}|{posting.ExternalLineId}")}",
            cancellationToken);

        var existing = await db.Set<ActualLedgerEntry>()
            .Include(x => x.Reversals)
            .Include(x => x.Dimensions)
            .SingleOrDefaultAsync(x =>
                x.TenantId == user.TenantId
                && x.CompanyId == posting.Context.CompanyId
                && x.SourceSystem == posting.SourceSystem
                && x.ExternalDocumentId == posting.ExternalDocumentId
                && x.ExternalLineId == posting.ExternalLineId
                && x.EntryType == ActualLedgerEntryType.Posting,
                cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, posting.PayloadHash, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The ERP document/line key already exists with a different payload. Reverse the original entry and post the corrected source line with a new external line/revision identifier.");

            // Exact retries also self-heal a missing or stale ledger projection.
            var healedProjection = await projection.EnsureProjectionAsync(existing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ActualLedgerPostResult(
                ToDto(existing, existing.Reversals.Count != 0),
                true,
                healedProjection.FactId,
                healedProjection.Value);
        }

        await projection.EnsureCoordinateOwnershipAndCurrencyAsync(
            request.VersionId,
            request.PeriodId,
            request.MeasureId,
            posting.CoordinateHash,
            posting.CurrencyCode,
            cancellationToken);

        var entry = new ActualLedgerEntry
        {
            TenantId = user.TenantId,
            CompanyId = posting.Context.CompanyId,
            VersionId = request.VersionId,
            PeriodId = request.PeriodId,
            MeasureId = request.MeasureId,
            CreatedByUserId = user.UserId,
            EntryType = ActualLedgerEntryType.Posting,
            SourceSystem = posting.SourceSystem,
            ExternalDocumentId = posting.ExternalDocumentId,
            ExternalLineId = posting.ExternalLineId,
            PayloadHash = posting.PayloadHash,
            PostingDate = posting.PostingDate,
            Amount = request.Amount,
            CurrencyCode = posting.CurrencyCode,
            CoordinateHash = posting.CoordinateHash,
            CoordinatesJson = posting.CoordinatesJson,
            Note = posting.Note
        };
        foreach (var selection in posting.Dimensions)
        {
            entry.Dimensions.Add(new ActualLedgerDimension
            {
                EntryId = entry.Id,
                DimensionId = selection.DimensionId,
                MemberId = selection.MemberId
            });
        }

        db.Set<ActualLedgerEntry>().Add(entry);
        AddAudit(entry.Id, "ACTUAL_LEDGER_POST", null, new
        {
            entry.SourceSystem,
            entry.ExternalDocumentId,
            entry.ExternalLineId,
            entry.PostingDate,
            entry.Amount,
            entry.CurrencyCode,
            entry.CoordinateHash
        });
        await db.SaveChangesAsync(cancellationToken);

        var projected = await projection.ProjectCoordinateAsync(
            entry.VersionId,
            entry.PeriodId,
            entry.MeasureId,
            entry.CoordinateHash,
            posting.Dimensions,
            entry.CurrencyCode,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ActualLedgerPostResult(
            ToDto(entry, false),
            false,
            projected.FactId,
            projected.Value);
    }

    public async Task<ActualLedgerPostResult> ReverseAsync(
        Guid entryId,
        ReverseActualLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        validation.EnsureAuthenticatedWriter();
        var reason = ActualLedgerValidationService.NormalizeRequired(
            request.Reason,
            1000,
            "Reversal reason");

        var original = await db.Set<ActualLedgerEntry>()
            .Include(x => x.Version).ThenInclude(x => x!.BudgetPlan)
            .Include(x => x.Period).ThenInclude(x => x!.FiscalYear)
            .Include(x => x.Dimensions)
            .Include(x => x.Reversals)
            .SingleOrDefaultAsync(x => x.Id == entryId && x.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Actual ledger entry was not found.");
        if (original.EntryType != ActualLedgerEntryType.Posting)
            throw new InvalidOperationException("Only a posting entry can be reversed.");
        if (original.Version?.BudgetPlan is null)
            throw new InvalidOperationException("Actual ledger entry is missing its budget plan.");

        validation.EnsureCompanyWrite(original.CompanyId);
        var writeDecision = BudgetFactWritePolicy.Evaluate(
            original.Version.Status,
            original.Version.IsLocked,
            ValueKind.Actual);
        if (!writeDecision.IsAllowed) throw new InvalidOperationException(writeDecision.DenialReason);
        if (original.Period?.FiscalYear is null || original.Period.IsClosed || original.Period.FiscalYear.IsClosed)
            throw new InvalidOperationException(
                "A ledger entry in a closed fiscal period cannot be reversed in-place. Post an approved adjustment in an open period instead.");

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await applicationLock.AcquireAsync($"actual-reversal:{entryId:N}", cancellationToken);

        var existingReversal = await db.Set<ActualLedgerEntry>()
            .SingleOrDefaultAsync(x =>
                x.OriginalEntryId == entryId
                && x.EntryType == ActualLedgerEntryType.Reversal,
                cancellationToken);
        var selections = original.Dimensions
            .OrderBy(x => x.DimensionId)
            .Select(x => new DimensionSelection(x.DimensionId, x.MemberId))
            .ToArray();

        if (existingReversal is not null)
        {
            var healedProjection = await projection.ProjectCoordinateAsync(
                original.VersionId,
                original.PeriodId,
                original.MeasureId,
                original.CoordinateHash,
                selections,
                original.CurrencyCode,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ActualLedgerPostResult(
                ToDto(existingReversal, false),
                true,
                healedProjection.FactId,
                healedProjection.Value);
        }

        var reversal = new ActualLedgerEntry
        {
            TenantId = original.TenantId,
            CompanyId = original.CompanyId,
            VersionId = original.VersionId,
            PeriodId = original.PeriodId,
            MeasureId = original.MeasureId,
            CreatedByUserId = user.UserId,
            OriginalEntryId = original.Id,
            EntryType = ActualLedgerEntryType.Reversal,
            SourceSystem = original.SourceSystem,
            ExternalDocumentId = original.ExternalDocumentId,
            ExternalLineId = original.ExternalLineId,
            PayloadHash = ActualLedgerValidationService.ComputeReversalHash(original.Id, reason),
            PostingDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Unspecified),
            Amount = -original.Amount,
            CurrencyCode = original.CurrencyCode,
            CoordinateHash = original.CoordinateHash,
            CoordinatesJson = original.CoordinatesJson,
            Note = $"Reversal of ledger entry {original.Id:N}",
            ReversalReason = reason
        };
        foreach (var selection in selections)
        {
            reversal.Dimensions.Add(new ActualLedgerDimension
            {
                EntryId = reversal.Id,
                DimensionId = selection.DimensionId,
                MemberId = selection.MemberId
            });
        }

        db.Set<ActualLedgerEntry>().Add(reversal);
        AddAudit(reversal.Id, "ACTUAL_LEDGER_REVERSE", new { OriginalEntryId = original.Id }, new
        {
            reversal.Amount,
            reversal.CurrencyCode,
            reversal.ReversalReason
        });
        await db.SaveChangesAsync(cancellationToken);

        var projected = await projection.ProjectCoordinateAsync(
            original.VersionId,
            original.PeriodId,
            original.MeasureId,
            original.CoordinateHash,
            selections,
            original.CurrencyCode,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ActualLedgerPostResult(
            ToDto(reversal, false),
            false,
            projected.FactId,
            projected.Value);
    }

    public async Task<IReadOnlyList<ActualLedgerEntryDto>> GetEntriesAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid? versionId = null,
        string? sourceSystem = null,
        int take = 500,
        CancellationToken cancellationToken = default)
    {
        await validation.EnsureCompanyReadAsync(companyId, cancellationToken);
        if (!await db.FiscalYears.AsNoTracking().AnyAsync(x =>
                x.Id == fiscalYearId && x.CompanyId == companyId,
                cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        take = Math.Clamp(take, 1, 5000);
        var normalizedSource = string.IsNullOrWhiteSpace(sourceSystem)
            ? null
            : sourceSystem.Trim().ToUpperInvariant();
        var query = db.Set<ActualLedgerEntry>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId
                && x.CompanyId == companyId
                && x.Period!.FiscalYearId == fiscalYearId);
        if (versionId.HasValue) query = query.Where(x => x.VersionId == versionId.Value);
        if (normalizedSource is not null) query = query.Where(x => x.SourceSystem == normalizedSource);

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .Select(x => new ActualLedgerEntryDto(
                x.Id,
                x.EntryType,
                x.OriginalEntryId,
                x.CompanyId,
                x.VersionId,
                x.PeriodId,
                x.MeasureId,
                x.SourceSystem,
                x.ExternalDocumentId,
                x.ExternalLineId,
                x.PostingDate,
                x.Amount,
                x.CurrencyCode,
                x.CoordinateHash,
                x.Note,
                x.ReversalReason,
                x.EntryType == ActualLedgerEntryType.Posting && x.Reversals.Any(),
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActualLedgerReconciliationDto>> ReconcileAsync(
        Guid versionId,
        decimal tolerance = 0.01m,
        CancellationToken cancellationToken = default)
    {
        await validation.GetVersionContextAsync(versionId, false, cancellationToken);
        tolerance = Math.Abs(tolerance);

        var ledger = await db.Set<ActualLedgerEntry>().AsNoTracking()
            .Where(x => x.VersionId == versionId && x.TenantId == user.TenantId)
            .Select(x => new
            {
                x.PeriodId,
                x.MeasureId,
                x.CoordinateHash,
                x.CurrencyCode,
                x.Amount
            })
            .ToListAsync(cancellationToken);
        var projections = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == versionId
                && x.ValueKind == ValueKind.Actual
                && x.Source == ActualLedgerProjectionService.ProjectionSource)
            .Select(x => new
            {
                x.PeriodId,
                x.MeasureId,
                x.CoordinateHash,
                x.CurrencyCode,
                x.Value
            })
            .ToListAsync(cancellationToken);

        var ledgerGroups = ledger
            .GroupBy(x => new { x.PeriodId, x.MeasureId, x.CoordinateHash })
            .ToDictionary(g => g.Key, g => new
            {
                Amount = g.Sum(x => x.Amount),
                Currencies = g.Select(x => x.CurrencyCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
        var projectionGroups = projections.ToDictionary(
            x => new { x.PeriodId, x.MeasureId, x.CoordinateHash },
            x => x);
        var keys = ledgerGroups.Keys.Union(projectionGroups.Keys).ToList();
        var result = new List<ActualLedgerReconciliationDto>(keys.Count);

        foreach (var key in keys)
        {
            var hasLedger = ledgerGroups.TryGetValue(key, out var ledgerGroup);
            var hasProjection = projectionGroups.TryGetValue(key, out var projected);
            var ledgerAmount = ledgerGroup?.Amount ?? 0m;
            var ledgerCurrency = ledgerGroup?.Currencies.Length == 1
                ? ledgerGroup.Currencies[0]
                : string.Empty;
            var projectedAmount = hasProjection ? projected!.Value : (decimal?)null;
            var projectedCurrency = hasProjection ? projected!.CurrencyCode : null;

            var status = ResolveReconciliationStatus(
                hasLedger,
                hasProjection,
                ledgerGroup?.Currencies ?? [],
                ledgerCurrency,
                projectedCurrency,
                ledgerAmount,
                projectedAmount,
                tolerance);

            result.Add(new ActualLedgerReconciliationDto(
                versionId,
                key.PeriodId,
                key.MeasureId,
                key.CoordinateHash,
                ledgerCurrency,
                ledgerAmount,
                projectedAmount,
                projectedCurrency,
                status,
                (projectedAmount ?? 0m) - ledgerAmount));
        }

        return result
            .OrderByDescending(x => x.Status != ActualLedgerReconciliationStatus.Reconciled)
            .ThenBy(x => x.PeriodId)
            .ThenBy(x => x.MeasureId)
            .ToList();
    }

    public async Task<int> RebuildProjectionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var context = await validation.GetVersionContextAsync(versionId, true, cancellationToken);
        validation.EnsureProjectionAdminRole();
        var decision = BudgetFactWritePolicy.Evaluate(context.Status, context.IsLocked, ValueKind.Actual);
        if (!decision.IsAllowed) throw new InvalidOperationException(decision.DenialReason);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await applicationLock.AcquireAsync($"actual-rebuild:{versionId:N}", cancellationToken);
        var rebuilt = await projection.RebuildVersionAsync(versionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rebuilt;
    }

    private static ActualLedgerReconciliationStatus ResolveReconciliationStatus(
        bool hasLedger,
        bool hasProjection,
        IReadOnlyCollection<string> ledgerCurrencies,
        string ledgerCurrency,
        string? projectedCurrency,
        decimal ledgerAmount,
        decimal? projectedAmount,
        decimal tolerance)
    {
        if (!hasLedger) return ActualLedgerReconciliationStatus.ProjectionWithoutLedger;
        if (ledgerCurrencies.Count != 1
            || (hasProjection && !string.Equals(ledgerCurrency, projectedCurrency, StringComparison.OrdinalIgnoreCase)))
            return ActualLedgerReconciliationStatus.CurrencyMismatch;
        if (!hasProjection) return ActualLedgerReconciliationStatus.MissingProjection;
        if (Math.Abs(projectedAmount!.Value - ledgerAmount) > tolerance)
            return ActualLedgerReconciliationStatus.AmountMismatch;
        return ActualLedgerReconciliationStatus.Reconciled;
    }

    private void AddAudit(Guid entityId, string action, object? oldValue, object? newValue) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "ActualLedger",
            EntityId = entityId.ToString(),
            Action = action,
            OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
        });

    private static ActualLedgerEntryDto ToDto(ActualLedgerEntry entry, bool isReversed) => new(
        entry.Id,
        entry.EntryType,
        entry.OriginalEntryId,
        entry.CompanyId,
        entry.VersionId,
        entry.PeriodId,
        entry.MeasureId,
        entry.SourceSystem,
        entry.ExternalDocumentId,
        entry.ExternalLineId,
        entry.PostingDate,
        entry.Amount,
        entry.CurrencyCode,
        entry.CoordinateHash,
        entry.Note,
        entry.ReversalReason,
        isReversed,
        entry.CreatedAtUtc);
}
