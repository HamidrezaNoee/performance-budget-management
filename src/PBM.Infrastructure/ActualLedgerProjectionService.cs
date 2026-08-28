using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed record ActualProjectionResult(Guid FactId, decimal Value);

public sealed class ActualLedgerProjectionService(
    PbmDbContext db,
    IUserContext user,
    ICalculationService calculation)
{
    public const string ProjectionSource = "ActualLedger";

    public async Task EnsureCoordinateOwnershipAndCurrencyAsync(
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

        if (existingFact is not null
            && !hasLedger
            && !string.Equals(existingFact.Source, ProjectionSource, StringComparison.OrdinalIgnoreCase))
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

    public async Task<ActualProjectionResult> ProjectCoordinateAsync(
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
        return new ActualProjectionResult(fact.Id, fact.Value);
    }

    public async Task<ActualProjectionResult> EnsureProjectionAsync(
        ActualLedgerEntry entry,
        CancellationToken cancellationToken)
    {
        var dimensions = entry.Dimensions
            .OrderBy(x => x.DimensionId)
            .Select(x => new DimensionSelection(x.DimensionId, x.MemberId))
            .ToArray();
        return await ProjectCoordinateAsync(
            entry.VersionId,
            entry.PeriodId,
            entry.MeasureId,
            entry.CoordinateHash,
            dimensions,
            entry.CurrencyCode,
            cancellationToken);
    }

    public async Task<int> RebuildVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var entries = await db.Set<ActualLedgerEntry>()
            .AsNoTracking()
            .Include(x => x.Dimensions)
            .Where(x => x.VersionId == versionId && x.TenantId == user.TenantId)
            .ToListAsync(cancellationToken);
        var groups = entries.GroupBy(x => new { x.PeriodId, x.MeasureId, x.CoordinateHash }).ToList();
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

        return rebuilt;
    }

    public async Task<ActualProjectionResult?> GetProjectionAsync(
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
        return fact is null ? null : new ActualProjectionResult(fact.Id, fact.Value);
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
}
