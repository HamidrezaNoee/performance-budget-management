namespace PBM.Domain;

public sealed class BudgetAttachment : Entity
{
    public Guid VersionId { get; set; }
    public Guid? CommentId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Length { get; set; }
    public required string Sha256 { get; set; }
    public required byte[] Content { get; set; }
    public BudgetVersion? Version { get; set; }
    public BudgetComment? Comment { get; set; }
    public AppUser? UploadedByUser { get; set; }
}
