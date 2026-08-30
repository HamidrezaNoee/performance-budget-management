using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class BudgetReservationService(
    PbmDbContext db,
    IUserContext currentUser,
    ICalculationService calculation) : IBudgetReservationService
{
    public async Task<IReadOnlyList<BudgetReservationDto>> GetAsync(
        Guid companyId,
        BudgetReservationStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        EnsureCompanyRead(companyId);
        take = Math.Clamp(take, 1, 500);
        var query = db.BudgetReservations.AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId && x.CompanyId == companyId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var items = await IncludeDetails(query)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<BudgetAvailabilityDto> GetAvailabilityAsync(
        Guid versionId,
        Guid periodId,
        Guid measureId,
        IReadOnlyList<DimensionSelection> dimensions,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(versionId, periodId, measureId, dimensions, requireOperationalVersion: false, cancellationToken);
        EnsureCompanyRead(context.CompanyId);
        return await GetAvailabilityInternalAsync(context, cancellationToken);
    }

    public async Task<BudgetReservationDto> CreateAsync(
        CreateBudgetReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0m) throw new ArgumentException("Reservation amount must be greater than zero.");
        var description = NormalizeRequired(request.Description, "Description", 1000);
        var currencyCode = NormalizeOptional(request.CurrencyCode, 12)?.ToUpperInvariant();
        var externalReference = NormalizeOptional(request.ExternalReference, 200);

        var context = await LoadContextAsync(request.VersionId, request.PeriodId, request.MeasureId, request.Dimensions, requireOperationalVersion: true, cancellationToken);
        if (context.CompanyId != request.CompanyId) throw new ArgumentException("Reservation company does not match the selected budget version.");
        EnsureCompanyWrite(context.CompanyId);
        EnsureAuthenticated();

        var availability = await GetAvailabilityInternalAsync(context, cancellationToken);
        if (request.Amount > availability.Available)
            throw new InvalidOperationException($"Insufficient available budget. Requested {request.Amount:0.########}; available {availability.Available:0.########}.");

        var reservation = new BudgetReservation
        {
            TenantId = currentUser.TenantId,
            CompanyId = context.CompanyId,
            VersionId = request.VersionId,
            PeriodId = request.PeriodId,
            MeasureId = request.MeasureId,
            RequestedByUserId = currentUser.UserId,
            ReservationNo = CreateReservationNo(),
            Description = description,
            Amount = request.Amount,
            CurrencyCode = currencyCode,
            CoordinateHash = context.CoordinateHash,
            CoordinatesJson = context.CoordinatesJson,
            ExternalReference = externalReference
        };
        foreach (var selection in context.Dimensions)
            reservation.Dimensions.Add(new BudgetReservationDimension
            {
                ReservationId = reservation.Id,
                DimensionId = selection.DimensionId,
                MemberId = selection.MemberId
            });

        db.BudgetReservations.Add(reservation);
        AddAudit(reservation.Id, "REQUEST", new
        {
            reservation.ReservationNo,
            reservation.CompanyId,
            reservation.VersionId,
            reservation.PeriodId,
            reservation.MeasureId,
            reservation.Amount,
            reservation.CurrencyCode,
            reservation.CoordinateHash,
            reservation.ExternalReference
        });
        await db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(reservation.Id, cancellationToken);
    }

    public async Task<BudgetReservationDto> ApproveAsync(
        Guid reservationId,
        BudgetReservationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureApprovalRole();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var reservation = await LoadForMutationAsync(reservationId, cancellationToken);
        EnsureCompanyWrite(reservation.CompanyId);
        if (reservation.Status != BudgetReservationStatus.Requested)
            throw new InvalidOperationException("Only a requested reservation can be approved.");

        var context = await LoadContextAsync(
            reservation.VersionId,
            reservation.PeriodId,
            reservation.MeasureId,
            reservation.Dimensions.Select(x => new DimensionSelection(x.DimensionId, x.MemberId)).ToList(),
            requireOperationalVersion: true,
            cancellationToken);
        var availability = await GetAvailabilityInternalAsync(context, cancellationToken);
        if (reservation.Amount > availability.Available)
            throw new InvalidOperationException($"Available budget changed after the request was created. Requested {reservation.Amount:0.########}; available {availability.Available:0.########}.");

        await ApplyCommitmentDeltaAsync(context, reservation.Amount, cancellationToken);
        reservation.Status = BudgetReservationStatus.Approved;
        reservation.DecidedByUserId = currentUser.UserId;
        reservation.DecidedAtUtc = DateTime.UtcNow;
        reservation.DecisionComment = NormalizeOptional(request.Comment, 1200);
        reservation.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(reservation.Id, "APPROVE", new { reservation.Amount, reservation.DecisionComment });
        await db.SaveChangesAsync(cancellationToken);
        await calculation.RecalculateCoordinateAsync(reservation.VersionId, reservation.PeriodId, ValueKind.Commitment, context.Dimensions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(reservation.Id, cancellationToken);
    }

    public async Task<BudgetReservationDto> RejectAsync(
        Guid reservationId,
        BudgetReservationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureApprovalRole();
        var reservation = await LoadForMutationAsync(reservationId, cancellationToken);
        EnsureCompanyWrite(reservation.CompanyId);
        if (reservation.Status != BudgetReservationStatus.Requested)
            throw new InvalidOperationException("Only a requested reservation can be rejected.");

        reservation.Status = BudgetReservationStatus.Rejected;
        reservation.DecidedByUserId = currentUser.UserId;
        reservation.DecidedAtUtc = DateTime.UtcNow;
        reservation.DecisionComment = NormalizeOptional(request.Comment, 1200);
        reservation.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(reservation.Id, "REJECT", new { reservation.DecisionComment });
        await db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(reservation.Id, cancellationToken);
    }

    public async Task<BudgetReservationDto> ReleaseAsync(
        Guid reservationId,
        BudgetReservationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var reservation = await LoadForMutationAsync(reservationId, cancellationToken);
        EnsureCompanyWrite(reservation.CompanyId);
        EnsureReleaseRole(reservation);
        if (reservation.Status != BudgetReservationStatus.Approved)
            throw new InvalidOperationException("Only an approved reservation can be released.");

        var dimensions = reservation.Dimensions.Select(x => new DimensionSelection(x.DimensionId, x.MemberId)).ToList();
        var context = await LoadContextAsync(reservation.VersionId, reservation.PeriodId, reservation.MeasureId, dimensions, requireOperationalVersion: false, cancellationToken);
        await ApplyCommitmentDeltaAsync(context, -reservation.Amount, cancellationToken);
        reservation.Status = BudgetReservationStatus.Released;
        reservation.ReleasedAtUtc = DateTime.UtcNow;
        reservation.DecisionComment = AppendComment(reservation.DecisionComment, request.Comment);
        reservation.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(reservation.Id, "RELEASE", new { reservation.Amount, reservation.DecisionComment });
        await db.SaveChangesAsync(cancellationToken);
        await calculation.RecalculateCoordinateAsync(reservation.VersionId, reservation.PeriodId, ValueKind.Commitment, context.Dimensions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(reservation.Id, cancellationToken);
    }

    public async Task<BudgetReservationDto> ConsumeAsync(
        Guid reservationId,
        ConsumeBudgetReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureApprovalRole();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var reservation = await LoadForMutationAsync(reservationId, cancellationToken);
        EnsureCompanyWrite(reservation.CompanyId);
        if (reservation.Status != BudgetReservationStatus.Approved)
            throw new InvalidOperationException("Only an approved reservation can be consumed.");

        var dimensions = reservation.Dimensions.Select(x => new DimensionSelection(x.DimensionId, x.MemberId)).ToList();
        var context = await LoadContextAsync(reservation.VersionId, reservation.PeriodId, reservation.MeasureId, dimensions, requireOperationalVersion: false, cancellationToken);
        await ApplyCommitmentDeltaAsync(context, -reservation.Amount, cancellationToken);
        reservation.Status = BudgetReservationStatus.Consumed;
        reservation.ConsumedAtUtc = DateTime.UtcNow;
        reservation.ExternalReference = NormalizeOptional(request.ExternalReference, 200) ?? reservation.ExternalReference;
        reservation.DecisionComment = AppendComment(reservation.DecisionComment, request.Comment);
        reservation.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(reservation.Id, "CONSUME", new { reservation.Amount, reservation.ExternalReference, reservation.DecisionComment });
        await db.SaveChangesAsync(cancellationToken);
        await calculation.RecalculateCoordinateAsync(reservation.VersionId, reservation.PeriodId, ValueKind.Commitment, context.Dimensions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(reservation.Id, cancellationToken);
    }

    private async Task<ReservationContext> LoadContextAsync(
        Guid versionId,
        Guid periodId,
        Guid measureId,
        IReadOnlyList<DimensionSelection> dimensions,
        bool requireOperationalVersion,
        CancellationToken cancellationToken)
    {
        var version = await db.BudgetVersions.AsNoTracking()
            .Include(x => x.BudgetPlan).ThenInclude(x => x!.FiscalYear)
            .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget version was not found.");
        var plan = version.BudgetPlan ?? throw new InvalidOperationException("Budget version has no plan.");
        if (plan.CompanyId == Guid.Empty) throw new InvalidOperationException("Budget plan has no company.");
        if (!await db.Companies.AsNoTracking().AnyAsync(x => x.Id == plan.CompanyId && x.TenantId == currentUser.TenantId && x.IsActive, cancellationToken))
            throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        if (requireOperationalVersion && version.Status is not (BudgetStatus.Approved or BudgetStatus.Closed))
            throw new InvalidOperationException("Reservations can only be requested against an approved or closed budget version.");
        if (plan.FiscalYear?.IsClosed == true)
            throw new InvalidOperationException("Fiscal year is closed and cannot accept reservation activity.");

        var period = await db.FiscalPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId && x.FiscalYearId == plan.FiscalYearId, cancellationToken)
            ?? throw new ArgumentException("Fiscal period does not belong to the selected budget version.");
        if (period.IsClosed) throw new InvalidOperationException("Fiscal period is closed and cannot accept reservation activity.");

        var measure = await db.Measures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == measureId && x.BudgetModelId == plan.BudgetModelId, cancellationToken)
            ?? throw new ArgumentException("Measure does not belong to the selected budget model.");
        if (measure.IsCalculated) throw new InvalidOperationException("Reservations cannot be created against a calculated measure.");
        if (measure.ValueType != MeasureValueType.Amount) throw new InvalidOperationException("Reservations require an amount-type measure.");

        var selections = (dimensions ?? []).OrderBy(x => x.DimensionId).ToArray();
        var suppliedDimensionIds = selections.Select(x => x.DimensionId).Distinct().ToArray();
        if (suppliedDimensionIds.Length != selections.Length) throw new ArgumentException("A reservation dimension can only be supplied once.");
        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking().Where(x => x.BudgetModelId == plan.BudgetModelId).ToListAsync(cancellationToken);
        var allowedDimensionIds = modelDimensions.Select(x => x.DimensionId).ToHashSet();
        if (selections.Any(x => !allowedDimensionIds.Contains(x.DimensionId))) throw new ArgumentException("A reservation dimension does not belong to the budget model.");
        if (modelDimensions.Where(x => x.IsRequired).Any(x => !suppliedDimensionIds.Contains(x.DimensionId)))
            throw new ArgumentException("One or more required budget dimensions are missing from the reservation coordinate.");

        var memberIds = selections.Select(x => x.MemberId).Distinct().ToArray();
        var members = await db.DimensionMembers.AsNoTracking().Where(x => memberIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (members.Count != memberIds.Length) throw new ArgumentException("One or more reservation dimension members are invalid.");
        foreach (var selection in selections)
        {
            var member = members[selection.MemberId];
            if (member.DimensionId != selection.DimensionId || (member.CompanyId.HasValue && member.CompanyId.Value != plan.CompanyId))
                throw new ArgumentException("A reservation dimension member is invalid for the selected company/model.");
        }

        var hash = BudgetCoordinateKey.Create(selections);
        return new ReservationContext(plan.CompanyId, version.Id, period.Id, measure.Id, hash, JsonSerializer.Serialize(selections), selections);
    }

    private async Task<BudgetAvailabilityDto> GetAvailabilityInternalAsync(ReservationContext context, CancellationToken cancellationToken)
    {
        var facts = await db.BudgetFacts.AsNoTracking()
            .Where(x => x.VersionId == context.VersionId
                && x.PeriodId == context.PeriodId
                && x.MeasureId == context.MeasureId
                && x.CoordinateHash == context.CoordinateHash)
            .Select(x => new { x.ValueKind, x.Value })
            .ToListAsync(cancellationToken);
        var budget = facts.Where(x => x.ValueKind == ValueKind.Budget).Sum(x => x.Value);
        var actual = facts.Where(x => x.ValueKind == ValueKind.Actual).Sum(x => x.Value);
        var commitment = facts.Where(x => x.ValueKind == ValueKind.Commitment).Sum(x => x.Value);
        var available = budget - actual - commitment;
        return new BudgetAvailabilityDto(budget, actual, commitment, available);
    }

    private async Task ApplyCommitmentDeltaAsync(ReservationContext context, decimal delta, CancellationToken cancellationToken)
    {
        var fact = await db.BudgetFacts.Include(x => x.Dimensions).SingleOrDefaultAsync(x =>
            x.VersionId == context.VersionId
            && x.PeriodId == context.PeriodId
            && x.MeasureId == context.MeasureId
            && x.ValueKind == ValueKind.Commitment
            && x.CoordinateHash == context.CoordinateHash, cancellationToken);

        if (fact is null)
        {
            if (delta < 0m) throw new InvalidOperationException("Commitment ledger is missing for this reservation coordinate.");
            fact = new BudgetFact
            {
                VersionId = context.VersionId,
                PeriodId = context.PeriodId,
                MeasureId = context.MeasureId,
                ValueKind = ValueKind.Commitment,
                Value = delta,
                CoordinateHash = context.CoordinateHash,
                CoordinatesJson = context.CoordinatesJson,
                Source = "ReservationLedger",
                Note = "Aggregated commitment maintained by the budget reservation lifecycle."
            };
            foreach (var selection in context.Dimensions)
                fact.Dimensions.Add(new BudgetFactDimension { BudgetFactId = fact.Id, DimensionId = selection.DimensionId, MemberId = selection.MemberId });
            db.BudgetFacts.Add(fact);
            return;
        }

        var next = fact.Value + delta;
        if (next < 0m)
            throw new InvalidOperationException("Commitment ledger is lower than the reservation amount. Reconcile commitment data before releasing or consuming this reservation.");
        fact.Value = next;
        fact.Source = "ReservationLedger";
        fact.Note = "Aggregated commitment maintained by the budget reservation lifecycle.";
        fact.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task<BudgetReservation> LoadForMutationAsync(Guid reservationId, CancellationToken cancellationToken) =>
        await db.BudgetReservations
            .Include(x => x.Dimensions)
            .SingleOrDefaultAsync(x => x.Id == reservationId && x.TenantId == currentUser.TenantId, cancellationToken)
        ?? throw new KeyNotFoundException("Budget reservation was not found.");

    private async Task<BudgetReservationDto> GetByIdAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var item = await IncludeDetails(db.BudgetReservations.AsNoTracking().Where(x => x.Id == reservationId && x.TenantId == currentUser.TenantId))
            .SingleAsync(cancellationToken);
        EnsureCompanyRead(item.CompanyId);
        return ToDto(item);
    }

    private static IQueryable<BudgetReservation> IncludeDetails(IQueryable<BudgetReservation> query) => query
        .Include(x => x.Version)
        .Include(x => x.Period)
        .Include(x => x.Measure)
        .Include(x => x.RequestedByUser)
        .Include(x => x.DecidedByUser)
        .Include(x => x.Dimensions);

    private static BudgetReservationDto ToDto(BudgetReservation item) => new(
        item.Id,
        item.ReservationNo,
        item.CompanyId,
        item.VersionId,
        item.Version?.VersionNumber ?? 0,
        item.PeriodId,
        item.Period?.Name ?? string.Empty,
        item.MeasureId,
        item.Measure?.Name ?? string.Empty,
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
        item.ReleasedAtUtc,
        item.ConsumedAtUtc,
        item.Dimensions.OrderBy(x => x.DimensionId).Select(x => new DimensionSelection(x.DimensionId, x.MemberId)).ToList());

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

    private void EnsureApprovalRole()
    {
        EnsureAuthenticated();
        if (currentUser.IsInRole("SUPERADMIN") || currentUser.IsInRole("ADMIN") || currentUser.IsInRole("BUDGET_MANAGER") || currentUser.IsInRole("CFO")) return;
        throw new UnauthorizedAccessException("Budget manager, CFO or administrator role is required for reservation decisions.");
    }

    private void EnsureReleaseRole(BudgetReservation reservation)
    {
        EnsureAuthenticated();
        if (reservation.RequestedByUserId == currentUser.UserId
            || currentUser.IsInRole("SUPERADMIN")
            || currentUser.IsInRole("ADMIN")
            || currentUser.IsInRole("BUDGET_MANAGER")
            || currentUser.IsInRole("CFO")) return;
        throw new UnauthorizedAccessException("Only the requester, budget manager, CFO or administrator can release a reservation.");
    }

    private void AddAudit(Guid reservationId, string action, object value) => db.AuditLogs.Add(new AuditLog
    {
        TenantId = currentUser.TenantId,
        UserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,
        EntityType = "BudgetReservation",
        EntityId = reservationId.ToString(),
        Action = action,
        NewValueJson = JsonSerializer.Serialize(value)
    });

    private static string CreateReservationNo() =>
        $"RSV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

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

    private static string? AppendComment(string? existing, string? addition)
    {
        var next = NormalizeOptional(addition, 1200);
        if (next is null) return existing;
        if (string.IsNullOrWhiteSpace(existing)) return next;
        var combined = $"{existing}\n{next}";
        return combined.Length <= 1200 ? combined : combined[^1200..];
    }

    private sealed record ReservationContext(
        Guid CompanyId,
        Guid VersionId,
        Guid PeriodId,
        Guid MeasureId,
        string CoordinateHash,
        string CoordinatesJson,
        IReadOnlyList<DimensionSelection> Dimensions);
}
