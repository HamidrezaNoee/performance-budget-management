namespace PBM.Application;

public sealed record BudgetAttachmentDto(
    Guid Id,
    Guid VersionId,
    Guid? CommentId,
    Guid UploadedByUserId,
    string UploadedByDisplayName,
    string FileName,
    string ContentType,
    long Length,
    string Sha256,
    DateTime CreatedAtUtc);

public sealed record BudgetAttachmentContentDto(string FileName, string ContentType, byte[] Content);

public interface IBudgetAttachmentService
{
    Task<IReadOnlyList<BudgetAttachmentDto>> GetAttachmentsAsync(Guid versionId, CancellationToken cancellationToken = default);
    Task<BudgetAttachmentDto> UploadAsync(Guid versionId, Guid? commentId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default);
    Task<BudgetAttachmentContentDto> DownloadAsync(Guid attachmentId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
