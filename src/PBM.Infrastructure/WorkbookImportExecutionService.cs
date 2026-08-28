using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class WorkbookImportExecutionService(PbmDbContext db, IUserContext user, IWorkbookNormalizationService normalizer) : IWorkbookImportExecutionService
{
    public async Task<WorkbookImportExecutionDto> ImportAsync(Stream stream, string fileName, WorkbookImportExecutionRequest request, CancellationToken ct = default)
    {
        if (!stream.CanSeek) throw new ArgumentException("Workbook stream must be seekable.", nameof(stream));
        EnsureCompanyWrite(request.CompanyId);
        stream.Position = 0;
        var normalized = await normalizer.NormalizeAsync(stream, request.SheetName, request.Profile, ct);
        if (string.IsNullOrWhiteSpace(normalized.ModelCode)) throw new InvalidOperationException("The selected worksheet profile has no automatic target model.");
        if (normalized.Facts.Count == 0) throw new InvalidOperationException("The worksheet did not produce importable facts.");

        var company = await db.Companies.SingleAsync(x => x.Id == request.CompanyId && x.TenantId == user.TenantId, ct);
        var fiscalYear = await db.FiscalYears.Include(x => x.Periods).SingleAsync(x => x.Id == request.FiscalYearId && x.CompanyId == company.Id, ct);
        if (fiscalYear.IsClosed) throw new InvalidOperationException("سال مالی بسته است و امکان ورود اطلاعات وجود ندارد.");
        var model = await db.BudgetModels.Include(x => x.Dimensions).ThenInclude(x => x.Dimension).Include(x => x.Measures)
            .SingleOrDefaultAsync(x => x.TenantId == user.TenantId && x.Code == normalized.ModelCode && x.IsActive, ct)
            ?? throw new InvalidOperationException($"Budget model '{normalized.ModelCode}' is not configured.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (plan, version) = await GetEditableVersionAsync(company, fiscalYear, model, ct);
        var periods = fiscalYear.Periods.GroupBy(x => PeriodKey(x.Name)).ToDictionary(x => x.Key, x => x.OrderBy(p => p.Sequence).First());
        var measures = model.Measures.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var modelDimensions = model.Dimensions.Where(x => x.Dimension is not null).ToDictionary(x => x.Dimension!.Code, StringComparer.OrdinalIgnoreCase);
        var members = await LoadMemberCacheAsync(model, company.Id, ct);
        var existing = (await db.BudgetFacts.Include(x => x.Dimensions).Where(x => x.VersionId == version.Id).ToListAsync(ct))
            .ToDictionary(x => (x.PeriodId, x.MeasureId, x.ValueKind, x.CoordinateHash));

        var warnings = normalized.Warnings.ToList();
        var createdFacts = 0; var updatedFacts = 0; var createdMembers = 0; var skipped = 0;

        foreach (var source in normalized.Facts)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(source.PeriodName) || !periods.TryGetValue(PeriodKey(source.PeriodName), out var period))
            { skipped++; Warn(warnings, $"ردیف {source.SourceRow}: دوره '{source.PeriodName ?? "-"}' در سال مالی پیدا نشد."); continue; }
            if (period.IsClosed)
            { skipped++; Warn(warnings, $"ردیف {source.SourceRow}: دوره '{period.Name}' بسته است و داده آن وارد نشد."); continue; }
            if (!measures.TryGetValue(source.MeasureCode, out var measure))
            { skipped++; Warn(warnings, $"ردیف {source.SourceRow}: مژر '{source.MeasureCode}' در مدل {model.Code} تعریف نشده است."); continue; }

            var selections = new List<DimensionSelection>();
            foreach (var pair in source.DimensionMembers)
            {
                if (!modelDimensions.TryGetValue(pair.Key, out var modelDimension) || string.IsNullOrWhiteSpace(pair.Value)) continue;
                var (member, created) = ResolveMember(modelDimension.Dimension!, company.Id, pair.Value, members);
                if (created) { db.DimensionMembers.Add(member); createdMembers++; }
                selections.Add(new DimensionSelection(modelDimension.DimensionId, member.Id));
            }
            if (model.Dimensions.Where(x => x.IsRequired).Any(required => selections.All(s => s.DimensionId != required.DimensionId)))
            { skipped++; Warn(warnings, $"ردیف {source.SourceRow}: یک یا چند بُعد اجباری مقدار ندارد."); continue; }

            var kind = request.OverrideValueKind ?? source.ValueKind;
            var hash = BudgetCoordinateKey.Create(selections);
            var key = (period.Id, measure.Id, kind, hash);
            var json = JsonSerializer.Serialize(selections.OrderBy(x => x.DimensionId));
            if (existing.TryGetValue(key, out var fact))
            {
                fact.Value = source.Value; fact.Source = $"Excel:{fileName}/{request.SheetName}"; fact.Note = source.SourceLabel;
                fact.CurrencyCode = source.Unit == "IRR" ? "IRR" : fact.CurrencyCode; fact.CoordinatesJson = json; fact.UpdatedAtUtc = DateTime.UtcNow;
                updatedFacts++;
            }
            else
            {
                fact = new BudgetFact { VersionId = version.Id, PeriodId = period.Id, MeasureId = measure.Id, ValueKind = kind, Value = source.Value,
                    CurrencyCode = source.Unit == "IRR" ? "IRR" : null, CoordinateHash = hash, CoordinatesJson = json,
                    Source = $"Excel:{fileName}/{request.SheetName}", Note = source.SourceLabel };
                foreach (var s in selections) fact.Dimensions.Add(new BudgetFactDimension { BudgetFactId = fact.Id, DimensionId = s.DimensionId, MemberId = s.MemberId });
                db.BudgetFacts.Add(fact); existing[key] = fact; createdFacts++;
            }
        }

        db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId, UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "WorkbookImport", EntityId = version.Id.ToString(), Action = "IMPORT",
            NewValueJson = JsonSerializer.Serialize(new { fileName, request.SheetName, request.Profile, normalized.ModelCode, createdFacts, updatedFacts, createdMembers, skipped }) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new WorkbookImportExecutionDto(request.SheetName, model.Code, plan.Id, version.Id, createdFacts, updatedFacts, createdMembers, skipped, warnings);
    }

    private async Task<(BudgetPlan, BudgetVersion)> GetEditableVersionAsync(Company company, FiscalYear year, BudgetModel model, CancellationToken ct)
    {
        var plan = await db.BudgetPlans.Include(x => x.Versions).Where(x => x.CompanyId == company.Id && x.FiscalYearId == year.Id && x.BudgetModelId == model.Id)
            .OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (plan is null)
        {
            var scenario = await db.BudgetScenarios.FirstAsync(x => x.TenantId == company.TenantId && x.Code == "BASE" && x.IsActive, ct);
            plan = new BudgetPlan { CompanyId = company.Id, FiscalYearId = year.Id, BudgetModelId = model.Id, Name = $"{model.Name} - {year.Name}" };
            var version = new BudgetVersion { BudgetPlanId = plan.Id, ScenarioId = scenario.Id, VersionNumber = 1, Name = "نسخه اولیه واردات اکسل", Status = BudgetStatus.Draft };
            plan.Versions.Add(version); db.BudgetPlans.Add(plan); return (plan, version);
        }
        var editable = plan.Versions.Where(x => !x.IsLocked && x.Status == BudgetStatus.Draft).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        return editable is null ? throw new InvalidOperationException($"برنامه '{plan.Name}' نسخه پیش‌نویس قابل ویرایش ندارد. ابتدا اصلاحیه جدید ایجاد کنید.") : (plan, editable);
    }

    private async Task<Dictionary<(Guid DimensionId, string Key), DimensionMember>> LoadMemberCacheAsync(BudgetModel model, Guid companyId, CancellationToken ct)
    {
        var result = new Dictionary<(Guid, string), DimensionMember>();
        foreach (var d in model.Dimensions.Where(x => x.Dimension is not null))
            foreach (var m in await db.DimensionMembers.Where(x => x.DimensionId == d.DimensionId && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId)).ToListAsync(ct))
            { result[(m.DimensionId, "C:" + Normalize(m.Code))] = m; result[(m.DimensionId, "N:" + Normalize(m.Name))] = m; }
        return result;
    }

    private static (DimensionMember Member, bool Created) ResolveMember(DimensionDefinition dimension, Guid companyId, string value, IDictionary<(Guid DimensionId, string Key), DimensionMember> cache)
    {
        var normalized = Normalize(value);
        if (cache.TryGetValue((dimension.Id, "C:" + normalized), out var byCode)) return (byCode, false);
        if (cache.TryGetValue((dimension.Id, "N:" + normalized), out var byName)) return (byName, false);
        var code = LooksLikeCode(value) ? value.Trim().ToUpperInvariant() : $"IMP-{dimension.Code}-{Hash(normalized)}";
        if (cache.TryGetValue((dimension.Id, "C:" + Normalize(code)), out var sameCode)) return (sameCode, false);
        var member = new DimensionMember { DimensionId = dimension.Id, CompanyId = companyId, Code = code, Name = value.Trim(), ExternalKey = value.Trim() };
        cache[(dimension.Id, "C:" + Normalize(code))] = member; cache[(dimension.Id, "N:" + normalized)] = member; return (member, true);
    }

    private static bool LooksLikeCode(string value) => value.Length <= 50 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
    private static string Normalize(string? value) => (value ?? "").Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ').Replace(" ", "").Trim().ToUpperInvariant();
    private static string PeriodKey(string? value) => Normalize(value).Replace("ماه", "");
    private static void Warn(ICollection<string> list, string text) { if (list.Count < 100 && !list.Contains(text)) list.Add(text); }
    private void EnsureCompanyWrite(Guid companyId) { if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId)) throw new UnauthorizedAccessException("You do not have write access to this company."); }
}
