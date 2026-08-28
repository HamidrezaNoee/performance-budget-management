using Microsoft.EntityFrameworkCore;
using PBM.Domain;

namespace PBM.Infrastructure;

public static class SecuritySeedData
{
    private static readonly (string Code, string Name)[] StandardRoles =
    [
        ("SUPERADMIN", "مدیر کل سامانه"),
        ("ADMIN", "مدیر سامانه"),
        ("CEO", "مدیرعامل"),
        ("CFO", "مدیر مالی"),
        ("BUDGET_MANAGER", "مدیر بودجه"),
        ("DEPARTMENT_MANAGER", "مدیر واحد"),
        ("BUDGET_EXPERT", "کارشناس بودجه"),
        ("INTEGRATION", "حساب سرویس یکپارچه‌سازی"),
        ("AUDITOR", "حسابرس"),
        ("VIEWER", "مشاهده‌گر")
    ];

    public static async Task InitializeAsync(PbmDbContext db, CancellationToken cancellationToken = default)
    {
        var tenants = await db.Tenants.Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var tenantId in tenants)
        {
            var existing = await db.Roles.Where(x => x.TenantId == tenantId).Select(x => x.Code).ToHashSetAsync(cancellationToken);
            foreach (var (code, name) in StandardRoles)
                if (!existing.Contains(code)) db.Roles.Add(new Role { TenantId = tenantId, Code = code, Name = name });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
