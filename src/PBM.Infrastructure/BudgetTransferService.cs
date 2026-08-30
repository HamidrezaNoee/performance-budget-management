using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class BudgetTransferService(
    PbmDbContext db,
    IUserContext currentUser,
    ICalculationService calculation) : IBudgetTransferService
{
    public async Task<IReadOnlyList<BudgetTransferDto>> GetAsync(
        Guid companyId,
        Guid? fiscalYearId = null,
        BudgetTransferStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        EnsureCompanyRead(companyId);
        take = Math.Clamp(take, 1, 500);
        var query = db.BudgetTransfers.AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId && x.CompanyId == companyId);
        if (fiscalYearId.HasValue)
            query = query.Where(x => x.Version!.BudgetPlan!.FiscalYearId == fiscalYearId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var items = await IncludeDetails(query)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<BudgetTransferAvailabilityDto> GetAvailabilityAsync(
        CreateBudgetTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await ValidateRequestAsync(request, cancellationToken);
        EnsureCompanyRead(context.CompanyId);
        return await GetAvailabilityInternalAsync(context, cancellationToken);
    }

    public async Task<BudgetTransferDto> CreateAsync(
        CreateBudgetTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        if (request.Amount <= 0m) throw new ArgumentException("Transfer amount must be greater than zero.");
        var description = NormalizeRequired(request.Description, "Description", 1000);
        var externalReference = NormalizeOptional(request.ExternalReference, 200);
        var context = await ValidateRequestAsync(request, cancellationToken);
        EnsureCompanyWrite(context.CompanyId);

        var availability = await GetAvailabilityInternalAsync(context, cancellationToken);
        if (request.Amount > availability.SourceAvailable)
            throw new InvalidOperationException($"Insufficient transferable budget. Requested {request.Amount:0.########}; available {availability.SourceAvailable:0.########}.");

        var transfer = new BudgetTransfer
        {
            TenantId = currentUser.TenantId,
            CompanyId = context.CompanyId,
            VersionId = request.VersionId,
            MeasureId = request.MeasureId,
            SourcePeriodId = request.SourcePeriodId,
            DestinationPeriodId = request.DestinationPeriodId,
            RequestedByUserId = currentUser.UserId,
            TransferNo = CreateTransferNo(),
            Description = description,
            Amount = request.Amount,
            CurrencyCode = NormalizeOptional(request.CurrencyCode, 12)?.ToUpperInvariant(),
            SourceCoordinateHash = context.SourceHash,
            SourceCoordinatesJson = JsonSerializer.Serialize(context.SourceDimensions),
            DestinationCoordinateHash = context.DestinationHash,
            DestinationCoordinatesJson = JsonSerializer.Serialize(context.DestinationDimensions),
            ExternalReference = externalReference
        };
        foreach (var item in request.Dimensions.OrderBy(x => x.DimensionId))
            transfer.Dimensions.Add(new BudgetTransferDimension
            {
                TransferId = transfer.Id,
                DimensionId = item.DimensionId,
                SourceMemberId = item.SourceMemberId,
                DestinationMemberId = item.DestinationMemberId
            });

        db.BudgetTransfers.Add(transfer);
        AddAudit(transfer.Id, "REQUEST", new
        {
            transfer.TransferNo,
            transfer.CompanyId,
            transfer.VersionId,
            transfer.MeasureId,
            transfer.SourcePeriodId,
            transfer.DestinationPeriodId,
            transfer.Amount,
            transfer.SourceCoordinateHash,
            transfer.DestinationCoordinateHash,
            transfer.ExternalReference
        });
        await db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(transfer.Id, cancellationToken);
    }

    public async Task<BudgetTransferDto> ApproveAsync(
        Guid transferId,
        BudgetTransferDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureDecisionRole();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var transfer = await LoadForMutationAsync(transferId, cancellationToken);
        EnsureCompanyWrite(transfer.CompanyId);
        if (transfer.Status != BudgetTransferStatus.Requested)
            throw new InvalidOperationException("Only a requested budget transfer can be approved.");

        var createRequest = new CreateBudgetTransferRequest(
            transfer.CompanyId,
            transfer.VersionId,
            transfer.MeasureId,
            transfer.SourcePeriodId,
            transfer.DestinationPeriodId,
            transfer.Amount,
            transfer.CurrencyCode,
            transfer.Description,
            transfer.Dimensions.Select(x => new BudgetTransferDimensionInput(x.DimensionId, x.SourceMemberId, x.DestinationMemberId)).ToList(),
            transfer.ExternalReference);
        var context = await ValidateRequestAsync(createRequest, cancellationToken);
        var availability = await GetAvailabilityInternalAsync(context, cancellationToken);
        if (transfer.Amount > availability.SourceAvailable)
            throw new InvalidOperationException($"Source availability changed after the transfer request was created. Requested {transfer.Amount:0.########}; available {availability.SourceAvailable:0.########}.");

        await ApplyTransferAsync(transfer, context, cancellationToken);
        transfer.Status = BudgetTransferStatus.Approved;
        transfer.DecidedByUserId = currentUser.UserId;
        transfer.DecidedAtUtc = DateTime.UtcNow;
        transfer.DecisionComment = NormalizeOptional(request.Comment, 1200);
        transfer.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(transfer.Id, "APPROVE", new { transfer.Amount, transfer.DecisionComment });
        await db.SaveChangesAsync(cancellationToken);

        await calculation.RecalculateCoordinateAsync(transfer.VersionId, transfer.SourcePeriodId, ValueKind.Budget, context.SourceDimensions, cancellationToken);
        if (transfer.SourcePeriodId != transfer.DestinationPeriodId || context.SourceHash != context.DestinationHash)
            await calculation.RecalculateCoordinateAsync(transfer.VersionId, transfer.DestinationPeriodId, ValueKind.Budget, context.DestinationDimensions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(transfer.Id, cancellationToken);
    }

    public async Task<BudgetTransferDto> RejectAsync(
        Guid transferId,
        BudgetTransferDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureDecisionRole();
        var transfer = await LoadForMutationAsync(transferId, cancellationToken);
        EnsureCompanyWrite(transfer.CompanyId);
        if (transfer.Status != BudgetTransferStatus.Requested)
            throw new InvalidOperationException("Only a requested budget transfer can be rejected.");

        transfer.Status = BudgetTransferStatus.Rejected;
        transfer.DecidedByUserId = currentUser.UserId;
        transfer.DecidedAtUtc = DateTime.UtcNow;
        transfer.DecisionComment = NormalizeOptional(request.Comment, 1200);
        transfer.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(transfer.Id, "REJECT", new { transfer.DecisionComment });
        await db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(transfer.Id, cancellationToken);
    }

    private async Task<TransferContext> ValidateRequestAsync(
        CreateBudgetTransferRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Amount <= 0m) throw new ArgumentException("Transfer amount must be greater than zero.");
        var version = await db.BudgetVersions.AsNoTracking()
            .Include(x => x.BudgetPlan).ThenInclude(x => x!.FiscalYear)
            .SingleOrDefaultAsync(x => x.Id == request.VersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget version was not found.");
        var plan = version.BudgetPlan ?? throw new InvalidOperationException("Budget version has no plan.");
        if (plan.CompanyId != request.CompanyId) throw new ArgumentException("Transfer company does not match the selected budget version.");
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == plan.CompanyId && x.TenantId == currentUser.TenantId && x.IsActive, cancellationToken))
            throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        if (version.Status is not (BudgetStatus.Approved or BudgetStatus.Closed))
            throw new InvalidOperationException("Transfers can only be requested against an approved or final budget version.");
        if (plan.FiscalYear?.IsClosed == true)
            throw new InvalidOperationException("Fiscal year is closed and cannot accept budget transfers.");

        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(x => (x.Id == request.SourcePeriodId || x.Id == request.DestinationPeriodId) && x.FiscalYearId == plan.FiscalYearId)
            .ToListAsync(cancellationToken);
        if (periods.Select(x => x.Id).Distinct().Count() != (request.SourcePeriodId == request.DestinationPeriodId ? 1 : 2))
            throw new ArgumentException("Source or destination period does not belong to the selected fiscal year.");
        if (periods.Any(x => x.IsClosed)) throw new InvalidOperationException("Source and destination fiscal periods must both be open.");

        var measure = await db.Measures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.MeasureId && x.BudgetModelId == plan.BudgetModelId, cancellationToken)
            ?? throw new ArgumentException("Measure does not belong to the selected budget model.");
        if (measure.IsCalculated) throw new InvalidOperationException("Calculated measures cannot be transferred directly.");
        if (measure.ValueType != MeasureValueType.Amount) throw new InvalidOperationException("Budget transfers require an amount-type measure.");

        var inputs = (request.Dimensions ?? []).OrderBy(x => x.DimensionId).ToArray();
        if (inputs.Select(x => x.DimensionId).Distinct().Count() != inputs.Length)
            throw new ArgumentException("A transfer dimension can only be supplied once.");
        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking().Where(x => x.BudgetModelId == plan.BudgetModelId).ToListAsync(cancellationToken);
        var allowedDimensions = modelDimensions.Select(x => x.DimensionId).ToHashSet();
        if (inputs.Any(x => !allowedDimensions.Contains(x.DimensionId))) throw new ArgumentException("A transfer dimension does not belong to the budget model.");
        var suppliedDimensions = inputs.Select(x => x.DimensionId).ToHashSet();
        if (modelDimensions.Where(x => x.IsRequired).Any(x => !suppliedDimensions.Contains(x.DimensionId)))
            throw new ArgumentException("One or more required budget dimensions are missing from the transfer coordinate.");

        var memberIds = inputs.SelectMany(x => new[] { x.SourceMemberId, x.DestinationMemberId }).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking().Where(x => memberIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (members.Count != memberIds.Length) throw new ArgumentException("One or more transfer dimension members are invalid.");
        foreach (var input in inputs)
        {
            ValidateMember(members[input.SourceMemberId], input.DimensionId, plan.CompanyId);
            ValidateMember(members[input.DestinationMemberId], input.DimensionId, plan.CompanyId);
        }

        var sourceDimensions = inputs.Select(x => new DimensionSelection(x.DimensionId, x.SourceMemberId)).OrderBy(x => x.DimensionId).ToArray();
        var destinationDimensions = inputs.Select(x => new DimensionSelection(x.DimensionId, x.DestinationMemberId)).OrderBy(x => x.DimensionId).ToArray();
        var sourceHash = BudgetCoordinateKey.Create(sourceDimensions);
        var destinationHash = BudgetCoordinateKey.Create(destinationDimensions);
        if (request.SourcePeriodId == request.DestinationPeriodId && sourceHash == destinationHash)
            throw new ArgumentException("Source and destination of a budget transfer cannot be identical.");

        return new TransferContext(plan.CompanyId, request.VersionId, request.MeasureId, request.SourcePeriodId, request.DestinationPeriodId, sourceHash, destinationHash, sourceDimensions, destinationDimensions);
    }

    private async Task<BudgetTransferAvailabilityDto> GetAvailabilityInternalAsync(
        TransferContext context,
        CancellationToken cancellationToken)
    {
        var facts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == context.VersionId
                && x.MeasureId == context.MeasureId
                && ((x.PeriodId == context.SourcePeriodId && x.CoordinateHash == context.SourceHash)
                    || (x.PeriodId == context.DestinationPeriodId && x.CoordinateHash == context.DestinationHash)))
            .Select(x => new { x.PeriodId, x.CoordinateHash, x.ValueKind, x.Value })
            .ToListAsync(cancellationToken);

        var source = facts.Where(x => x.PeriodId == context.SourcePeriodId && x.CoordinateHash == context.SourceHash).ToList();
        var sourceBudget = source.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value);
        var sourceActual = source.Where(x => x.ValueKind == ValueKind.Actual).Sum(x => x.Value);
        var sourceCommitment = source.Where(x => x.ValueKind == ValueKind.Commitment).Sum(x => x.Value);
        var destinationBudget = facts.Where(x => x.PeriodId == context.DestinationPeriodId && x.CoordinateHash == context.DestinationHash && x.ValueKind == ValueKind.Budget).Sum(x => x.Value);
        return new BudgetTransferAvailabilityDto(sourceBudget, sourceActual, sourceCommitment, sourceBudget - sourceActual - sourceCommitment, destinationBudget);
    }

    private async Task ApplyTransferAsync(
        BudgetTransfer transfer,
        TransferContext context,
        CancellationToken cancellationToken)
    {
        var source = await db.BudgetFacts.Include(x => x.Dimensions).SingleOrDefaultAsync(x =>
            x.VersionId == context.VersionId
            && x.PeriodId == context.SourcePeriodId
            && x.MeasureId == context.MeasureId
            && x.ValueKind == ValueKind.Budget
            && x.CoordinateHash == context.SourceHash, cancellationToken)
            ?? throw new InvalidOperationException("Source budget fact does not exist for the transfer coordinate.");

        if (source.Value < transfer.Amount)
            throw new InvalidOperationException("Source budget is lower than the requested transfer amount.");

        var effectiveCurrency = NormalizeOptional(transfer.CurrencyCode, 12)?.ToUpperInvariant() ?? source.CurrencyCode;
        if (!string.IsNullOrWhiteSpace(source.CurrencyCode) && !string.IsNullOrWhiteSpace(effectiveCurrency)
            && !string.Equals(source.CurrencyCode, effectiveCurrency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Transfer currency must match the source budget currency.");

        var destination = await db.BudgetFacts.Include(x => x.Dimensions).SingleOrDefaultAsync(x =>
            x.VersionId == context.VersionId
            && x.PeriodId == context.DestinationPeriodId
            && x.MeasureId == context.MeasureId
            && x.ValueKind == ValueKind.Budget
            && x.CoordinateHash == context.DestinationHash, cancellationToken);
        if (destination is not null && !string.IsNullOrWhiteSpace(destination.CurrencyCode) && !string.IsNullOrWhiteSpace(effectiveCurrency)
            && !string.Equals(destination.CurrencyCode, effectiveCurrency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination budget currencies do not match.");

        source.Value -= transfer.Amount;
        source.Source = "BudgetTransfer";
        source.Note = $"Reduced by approved transfer {transfer.TransferNo}.";
        source.UpdatedAtUtc = DateTime.UtcNow;

        if (destination is null)
        {
            destination = new BudgetFact
            {
                VersionId = context.VersionId,
                PeriodId = context.DestinationPeriodId,
                MeasureId = context.MeasureId,
                ValueKind = ValueKind.Budget,
                Value = transfer.Amount,
                CurrencyCode = effectiveCurrency,
                CoordinateHash = context.DestinationHash,
                CoordinatesJson = JsonSerializer.Serialize(context.DestinationDimensions),
                Source = "BudgetTransfer",
                Note = $"Created by approved transfer {transfer.TransferNo}."
            };
            foreach (var selection in context.DestinationDimensions)
                destination.Dimensions.Add(new BudgetFactDimension { BudgetFactId = destination.Id, DimensionId = selection.DimensionId, MemberId = selection.MemberId });
            db.BudgetFacts.Add(destination);
        }
        else
        {
            destination.Value += transfer.Amount;
            destination.CurrencyCode ??= effectiveCurrency;
            destination.Source = "BudgetTransfer";
            destination.Note = $"Increased by approved transfer {transfer.TransferNo}.";
            destination.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task<BudgetTransfer> LoadForMutationAsync(Guid transferId, CancellationToken cancellationToken) =>
        await db.BudgetTransfers.Include(x => x.Dimensions)
            .SingleOrDefaultAsync(x => x.Id == transferId && x.TenantId == currentUser.TenantId, cancellationToken)
        ?? throw new KeyNotFoundException("Budget transfer was not found.");

    private async Task<BudgetTransferDto> GetByIdAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var item = await IncludeDetails(db.BudgetTransfers.AsNoTracking().Where(x => x.Id == transferId && x.TenantId == currentUser.TenantId))
            .SingleAsync(cancellationToken);
        EnsureCompanyRead(item.CompanyId);
        return ToDto(item);
    }

    private static IQueryable<BudgetTransfer> IncludeDetails(IQueryable<BudgetTransfer> query) => query
        .Include(x => x.Version)
        .Include(x => x.Measure)
        .Include(x => x.SourcePeriod)
        .Include(x => x.DestinationPeriod)
        .Include(x => x.RequestedByUser)
        .Include(x => x.DecidedByUser)
        .Include(x => x.Dimensions);

    private static BudgetTransferDto ToDto(BudgetTransfer item) => new(
        item.Id,
        item.TransferNo,
        item.CompanyId,
        item.VersionId,
        item.Version?.VersionNumber ?? 0,
        item.MeasureId,
        item.Measure?.Name ?? string.Empty,
        item.SourcePeriodId,
        item.SourcePeriod?.Name ?? string.Empty,
        item.DestinationPeriodId,
        item.DestinationPeriod?.Name ?? string.Empty,
        item.Amount,
        item.CurrencyCode,
        item.Status,
        item.Description,
        item.ExternalReference,
        item.RequestedByUserId,
        item.RequestedByUser?.DisplayName ?? string.Empty,
        item.DecidedByUserId,
        item.DecidedByUser?.DisplayName,
        item.DecisionComment,
        item.CreatedAtUtc,
        item.DecidedAtUtc,
        item.Dimensions.OrderBy(x => x.DimensionId).Select(x => new BudgetTransferDimensionInput(x.DimensionId, x.SourceMemberId, x.DestinationMemberId)).ToList());

    private static void ValidateMember(DimensionMember member, Guid dimensionId, Guid companyId)
    {
        if (member.DimensionId != dimensionId || !member.IsActive || (member.CompanyId.HasValue && member.CompanyId.Value != companyId))
            throw new ArgumentException("A transfer dimension member is invalid for the selected company/model.");
    }

    private void EnsureCompanyRead(Guid companyId)
    {
        if (!currentUser.IsInRole("SUPERADMIN") && !currentUser.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private void EnsureCompanyWrite(Guid companyId)
    {
        if (!currentUser.IsInRole("SUPERADMIN") && !currentUser.CanWriteCompany(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private void EnsureAuthenticated()
    {
        if (currentUser.UserId == Guid.Empty || currentUser.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated user is required.");
    }

    private void EnsureDecisionRole()
    {
        EnsureAuthenticated();
        if (currentUser.IsInRole("SUPERADMIN") || currentUser.IsInRole("ADMIN") || currentUser.IsInRole("CFO") || currentUser.IsInRole("CEO")) return;
        throw new UnauthorizedAccessException("CFO, CEO or administrator role is required to decide budget transfers.");
    }

    private void AddAudit(Guid transferId, string action, object value) => db.AuditLogs.Add(new AuditLog
    {
        TenantId = currentUser.TenantId,
        UserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,
        EntityType = "BudgetTransfer",
        EntityId = transferId.ToString(),
        Action = action,
        NewValueJson = JsonSerializer.Serialize(value)
    });

    private static string CreateTransferNo() => $"TRF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    private static string NormalizeRequired(string? value, string field, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > maxLength)
            throw new ArgumentException($"{field} is required and must be at most {maxLength} characters.");
        return text;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > maxLength) throw new ArgumentException($"Value must be at most {maxLength} characters.");
        return text;
    }

    private sealed record TransferContext(
        Guid CompanyId,
        Guid VersionId,
        Guid MeasureId,
        Guid SourcePeriodId,
        Guid DestinationPeriodId,
        string SourceHash,
        string DestinationHash,
        IReadOnlyList<DimensionSelection> SourceDimensions,
        IReadOnlyList<DimensionSelection> DestinationDimensions);
}
