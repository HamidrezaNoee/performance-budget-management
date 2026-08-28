using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class OrganizationAdminService(PbmDbContext db, IUserContext user) : IOrganizationAdminService
{
    public async Task<IReadOnlyList<AdminCompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        return await db.Companies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
            .Select(x => new AdminCompanyDto(x.Id, x.Code, x.Name, x.Industry, x.IsActive, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminCompanyDto> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var code = NormalizeCode(request.Code, "Company code");
        var name = NormalizeName(request.Name, "Company name");
        if (await db.Companies.AnyAsync(x => x.TenantId == user.TenantId && x.Code == code, cancellationToken))
            throw new InvalidOperationException("A company with this code already exists.");

        var license = await db.LicenseSubscriptions.AsNoTracking().Where(x => x.TenantId == user.TenantId)
            .OrderByDescending(x => x.ExpiresAtUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No license is configured for this tenant.");
        if (!license.IsActive || license.StartsAtUtc > DateTime.UtcNow || license.ExpiresAtUtc < DateTime.UtcNow)
            throw new InvalidOperationException("The tenant license is not active.");
        var activeCompanies = await db.Companies.CountAsync(x => x.TenantId == user.TenantId && x.IsActive, cancellationToken);
        if (activeCompanies >= license.MaxCompanies)
            throw new InvalidOperationException($"The license allows a maximum of {license.MaxCompanies} active companies.");

        var company = new Company { TenantId = user.TenantId, Code = code, Name = name, Industry = NormalizeOptional(request.Industry), IsActive = true };
        db.Companies.Add(company);
        if (user.UserId != Guid.Empty && !await db.UserCompanyAccess.AnyAsync(x => x.UserId == user.UserId && x.CompanyId == company.Id, cancellationToken))
            db.UserCompanyAccess.Add(new UserCompanyAccess { UserId = user.UserId, CompanyId = company.Id, CanRead = true, CanWrite = true });
        AddAudit("Company", company.Id, "CREATE", null, new { company.Code, company.Name, company.Industry, company.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return Map(company);
    }

    public async Task<AdminCompanyDto> UpdateCompanyAsync(Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Company was not found.");
        var name = NormalizeName(request.Name, "Company name");
        if (!company.IsActive && request.IsActive)
        {
            var license = await db.LicenseSubscriptions.AsNoTracking().Where(x => x.TenantId == user.TenantId).OrderByDescending(x => x.ExpiresAtUtc).FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("No license is configured for this tenant.");
            var activeCompanies = await db.Companies.CountAsync(x => x.TenantId == user.TenantId && x.IsActive, cancellationToken);
            if (!license.IsActive || license.ExpiresAtUtc < DateTime.UtcNow || activeCompanies >= license.MaxCompanies)
                throw new InvalidOperationException("The company cannot be activated because the tenant license limit is reached or inactive.");
        }

        var old = new { company.Name, company.Industry, company.IsActive };
        company.Name = name;
        company.Industry = NormalizeOptional(request.Industry);
        company.IsActive = request.IsActive;
        company.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("Company", company.Id, "UPDATE", old, new { company.Name, company.Industry, company.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return Map(company);
    }

    public async Task<IReadOnlyList<OrganizationUnitDto>> GetUnitsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await EnsureTenantCompanyAsync(companyId, cancellationToken);
        return await db.OrganizationUnits.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
            .Select(x => new OrganizationUnitDto(x.Id, x.CompanyId, x.ParentId, x.Code, x.Name, x.UnitType, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationUnitDto> CreateUnitAsync(CreateOrganizationUnitRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await EnsureTenantCompanyAsync(request.CompanyId, cancellationToken);
        var code = NormalizeCode(request.Code, "Organization unit code");
        var name = NormalizeName(request.Name, "Organization unit name");
        var unitType = NormalizeUnitType(request.UnitType);
        if (await db.OrganizationUnits.AnyAsync(x => x.CompanyId == request.CompanyId && x.Code == code, cancellationToken))
            throw new InvalidOperationException("An organization unit with this code already exists in the company.");
        if (request.ParentId.HasValue && !await db.OrganizationUnits.AnyAsync(x => x.Id == request.ParentId && x.CompanyId == request.CompanyId, cancellationToken))
            throw new ArgumentException("Parent organization unit is invalid.");

        var unit = new OrganizationUnit { CompanyId = request.CompanyId, ParentId = request.ParentId, Code = code, Name = name, UnitType = unitType };
        db.OrganizationUnits.Add(unit);
        await SyncDepartmentDimensionAsync(unit, cancellationToken);
        AddAudit("OrganizationUnit", unit.Id, "CREATE", null, new { unit.CompanyId, unit.ParentId, unit.Code, unit.Name, unit.UnitType });
        await db.SaveChangesAsync(cancellationToken);
        return Map(unit);
    }

    public async Task<OrganizationUnitDto> UpdateUnitAsync(Guid unitId, UpdateOrganizationUnitRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var unit = await db.OrganizationUnits.SingleOrDefaultAsync(x => x.Id == unitId && x.Company!.TenantId == user.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Organization unit was not found.");
        if (request.ParentId == unit.Id) throw new ArgumentException("An organization unit cannot be its own parent.");
        if (request.ParentId.HasValue)
        {
            var parent = await db.OrganizationUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ParentId && x.CompanyId == unit.CompanyId, cancellationToken)
                ?? throw new ArgumentException("Parent organization unit is invalid.");
            if (await WouldCreateCycleAsync(unit.Id, parent.Id, cancellationToken)) throw new InvalidOperationException("The selected parent would create an organization hierarchy cycle.");
        }

        var old = new { unit.ParentId, unit.Name, unit.UnitType, unit.IsActive };
        unit.ParentId = request.ParentId;
        unit.Name = NormalizeName(request.Name, "Organization unit name");
        unit.UnitType = NormalizeUnitType(request.UnitType);
        unit.IsActive = request.IsActive;
        unit.UpdatedAtUtc = DateTime.UtcNow;
        await SyncDepartmentDimensionAsync(unit, cancellationToken);
        AddAudit("OrganizationUnit", unit.Id, "UPDATE", old, new { unit.ParentId, unit.Name, unit.UnitType, unit.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return Map(unit);
    }

    private async Task SyncDepartmentDimensionAsync(OrganizationUnit unit, CancellationToken ct)
    {
        var tenantId = await db.Companies.Where(x => x.Id == unit.CompanyId).Select(x => x.TenantId).SingleAsync(ct);
        var dimension = await db.Dimensions.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "DEPARTMENT", ct);
        if (dimension is null) return;
        var externalKey = $"ORG:{unit.Id}";
        var member = await db.DimensionMembers.FirstOrDefaultAsync(x => x.DimensionId == dimension.Id && x.CompanyId == unit.CompanyId && x.ExternalKey == externalKey, ct);
        Guid? parentMemberId = null;
        if (unit.ParentId.HasValue)
            parentMemberId = await db.DimensionMembers.Where(x => x.DimensionId == dimension.Id && x.CompanyId == unit.CompanyId && x.ExternalKey == $"ORG:{unit.ParentId.Value}").Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);

        if (member is null)
        {
            member = new DimensionMember { DimensionId = dimension.Id, CompanyId = unit.CompanyId, ParentId = parentMemberId, Code = unit.Code, Name = unit.Name, ExternalKey = externalKey, IsActive = unit.IsActive };
            db.DimensionMembers.Add(member);
        }
        else
        {
            member.ParentId = parentMemberId;
            member.Name = unit.Name;
            member.IsActive = unit.IsActive;
            member.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task<bool> WouldCreateCycleAsync(Guid unitId, Guid proposedParentId, CancellationToken ct)
    {
        var cursor = (Guid?)proposedParentId;
        var guard = 0;
        while (cursor.HasValue && guard++ < 100)
        {
            if (cursor.Value == unitId) return true;
            cursor = await db.OrganizationUnits.AsNoTracking().Where(x => x.Id == cursor.Value).Select(x => x.ParentId).FirstOrDefaultAsync(ct);
        }
        return guard >= 100;
    }

    private async Task EnsureTenantCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId, ct))
            throw new UnauthorizedAccessException("Company is outside the current tenant.");
    }

    private void EnsureAdmin()
    {
        if (!user.IsInRole("SUPERADMIN") && !user.IsInRole("ADMIN"))
            throw new UnauthorizedAccessException("Administrator role is required.");
    }

    private static string NormalizeCode(string? value, string field)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')))
            throw new ArgumentException($"{field} must contain 2-64 letters, numbers, underscore, dash or dot characters.");
        return code;
    }

    private static string NormalizeName(string? value, string field)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 200) throw new ArgumentException($"{field} is required and must be at most 200 characters.");
        return name;
    }

    private static string NormalizeUnitType(string? value)
    {
        var type = string.IsNullOrWhiteSpace(value) ? "Department" : value.Trim();
        return type.Length <= 64 ? type : throw new ArgumentException("Unit type must be at most 64 characters.");
    }

    private void AddAudit(string entityType, Guid entityId, string action, object? oldValue, object? newValue) => db.AuditLogs.Add(new AuditLog
    {
        TenantId = user.TenantId,
        UserId = user.UserId == Guid.Empty ? null : user.UserId,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        Action = action,
        OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
        NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue)
    });

    private static AdminCompanyDto Map(Company x) => new(x.Id, x.Code, x.Name, x.Industry, x.IsActive, x.CreatedAtUtc);
    private static OrganizationUnitDto Map(OrganizationUnit x) => new(x.Id, x.CompanyId, x.ParentId, x.Code, x.Name, x.UnitType, x.IsActive);
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
