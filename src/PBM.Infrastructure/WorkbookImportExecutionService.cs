using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class WorkbookImportExecutionService(
    PbmDbContext db,
    IUserContext user,
    IWorkbookNormalizationService normalizer) : IWorkbookImportExecutionService
{
    public async Task<WorkbookImportExecutionDto> ImportAsync(Stream stream, string fileName, WorkbookImportExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek) throw new ArgumentException("Workbook stream must be seekable.", nameof(stream));
        EnsureCompany(request.CompanyId);

        stream.Position = 0;
        var normalized = await normalizer.NormalizeAsync(stream, request.SheetName, request.Profile, cancellationToken);
        if (string.IsNullOrWhiteSpace(normalized.ModelCode)) throw new InvalidOperationException("The selected worksheet profile has no automatic target budget model.");
        if (normalized.Facts.Count == 0) throw new InvalidOperationException("The worksheet did not produce any normalized facts.");

        var company = await db.Companies.SingleAsync(x => x.Id == request.CompanyId && x.TenantId == user.TenantId, cancellationToken);
        var fiscalYear = await db.FiscalYears.Include(x => x.Periods).SingleAsync(x => x.Id == request.FiscalYearId && x.CompanyId == company.Id, cancellationToken);
        var model = await db.BudgetModels
            .Include(x => x.Dimensions).ThenInclude(x => x.Dimension)
            .Include(x => x.Measures)
            .SingleOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == normalized.ModelCode && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException($"Budget model '{normalized.ModelCode}' is not configured.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (plan, version) = await GetOrCreateEditableVersionAsync(company, fiscalYear, model, request.SheetName, cancellationToken);

        var periodMap = fiscalYear.Periods.GroupBy(x => PeriodKey(x.Name)).ToDictionary(x => x.Key, x => x.OrderBy(p => p.Sequence).First());
        var measureMap = model.Measures.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var dimensionMap = model.Dimensions.Where(x => x.Dimension is not null).ToDictionary(x => x.Dimension!.Code, x => x, StringComparer.OrdinalIgnoreCase);

        var memberCache = new Dictionary<(Guid DimensionId, string Key), DimensionMember>();
        foreach (var modelDimension in model.Dimensions.Where(x => x.Dimension is not null))
        {
            var members = await db.DimensionMembers.Where(x => x.DimensionId == modelDimension.DimensionId && x.IsActive && (x.CompanyId == null || x.CompanyId == company.Id)).ToListAsync(cancellationToken);
            foreach (var member in members)
            {
                memberCache[(member.DimensionId, $"C:{Normalize(member.Code)}")] = member;
                memberCache[(member.DimensionId, $"N:{Normalize(member.Name)}")] = member;
            }
        }

        var existingFacts = await db.BudgetFacts.Include(x => x.Dimensions).Where(x => x.VersionId == version.Id).ToListAsync(cancellationToken);
        var existingMap = existingFacts.ToDictionary(x => (x.PeriodId, x.MeasureId, x.ValueKind, x.CoordinateHash));

        var warnings = normalized.Warnings.ToList();
        var createdMembers = 0;
        var createdFacts = 0;
        var updatedFacts = 0;
        var skippedFacts = 0;

        foreach (var sourceFact in normalized.Facts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(sourceFact.PeriodName) || !periodMap.TryGetValue(PeriodKey(sourceFact.PeriodName), out var period))
            {
                skippedFacts++;
                AddWarning(warnings, $"ردیف {sourceFact.SourceRow}: دوره '{sourceFact.PeriodName ?? "-"}' در سال مالی انتخاب‌شده پیدا نشد.");
                continue;
            }
            if (!measureMap.TryGetValue(sourceFact.MeasureCode, out var measure))
            {
                skippedFacts++;
                AddWarning(warnings, $"ردیف {sourceFact.SourceRow}: مژر '{sourceFact.MeasureCode}' در مدل {model.Code} تعریف نشده است.");
                continue;
            }

            var selections = new List<DimensionSelection>();
            var invalid = false;
            foreach (var pair in sourceFact.DimensionMembers)
            {
                if (!dimensionMap.TryGetValue(pair.Key, out var modelDimension)) continue;
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    if (modelDimension.IsRequired) invalid = true;
                    continue;
                }

                var (member, created) = ResolveOrCreateMember(modelDimension.Dimension!, company.Id, pair.Value, memberCache);
                if (created) createdMembers++;
                selections.Add(new DimensionSelection(modelDimension.DimensionId, member.Id));
            }

            foreach (var required in model.Dimensions.Where(x => x.IsRequired))
                if (selections.All(x => x.DimensionId != required.DimensionId)) invalid = true;

            if (invalid)
            {
                skippedFacts++;
                AddWarning(warnings, $"ردیف {sourceFact.SourceRow}: یک یا چند بُعد اجباری مقدار ندارد.");
                continue;
            }

            var hash = BudgetCoordinateKey.Create(selections);
            var valueKind = request.OverrideValueKind ?? sourceFact.ValueKind;
            var key = (period.Id, measure.Id, valueKind, hash);
            var coordinatesJson = JsonSerializer.Serialize(selections.OrderBy(x => x.DimensionId));
            if (existingMap.TryGetValue(key, out var fact))
            {
                fact.Value = sourceFact.Value;
                fact.CurrencyCode = sourceFact.Unit == "IRR" ? "IRR" : fact.CurrencyCode;
                fact.Source = $"Excel:{fileName}/{request.SheetName}";
                fact.Note = sourceFact.SourceLabel;
                fact.CoordinatesJson = coordinatesJson;
                fact.UpdatedAtUtc = DateTime.UtcNow;
                updatedFacts++;
            }
            else
            {
                fact = new BudgetFact
                {
                    VersionId = version.Id,
                    PeriodId = period.Id,
                    MeasureId = measure.Id,
                    ValueKind = valueKind,
                    Value = sourceFact.Value,
                    CurrencyCode = sourceFact.Unit == "IRR" ? "IRR" : null,
                    CoordinateHash = hash,
                    CoordinatesJson = coordinatesJson,
                    Source = $"Excel:{fileName}/{request.SheetName}",
                    Note = sourceFact.SourceLabel
                };
                foreach (var selection in selections)
                    fact.Dimensions.Add(new BudgetFactDimension { BudgetFactId = fact.Id, DimensionId = selection.DimensionId, MemberId = selection.MemberId });
                db.BudgetFacts.Add(fact);
                existingMap[key] = fact;
                createdFacts++;
            }
        }

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "WorkbookImport",
            EntityId = version.Id.ToString(),
            Action = "IMPORT",
            NewValueJson = JsonSerializer.Serialize(new
            {
                fileName,
                request.SheetName,
                request.Profile,
                normalized.ModelCode,
                CreatedFacts = createdFacts,
                UpdatedFacts = updatedFacts,
                CreatedMembers = createdMembers,
                SkippedFacts = skippedFacts
            })
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WorkbookImportExecutionDto(request.SheetName, model.Code, plan.Id, version.Id, createdFacts, updatedFacts, createdMembers, skippedFacts, warnings);
    }

    private async Task<(BudgetPlan Plan, BudgetVersion Version)> GetOrCreateEditableVersionAsync(Company company, FiscalYear fiscalYear, BudgetModel model, string sheetName, CancellationToken ct)
    {
        var plan = await db.BudgetPlans.Include(x => x.Versions)
            .Where(x => x.CompanyId == company.Id && x.FiscalYearId == fiscalYear.Id && x.BudgetModelId == model.Id)
            .OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);

        if (plan is null)
        {
            var scenario = await db.BudgetScenarios.FirstAsync(x => x.TenantId == company.TenantId && x.Code == "BASE" && x.IsActive, ct);
            plan = new BudgetPlan { CompanyId = company.Id, FiscalYearId = fiscalYear.Id, BudgetModelId = model.Id, Name = $"{model.Name} - {fiscalYear.Name}" };
            var version = new BudgetVersion { BudgetPlanId = plan.Id, ScenarioId = scenario.Id, VersionNumber = 1, Name = "نسخه اولیه واردات اکسل", Status = BudgetStatus.Draft };
            plan.Versions.Add(version);
            db.BudgetPlans.Add(plan);
            return (plan, version);
        }

        var editable = plan.Versions.Where(x => !x.IsLocked && x.Status == BudgetStatus.Draft).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (editable is null)
            throw new InvalidOperationException($"برنامه '{plan.Name}' نسخه پیش‌نویس قابل ویرایش ندارد. ابتدا یک اصلاحیه جدید ایجاد کنید.");
        return (plan, editable);
    }

    private static (DimensionMember Member, bool Created) ResolveOrCreateMember(DimensionDefinition dimension, Guid companyId, string sourceValue, IDictionary<(Guid DimensionId, string Key), DimensionMember> cache)
    {
        var normalized = Normalize(sourceValue);
        if (cache.TryGetValue((dimension.Id, $"C:{normalized}"), out var byCode)) return (byCode, false);
        if (cache.TryGetValue((dimension.Id, $"N:{normalized}"), out var byName)) return (byName, false);

        var code = LooksLikeCode(sourceValue) ? sourceValue.Trim().ToUpperInvariant() : $"IMP-{dimension.Code}-{ShortHash(normalized)}";
        if (cache.TryGetValue((dimension.Id, $"C:{Normalize(code)}"), out var existingCode)) return (existingCode, false);
        var member = new DimensionMember { DimensionId = dimension.Id, CompanyId = companyId, Code = code, Name = sourceValue.Trim(), ExternalKey = sourceValue.Trim() };
        cache[(dimension.Id, $"C:{Normalize(code)}")] = member;
        cache[(dimension.Id, $"N:{normalized}")] = member;
        return (member, true);
    }

    private static bool LooksLikeCode(string value) => value.Length <= 50 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.');
    private static string ShortHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
    private static string Normalize(string? value) => (value ?? "").Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ').Replace(" ", "").Trim().ToUpperInvariant();
    private static string PeriodKey(string? value) => Normalize(value).Replace("ماه", "");
    private static void AddWarning(ICollection<string> warnings, string text) { if (warnings.Count < 100 && !warnings.Contains(text)) warnings.Add(text); }
    private void EnsureCompany(Guid companyId) { if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company."); }
}
