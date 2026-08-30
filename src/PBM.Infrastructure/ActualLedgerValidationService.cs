using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed record ActualLedgerWriteContext(
    Guid CompanyId,
    Guid FiscalYearId,
    Guid BudgetModelId,
    BudgetStatus Status,
    bool IsLocked);

public sealed record ValidatedActualPosting(
    ActualLedgerWriteContext Context,
    DateTime PostingDate,
    string SourceSystem,
    string ExternalDocumentId,
    string ExternalLineId,
    string CurrencyCode,
    IReadOnlyList<DimensionSelection> Dimensions,
    string CoordinateHash,
    string CoordinatesJson,
    string PayloadHash,
    string? Note);

public sealed class ActualLedgerValidationService(PbmDbContext db, IUserContext user)
{
    public async Task<ValidatedActualPosting> ValidatePostingAsync(
        PostActualLedgerRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedWriter();
        EnsureLedgerWriterRole();
        if (request.Amount == 0m)
            throw new ArgumentException("A source-system Actual ledger posting cannot have a zero amount.");

        var context = await GetVersionContextAsync(request.VersionId, true, cancellationToken);
        var writeDecision = BudgetFactWritePolicy.Evaluate(context.Status, context.IsLocked, ValueKind.Actual);
        if (!writeDecision.IsAllowed) throw new InvalidOperationException(writeDecision.DenialReason);

        var period = await db.FiscalPeriods.AsNoTracking()
            .Include(x => x.FiscalYear)
            .SingleOrDefaultAsync(x => x.Id == request.PeriodId && x.FiscalYearId == context.FiscalYearId, cancellationToken)
            ?? throw new ArgumentException("Period does not belong to the budget plan fiscal year.");
        if (period.IsClosed || period.FiscalYear is null || period.FiscalYear.IsClosed)
            throw new InvalidOperationException("Closed fiscal periods cannot accept Actual ledger entries.");

        var postingDate = CanonicalBusinessDate(request.PostingDate);
        if (postingDate < period.StartDate.Date || postingDate > period.EndDate.Date)
            throw new ArgumentException(
                $"Posting date {postingDate:yyyy-MM-dd} is outside fiscal period {period.Code} ({period.StartDate:yyyy-MM-dd}..{period.EndDate:yyyy-MM-dd}).");

        var measure = await db.Measures.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.MeasureId && x.BudgetModelId == context.BudgetModelId, cancellationToken)
            ?? throw new ArgumentException("Measure does not belong to the budget model.");
        if (measure.IsCalculated)
            throw new InvalidOperationException("Calculated measures cannot receive source-system Actual postings directly.");

        var normalizedDimensions = request.Dimensions
            .OrderBy(x => x.DimensionId)
            .ThenBy(x => x.MemberId)
            .ToArray();
        await ValidateDimensionsAsync(context, normalizedDimensions, cancellationToken);

        var sourceSystem = NormalizeRequired(request.SourceSystem, 80, "Source system").ToUpperInvariant();
        var externalDocumentId = NormalizeRequired(request.ExternalDocumentId, 160, "External document ID");
        var externalLineId = NormalizeRequired(request.ExternalLineId, 160, "External line ID");
        var currencyCode = NormalizeRequired(request.CurrencyCode, 12, "Currency code").ToUpperInvariant();
        if (!await db.Currencies.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Code == currencyCode && x.IsActive,
                cancellationToken))
            throw new ArgumentException("Currency is not defined or active for the current tenant.");

        var coordinateHash = BudgetCoordinateKey.Create(normalizedDimensions);
        var coordinatesJson = JsonSerializer.Serialize(normalizedDimensions);
        var note = NormalizeOptional(request.Note, 1000);
        var payloadHash = ComputePostingHash(
            context.CompanyId,
            request.VersionId,
            request.PeriodId,
            request.MeasureId,
            postingDate,
            request.Amount,
            currencyCode,
            sourceSystem,
            externalDocumentId,
            externalLineId,
            normalizedDimensions,
            note);

        return new ValidatedActualPosting(
            context,
            postingDate,
            sourceSystem,
            externalDocumentId,
            externalLineId,
            currencyCode,
            normalizedDimensions,
            coordinateHash,
            coordinatesJson,
            payloadHash,
            note);
    }

    public async Task<ActualLedgerWriteContext> GetVersionContextAsync(
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

        if (write)
            EnsureCompanyWrite(plan.CompanyId);
        else if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(plan.CompanyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");

        return new ActualLedgerWriteContext(
            plan.CompanyId,
            plan.FiscalYearId,
            plan.BudgetModelId,
            version.Status,
            version.IsLocked);
    }

    public async Task EnsureCompanyReadAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(x =>
                x.Id == companyId && x.TenantId == user.TenantId && x.IsActive,
                cancellationToken))
            throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    public void EnsureCompanyWrite(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    public void EnsureLedgerWriterRole()
    {
        if (user.IsInRole("SUPERADMIN")
            || user.IsInRole("ADMIN")
            || user.IsInRole("CFO")
            || user.IsInRole("BUDGET_MANAGER")
            || user.IsInRole("BUDGET_EXPERT")
            || user.IsInRole("INTEGRATION"))
            return;
        throw new UnauthorizedAccessException(
            "Actual Ledger posting requires finance, budget or INTEGRATION service-account permission.");
    }

    public void EnsureProjectionAdminRole()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("CFO") || user.IsInRole("BUDGET_MANAGER")) return;
        throw new UnauthorizedAccessException("Administrator, CFO or budget manager role is required to rebuild Actual projections.");
    }

    public void EnsureAuthenticatedWriter()
    {
        if (user.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("An authenticated user or service account is required to post Actual ledger entries.");
    }

    private async Task ValidateDimensionsAsync(
        ActualLedgerWriteContext context,
        IReadOnlyList<DimensionSelection> dimensions,
        CancellationToken cancellationToken)
    {
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
    }

    private static DateTime CanonicalBusinessDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

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
            PostingDate = postingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Amount = amount,
            CurrencyCode = currencyCode,
            SourceSystem = sourceSystem,
            ExternalDocumentId = documentId,
            ExternalLineId = lineId,
            Dimensions = dimensions.Select(x => new { x.DimensionId, x.MemberId }).ToArray(),
            Note = note
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string ComputeReversalHash(Guid originalEntryId, string reason) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{originalEntryId:N}|{reason}"))).ToLowerInvariant();

    public static string HashLockKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string NormalizeRequired(string? value, int maxLength, string field)
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
}
