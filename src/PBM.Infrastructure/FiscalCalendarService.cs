using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class FiscalCalendarService(PbmDbContext db, IUserContext user) : IFiscalCalendarService
{
    private static readonly PersianCalendar Persian = new();
    private static readonly string[] MonthNames =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    public async Task<IReadOnlyList<FiscalYearDetailsDto>> GetYearsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(companyId, cancellationToken);
        var years = await db.FiscalYears.AsNoTracking().Include(x => x.Periods)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken);
        return years.Select(ToDto).ToList();
    }

    public async Task<FiscalYearDetailsDto> CreateYearAsync(CreateFiscalYearRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, cancellationToken);
        ValidateYearRequest(request);
        var code = request.Code.Trim();
        if (await db.FiscalYears.AnyAsync(x => x.CompanyId == request.CompanyId && x.Code == code, cancellationToken))
            throw new InvalidOperationException($"سال مالی با کد '{code}' قبلاً تعریف شده است.");

        var periods = new List<FiscalPeriod>(request.MonthCount);
        for (var sequence = 1; sequence <= request.MonthCount; sequence++)
        {
            var (year, month) = AddPersianMonths(request.JalaliYear, request.StartJalaliMonth, sequence - 1);
            var start = Persian.ToDateTime(year, month, 1, 0, 0, 0, 0);
            var (nextYear, nextMonth) = AddPersianMonths(year, month, 1);
            var end = Persian.ToDateTime(nextYear, nextMonth, 1, 0, 0, 0, 0).AddDays(-1);
            periods.Add(new FiscalPeriod
            {
                Sequence = sequence,
                Code = $"P{sequence:00}",
                Name = MonthNames[month - 1],
                JalaliMonth = month,
                StartDate = start,
                EndDate = end
            });
        }

        var fiscalYear = new FiscalYear
        {
            CompanyId = request.CompanyId,
            Code = code,
            Name = request.Name.Trim(),
            JalaliYear = request.JalaliYear,
            StartDate = periods[0].StartDate,
            EndDate = periods[^1].EndDate
        };
        foreach (var period in periods)
        {
            period.FiscalYearId = fiscalYear.Id;
            fiscalYear.Periods.Add(period);
        }

        db.FiscalYears.Add(fiscalYear);
        AddAudit("FiscalYear", fiscalYear.Id, "CREATE", new
        {
            fiscalYear.CompanyId,
            fiscalYear.Code,
            fiscalYear.Name,
            fiscalYear.JalaliYear,
            request.StartJalaliMonth,
            request.MonthCount,
            fiscalYear.StartDate,
            fiscalYear.EndDate
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(fiscalYear);
    }

    public async Task<FiscalPeriodDto> AddPeriodAsync(Guid fiscalYearId, CreateFiscalPeriodRequest request, CancellationToken cancellationToken = default)
    {
        var fiscalYear = await db.FiscalYears.Include(x => x.Periods).SingleAsync(x => x.Id == fiscalYearId, cancellationToken);
        await EnsureCompanyAsync(fiscalYear.CompanyId, cancellationToken);
        if (fiscalYear.IsClosed) throw new InvalidOperationException("سال مالی بسته است و امکان افزودن دوره وجود ندارد.");
        if (request.Sequence <= 0) throw new ArgumentException("Sequence must be greater than zero.");
        if (request.JalaliMonth is < 1 or > 12) throw new ArgumentException("Jalali month must be between 1 and 12.");
        if (request.StartDate.Date > request.EndDate.Date) throw new ArgumentException("Period start date cannot be after end date.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Period code and name are required.");
        if (fiscalYear.Periods.Any(x => x.Sequence == request.Sequence)) throw new InvalidOperationException("شماره ترتیب دوره تکراری است.");
        if (fiscalYear.Periods.Any(x => string.Equals(x.Code, request.Code.Trim(), StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("کد دوره تکراری است.");
        if (fiscalYear.Periods.Any(x => request.StartDate.Date <= x.EndDate.Date && request.EndDate.Date >= x.StartDate.Date)) throw new InvalidOperationException("بازه زمانی دوره با یک دوره موجود هم‌پوشانی دارد.");

        var period = new FiscalPeriod
        {
            FiscalYearId = fiscalYear.Id,
            Sequence = request.Sequence,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            JalaliMonth = request.JalaliMonth,
            StartDate = request.StartDate.Date,
            EndDate = request.EndDate.Date
        };
        db.FiscalPeriods.Add(period);
        fiscalYear.StartDate = fiscalYear.Periods.Count == 0 ? period.StartDate : Min(fiscalYear.StartDate, period.StartDate);
        fiscalYear.EndDate = fiscalYear.Periods.Count == 0 ? period.EndDate : Max(fiscalYear.EndDate, period.EndDate);
        fiscalYear.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("FiscalPeriod", period.Id, "CREATE", new { period.FiscalYearId, period.Sequence, period.Code, period.Name, period.StartDate, period.EndDate });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(period);
    }

    public async Task<FiscalPeriodDto> SetPeriodClosedAsync(Guid periodId, bool isClosed, CancellationToken cancellationToken = default)
    {
        var period = await db.FiscalPeriods.Include(x => x.FiscalYear).SingleAsync(x => x.Id == periodId, cancellationToken);
        await EnsureCompanyAsync(period.FiscalYear!.CompanyId, cancellationToken);
        var oldValue = period.IsClosed;
        period.IsClosed = isClosed;
        period.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("FiscalPeriod", period.Id, isClosed ? "CLOSE" : "REOPEN", new { IsClosed = oldValue }, new { IsClosed = isClosed });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(period);
    }

    public async Task<FiscalYearDetailsDto> SetYearClosedAsync(Guid fiscalYearId, bool isClosed, CancellationToken cancellationToken = default)
    {
        var fiscalYear = await db.FiscalYears.Include(x => x.Periods).SingleAsync(x => x.Id == fiscalYearId, cancellationToken);
        await EnsureCompanyAsync(fiscalYear.CompanyId, cancellationToken);
        var oldValue = fiscalYear.IsClosed;
        fiscalYear.IsClosed = isClosed;
        fiscalYear.UpdatedAtUtc = DateTime.UtcNow;
        foreach (var period in fiscalYear.Periods)
        {
            period.IsClosed = isClosed;
            period.UpdatedAtUtc = DateTime.UtcNow;
        }
        AddAudit("FiscalYear", fiscalYear.Id, isClosed ? "CLOSE" : "REOPEN", new { IsClosed = oldValue }, new { IsClosed = isClosed, PeriodsAffected = fiscalYear.Periods.Count });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(fiscalYear);
    }

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var exists = await db.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == user.TenantId && x.IsActive, cancellationToken);
        if (!exists) throw new UnauthorizedAccessException("Company is outside the current tenant or inactive.");
        if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    private static void ValidateYearRequest(CreateFiscalYearRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Fiscal year code and name are required.");
        if (request.JalaliYear is < 1200 or > 1600) throw new ArgumentException("Jalali year is outside the supported range.");
        if (request.StartJalaliMonth is < 1 or > 12) throw new ArgumentException("Start Jalali month must be between 1 and 12.");
        if (request.MonthCount is < 1 or > 24) throw new ArgumentException("Month count must be between 1 and 24.");
    }

    private static (int Year, int Month) AddPersianMonths(int year, int month, int offset)
    {
        var zeroBased = month - 1 + offset;
        return (year + zeroBased / 12, zeroBased % 12 + 1);
    }

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private void AddAudit(string entityType, Guid entityId, string action, object newValue) => AddAudit(entityType, entityId, action, null, newValue);
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

    private static FiscalYearDetailsDto ToDto(FiscalYear year) => new(
        year.Id, year.CompanyId, year.Code, year.Name, year.JalaliYear, year.StartDate, year.EndDate, year.IsClosed,
        year.Periods.OrderBy(x => x.Sequence).Select(ToDto).ToList());

    private static FiscalPeriodDto ToDto(FiscalPeriod period) => new(
        period.Id, period.FiscalYearId, period.Sequence, period.Code, period.Name, period.JalaliMonth,
        period.StartDate, period.EndDate, period.IsClosed);
}
