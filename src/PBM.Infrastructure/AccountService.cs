using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class AccountService(
    PbmDbContext db,
    IUserContext currentUser,
    IPasswordHasher<AppUser> passwordHasher) : IAccountService
{
    public async Task ChangePasswordAsync(ChangeOwnPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId == Guid.Empty) throw new UnauthorizedAccessException("Authenticated user is required.");
        if (string.IsNullOrEmpty(request.CurrentPassword)) throw new ArgumentException("Current password is required.");
        ValidatePassword(request.NewPassword);
        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
            throw new ArgumentException("New password must be different from the current password.");

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == currentUser.UserId && x.TenantId == currentUser.TenantId && x.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authenticated user account is not active or no longer exists.");

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException("Current password is incorrect.");

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = currentUser.TenantId,
            UserId = currentUser.UserId,
            EntityType = "AppUser",
            EntityId = user.Id.ToString(),
            Action = "PASSWORD_CHANGE",
            NewValueJson = JsonSerializer.Serialize(new { ChangedBy = currentUser.UserId, ChangedAtUtc = DateTime.UtcNow })
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePassword(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length < 12 || !text.Any(char.IsUpper) || !text.Any(char.IsLower) || !text.Any(char.IsDigit))
            throw new ArgumentException("New password must be at least 12 characters and include uppercase, lowercase and a number.");
    }
}
