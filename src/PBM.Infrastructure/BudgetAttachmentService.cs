using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class BudgetAttachmentService(PbmDbContext db, IUserContext user) : IBudgetAttachmentService
{
    private const int MaxAttachmentBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".png", ".jpg", ".jpeg", ".zip"
    };

    public async Task<IReadOnlyList<BudgetAttachmentDto>> GetAttachmentsAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        await EnsureVersionReadAsync(versionId, cancellationToken);
        return await db.BudgetAttachments.AsNoTracking()
            .Where(x => x.VersionId == versionId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new BudgetAttachmentDto(
                x.Id,
                x.VersionId,
                x.CommentId,
                x.UploadedByUserId,
                x.UploadedByUser!.DisplayName,
                x.FileName,
                x.ContentType,
                x.Length,
                x.Sha256,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<BudgetAttachmentDto> UploadAsync(
        Guid versionId,
        Guid? commentId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        if (user.UserId == Guid.Empty) throw new UnauthorizedAccessException("Authenticated user is required for attachments.");
        var companyId = await EnsureVersionWriteAsync(versionId, cancellationToken);
        if (content is null || content.Length == 0) throw new ArgumentException("Attachment content is required.");
        if (content.Length > MaxAttachmentBytes) throw new ArgumentException("Attachment is larger than the 10 MB limit.");

        var safeFileName = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (safeFileName.Length is < 1 or > 240) throw new ArgumentException("Attachment file name is invalid or too long.");
        var extension = Path.GetExtension(safeFileName);
        if (!AllowedExtensions.Contains(extension)) throw new ArgumentException("This attachment file type is not allowed.");
        var safeContentType = NormalizeContentType(contentType);

        if (commentId.HasValue)
        {
            var validComment = await db.BudgetComments.AsNoTracking().AnyAsync(x => x.Id == commentId.Value && x.VersionId == versionId, cancellationToken);
            if (!validComment) throw new ArgumentException("Attachment comment does not belong to the selected budget version.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(content));
        var existing = await db.BudgetAttachments.AsNoTracking()
            .Where(x => x.VersionId == versionId && x.CommentId == commentId && x.Sha256 == hash && x.FileName == safeFileName)
            .Select(x => new BudgetAttachmentDto(
                x.Id,
                x.VersionId,
                x.CommentId,
                x.UploadedByUserId,
                x.UploadedByUser!.DisplayName,
                x.FileName,
                x.ContentType,
                x.Length,
                x.Sha256,
                x.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) return existing;

        var attachment = new BudgetAttachment
        {
            VersionId = versionId,
            CommentId = commentId,
            UploadedByUserId = user.UserId,
            FileName = safeFileName,
            ContentType = safeContentType,
            Length = content.LongLength,
            Sha256 = hash,
            Content = content
        };
        db.BudgetAttachments.Add(attachment);
        AddAudit("BudgetAttachment", attachment.Id, "CREATE", new { attachment.VersionId, attachment.CommentId, attachment.FileName, attachment.ContentType, attachment.Length, attachment.Sha256, CompanyId = companyId });
        await db.SaveChangesAsync(cancellationToken);

        var displayName = await db.Users.Where(x => x.Id == user.UserId).Select(x => x.DisplayName).SingleAsync(cancellationToken);
        return new BudgetAttachmentDto(attachment.Id, attachment.VersionId, attachment.CommentId, attachment.UploadedByUserId, displayName, attachment.FileName, attachment.ContentType, attachment.Length, attachment.Sha256, attachment.CreatedAtUtc);
    }

    public async Task<BudgetAttachmentContentDto> DownloadAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await db.BudgetAttachments.AsNoTracking()
            .Where(x => x.Id == attachmentId)
            .Select(x => new
            {
                x.VersionId,
                CompanyId = x.Version!.BudgetPlan!.CompanyId,
                TenantId = x.Version.BudgetPlan.Company!.TenantId,
                x.FileName,
                x.ContentType,
                x.Content
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Attachment was not found.");

        EnsureCompanyRead(attachment.CompanyId, attachment.TenantId);
        return new BudgetAttachmentContentDto(attachment.FileName, attachment.ContentType, attachment.Content);
    }

    public async Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await db.BudgetAttachments
            .Include(x => x.Version).ThenInclude(x => x!.BudgetPlan).ThenInclude(x => x!.Company)
            .SingleOrDefaultAsync(x => x.Id == attachmentId, cancellationToken)
            ?? throw new KeyNotFoundException("Attachment was not found.");
        var plan = attachment.Version?.BudgetPlan ?? throw new InvalidOperationException("Attachment budget version is invalid.");
        var company = plan.Company ?? throw new InvalidOperationException("Attachment company is invalid.");
        EnsureCompanyWrite(plan.CompanyId, company.TenantId);
        if (attachment.UploadedByUserId != user.UserId && !user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN"))
            throw new UnauthorizedAccessException("Only the uploader or an administrator can delete this attachment.");

        db.BudgetAttachments.Remove(attachment);
        AddAudit("BudgetAttachment", attachment.Id, "DELETE", new { attachment.VersionId, attachment.CommentId, attachment.FileName, attachment.Length, attachment.Sha256 });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureVersionReadAsync(Guid versionId, CancellationToken ct)
    {
        var target = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new { x.BudgetPlan!.CompanyId, TenantId = x.BudgetPlan.Company!.TenantId })
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Budget version was not found.");
        EnsureCompanyRead(target.CompanyId, target.TenantId);
    }

    private async Task<Guid> EnsureVersionWriteAsync(Guid versionId, CancellationToken ct)
    {
        var target = await db.BudgetVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new { x.BudgetPlan!.CompanyId, TenantId = x.BudgetPlan.Company!.TenantId })
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Budget version was not found.");
        EnsureCompanyWrite(target.CompanyId, target.TenantId);
        return target.CompanyId;
    }

    private void EnsureCompanyRead(Guid companyId, Guid tenantId)
    {
        if (tenantId != user.TenantId) throw new UnauthorizedAccessException("Attachment is outside the current tenant.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private void EnsureCompanyWrite(Guid companyId, Guid tenantId)
    {
        if (tenantId != user.TenantId) throw new UnauthorizedAccessException("Attachment is outside the current tenant.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId)) throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    private void AddAudit(string entityType, Guid entityId, string action, object value) => db.AuditLogs.Add(new AuditLog
    {
        TenantId = user.TenantId,
        UserId = user.UserId == Guid.Empty ? null : user.UserId,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        Action = action,
        NewValueJson = JsonSerializer.Serialize(value)
    });

    private static string NormalizeContentType(string? contentType)
    {
        var value = contentType?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || value.Any(char.IsControl)) return "application/octet-stream";
        return value;
    }
}
