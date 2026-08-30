using PBM.Application;

namespace PBM.Api;

public static class BudgetAttachmentEndpoints
{
    private const long MaxAttachmentBytes = 10 * 1024 * 1024;

    public static RouteGroupBuilder MapBudgetAttachmentEndpoints(this RouteGroupBuilder api)
    {
        var attachments = api.MapGroup("/budget");

        attachments.MapGet("/versions/{versionId:guid}/attachments", (Guid versionId, IBudgetAttachmentService service, CancellationToken ct) =>
            service.GetAttachmentsAsync(versionId, ct));

        attachments.MapPost("/versions/{versionId:guid}/attachments", async (
            Guid versionId,
            Guid? commentId,
            IFormFile file,
            IBudgetAttachmentService service,
            CancellationToken ct) =>
        {
            if (file.Length == 0) return Results.BadRequest(new { message = "Attachment file is empty." });
            if (file.Length > MaxAttachmentBytes) return Results.BadRequest(new { message = "Attachment is larger than the 10 MB limit." });

            await using var input = file.OpenReadStream();
            using var memory = new MemoryStream((int)file.Length);
            await input.CopyToAsync(memory, ct);
            var result = await service.UploadAsync(versionId, commentId, file.FileName, file.ContentType, memory.ToArray(), ct);
            return Results.Ok(result);
        }).DisableAntiforgery();

        attachments.MapGet("/attachments/{attachmentId:guid}/content", async (Guid attachmentId, IBudgetAttachmentService service, CancellationToken ct) =>
        {
            var result = await service.DownloadAsync(attachmentId, ct);
            return Results.File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true);
        });

        attachments.MapDelete("/attachments/{attachmentId:guid}", async (Guid attachmentId, IBudgetAttachmentService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(attachmentId, ct);
            return Results.NoContent();
        });

        return api;
    }
}
