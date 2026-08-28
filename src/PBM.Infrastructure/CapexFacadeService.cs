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
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");

        return await db.OrganizationUnits.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new CapexOwnerUnitDto(x.Id, x.ParentId, x.Code, x.Name, x.UnitType))
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<CapexProjectDto>> GetProjectsAsync(Guid companyId, CapexProjectStatus? status = null, CancellationToken cancellationToken = default) =>
        inner.GetProjectsAsync(companyId, status, cancellationToken);

    public Task<CapexProjectDto> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        inner.GetProjectAsync(projectId, cancellationToken);

    public Task<CapexProjectDto> CreateProjectAsync(CreateCapexProjectRequest request, CancellationToken cancellationToken = default) =>
        inner.CreateProjectAsync(request, cancellationToken);

    public Task<CapexProjectDto> UpdateProjectAsync(Guid projectId, UpdateCapexProjectRequest request, CancellationToken cancellationToken = default) =>
        inner.UpdateProjectAsync(projectId, request, cancellationToken);

    public Task<CapexProjectDto> ChangeStatusAsync(Guid projectId, ChangeCapexProjectStatusRequest request, CancellationToken cancellationToken = default) =>
        inner.ChangeStatusAsync(projectId, request, cancellationToken);

    public Task<CapexMilestoneDto> UpsertMilestoneAsync(Guid projectId, UpsertCapexMilestoneRequest request, CancellationToken cancellationToken = default) =>
        inner.UpsertMilestoneAsync(projectId, request, cancellationToken);

    public Task DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken = default) =>
        inner.DeleteMilestoneAsync(projectId, milestoneId, cancellationToken);

    public Task<CapexFinancialSummaryDto> GetFinancialSummaryAsync(Guid projectId, Guid fiscalYearId, CancellationToken cancellationToken = default) =>
        inner.GetFinancialSummaryAsync(projectId, fiscalYearId, cancellationToken);
}
