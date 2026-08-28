using PBM.Application;
using PBM.Domain;

namespace PBM.Api;

public static class CapexEndpoints
{
    public static RouteGroupBuilder MapCapexEndpoints(this RouteGroupBuilder api)
    {
        var capex = api.MapGroup("/capex");

        capex.MapGet("/owner-units", (
            Guid companyId,
            ICapexService service,
            CancellationToken ct) =>
            service.GetOwnerUnitsAsync(companyId, ct));

        capex.MapGet("/projects", (
            Guid companyId,
            CapexProjectStatus? status,
            ICapexService service,
            CancellationToken ct) =>
            service.GetProjectsAsync(companyId, status, ct));

        capex.MapGet("/projects/{projectId:guid}", (
            Guid projectId,
            ICapexService service,
            CancellationToken ct) =>
            service.GetProjectAsync(projectId, ct));

        capex.MapPost("/projects", (
            CreateCapexProjectRequest request,
            ICapexService service,
            CancellationToken ct) =>
            service.CreateProjectAsync(request, ct));

        capex.MapPut("/projects/{projectId:guid}", (
            Guid projectId,
            UpdateCapexProjectRequest request,
            ICapexService service,
            CancellationToken ct) =>
            service.UpdateProjectAsync(projectId, request, ct));

        capex.MapPost("/projects/{projectId:guid}/status", (
            Guid projectId,
            ChangeCapexProjectStatusRequest request,
            ICapexService service,
            CancellationToken ct) =>
            service.ChangeStatusAsync(projectId, request, ct));

        capex.MapPut("/projects/{projectId:guid}/milestones", (
            Guid projectId,
            UpsertCapexMilestoneRequest request,
            ICapexService service,
            CancellationToken ct) =>
            service.UpsertMilestoneAsync(projectId, request, ct));

        capex.MapDelete("/projects/{projectId:guid}/milestones/{milestoneId:guid}", async (
            Guid projectId,
            Guid milestoneId,
            ICapexService service,
            CancellationToken ct) =>
        {
            await service.DeleteMilestoneAsync(projectId, milestoneId, ct);
            return Results.NoContent();
        });

        capex.MapGet("/projects/{projectId:guid}/financial-summary", (
            Guid projectId,
            Guid fiscalYearId,
            ICapexService service,
            CancellationToken ct) =>
            service.GetFinancialSummaryAsync(projectId, fiscalYearId, ct));

        return api;
    }
}
