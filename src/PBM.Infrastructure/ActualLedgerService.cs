using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ActualLedgerService(
    PbmDbContext db,
    IUserContext user,
    ICalculationService calculation) : IActualLedgerService
{
    private const string ProjectionSource = "ActualLedger";

    public async Task<ActualLedgerPostResult> PostAsync(
        PostActualLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (user.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("An authenticated user or service account is required to post Actual ledger entries.");

        var context = await ValidateWriteContextAsync(
            request.VersionId,
            request.PeriodId,
            request.MeasureId,
            request.Dimensions,
            cancellationToken);
        var sourceSystem = NormalizeRequired(request.SourceSystem, 80, "Source system").ToUpperInvariant();
        var externalDocumentId = NormalizeRequired(request.ExternalDocumentId, 160, "External document ID");
        var externalLineId = NormalizeRequired(request.ExternalLineId, 160, "External line ID");
        var currencyCode = NormalizeRequired(request.CurrencyCode, 12, "Currency code").ToUpperInvariant();
        await ValidateCurrencyAsync(currencyCode, cancellationToken);

        var normalizedDimensions = request.Dimensions
            .OrderBy(x => x.DimensionId)
            .ThenBy(x => x.MemberId)
            .ToArray();
        var coordinateHash = BudgetCoordinateKey.Create(normalizedDimensions);
        var coordinatesJson = JsonSerializer.Serialize(normalizedDimensions);
        var payloadHash = ComputePostingHash(
            context.CompanyId,
            request.VersionId,
            request.PeriodId,
            request.MeasureId,
            request.PostingDate,
            request.Amount,
            currencyCode,
            sourceSystem,
            externalDocumentId,
            externalLineId,
            normalizedDimensions,
            request.Note);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AcquireApplicationLockAsync(
            $"actual-ext:{HashLockKey($"{user.TenantId:N}|{context.CompanyId:N}|{sourceSystem}|{externalDocumentId}|{externalLineId}")}",
            cancellationToken);

        var entries = db.Set<ActualLedgerEntry>();
        var existing = await entries
            .Include(x => x.Reversals)
            .SingleOrDefaultAsync(x =>
                x.TenantId == user.TenantId
                && x.CompanyId == context.CompanyId
                && x.SourceSystem == sourceSystem
                && x.ExternalDocumentId == externalDocumentId
                && x.ExternalLineId == externalLineId
                && x.EntryType == ActualLedgerEntryType.Posting,
                cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The ERP document/line key already exists with a different payload. Reverse the original entry and post the corrected source line with a new external line/revision identifier.");

            var projection = await GetProjectionAsync(existing.VersionId, existing.PeriodId, existing.MeasureId, existing.CoordinateHash, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ActualLedgerPostResult(
                ToDto(existing, existing.Reversals.Count != 0),
                true,
                projection.FactId,
                projection.Value);
        }

        await EnsureCoordinateOwnershipAndCurrencyAsync(
            request.VersionId,
            request.PeriodId,
            request.MeasureId,
            coordinateHash,
            currencyCode,
            cancellationToken);

        var entry = new ActualLedgerEntry
        {
            TenantId = user.TenantId,
            CompanyId = context.CompanyId,
            VersionId = request.VersionId,
            PeriodId = request.PeriodId,
            MeasureId = request.MeasureId,
            CreatedByUserId = user.UserId,
            EntryType = ActualLedgerEntryType.Posting,
            SourceSystem = sourceSystem,
            ExternalDocumentId = externalDocumentId,
            ExternalLineId = externalLineId,
            PayloadHash = payloadHash,
            PostingDate = request.PostingDate,
            Amount = request.Amount,
            CurrencyCode = currencyCode,
            CoordinateHash = coordinateHash,
            CoordinatesJson = coordinatesJson,
            Note = NormalizeOptional(request.Note, 1000)
        };
        foreach (var selection in normalizedDimensions)
        {
            entry.Dimensions.Add(new ActualLedgerDimension
            {
                EntryId = entry.Id,
                DimensionId = selection.DimensionId,
                MemberId = selection.MemberId
            });
        }
        entries.Add(entry);
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

        var projected = await ProjectCoordinateAsync(
            request.VersionId,
            request.PeriodId,
            request.MeasureId,
            coordinateHash,
            normalizedDimensions,
            currencyCode,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ActualLedgerPostResult(ToDto(entry, false), false, projected.FactId, projected.Value);
    }

    public async Task<ActualLedgerPostResult> ReverseAsync(
        Guid entryId,
        ReverseActualLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (user.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("An authenticated user or service account is required to reverse Actual ledger entries.");
        var reason = NormalizeRequired(request.Reason, 1000, "Reversal reason");

        var entries = db.Set<ActualLedgerEntry>();
        var original = await entries
            .Include(x => x.Version).ThenInclude(x => x!.BudgetPlan)
            .Include(x => x.Period).ThenInclude(x => x!.FiscalYear)
            .Include(x => x.Dimensions)
            .Include(x => x.Reversals)
            .SingleOrDefaultAsync(x => x.Id == entryId && x.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Actual ledger entry was not found.");
        if (original.EntryType != ActualLedgerEntryType.Posting)
            throw new InvalidOperationException("Only a posting entry can be reversed.");

        var plan = original.Version?.BudgetPlan ?? throw new InvalidOperationException("Actual ledger entry is missing its budget plan.");
        EnsureCompanyWrite(original.CompanyId);
        var writeDecision = BudgetFactWritePolicy.Evaluate(original.Version!.Status, original.Version.IsLocked, ValueKind.Actual);
        if (!writeDecision.IsAllowed) throw new InvalidOperationException(writeDecision.DenialReason);
        if (original.Period is null || original.Period.FiscalYear is null || original.Period.IsClosed || original.Period.FiscalYear.IsClosed)
            throw new InvalidOperationException("A ledger entry in a closed fiscal period cannot be reversed in-place. Post an approved adjustment in an open period instead.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AcquireApplicationLockAsync($"actual-reversal:{entryId:N}", cancellationToken);

        var existingReversal = await entries
            .Include(x => x.Reversals)
            .SingleOrDefaultAsync(x => x.OriginalEntryId == entryId && x.EntryType == ActualLedgerEntryType.Reversal, cancellationToken);
        if (existingReversal is not null)
        {
            var projection = await GetProjectionAsync(
                original.VersionId,
                original.PeriodId,
                original.MeasureId,
                original.CoordinateHash,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ActualLedgerPostResult(ToDto(existingReversal, false), true, projection.FactId, projection.Value);
        }

        var selections = original.Dimensions
            .OrderBy(x => x.DimensionId)
            .Select(x => new DimensionSelection(x.DimensionId, x.MemberId))
            .ToArray();
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
            PayloadHash = ComputeReversalHash(original.Id, reason),
            PostingDate = DateTime.UtcNow,
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
        entries.Add(reversal);
        AddAudit(reversal.Id, "ACTUAL_LEDGER_REVERSE", new { OriginalEntryId = original.Id }, new
        {
            reversal.Amount,
            reversal.CurrencyCode,
            reversal.ReversalReason
        });
        await db.SaveChangesAsync(cancellationToken);

        var projected = await ProjectCoordinateAsync(
            original.VersionId,
            original.PeriodId,
            original.MeasureId,
            original.CoordinateHash,
            selections,
            original.CurrencyCode,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ActualLedgerPostResult(ToDto(reversal, false), false, projected.FactId, projected.Value);
    }

    public async Task<IReadOnlyList<ActualLedgerEntryDto>> GetEntriesAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid? versionId = null,
        string? sourceSystem = null,
        int take = 500,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyReadAsync(companyId, cancellationToken);
        if (!await db.FiscalYears.AsNoTracking().AnyAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken))
            throw new ArgumentException("Fiscal year does not belong to the selected company.");

        take = Math.Clamp(take, 1, 5000);
        var normalizedSource = string.IsNullOrWhiteSpace(sourceSystem) ? null : sourceSystem.Trim().ToUpperInvariant();
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
        var context = await GetVersionContextAsync(versionId, false, cancellationToken);
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
                && x.Source == ProjectionSource)
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
                Currencies = g.Select(x => x.CurrencyCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            });
        var projectionGroups = projections.ToDictionary(
            x => new { x.PeriodId, x.MeasureId, x.CoordinateHash },
            x => x);
        var keys = ledgerGroups.Keys.Union(projectionGroups.Keys).ToList();
        var result = new List<ActualLedgerReconciliationDto>(keys.Count);

        foreach (var key in keys)
        {
            var hasLedger = ledgerGroups.TryGetValue(key, out var ledgerGroup);
            var hasProjection = projectionGroups.TryGetValue(key, out var projection);
            var ledgerAmount = ledgerGroup?.Amount ?? 0m;
            var ledgerCurrency = ledgerGroup?.Currencies.Length == 1 ? ledgerGroup.Currencies[0] : string.Empty;
            var projectedAmount = hasProjection ? projection!.Value : (decimal?)null;
            var projectedCurrency = hasProjection ? projection!.CurrencyCode : null;
            ActualLedgerReconciliationStatus status;

            if (!hasLedger)
                status = ActualLedgerReconciliationStatus.ProjectionWithoutLedger;
            else if (ledgerGroup!.Currencies.Length != 1
                || (hasProjection && !string.Equals(ledgerCurrency, projectedCurrency, StringComparison.OrdinalIgnoreCase)))
                status = ActualLedgerReconciliationStatus.CurrencyMismatch;
            else if (!hasProjection)
                status = ActualLedgerReconciliationStatus.MissingProjection;
            else if (Math.Abs(projection!.Value - ledgerAmount) > tolerance)
                status = ActualLedgerReconciliationStatus.AmountMismatch;
            else
                status = ActualLedgerReconciliationStatus.Reconciled;

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

        _ = context;
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
        var context = await GetVersionContextAsync(versionId, true, cancellationToken);
        EnsureProjectionAdminRole();
        var writeDecision = BudgetFactWritePolicy.Evaluate(context.Status, context.IsLocked, ValueKind.Actual);
        if (!writeDecision.IsAllowed) throw new InvalidOperationException(writeDecision.DenialReason);

        var entries = await db.Set<ActualLedgerEntry>()
            .AsNoTracking()
            .Include(x => x.Dimensions)
            .Where(x => x.VersionId == versionId && x.TenantId == user.TenantId)
            .ToListAsync(cancellationToken);
        var groups = entries.GroupBy(x => new { x.PeriodId, x.MeasureId, x.CoordinateHash }).ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AcquireApplicationLockAsync($"actual-rebuild:{versionId:N}", cancellationToken);
        var rebuilt = 0;
        foreach (var group in groups)
        {
            var currencies = group.Select(x => x.CurrencyCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (currencies.Length != 1)
                throw new InvalidOperationException($"Ledger coordinate {group.Key.CoordinateHash} contains multiple currencies and cannot be projected safely.");
            var first = group.First();
            var dimensions = first.Dimensions
                .OrderBy(x => x.DimensionId)
                .Select(x => new DimensionSelection(x.DimensionId, x.MemberId))
                .ToArray();
            await ProjectCoordinateAsync(
                versionId,
                group.Key.PeriodId,
                group.Key.MeasureId,
                group.Key.CoordinateHash,
                dimensions,
                currencies[0],
                cancellationToken);
            rebuilt++;
        }
        await transaction.CommitAsync(cancellationToken);
        return rebuilt;
    }

    private async Task<LedgerWriteContext> ValidateWriteContextAsync(
        Guid versionId,
        Guid periodId,
        Guid measureId,
        IReadOnlyList<DimensionSelection> dimensions,
        CancellationToken cancellationToken)
    {
        var context = await GetVersionContextAsync(versionId, true, cancellationToken);
        var writeDecision = BudgetFactWritePolicy.Evaluate(context.Status, context.IsLocked, ValueKind.Actual);
        if (!writeDecision.IsAllowed) throw new InvalidOperationException(writeDecision.DenialReason);

        var period = await db.FiscalPeriods.AsNoTracking()
            .Include(x => x.FiscalYear)
            .SingleOrDefaultAsync(x => x.Id == periodId && x.FiscalYearId == context.FiscalYearId, cancellationToken)
            ?? throw new ArgumentException("Period does not belong to the budget plan fiscal year.");
        if (period.IsClosed || period.FiscalYear!.IsClosed)
            throw new InvalidOperationException("Closed fiscal periods cannot accept Actual ledger entries.");

        var measure = await db.Measures.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == measureId && x.BudgetModelId == context.BudgetModelId, cancellationToken)
            ?? throw new ArgumentException("Measure does not belong to the budget model.");
        if (measure.IsCalculated)
            throw new InvalidOperationException("Calculated measures cannot receive source-system Actual postings directly.");

        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == context.BudgetModelId)
            .ToListAsync(cancellationToken);
        var allowed = modelDimensions.Select(x => x.DimensionId).ToHashSet();
        var supplied = dimensions.Select(x => x.DimensionId).ToArray();
        if (supplied.Length != supplied.Distinct().Count())
            throw new ArgumentException("A dimension can only be supplied once.");
        if (modelDimensions.Where(x => x.IsRequired).Any(x => !supplied.Contains(x.DimensionId)))
            throw new ArgumentException("One or more required dimensions are missing.");
        if (supplied.Any(x => !allowed.Contains(x)))
            throw new ArgumentException("A supplied dimension does not belong to the budget model.");

        foreach (var selection in dimensions)
        {
            var valid = await db.DimensionMembers.AsNoTracking().AnyAsync(x =>
                x.Id == selection.MemberId
                && x.DimensionId == selection.DimensionId
                && x.IsActive
                && (x.CompanyId == null || x.CompanyId == context.CompanyId), cancellationToken);
            if (!valid) throw new ArgumentException("Invalid dimension member selection.");
        }

        return context;
    }

    private async Task EnsureCoordinateOwnershipAndCurrencyAsync(
        Guid versionId,
        Guid periodId,
        Guid measureId,
        string coordinateHash,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        var existingFact = await db.BudgetFacts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.VersionId == versionId
            && x.PeriodId == periodId
            && x.MeasureId == measureId
            && x.ValueKind == ValueKind.Actual
            && x.CoordinateHash == coordinateHash,
            cancellationToken);
        var hasLedger = await db.Set<ActualLedgerEntry>().AsNoTracking().AnyAsync(x =>
            x.VersionId == versionId
            && x.PeriodId == periodId
            && x.MeasureId == measureId
            && x.CoordinateHash == coordinateHash,
            cancellationToken);

        if (existingFact is not null && !hasLedger && !string.Equals(existingFact.Source, ProjectionSource, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "This Actual coordinate is already managed by a non-ledger source (manual/import). Reconcile or migrate that coordinate before ERP ledger posting to avoid a silent overwrite.");

        var currencies = await db.Set<ActualLedgerEntry>().AsNoTracking()
            .Where(x => x.VersionId == versionId
                && x.PeriodId == periodId
                && x.MeasureId == measureId
                && x.CoordinateHash == coordinateHash)
            .Select(x => x.CurrencyCode)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (currencies.Any(x => !string.Equals(x, currencyCode, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "This Actual coordinate already contains a different currency. Currency must be represented by a model dimension when multiple currencies can exist at the same business coordinate.");
    }

    private async Task<ProjectionResult> ProjectCoordinateAsync(
        Guid versionId,
        Guid periodId,
        Guid measureId,
        string coordinateHash,
        IReadOnlyList<DimensionSelection> dimensions,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        var ledgerEntries = await db.Set<ActualLedgerEntry>().AsNoTracking()
            .Where(x => x.VersionId == versionId
                && x.PeriodId == periodId
                && x.MeasureId == measureId
                && x.CoordinateHash == coordinateHash)
            .Select(x => new { x.Amount, x.CurrencyCode })
            .ToListAsync(cancellationToken);
        var currencies = ledgerEntries.Select(x => x.CurrencyCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (currencies.Length > 1)
            throw new InvalidOperationException("Ledger projection cannot combine multiple currencies in one BudgetFact coordinate.");
        if (currencies.Length == 1) currencyCode = currencies[0];
        var value = ledgerEntries.Sum(x => x.Amount);

        var fact = await db.BudgetFacts
            .Include(x => x.Dimensions)
            .SingleOrDefaultAsync(x =>
                x.VersionId == versionId
                && x.PeriodId == periodId
                && x.MeasureId == measureId
                && x.ValueKind == ValueKind.Actual
                && x.CoordinateHash == coordinateHash,
                cancellationToken);
        var old = fact is null ? null : new { fact.Value, fact.CurrencyCode, fact.Source };
        if (fact is null)
        {
            fact = new BudgetFact
            {
                VersionId = versionId,
                PeriodId = periodId,
                MeasureId = measureId,
                ValueKind = ValueKind.Actual,
                CoordinateHash = coordinateHash,
                CoordinatesJson = JsonSerializer.Serialize(dimensions.OrderBy(x => x.DimensionId))
            };
            db.BudgetFacts.Add(fact);
        }
        else
        {
            if (!string.Equals(fact.Source, ProjectionSource, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ledger projection refused to overwrite an Actual fact owned by another source.");
            db.BudgetFactDimensions.RemoveRange(fact.Dimensions);
            fact.Dimensions.Clear();
        }

        fact.Value = value;
        fact.CurrencyCode = currencyCode;
        fact.Source = ProjectionSource;
        fact.Note = "Aggregated projection from immutable Actual Ledger entries.";
        fact.UpdatedAtUtc = DateTime.UtcNow;
        foreach (var selection in dimensions.OrderBy(x => x.DimensionId))
        {
            fact.Dimensions.Add(new BudgetFactDimension
            {
                BudgetFactId = fact.Id,
                DimensionId = selection.DimensionId,
                MemberId = selection.MemberId
            });
        }
        AddAudit(fact.Id, "ACTUAL_LEDGER_PROJECT", old, new
        {
            fact.Value,
            fact.CurrencyCode,
            fact.CoordinateHash,
            LedgerEntryCount = ledgerEntries.Count
        });
        await db.SaveChangesAsync(cancellationToken);
        await calculation.RecalculateCoordinateAsync(
            versionId,
            periodId,
            ValueKind.Actual,
            dimensions,
            cancellationToken);
        return new ProjectionResult(fact.Id, fact.Value);
    }

    private async Task<ProjectionResult> GetProjectionAsync(
        Guid versionId,
        Guid periodId,
        Guid measureId,
        string coordinateHash,
        CancellationToken cancellationToken)
    {
        var fact = await db.BudgetFacts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.VersionId == versionId
            && x.PeriodId == periodId
            && x.MeasureId == measureId
            && x.ValueKind == ValueKind.Actual
            && x.CoordinateHash == coordinateHash,
            cancellationToken);
        return fact is null ? new ProjectionResult(Guid.Empty, 0m) : new ProjectionResult(fact.Id, fact.Value);
    }

    private async Task<LedgerWriteContext> GetVersionContextAsync(
        Guid versionId,
        bool write,
        CancellationToken cancellationToken)
    {
        var version = await db.BudgetVersions.AsNoTracking()
            .Include(x => x.BudgetPlan)
            .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget version was not found.");
        var plan = version.BudgetPlan ?? throw new InvalidOperationException("Budget version has no budget plan.");
        if (!await db.Companies.AsNoTracking().AnyAsync(x =>
                x.Id == plan.CompanyId && x.TenantId == user.TenantId && x.IsActive,
                cancellationToken))
            throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        if (write) EnsureCompanyWrite(plan.CompanyId);
        else if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(plan.CompanyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");

        return new LedgerWriteContext(
            plan.CompanyId,
            plan.FiscalYearId,
            plan.BudgetModelId,
            version.Status,
            version.IsLocked);
    }

    private async Task EnsureCompanyReadAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, cancellationToken))
            throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private void EnsureCompanyWrite(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private void EnsureProjectionAdminRole()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("CFO") || user.IsInRole("BUDGET_MANAGER")) return;
        throw new UnauthorizedAccessException("Administrator, CFO or budget manager role is required to rebuild Actual projections.");
    }

    private async Task ValidateCurrencyAsync(string currencyCode, CancellationToken cancellationToken)
    {
        if (!await db.Currencies.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Code == currencyCode && x.IsActive,
                cancellationToken))
            throw new ArgumentException("Currency is not defined or active for the current tenant.");
    }

    private async Task AcquireApplicationLockAsync(string resource, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0) throw new TimeoutException("Could not acquire the Actual ledger concurrency lock.");
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

    private static string ComputePostingHash(
        Guid companyId,
        Guid versionId,
        Guid periodId,
        Guid measureId,
        DateTime postingDate,
        decimal amount,
        string currencyCode,
        string sourceSystem,
        string documentId,
        string lineId,
        IReadOnlyList<DimensionSelection> dimensions,
        string? note)
    {
        var payload = JsonSerializer.Serialize(new
        {
            CompanyId = companyId,
            VersionId = versionId,
            PeriodId = periodId,
            MeasureId = measureId,
            PostingDate = postingDate.ToUniversalTime(),
            Amount = amount,
            CurrencyCode = currencyCode,
            SourceSystem = sourceSystem,
            ExternalDocumentId = documentId,
            ExternalLineId = lineId,
            Dimensions = dimensions.Select(x => new { x.DimensionId, x.MemberId }).ToArray(),
            Note = note?.Trim()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string ComputeReversalHash(Guid originalEntryId, string reason) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{originalEntryId:N}|{reason}"))).ToLowerInvariant();

    private static string HashLockKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeRequired(string? value, int maxLength, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException($"{field} is required.");
        if (normalized.Length > maxLength) throw new ArgumentException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > maxLength) throw new ArgumentException($"Value cannot exceed {maxLength} characters.");
        return normalized;
    }

    private sealed record LedgerWriteContext(
        Guid CompanyId,
        Guid FiscalYearId,
        Guid BudgetModelId,
        BudgetStatus Status,
        bool IsLocked);

    private sealed record ProjectionResult(Guid FactId, decimal Value);
}
