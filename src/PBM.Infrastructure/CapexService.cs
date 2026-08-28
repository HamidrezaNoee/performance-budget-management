using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class CapexService(
    PbmDbContext db,
    IUserContext user,
    INotificationService notifications) : ICapexService
{
    public async Task<IReadOnlyList<CapexProjectDto>> GetProjectsAsync(
        Guid companyId,
        CapexProjectStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCompanyRead(companyId);
        var query = db.Set<CapexProject>().AsNoTracking().Where(x => x.CompanyId == companyId && x.TenantId == user.TenantId && x.IsActive);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        var projects = await query
            .Include(x => x.OwnerOrganizationUnit)
            .Include(x => x.RequestedByUser)
            .Include(x => x.ApprovedByUser)
            .Include(x => x.Milestones)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.StartDate).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return projects.Select(Map).ToList();
    }

    public async Task<CapexProjectDto> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, tracking: false, cancellationToken);
        EnsureCompanyRead(project.CompanyId);
        return Map(project);
    }

    public async Task<CapexProjectDto> CreateProjectAsync(
        CreateCapexProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureCompanyWrite(request.CompanyId);
        if (user.UserId == Guid.Empty) throw new UnauthorizedAccessException("Authenticated user is required.");
        var code = NormalizeCode(request.Code);
        var name = NormalizeRequired(request.Name, "Project name", 200);
        ValidateDates(request.StartDate, request.EndDate);
        ValidateMoney(request.RequestedBudget, "Requested budget");
        var currencyCode = await ValidateCurrencyAsync(request.CurrencyCode, cancellationToken);
        await ValidateOwnerAsync(request.CompanyId, request.OwnerOrganizationUnitId, cancellationToken);
        if (await db.Set<CapexProject>().AnyAsync(x => x.CompanyId == request.CompanyId && x.Code == code, cancellationToken))
            throw new InvalidOperationException("A CAPEX project with this code already exists in the selected company.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var infrastructure = await EnsureCapexInfrastructureAsync(request.CompanyId, cancellationToken);
        var project = new CapexProject
        {
            TenantId = user.TenantId,
            CompanyId = request.CompanyId,
            ProjectDimensionMemberId = Guid.Empty,
            Code = code,
            Name = name,
            Description = NormalizeOptional(request.Description, 2000),
            Priority = request.Priority,
            StartDate = request.StartDate.Date,
            EndDate = request.EndDate.Date,
            RequestedBudget = request.RequestedBudget,
            CurrencyCode = currencyCode,
            OwnerOrganizationUnitId = request.OwnerOrganizationUnitId,
            RequestedByUserId = user.UserId,
            Status = CapexProjectStatus.Proposed
        };
        var member = new DimensionMember
        {
            DimensionId = infrastructure.ProjectDimension.Id,
            CompanyId = request.CompanyId,
            Code = code,
            Name = name,
            ExternalKey = project.Id.ToString()
        };
        project.ProjectDimensionMemberId = member.Id;
        db.DimensionMembers.Add(member);
        db.Set<CapexProject>().Add(project);
        AddAudit("CapexProject", project.Id, "CREATE", null, new
        {
            project.Code, project.Name, project.CompanyId, project.RequestedBudget,
            project.CurrencyCode, project.Priority, project.StartDate, project.EndDate,
            ProjectDimensionId = infrastructure.ProjectDimension.Id,
            project.ProjectDimensionMemberId,
            CapexBudgetModelId = infrastructure.CapexModel.Id
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetProjectAsync(project.Id, cancellationToken);
    }

    public async Task<CapexProjectDto> UpdateProjectAsync(
        Guid projectId,
        UpdateCapexProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, tracking: true, cancellationToken);
        EnsureCompanyWrite(project.CompanyId);
        if (project.Status is CapexProjectStatus.Completed or CapexProjectStatus.Cancelled)
            throw new InvalidOperationException("Completed or cancelled CAPEX projects are read-only.");
        ValidateDates(request.StartDate, request.EndDate);
        ValidateMoney(request.RequestedBudget, "Requested budget");
        ValidateMoney(request.ApprovedBudgetLimit, "Approved budget limit");
        ValidatePercent(request.CompletionPercent, "Completion percent");
        await ValidateOwnerAsync(project.CompanyId, request.OwnerOrganizationUnitId, cancellationToken);
        var currencyCode = await ValidateCurrencyAsync(request.CurrencyCode, cancellationToken);
        var reviewer = IsReviewer();

        if (project.Status != CapexProjectStatus.Proposed && project.RequestedBudget != request.RequestedBudget && !reviewer)
            throw new UnauthorizedAccessException("Requested budget can only be changed by a reviewer after submission.");
        if (project.ApprovedBudgetLimit != request.ApprovedBudgetLimit && !reviewer)
            throw new UnauthorizedAccessException("Approved CAPEX budget limit can only be changed by CFO, budget manager or administrator.");

        var old = new
        {
            project.Name, project.Description, project.Priority, project.StartDate, project.EndDate,
            project.RequestedBudget, project.ApprovedBudgetLimit, project.CurrencyCode,
            project.OwnerOrganizationUnitId, project.CompletionPercent
        };
        project.Name = NormalizeRequired(request.Name, "Project name", 200);
        project.Description = NormalizeOptional(request.Description, 2000);
        project.Priority = request.Priority;
        project.StartDate = request.StartDate.Date;
        project.EndDate = request.EndDate.Date;
        project.RequestedBudget = request.RequestedBudget;
        project.ApprovedBudgetLimit = request.ApprovedBudgetLimit;
        project.CurrencyCode = currencyCode;
        project.OwnerOrganizationUnitId = request.OwnerOrganizationUnitId;
        project.CompletionPercent = request.CompletionPercent;
        project.UpdatedAtUtc = DateTime.UtcNow;
        var member = await db.DimensionMembers.SingleAsync(x => x.Id == project.ProjectDimensionMemberId, cancellationToken);
        member.Name = project.Name;
        member.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("CapexProject", project.Id, "UPDATE", old, new
        {
            project.Name, project.Description, project.Priority, project.StartDate, project.EndDate,
            project.RequestedBudget, project.ApprovedBudgetLimit, project.CurrencyCode,
            project.OwnerOrganizationUnitId, project.CompletionPercent
        });
        await db.SaveChangesAsync(cancellationToken);
        return await GetProjectAsync(project.Id, cancellationToken);
    }

    public async Task<CapexProjectDto> ChangeStatusAsync(
        Guid projectId,
        ChangeCapexProjectStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, tracking: true, cancellationToken);
        EnsureCompanyWrite(project.CompanyId);
        var oldStatus = project.Status;
        if (oldStatus == request.Status) return Map(project);
        if (!CanTransition(oldStatus, request.Status))
            throw new InvalidOperationException($"CAPEX project cannot move from {oldStatus} to {request.Status}.");

        var reviewerAction = request.Status is CapexProjectStatus.Approved or CapexProjectStatus.Cancelled
            || (oldStatus == CapexProjectStatus.Submitted && request.Status == CapexProjectStatus.Proposed);
        if (reviewerAction && !IsReviewer())
            throw new UnauthorizedAccessException("CFO, budget manager or administrator role is required for this CAPEX decision.");
        if (request.Status == CapexProjectStatus.Submitted && (!project.RequestedBudget.HasValue || project.RequestedBudget <= 0))
            throw new InvalidOperationException("Requested budget must be greater than zero before the project is submitted.");
        if (request.Status == CapexProjectStatus.Approved && (!project.ApprovedBudgetLimit.HasValue || project.ApprovedBudgetLimit <= 0))
            throw new InvalidOperationException("Approved budget limit must be set before CAPEX approval.");
        if (request.Status == CapexProjectStatus.Completed)
        {
            if (project.CompletionPercent < 100m) throw new InvalidOperationException("Project completion must be 100% before closure.");
            if (project.Milestones.Any(x => !x.IsCompleted)) throw new InvalidOperationException("All CAPEX milestones must be completed before the project is closed.");
        }
        if (request.Status is CapexProjectStatus.Cancelled or CapexProjectStatus.Proposed && oldStatus == CapexProjectStatus.Submitted)
        {
            if (string.IsNullOrWhiteSpace(request.Comment)) throw new ArgumentException("A decision comment is required for cancellation or return for correction.");
        }

        project.Status = request.Status;
        project.LastDecisionComment = NormalizeOptional(request.Comment, 2000);
        project.UpdatedAtUtc = DateTime.UtcNow;
        if (request.Status == CapexProjectStatus.Approved)
        {
            project.ApprovedByUserId = user.UserId;
            project.ApprovedAtUtc = DateTime.UtcNow;
        }
        AddAudit("CapexProject", project.Id, "STATUS_CHANGE", new { Status = oldStatus }, new { Status = project.Status, project.LastDecisionComment });
        await db.SaveChangesAsync(cancellationToken);
        await DispatchStatusNotificationsAsync(project, oldStatus, cancellationToken);
        return await GetProjectAsync(project.Id, cancellationToken);
    }

    public async Task<CapexMilestoneDto> UpsertMilestoneAsync(
        Guid projectId,
        UpsertCapexMilestoneRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, tracking: true, cancellationToken);
        EnsureCompanyWrite(project.CompanyId);
        if (project.Status is CapexProjectStatus.Completed or CapexProjectStatus.Cancelled)
            throw new InvalidOperationException("Milestones of completed or cancelled projects are read-only.");
        var code = NormalizeCode(request.Code);
        var name = NormalizeRequired(request.Name, "Milestone name", 200);
        ValidatePercent(request.Weight, "Milestone weight");
        ValidatePercent(request.ProgressPercent, "Milestone progress");
        if (request.DueDate.Date < project.StartDate.Date || request.DueDate.Date > project.EndDate.Date)
            throw new ArgumentException("Milestone due date must be inside the CAPEX project date range.");

        CapexMilestone? milestone = null;
        if (request.Id.HasValue)
            milestone = project.Milestones.SingleOrDefault(x => x.Id == request.Id.Value)
                ?? throw new KeyNotFoundException("CAPEX milestone was not found.");
        var duplicate = project.Milestones.Any(x => x.Id != milestone?.Id && string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        if (duplicate) throw new InvalidOperationException("Milestone code must be unique inside a CAPEX project.");
        var totalOtherWeight = project.Milestones.Where(x => x.Id != milestone?.Id).Sum(x => x.Weight);
        if (totalOtherWeight + request.Weight > 100m)
            throw new InvalidOperationException("Total CAPEX milestone weight cannot exceed 100%.");

        var isNew = milestone is null;
        milestone ??= new CapexMilestone { ProjectId = project.Id, Code = code, Name = name, DueDate = request.DueDate.Date };
        milestone.Code = code;
        milestone.Name = name;
        milestone.DueDate = request.DueDate.Date;
        milestone.Weight = request.Weight;
        milestone.ProgressPercent = request.IsCompleted ? 100m : request.ProgressPercent;
        milestone.IsCompleted = request.IsCompleted || milestone.ProgressPercent >= 100m;
        milestone.CompletedAtUtc = milestone.IsCompleted ? milestone.CompletedAtUtc ?? DateTime.UtcNow : null;
        milestone.Note = NormalizeOptional(request.Note, 1000);
        milestone.UpdatedAtUtc = DateTime.UtcNow;
        if (isNew) project.Milestones.Add(milestone);
        RecalculateProjectProgress(project);
        AddAudit("CapexMilestone", milestone.Id, isNew ? "CREATE" : "UPDATE", null, new
        {
            project.Id, milestone.Code, milestone.Name, milestone.DueDate, milestone.Weight,
            milestone.ProgressPercent, milestone.IsCompleted, project.CompletionPercent
        });
        await db.SaveChangesAsync(cancellationToken);
        return Map(milestone);
    }

    public async Task DeleteMilestoneAsync(
        Guid projectId,
        Guid milestoneId,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, tracking: true, cancellationToken);
        EnsureCompanyWrite(project.CompanyId);
        if (project.Status is CapexProjectStatus.Completed or CapexProjectStatus.Cancelled)
            throw new InvalidOperationException("Milestones of completed or cancelled projects are read-only.");
        var milestone = project.Milestones.SingleOrDefault(x => x.Id == milestoneId)
            ?? throw new KeyNotFoundException("CAPEX milestone was not found.");
        db.Set<CapexMilestone>().Remove(milestone);
        project.Milestones.Remove(milestone);
        RecalculateProjectProgress(project);
        AddAudit("CapexMilestone", milestone.Id, "DELETE", new { milestone.Code, milestone.Name, milestone.Weight }, new { Deleted = true });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CapexFinancialSummaryDto> GetFinancialSummaryAsync(
        Guid projectId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, tracking: false, cancellationToken);
        EnsureCompanyRead(project.CompanyId);
        var fiscalYear = await db.FiscalYears.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fiscalYearId && x.CompanyId == project.CompanyId, cancellationToken)
            ?? throw new ArgumentException("Fiscal year does not belong to the CAPEX project company.");
        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == fiscalYear.Id).OrderBy(x => x.Sequence).ToListAsync(cancellationToken);

        var capexModel = await db.BudgetModels.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == "CAPEX", cancellationToken);
        var measure = capexModel is null ? null : await db.Measures.AsNoTracking().SingleOrDefaultAsync(x => x.BudgetModelId == capexModel.Id && x.Code == "CAPEX_AMOUNT", cancellationToken);
        var plan = capexModel is null ? null : await db.BudgetPlans.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == project.CompanyId && x.FiscalYearId == fiscalYearId && x.BudgetModelId == capexModel.Id, cancellationToken);

        BudgetVersion? version = null;
        if (plan is not null)
        {
            var versions = await db.BudgetVersions.AsNoTracking().Where(x => x.BudgetPlanId == plan.Id && x.Status != BudgetStatus.Rejected).ToListAsync(cancellationToken);
            version = versions.Where(x => x.Status is BudgetStatus.Approved or BudgetStatus.Closed).OrderByDescending(x => x.VersionNumber).FirstOrDefault()
                ?? versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        }

        var facts = version is null || measure is null
            ? []
            : await db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
                .Where(x => x.VersionId == version.Id && x.MeasureId == measure.Id
                    && x.Dimensions.Any(d => d.MemberId == project.ProjectDimensionMemberId)
                    && (x.CurrencyCode == null || x.CurrencyCode == project.CurrencyCode))
                .ToListAsync(cancellationToken);

        var grouped = facts.GroupBy(x => x.PeriodId).ToDictionary(x => x.Key, SummarizeKinds);
        var monthly = periods.Select(period =>
        {
            var values = grouped.GetValueOrDefault(period.Id) ?? new KindSummary();
            return new CapexMonthlyFinancialDto(period.Id, period.Name, period.Sequence,
                values.Budget, values.Actual, values.Commitment, values.Forecast,
                values.Budget - values.Actual - values.Commitment);
        }).ToList();
        var total = SummarizeKinds(facts);
        return new CapexFinancialSummaryDto(
            project.Id,
            fiscalYearId,
            total.Budget,
            total.Actual,
            total.Commitment,
            total.Forecast,
            total.Budget - total.Actual - total.Commitment,
            project.RequestedBudget,
            project.ApprovedBudgetLimit,
            project.ApprovedBudgetLimit.HasValue ? total.Budget - project.ApprovedBudgetLimit.Value : 0m,
            monthly);
    }

    private async Task<(DimensionDefinition ProjectDimension, BudgetModel CapexModel)> EnsureCapexInfrastructureAsync(Guid companyId, CancellationToken ct)
    {
        var tenantId = await db.Companies.Where(x => x.Id == companyId && x.TenantId == user.TenantId).Select(x => x.TenantId).SingleAsync(ct);
        var projectDimension = await db.Dimensions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "PROJECT", ct);
        if (projectDimension is null)
        {
            projectDimension = new DimensionDefinition { TenantId = tenantId, Code = "PROJECT", Name = "پروژه / طرح سرمایه‌ای", IsSystem = true, IsHierarchical = true };
            db.Dimensions.Add(projectDimension);
            await db.SaveChangesAsync(ct);
        }

        var model = await db.BudgetModels.Include(x => x.Dimensions).Include(x => x.Measures)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "CAPEX", ct);
        if (model is null)
        {
            model = new BudgetModel { TenantId = tenantId, Code = "CAPEX", Name = "بودجه سرمایه‌ای و پروژه‌ها", Description = "برنامه‌ریزی CAPEX، پروژه‌های سرمایه‌ای، تعهدات و عملکرد واقعی" };
            model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = projectDimension.Id, Sequence = 1, IsRequired = true });
            var optionalCodes = new[] { "DEPARTMENT", "COSTCENTER", "ACCOUNT" };
            var optionalDimensions = await db.Dimensions.Where(x => x.TenantId == tenantId && optionalCodes.Contains(x.Code)).ToListAsync(ct);
            var sequence = 2;
            foreach (var dimension in optionalDimensions.OrderBy(x => Array.IndexOf(optionalCodes, x.Code)))
                model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = dimension.Id, Sequence = sequence++, IsRequired = false });
            model.Measures.Add(new MeasureDefinition
            {
                BudgetModelId = model.Id, Code = "CAPEX_AMOUNT", Name = "مبلغ سرمایه‌گذاری", Unit = "ریال",
                ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Sum, DisplayOrder = 1
            });
            db.BudgetModels.Add(model);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            if (!model.Dimensions.Any(x => x.DimensionId == projectDimension.Id))
                model.Dimensions.Add(new BudgetModelDimension { BudgetModelId = model.Id, DimensionId = projectDimension.Id, Sequence = 1, IsRequired = true });
            if (!model.Measures.Any(x => x.Code == "CAPEX_AMOUNT"))
                model.Measures.Add(new MeasureDefinition
                {
                    BudgetModelId = model.Id, Code = "CAPEX_AMOUNT", Name = "مبلغ سرمایه‌گذاری", Unit = "ریال",
                    ValueType = MeasureValueType.Amount, Aggregation = MeasureAggregation.Sum, DisplayOrder = 1
                });
            await db.SaveChangesAsync(ct);
        }
        return (projectDimension, model);
    }

    private async Task<CapexProject> LoadProjectAsync(Guid projectId, bool tracking, CancellationToken ct)
    {
        IQueryable<CapexProject> query = db.Set<CapexProject>();
        if (!tracking) query = query.AsNoTracking();
        return await query.Where(x => x.Id == projectId && x.TenantId == user.TenantId)
            .Include(x => x.OwnerOrganizationUnit)
            .Include(x => x.RequestedByUser)
            .Include(x => x.ApprovedByUser)
            .Include(x => x.Milestones)
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("CAPEX project was not found.");
    }

    private async Task DispatchStatusNotificationsAsync(CapexProject project, CapexProjectStatus oldStatus, CancellationToken ct)
    {
        if (project.Status == CapexProjectStatus.Submitted)
        {
            var reviewerIds = await ResolveReviewerIdsAsync(project.CompanyId, ct);
            reviewerIds.Remove(user.UserId);
            if (reviewerIds.Count > 0)
                await notifications.DispatchAsync(new NotificationDispatchRequest(
                    reviewerIds, project.CompanyId, "CAPEX", "پروژه سرمایه‌ای منتظر بررسی",
                    $"پروژه {project.Code} — {project.Name} با بودجه درخواستی {project.RequestedBudget:N0} برای بررسی ارسال شد.",
                    NotificationSeverity.Info, "CapexProject", project.Id.ToString(), "#capex"), ct);
            return;
        }

        if (project.RequestedByUserId != user.UserId)
        {
            var (title, severity) = project.Status switch
            {
                CapexProjectStatus.Approved => ("پروژه سرمایه‌ای تأیید شد", NotificationSeverity.Success),
                CapexProjectStatus.Cancelled => ("پروژه سرمایه‌ای لغو شد", NotificationSeverity.Error),
                CapexProjectStatus.Proposed when oldStatus == CapexProjectStatus.Submitted => ("پروژه سرمایه‌ای برای اصلاح برگشت شد", NotificationSeverity.Warning),
                CapexProjectStatus.Completed => ("پروژه سرمایه‌ای تکمیل شد", NotificationSeverity.Success),
                _ => ("وضعیت پروژه سرمایه‌ای تغییر کرد", NotificationSeverity.Info)
            };
            await notifications.DispatchAsync(new NotificationDispatchRequest(
                [project.RequestedByUserId], project.CompanyId, "CAPEX", title,
                $"وضعیت پروژه {project.Code} — {project.Name} به {project.Status} تغییر کرد.",
                severity, "CapexProject", project.Id.ToString(), "#capex"), ct);
        }
    }

    private async Task<HashSet<Guid>> ResolveReviewerIdsAsync(Guid companyId, CancellationToken ct)
    {
        var roles = new[] { "BUDGET_MANAGER", "CFO", "ADMIN", "SUPERADMIN" };
        var ids = await db.Users.AsNoTracking().Where(x => x.TenantId == user.TenantId && x.IsActive
                && x.UserRoles.Any(r => roles.Contains(r.Role!.Code))
                && (x.UserRoles.Any(r => r.Role!.Code == "SUPERADMIN" || r.Role.Code == "ADMIN")
                    || x.CompanyAccess.Any(a => a.CompanyId == companyId && a.CanRead)))
            .Select(x => x.Id).ToListAsync(ct);
        return ids.ToHashSet();
    }

    private async Task ValidateOwnerAsync(Guid companyId, Guid? ownerId, CancellationToken ct)
    {
        if (ownerId.HasValue && !await db.OrganizationUnits.AnyAsync(x => x.Id == ownerId.Value && x.CompanyId == companyId && x.IsActive, ct))
            throw new ArgumentException("Owner organization unit is invalid for the selected company.");
    }

    private async Task<string> ValidateCurrencyAsync(string? currencyCode, CancellationToken ct)
    {
        var code = (currencyCode ?? "IRR").Trim().ToUpperInvariant();
        if (!await db.Currencies.AnyAsync(x => x.TenantId == user.TenantId && x.Code == code, ct))
            throw new ArgumentException("CAPEX currency is not defined for the current tenant.");
        return code;
    }

    private static bool CanTransition(CapexProjectStatus from, CapexProjectStatus to) => (from, to) switch
    {
        (CapexProjectStatus.Proposed, CapexProjectStatus.Submitted) => true,
        (CapexProjectStatus.Submitted, CapexProjectStatus.Proposed) => true,
        (CapexProjectStatus.Submitted, CapexProjectStatus.Approved) => true,
        (CapexProjectStatus.Submitted, CapexProjectStatus.Cancelled) => true,
        (CapexProjectStatus.Approved, CapexProjectStatus.InProgress) => true,
        (CapexProjectStatus.Approved, CapexProjectStatus.Cancelled) => true,
        (CapexProjectStatus.InProgress, CapexProjectStatus.OnHold) => true,
        (CapexProjectStatus.InProgress, CapexProjectStatus.Completed) => true,
        (CapexProjectStatus.InProgress, CapexProjectStatus.Cancelled) => true,
        (CapexProjectStatus.OnHold, CapexProjectStatus.InProgress) => true,
        (CapexProjectStatus.OnHold, CapexProjectStatus.Cancelled) => true,
        _ => false
    };

    private static void RecalculateProjectProgress(CapexProject project)
    {
        if (project.Milestones.Count == 0) return;
        var totalWeight = project.Milestones.Sum(x => x.Weight);
        project.CompletionPercent = totalWeight <= 0m ? 0m
            : Math.Round(project.Milestones.Sum(x => x.Weight * x.ProgressPercent) / totalWeight, 2, MidpointRounding.AwayFromZero);
        project.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static KindSummary SummarizeKinds(IEnumerable<BudgetFact> facts)
    {
        var result = new KindSummary();
        foreach (var fact in facts)
        {
            switch (fact.ValueKind)
            {
                case ValueKind.Budget: result.Budget += fact.Value; break;
                case ValueKind.Actual: result.Actual += fact.Value; break;
                case ValueKind.Commitment: result.Commitment += fact.Value; break;
                case ValueKind.Forecast: result.Forecast += fact.Value; break;
            }
        }
        return result;
    }

    private bool IsReviewer() => user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("CFO") || user.IsInRole("BUDGET_MANAGER");
    private void EnsureCompanyRead(Guid companyId) { if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company."); }
    private void EnsureCompanyWrite(Guid companyId) { if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId)) throw new UnauthorizedAccessException("You do not have write access to this company."); }

    private void AddAudit(string entityType, Guid entityId, string action, object? oldValue, object? newValue) => db.AuditLogs.Add(new AuditLog
    {
        TenantId = user.TenantId, UserId = user.UserId == Guid.Empty ? null : user.UserId,
        EntityType = entityType, EntityId = entityId.ToString(), Action = action,
        OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
        NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
    });

    private static CapexProjectDto Map(CapexProject x) => new(
        x.Id, x.CompanyId, x.ProjectDimensionMemberId, x.Code, x.Name, x.Description, x.Status, x.Priority,
        x.StartDate, x.EndDate, x.RequestedBudget, x.ApprovedBudgetLimit, x.CurrencyCode,
        x.OwnerOrganizationUnitId, x.OwnerOrganizationUnit?.Name, x.RequestedByUserId,
        x.RequestedByUser?.DisplayName ?? "-", x.ApprovedByUserId, x.ApprovedByUser?.DisplayName,
        x.ApprovedAtUtc, x.CompletionPercent, x.LastDecisionComment, x.IsActive,
        x.Milestones.OrderBy(m => m.DueDate).ThenBy(m => m.Code).Select(Map).ToList());

    private static CapexMilestoneDto Map(CapexMilestone x) => new(
        x.Id, x.ProjectId, x.Code, x.Name, x.DueDate, x.Weight, x.ProgressPercent,
        x.IsCompleted, x.CompletedAtUtc, x.Note);

    private static string NormalizeCode(string? value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9' || ch is '_' or '-' or '.')))
            throw new ArgumentException("CAPEX code must contain 2-64 ASCII letters, numbers, underscore, dash or dot characters.");
        return code;
    }

    private static string NormalizeRequired(string? value, string field, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length is < 2 || text.Length > maxLength) throw new ArgumentException($"{field} is required and must be at most {maxLength} characters.");
        return text;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > maxLength) throw new ArgumentException($"Text must be at most {maxLength} characters.");
        return text;
    }

    private static void ValidateDates(DateTime start, DateTime end)
    {
        if (start.Date > end.Date) throw new ArgumentException("CAPEX project start date cannot be after its end date.");
    }

    private static void ValidateMoney(decimal? value, string field)
    {
        if (value.HasValue && value.Value < 0m) throw new ArgumentException($"{field} cannot be negative.");
    }

    private static void ValidatePercent(decimal value, string field)
    {
        if (value is < 0m or > 100m) throw new ArgumentException($"{field} must be between 0 and 100.");
    }

    private sealed class KindSummary
    {
        public decimal Budget { get; set; }
        public decimal Actual { get; set; }
        public decimal Commitment { get; set; }
        public decimal Forecast { get; set; }
    }
}
