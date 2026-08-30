using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class MasterDataService(PbmDbContext db, IUserContext user) : IMasterDataService
{
    private static readonly string[] PreferredDimensionOrder =
    [
        "PRODUCT", "BRAND", "UOM", "SUPPLIER", "COUNTRY", "GEOGRAPHY", "CURRENCY", "WAREHOUSE", "CUSTOMS",
        "CUSTOMER", "CONTRACT", "REGION", "DEPARTMENT", "COSTCENTER", "ACCOUNT", "PROGRAM", "ACTIVITY",
        "PROJECT", "FUNDINGSOURCE", "EXPENSECLASS", "EXPENSEITEM", "PURCHASECOST"
    ];

    public async Task<IReadOnlyList<MasterDataDimensionDto>> GetDimensionsAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.Dimensions.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.IsActive)
            .Select(x => new MasterDataDimensionDto(x.Id, x.Code, x.Name, x.IsHierarchical, x.IsSystem, x.IsActive))
            .ToListAsync(cancellationToken);

        return items
            .OrderBy(x => Array.IndexOf(PreferredDimensionOrder, x.Code.ToUpperInvariant()) is var index && index >= 0 ? index : int.MaxValue)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<MasterDataMemberDto>> GetMembersAsync(
        Guid dimensionId,
        Guid? companyId,
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureDimensionAsync(dimensionId, cancellationToken);
        if (companyId.HasValue) await EnsureCompanyReadAsync(companyId.Value, cancellationToken);

        var query = db.DimensionMembers.AsNoTracking()
            .Where(x => x.DimensionId == dimensionId && (x.CompanyId == null || x.CompanyId == companyId));
        if (!includeInactive) query = query.Where(x => x.IsActive);

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .Select(x => new MasterDataMemberDto(x.Id, x.DimensionId, x.ParentId, x.CompanyId, x.Code, x.Name, x.ExternalKey, x.MetadataJson, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<MasterDataMemberDto> CreateMemberAsync(CreateMasterDataMemberRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManager();
        await EnsureDimensionAsync(request.DimensionId, cancellationToken);
        await EnsureWriteScopeAsync(request.CompanyId, cancellationToken);
        await EnsureParentAsync(request.DimensionId, request.ParentId, request.CompanyId, null, cancellationToken);

        var code = NormalizeCode(request.Code);
        var name = NormalizeName(request.Name);
        var externalKey = NormalizeOptional(request.ExternalKey, 200);
        var metadataJson = NormalizeJson(request.MetadataJson);

        if (await db.DimensionMembers.AnyAsync(
                x => x.DimensionId == request.DimensionId && x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken))
            throw new InvalidOperationException("عضوی با این کد در این Dimension و محدوده شرکت قبلاً ثبت شده است.");

        var member = new DimensionMember
        {
            DimensionId = request.DimensionId,
            ParentId = request.ParentId,
            CompanyId = request.CompanyId,
            Code = code,
            Name = name,
            ExternalKey = externalKey,
            MetadataJson = metadataJson,
            IsActive = true
        };

        db.DimensionMembers.Add(member);
        AddAudit(member.Id, "CREATE", null, new
        {
            member.DimensionId, member.ParentId, member.CompanyId, member.Code, member.Name,
            member.ExternalKey, member.MetadataJson, member.IsActive
        });
        await db.SaveChangesAsync(cancellationToken);
        return Map(member);
    }

    public async Task<MasterDataMemberDto> UpdateMemberAsync(Guid memberId, UpdateMasterDataMemberRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManager();
        var member = await db.DimensionMembers
            .Include(x => x.Dimension)
            .SingleOrDefaultAsync(x => x.Id == memberId && x.Dimension!.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("عضو داده پایه پیدا نشد.");

        await EnsureWriteScopeAsync(member.CompanyId, cancellationToken);
        await EnsureParentAsync(member.DimensionId, request.ParentId, member.CompanyId, member.Id, cancellationToken);

        var old = new { member.ParentId, member.Name, member.ExternalKey, member.MetadataJson, member.IsActive };
        member.ParentId = request.ParentId;
        member.Name = NormalizeName(request.Name);
        member.ExternalKey = NormalizeOptional(request.ExternalKey, 200);
        member.MetadataJson = NormalizeJson(request.MetadataJson);
        member.IsActive = request.IsActive;
        member.UpdatedAtUtc = DateTime.UtcNow;

        AddAudit(member.Id, "UPDATE", old, new { member.ParentId, member.Name, member.ExternalKey, member.MetadataJson, member.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return Map(member);
    }

    private async Task EnsureParentAsync(Guid dimensionId, Guid? parentId, Guid? companyId, Guid? memberId, CancellationToken ct)
    {
        if (!parentId.HasValue) return;
        if (parentId == memberId) throw new ArgumentException("عضو نمی‌تواند والد خودش باشد.");

        var parent = await db.DimensionMembers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == parentId.Value && x.DimensionId == dimensionId, ct)
            ?? throw new ArgumentException("والد انتخاب‌شده باید عضو همان نوع داده پایه باشد.");
        if (companyId is null && parent.CompanyId is not null)
            throw new ArgumentException("عضو سراسری نمی‌تواند زیرمجموعه عضو مختص یک شرکت باشد.");
        if (companyId.HasValue && parent.CompanyId.HasValue && parent.CompanyId != companyId)
            throw new ArgumentException("والد انتخاب‌شده متعلق به شرکت دیگری است.");

        if (!memberId.HasValue) return;
        var cursor = parent.ParentId;
        while (cursor.HasValue)
        {
            if (cursor == memberId) throw new InvalidOperationException("این تغییر باعث ایجاد حلقه در ساختار درختی می‌شود.");
            cursor = await db.DimensionMembers.AsNoTracking().Where(x => x.Id == cursor.Value).Select(x => x.ParentId).SingleOrDefaultAsync(ct);
        }
    }

    private async Task EnsureDimensionAsync(Guid dimensionId, CancellationToken cancellationToken)
    {
        var valid = await db.Dimensions.AsNoTracking()
            .AnyAsync(x => x.Id == dimensionId && x.TenantId == user.TenantId && x.IsActive, cancellationToken);
        if (!valid) throw new KeyNotFoundException("Dimension موردنظر در Tenant جاری پیدا نشد یا غیرفعال است.");
    }

    private async Task EnsureCompanyReadAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var valid = await db.Companies.AsNoTracking()
            .AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, cancellationToken);
        if (!valid) throw new KeyNotFoundException("شرکت موردنظر پیدا نشد.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("به این شرکت دسترسی ندارید.");
    }

    private async Task EnsureWriteScopeAsync(Guid? companyId, CancellationToken cancellationToken)
    {
        if (!companyId.HasValue)
        {
            if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN")) return;
            throw new UnauthorizedAccessException("ایجاد یا تغییر داده پایه سراسری فقط برای مدیر سامانه مجاز است.");
        }

        var valid = await db.Companies.AsNoTracking()
            .AnyAsync(x => x.Id == companyId.Value && x.TenantId == user.TenantId && x.IsActive, cancellationToken);
        if (!valid) throw new KeyNotFoundException("شرکت موردنظر پیدا نشد.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId.Value))
            throw new UnauthorizedAccessException("دسترسی ثبت داده پایه برای این شرکت را ندارید.");
    }

    private void EnsureManager()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("BUDGET_MANAGER")) return;
        throw new UnauthorizedAccessException("نقش مدیر سامانه یا مدیر بودجه برای مدیریت داده‌های پایه لازم است.");
    }

    private static string NormalizeCode(string? value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 1 or > 80 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')))
            throw new ArgumentException("کد باید ۱ تا ۸۰ کاراکتر و شامل حرف، عدد، خط تیره، نقطه یا underscore باشد.");
        return code;
    }

    private static string NormalizeName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 1 or > 240) throw new ArgumentException("نام داده پایه الزامی و حداکثر ۲۴۰ کاراکتر است.");
        return name;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > maxLength) throw new ArgumentException($"مقدار اختیاری نمی‌تواند بیش از {maxLength} کاراکتر باشد.");
        return normalized;
    }

    private static string? NormalizeJson(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > 16000) throw new ArgumentException("مشخصات تکمیلی داده پایه بیش از حد مجاز است.");
        try { using var _ = JsonDocument.Parse(normalized); }
        catch (JsonException) { throw new ArgumentException("مشخصات تکمیلی باید JSON معتبر باشد."); }
        return normalized;
    }

    private void AddAudit(Guid entityId, string action, object? oldValue, object? newValue) => db.AuditLogs.Add(new AuditLog
    {
        TenantId = user.TenantId,
        UserId = user.UserId == Guid.Empty ? null : user.UserId,
        EntityType = "DimensionMember",
        EntityId = entityId.ToString(),
        Action = action,
        OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
        NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
    });

    private static MasterDataMemberDto Map(DimensionMember member) =>
        new(member.Id, member.DimensionId, member.ParentId, member.CompanyId, member.Code, member.Name, member.ExternalKey, member.MetadataJson, member.IsActive);
}
