using Microsoft.EntityFrameworkCore;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class OutboxModelTests
{
    private static PbmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PbmDbContext>()
            .UseSqlServer("Server=localhost;Database=PBM_Model_Test;User Id=sa;Password=NotUsed_123!;TrustServerCertificate=True")
            .Options;
        return new PbmDbContext(options);
    }

    [Fact]
    public void Outbox_message_is_discovered_through_tenant_navigation()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(OutboxMessage));

        Assert.NotNull(entity);
        Assert.Equal(100, entity!.FindProperty(nameof(OutboxMessage.MessageType))!.GetMaxLength());
        Assert.Equal(100, entity.FindProperty(nameof(OutboxMessage.Destination))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(OutboxMessage.CorrelationId))!.GetMaxLength());
        Assert.Equal(200, entity.FindProperty(nameof(OutboxMessage.DeduplicationKey))!.GetMaxLength());
        Assert.Equal(2000, entity.FindProperty(nameof(OutboxMessage.LastError))!.GetMaxLength());
    }

    [Fact]
    public void Outbox_message_has_required_tenant_relationship_and_lock_token()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(OutboxMessage))!;
        var tenantFk = entity.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(Tenant));

        Assert.True(tenantFk.IsRequired);
        Assert.Equal([nameof(OutboxMessage.TenantId)], tenantFk.Properties.Select(x => x.Name));
        Assert.NotNull(entity.FindProperty(nameof(OutboxMessage.LockToken)));
    }

    [Fact]
    public void Default_new_message_is_pending_and_ready_for_delivery()
    {
        var before = DateTime.UtcNow;
        var message = new OutboxMessage
        {
            TenantId = Guid.NewGuid(),
            MessageType = "notification.webhook.v1",
            Destination = "notification-webhook",
            PayloadJson = "{}"
        };
        var after = DateTime.UtcNow;

        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        Assert.InRange(message.NextAttemptAtUtc, before, after);
        Assert.Null(message.LockToken);
        Assert.Null(message.CompletedAtUtc);
    }
}
