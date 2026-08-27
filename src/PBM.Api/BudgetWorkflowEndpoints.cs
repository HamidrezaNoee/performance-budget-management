using PBM.Application;

namespace PBM.Api;

public static class BudgetWorkflowEndpoints
{
    public static RouteGroupBuilder MapBudgetWorkflowEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/budget/versions/revision", (CreateBudgetRevisionRequest request, IBudgetWorkflowService service, CancellationToken ct) =>
            service.CreateRevisionAsync(request, ct));

        api.MapPost("/budget/versions/{versionId:guid}/status", (Guid versionId, ChangeBudgetVersionStatusRequest request, IBudgetWorkflowService service, CancellationToken ct) =>
            service.ChangeStatusAsync(versionId, request, ct));

        api.MapGet("/budget/versions/{versionId:guid}/comments", (Guid versionId, IBudgetWorkflowService service, CancellationToken ct) =>
            service.GetCommentsAsync(versionId, ct));

        api.MapPost("/budget/versions/{versionId:guid}/comments", (Guid versionId, AddBudgetCommentRequest request, IBudgetWorkflowService service, CancellationToken ct) =>
            service.AddCommentAsync(versionId, request.Text, ct));

        return api;
    }
}
