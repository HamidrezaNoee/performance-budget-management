using Microsoft.EntityFrameworkCore;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class IntegrationCredentialModelTests
{
    private static PbmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PbmDbContext>()
            .UseSqlServer("Server=localhost;Database=PBM_Model_Test;User Id=sa;Password=NotUsed_123!;TrustServerCertificate=True")
            .Options;
        return new PbmDbContext(options);
    }

    [Fact]
    public void Credential_has_safe_storage_unique_client_id_and_restricted_user_fk()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(IntegrationCredential));

        Assert.NotNull(entity);
        Assert.Equal(160, entity!.FindProperty(nameof(IntegrationCredential.Name))!.GetMaxLength());
        Assert.Equal(80, entity.FindProperty(nameof(IntegrationCredential.ClientId))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(IntegrationCredential.SecretHash))!.GetMaxLength());
        Assert.Equal(32, entity.FindProperty(nameof(IntegrationCredential.SecretSalt))!.GetMaxLength());
        Assert.Equal(500, entity.FindProperty(nameof(IntegrationCredential.RevocationReason))!.GetMaxLength());

        var clientIdIndex = entity.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual([nameof(IntegrationCredential.ClientId)]));
        Assert.True(clientIdIndex.IsUnique);

        var userFk = entity.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(AppUser));
        Assert.Equal(DeleteBehavior.Restrict, userFk.DeleteBehavior);
        Assert.Equal([nameof(IntegrationCredential.UserId)], userFk.Properties.Select(x => x.Name));
    }

    [Fact]
    public void Active_state_respects_expiry_and_revocation()
    {
        var now = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
        var credential = Credential(now.AddHours(1));
        Assert.True(credential.IsActive(now));

        credential.ExpiresAtUtc = now.AddSeconds(-1);
        Assert.False(credential.IsActive(now));

        credential.ExpiresAtUtc = now.AddHours(1);
        credential.RevokedAtUtc = now.AddMinutes(-1);
        Assert.False(credential.IsActive(now));
    }

    private static IntegrationCredential Credential(DateTime expiresAtUtc) => new()
    {
        TenantId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Name = "ERP Production",
        ClientId = "pbm_0123456789abcdef0123456789abcdef",
        SecretHash = new string('A', 44),
        SecretSalt = new string('B', 24),
        SecretIterations = 210_000,
        ExpiresAtUtc = expiresAtUtc
    };
}
