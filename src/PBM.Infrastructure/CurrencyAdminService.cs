using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class CurrencyAdminService(PbmDbContext db, IUserContext user) : ICurrencyAdminService
{
    public async Task<CurrencyDto> SaveAsync(SaveCurrencyRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManager();
        var code = NormalizeCode(request.Code);
        var name = NormalizeName(request.Name);
        var symbol = NormalizeOptional(request.Symbol, 20);

        CurrencyDefinition currency;
        string action;
        object? oldValue = null;
        if (request.Id.HasValue)
        {
            currency = await db.Currencies.SingleOrDefaultAsync(x => x.Id == request.Id.Value && x.TenantId == user.TenantId, cancellationToken)
                ?? throw new KeyNotFoundException("ارز موردنظر پیدا نشد.");
            if (!string.Equals(currency.Code, code, StringComparison.OrdinalIgnoreCase)
                && await db.Currencies.AnyAsync(x => x.TenantId == user.TenantId && x.Code == code && x.Id != currency.Id, cancellationToken))
                throw new InvalidOperationException("ارزی با این کد قبلاً تعریف شده است.");
            if (currency.IsBaseCurrency && !request.IsActive)
                throw new InvalidOperationException("ارز پایه را نمی‌توان غیرفعال کرد. ابتدا ارز پایه دیگری تعیین کنید.");
            oldValue = new { currency.Code, currency.Name, currency.Symbol, currency.IsBaseCurrency, currency.IsActive };
            currency.Code = code;
            currency.Name = name;
            currency.Symbol = symbol;
            currency.IsBaseCurrency = request.IsBaseCurrency;
            currency.IsActive = request.IsActive;
            currency.UpdatedAtUtc = DateTime.UtcNow;
            action = "UPDATE";
        }
        else
        {
            if (await db.Currencies.AnyAsync(x => x.TenantId == user.TenantId && x.Code == code, cancellationToken))
                throw new InvalidOperationException("ارزی با این کد قبلاً تعریف شده است.");
            currency = new CurrencyDefinition
            {
                TenantId = user.TenantId,
                Code = code,
                Name = name,
                Symbol = symbol,
                IsBaseCurrency = request.IsBaseCurrency,
                IsActive = request.IsActive
            };
            db.Currencies.Add(currency);
            action = "CREATE";
        }

        if (currency.IsBaseCurrency)
        {
            var otherBaseCurrencies = await db.Currencies
                .Where(x => x.TenantId == user.TenantId && x.Id != currency.Id && x.IsBaseCurrency)
                .ToListAsync(cancellationToken);
            foreach (var item in otherBaseCurrencies)
            {
                item.IsBaseCurrency = false;
                item.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await SyncDimensionMemberAsync(currency, cancellationToken);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "CurrencyDefinition",
            EntityId = currency.Id.ToString(),
            Action = action,
            OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson = JsonSerializer.Serialize(new { currency.Code, currency.Name, currency.Symbol, currency.IsBaseCurrency, currency.IsActive })
        });
        await db.SaveChangesAsync(cancellationToken);
        return new CurrencyDto(currency.Id, currency.Code, currency.Name, currency.Symbol, currency.IsBaseCurrency, currency.IsActive);
    }

    private async Task SyncDimensionMemberAsync(CurrencyDefinition currency, CancellationToken ct)
    {
        var dimension = await db.Dimensions.FirstOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == "CURRENCY", ct);
        if (dimension is null) return;
        var externalKey = $"CURRENCY:{currency.Id}";
        var member = await db.DimensionMembers.FirstOrDefaultAsync(x => x.DimensionId == dimension.Id && x.CompanyId == null && x.ExternalKey == externalKey, ct)
            ?? await db.DimensionMembers.FirstOrDefaultAsync(x => x.DimensionId == dimension.Id && x.CompanyId == null && x.Code == currency.Code, ct);
        var metadata = JsonSerializer.Serialize(new { currency.Symbol, currency.IsBaseCurrency });
        if (member is null)
        {
            db.DimensionMembers.Add(new DimensionMember
            {
                DimensionId = dimension.Id,
                CompanyId = null,
                Code = currency.Code,
                Name = currency.Name,
                ExternalKey = externalKey,
                MetadataJson = metadata,
                IsActive = currency.IsActive
            });
        }
        else
        {
            member.Code = currency.Code;
            member.Name = currency.Name;
            member.ExternalKey = externalKey;
            member.MetadataJson = metadata;
            member.IsActive = currency.IsActive;
            member.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private void EnsureManager()
    {
        if (user.IsInRole("SUPERADMIN") || user.IsInRole("ADMIN") || user.IsInRole("CFO") || user.IsInRole("BUDGET_MANAGER")) return;
        throw new UnauthorizedAccessException("مدیریت ارز برای مدیر سامانه، مدیر مالی یا مدیر بودجه مجاز است.");
    }

    private static string NormalizeCode(string? value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 12 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
            throw new ArgumentException("کد ارز باید ۲ تا ۱۲ کاراکتر و شامل حرف، عدد، خط تیره یا underscore باشد.");
        return code;
    }

    private static string NormalizeName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 120) throw new ArgumentException("نام ارز الزامی و حداکثر ۱۲۰ کاراکتر است.");
        return name;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > maxLength) throw new ArgumentException($"مقدار بیش از {maxLength} کاراکتر است.");
        return normalized;
    }
}
