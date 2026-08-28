using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class BudgetWorkflowService(PbmDbContext db, IUserContext user) : IBudgetWorkflowService
{
    public async Task<BudgetVersionDetailsDto> CreateRevisionAsync(CreateBudgetRevisionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Revision name is required.");

        var source = await db.BudgetVersions
            .Include(x => x.BudgetPlan)
            .Include(x => x.Facts).ThenInclude(x => x.Dimensions)
            .SingleAsync(x => x.Id == request.SourceVersionId, cancellationToken);

        EnsureCompanyWrite(source.BudgetPlan!.CompanyId);
        if (await db.FiscalYears.Where(x => x.Id == source.BudgetPlan.FiscalYearId).Select(x => x.IsClosed).SingleAsync(cancellationToken))
            throw new InvalidOperationException("Fiscal year is closed and a revision cannot be created.");
        if (source.Status is BudgetStatus.Submitted or BudgetStatus.UnderReview)
            throw new InvalidOperationException("A revision cannot be created while the source version is in review.");

        var scenarioId = request.ScenarioId ?? source.ScenarioId;
        if (!await db.BudgetScenarios.AnyAsync(x => x.Id == scenarioId && x.TenantId == user.TenantId && x.IsActive, cancellationToken))
            throw new ArgumentException("Scenario is invalid for the current tenant.");

        var nextVersionNumber = await db.BudgetVersions
            .Where(x => x.BudgetPlanId == source.BudgetPlanId)
            .MaxAsync(x => (int?)x.VersionNumber, cancellationToken) ?? 0;

        var revision = new BudgetVersion
        {
            BudgetPlanId = source.BudgetPlanId,
            ScenarioId = scenarioId,
            VersionNumber = nextVersionNumber + 1,
            Name = request.Name.Trim(),
            Status = BudgetStatus.Draft,
            IsLocked = false
        };

        foreach (var fact in source.Facts)
        {
            var clone = new BudgetFact
            {
                VersionId = revision.Id,
                PeriodId = fact.PeriodId,
                MeasureId = fact.MeasureId,
                ValueKind = fact.ValueKind,
                Value = fact.Value,
                CurrencyCode = fact.CurrencyCode,
                CoordinateHash = fact.CoordinateHash,
                CoordinatesJson = fact.CoordinatesJson,
                Source = $"Revision:{source.VersionNumber}",
                Note = fact.Note
            };
            foreach (var dimension in fact.Dimensions)
                clone.Dimensions.Add(new BudgetFactDimension { BudgetFactId = clone.Id, DimensionId = dimension.DimensionId, MemberId = dimension.MemberId });
            revision.Facts.Add(clone);
        }

        source.IsLocked = true;
        source.UpdatedAtUtc = DateTime.UtcNow;
        source.BudgetPlan.Status = BudgetStatus.Revised;
        source.BudgetPlan.UpdatedAtUtc = DateTime.UtcNow;
        db.BudgetVersions.Add(revision);
        AddAudit("BudgetVersion", revision.Id, "CREATE_REVISION", null, new
        {
            SourceVersionId = source.Id,
            SourceVersionNumber = source.VersionNumber,
            RevisionVersionNumber = revision.VersionNumber,
            RevisionName = revision.Name,
            CopiedFacts = source.Facts.Count
        });
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(revision);
    }

    public async Task<BudgetVersionDetailsDto> ChangeStatusAsync(Guid versionId, ChangeBudgetVersionStatusRequest request, CancellationToken cancellationToken = default)
    {
        var version = await db.BudgetVersions.Include(x => x.BudgetPlan).SingleAsync(x => x.Id == versionId, cancellationToken);
        EnsureCompanyWrite(version.BudgetPlan!.CompanyId);

        var oldStatus = version.Status;
        if (oldStatus == request.Status) return ToDto(version);
        if (!CanTransition(oldStatus, request.Status))
            throw new InvalidOperationException($"Budget version cannot move from {oldStatus} to {request.Status}.");
        EnsureTransitionRole(oldStatus, request.Status);

        var fiscalYearClosed = await db.FiscalYears.Where(x => x.Id == version.BudgetPlan.FiscalYearId).Select(x => x.IsClosed).SingleAsync(cancellationToken);
        if (fiscalYearClosed && request.Status is not BudgetStatus.Closed)
            throw new InvalidOperationException("Fiscal year is closed; only final closure state is permitted.");

        version.Status = request.Status;
        version.IsLocked = request.Status is BudgetStatus.Submitted or BudgetStatus.UnderReview or BudgetStatus.Approved or BudgetStatus.Rejected or BudgetStatus.Closed;
        version.UpdatedAtUtc = DateTime.UtcNow;
        version.BudgetPlan.Status = request.Status;
        version.BudgetPlan.UpdatedAtUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Comment))
            await AddCommentInternalAsync(version.Id, request.Comment.Trim(), cancellationToken);

        AddAudit("BudgetVersion", version.Id, "STATUS_CHANGE", new { Status = oldStatus }, new { Status = version.Status, version.IsLocked, request.Comment });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(version);
    }

    public async Task<IReadOnlyList<BudgetCommentDto>> GetCommentsAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var companyId = await db.BudgetVersions.Where(x => x.Id == versionId).Select(x => x.BudgetPlan!.CompanyId).SingleAsync(cancellationToken);
        EnsureCompanyRead(companyId);
        return await db.BudgetComments.AsNoTracking().Where(x => x.VersionId == versionId).OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new BudgetCommentDto(x.Id, x.VersionId, x.UserId, x.User!.DisplayName, x.Text, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<BudgetCommentDto> AddCommentAsync(Guid versionId, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Comment text is required.");
        var companyId = await db.BudgetVersions.Where(x => x.Id == versionId).Select(x => x.BudgetPlan!.CompanyId).SingleAsync(cancellationToken);
        EnsureCompanyRead(companyId);
        var comment = await AddCommentInternalAsync(versionId, text.Trim(), cancellationToken);
        AddAudit("BudgetComment", comment.Id, "CREATE", null, new { comment.VersionId, comment.Text });
        await db.SaveChangesAsync(cancellationToken);
        var displayName = await db.Users.Where(x => x.Id == comment.UserId).Select(x => x.DisplayName).SingleAsync(cancellationToken);
        return new BudgetCommentDto(comment.Id, comment.VersionId, comment.UserId, displayName, comment.Text, comment.CreatedAtUtc);
    }

    private Task<BudgetComment> AddCommentInternalAsync(Guid versionId, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (user.UserId == Guid.Empty) throw new UnauthorizedAccessException("Authenticated user is required for comments.");
        var comment = new BudgetComment { VersionId = versionId, UserId = user.UserId, Text = text };
        db.BudgetComments.Add(comment);
        return Task.FromResult(comment);
    }

    private static bool CanTransition(BudgetStatus from, BudgetStatus to) => (from, to) switch
    {
        (BudgetStatus.Draft, BudgetStatus.Submitted) => true,
        (BudgetStatus.Submitted, BudgetStatus.UnderReview) => true,
        (BudgetStatus.Submitted, BudgetStatus.Returned) => true,
        (BudgetStatus.Submitted, BudgetStatus.Rejected) => true,
        (BudgetStatus.UnderReview, BudgetStatus.Approved) => true,
        (BudgetStatus.UnderReview, BudgetStatus.Returned) => true,
        (BudgetStatus.UnderReview, BudgetStatus.Rejected) => true,
        (BudgetStatus.Returned, BudgetStatus.Draft) => true,
        (BudgetStatus.Approved, BudgetStatus.Closed) => true,
        _ => false
    };

    private void EnsureTransitionRole(BudgetStatus from, BudgetStatus to)
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN")) return;
        if ((from, to) is (BudgetStatus.Draft, BudgetStatus.Submitted) or (BudgetStatus.Returned, BudgetStatus.Draft)) return;

        if (from == BudgetStatus.Submitted && to is BudgetStatus.UnderReview or BudgetStatus.Returned or BudgetStatus.Rejected)
        {
            if (user.IsInRole("BUDGET_MANAGER") || user.IsInRole("CFO")) return;
            throw new UnauthorizedAccessException("Budget manager or CFO role is required for review decisions.");
        }

        if (from == BudgetStatus.UnderReview && to is BudgetStatus.Returned or BudgetStatus.Rejected)
        {
            if (user.IsInRole("BUDGET_MANAGER") || user.IsInRole("CFO") || user.IsInRole("CEO")) return;
            throw new UnauthorizedAccessException("Budget manager, CFO or CEO role is required for this review decision.");
        }

        if ((from, to) is (BudgetStatus.UnderReview, BudgetStatus.Approved) or (BudgetStatus.Approved, BudgetStatus.Closed))
        {
            if (user.IsInRole("CFO") || user.IsInRole("CEO")) return;
            throw new UnauthorizedAccessException("CFO or CEO role is required for approval and closure.");
        }

        throw new UnauthorizedAccessException("Your role cannot perform this workflow transition.");
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

    private void AddAudit(string entityType, Guid entityId, string action, object? oldValue, object? newValue) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Action = action,
            OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
        });

    private static BudgetVersionDetailsDto ToDto(BudgetVersion x) =>
        new(x.Id, x.BudgetPlanId, x.ScenarioId, x.VersionNumber, x.Name, x.Status, x.IsLocked, x.CreatedAtUtc, x.UpdatedAtUtc);
}
