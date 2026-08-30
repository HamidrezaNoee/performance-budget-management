using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class CapexFacadeService(
    CapexService inner,
    PbmDbContext db,
    IUserContext user) : ICapexService
{
    public async Task<IReadOnlyList<CapexOwnerUnitDto>> GetOwnerUnitsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureCompanyRead(companyId);
        return await db.OrganizationUnits.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new CapexOwnerUnitDto(x.Id, x.ParentId, x.Code, x.Name, x.UnitType))
            .ToListAsync(cancellationToken);
    }

    public async Task<CapexPortfolioSummaryDto> GetPortfolioAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        EnsureCompanyRead(companyId);
        var fiscalYear = await db.FiscalYears.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == fiscalYearId && x.CompanyId == companyId, cancellationToken)
            ?? throw new ArgumentException("Fiscal year does not belong to the selected company.");

        var projects = await db.CapexProjects.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TenantId == user.TenantId && x.IsActive
                && x.StartDate <= fiscalYear.EndDate && x.EndDate >= fiscalYear.StartDate)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.EndDate)
            .ToListAsync(cancellationToken);

        var projectMemberIds = projects.Select(x => x.ProjectDimensionMemberId).ToHashSet();
        var facts = new List<BudgetFact>();
        if (projects.Count > 0)
        {
            var capexModelId = await db.BudgetModels.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && x.Code == "CAPEX")
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (capexModelId.HasValue)
            {
                var measureId = await db.Measures.AsNoTracking()
                    .Where(x => x.BudgetModelId == capexModelId.Value && x.Code == "CAPEX_AMOUNT")
                    .Select(x => (Guid?)x.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                var planId = await db.BudgetPlans.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.FiscalYearId == fiscalYearId && x.BudgetModelId == capexModelId.Value)
                    .Select(x => (Guid?)x.Id)
                    .SingleOrDefaultAsync(cancellationToken);

                if (measureId.HasValue && planId.HasValue)
                {
                    var versions = await db.BudgetVersions.AsNoTracking()
                        .Where(x => x.BudgetPlanId == planId.Value && x.Status != BudgetStatus.Rejected)
                        .ToListAsync(cancellationToken);
                    var version = versions
                        .Where(x => x.Status is BudgetStatus.Approved or BudgetStatus.Closed)
                        .OrderByDescending(x => x.VersionNumber)
                        .FirstOrDefault()
                        ?? versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
                    if (version is not null)
                    {
                        facts = await db.BudgetFacts.AsNoTracking().Include(x => x.Dimensions)
                            .Where(x => x.VersionId == version.Id && x.MeasureId == measureId.Value
                                && x.Dimensions.Any(d => projectMemberIds.Contains(d.MemberId)))
                            .ToListAsync(cancellationToken);
                    }
                }
            }
        }

        var today = DateTime.UtcNow.Date;
        var projectItems = new List<CapexPortfolioProjectDto>(projects.Count);
        foreach (var project in projects)
        {
            var values = Summarize(facts.Where(x =>
                x.Dimensions.Any(d => d.MemberId == project.ProjectDimensionMemberId)
                && (string.IsNullOrWhiteSpace(x.CurrencyCode) || string.Equals(x.CurrencyCode, project.CurrencyCode, StringComparison.OrdinalIgnoreCase))));
            var overdue = project.EndDate.Date < today && project.Status is not CapexProjectStatus.Completed and not CapexProjectStatus.Cancelled;
            projectItems.Add(new CapexPortfolioProjectDto(
                project.Id, project.Code, project.Name, project.Status, project.Priority, project.CurrencyCode,
                project.RequestedBudget, project.ApprovedBudgetLimit,
                values.Budget, values.Actual, values.Commitment, values.Budget - values.Actual - values.Commitment,
                project.CompletionPercent, overdue));
        }

        var currencyTotals = projectItems
            .GroupBy(x => x.CurrencyCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key)
            .Select(group => new CapexCurrencyPortfolioDto(
                group.Key,
                group.Sum(x => x.RequestedBudget ?? 0m),
                group.Sum(x => x.ApprovedBudgetLimit ?? 0m),
                group.Sum(x => x.Budget),
                group.Sum(x => x.Actual),
                group.Sum(x => x.Commitment),
                projects.Where(p => string.Equals(p.CurrencyCode, group.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(p => Summarize(facts.Where(x => x.Dimensions.Any(d => d.MemberId == p.ProjectDimensionMemberId)
                        && x.ValueKind == ValueKind.Forecast
                        && (string.IsNullOrWhiteSpace(x.CurrencyCode) || string.Equals(x.CurrencyCode, p.CurrencyCode, StringComparison.OrdinalIgnoreCase)))).Forecast),
                group.Sum(x => x.Available)))
            .ToList();

        return new CapexPortfolioSummaryDto(
            companyId,
            fiscalYearId,
            projectItems.Count,
            projectItems.Count(x => x.Status == CapexProjectStatus.Proposed),
            projectItems.Count(x => x.Status == CapexProjectStatus.Submitted),
            projectItems.Count(x => x.Status == CapexProjectStatus.Approved),
            projectItems.Count(x => x.Status == CapexProjectStatus.InProgress),
            projectItems.Count(x => x.Status == CapexProjectStatus.OnHold),
            projectItems.Count(x => x.Status == CapexProjectStatus.Completed),
            projectItems.Count(x => x.Status == CapexProjectStatus.Cancelled),
            projectItems.Count(x => x.IsOverdue),
            currencyTotals,
            projectItems.OrderByDescending(x => x.IsOverdue).ThenByDescending(x => x.Priority).ThenBy(x => x.Name).ToList());
    }

    public Task<IReadOnlyList<CapexProjectDto>> GetProjectsAsync(Guid companyId, CapexProjectStatus? status = null, CancellationToken cancellationToken = default) =>
        inner.GetProjectsAsync(companyId, status, cancellationToken);

    public Task<CapexProjectDto> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        inner.GetProjectAsync(projectId, cancellationToken);

    public Task<CapexProjectDto> CreateProjectAsync(CreateCapexProjectRequest request, CancellationToken cancellationToken = default) =>
        inner.CreateProjectAsync(request, cancellationToken);

    public async Task<CapexProjectDto> UpdateProjectAsync(Guid projectId, UpdateCapexProjectRequest request, CancellationToken cancellationToken = default)
    {
        var state = await db.CapexProjects.AsNoTracking()
            .Where(x => x.Id == projectId && x.TenantId == user.TenantId)
            .Select(x => new { x.CompanyId, x.CompletionPercent, HasMilestones = x.Milestones.Any() })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("CAPEX project was not found.");

        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(state.CompanyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
        if (state.HasMilestones && request.CompletionPercent != state.CompletionPercent)
            throw new InvalidOperationException("CAPEX completion is milestone-driven and cannot be edited manually while milestones exist.");

        return await inner.UpdateProjectAsync(projectId, request, cancellationToken);
    }

    public async Task<CapexProjectDto> ChangeStatusAsync(Guid projectId, ChangeCapexProjectStatusRequest request, CancellationToken cancellationToken = default)
    {
        var current = await db.CapexProjects.AsNoTracking()
            .Where(x => x.Id == projectId && x.TenantId == user.TenantId)
            .Select(x => new { x.Status, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("CAPEX project was not found.");

        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(current.CompanyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");

        var requiresDecisionComment = request.Status == CapexProjectStatus.Cancelled
            || (current.Status == CapexProjectStatus.Submitted && request.Status == CapexProjectStatus.Proposed);
        if (requiresDecisionComment && string.IsNullOrWhiteSpace(request.Comment))
            throw new ArgumentException("A decision comment is required for cancellation or return for correction.");

        return await inner.ChangeStatusAsync(projectId, request, cancellationToken);
    }

    public Task<CapexMilestoneDto> UpsertMilestoneAsync(Guid projectId, UpsertCapexMilestoneRequest request, CancellationToken cancellationToken = default) =>
        inner.UpsertMilestoneAsync(projectId, request, cancellationToken);

    public Task DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken = default) =>
        inner.DeleteMilestoneAsync(projectId, milestoneId, cancellationToken);

    public Task<CapexFinancialSummaryDto> GetFinancialSummaryAsync(Guid projectId, Guid fiscalYearId, CancellationToken cancellationToken = default) =>
        inner.GetFinancialSummaryAsync(projectId, fiscalYearId, cancellationToken);

    private void EnsureCompanyRead(Guid companyId)
    {
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private static FinancialValues Summarize(IEnumerable<BudgetFact> facts)
    {
        var result = new FinancialValues();
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

    private sealed class FinancialValues
    {
        public decimal Budget { get; set; }
        public decimal Actual { get; set; }
        public decimal Commitment { get; set; }
        public decimal Forecast { get; set; }
    }
}
