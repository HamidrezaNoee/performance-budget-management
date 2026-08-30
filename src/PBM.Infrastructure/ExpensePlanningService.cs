using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;

namespace PBM.Infrastructure;

public sealed class ExpensePlanningService(
    PbmDbContext db,
    IUserContext user,
    BudgetService budgetService,
    CommercialPlanningProvisioner provisioner) : IExpensePlanningService
{
    private const string ModelCode = "EXPENSE";
    private const string MeasureCode = "EXPENSE_AMOUNT";
    private const string ClassCode = "EXPENSECLASS";
    private const string ItemCode = "EXPENSEITEM";

    public async Task<ExpensePlanningSetupDto> GetSetupAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureCompany(companyId);
        var tenantId = await GetCompanyTenantAsync(companyId, cancellationToken);
        await provisioner.EnsureExpenseAsync(tenantId, cancellationToken);
        var model = await db.BudgetModels.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.Code == ModelCode && x.IsActive, cancellationToken);
        var dims = await db.BudgetModelDimensions.AsNoTracking()
            .Where(x => x.BudgetModelId == model.Id && x.Dimension!.IsActive)
            .OrderBy(x => x.Sequence)
            .Select(x => new { x.DimensionId, x.Dimension!.Code, x.Dimension.Name, x.Sequence, x.IsRequired })
            .ToListAsync(cancellationToken);
        var ids = dims.Select(x => x.DimensionId).ToArray();
        var members = await db.DimensionMembers.AsNoTracking()
            .Where(x => ids.Contains(x.DimensionId) && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .OrderBy(x => x.Name)
            .Select(x => new ExpensePlanningMemberDto(x.Id, x.DimensionId, x.Code, x.Name))
            .ToListAsync(cancellationToken);
        var map = members.GroupBy(x => x.DimensionId).ToDictionary(x => x.Key, x => (IReadOnlyList<ExpensePlanningMemberDto>)x.ToList());
        var dimensions = dims.Select(x => new ExpensePlanningDimensionDto(x.DimensionId, x.Code, x.Name, x.Sequence,
            x.IsRequired || x.Code is ClassCode or ItemCode, map.GetValueOrDefault(x.DimensionId, []))).ToList();
        var measureId = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == model.Id && x.Code == MeasureCode).Select(x => x.Id).SingleAsync(cancellationToken);
        return new ExpensePlanningSetupDto(model.Id, model.Name, await GetBaseCurrencyAsync(tenantId, cancellationToken), dimensions, measureId);
    }

    public async Task<ExpensePlanningDataDto> QueryAsync(ExpensePlanningQueryRequest request, CancellationToken cancellationToken = default)
    {
        ValidateKind(request.ValueKind);
        var context = await ResolveContextAsync(request.VersionId, cancellationToken);
        EnsureCompany(context.CompanyId);
        await provisioner.EnsureExpenseAsync(context.TenantId, cancellationToken);
        var dimensions = await ValidateDimensionsAsync(context.ModelId, context.CompanyId, request.Dimensions, cancellationToken);
        var hash = BudgetCoordinateKey.Create(dimensions);
        var measureId = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == context.ModelId && x.Code == MeasureCode).Select(x => x.Id).SingleAsync(cancellationToken);
        var periods = await db.FiscalPeriods.AsNoTracking().Where(x => x.FiscalYearId == context.FiscalYearId).OrderBy(x => x.Sequence)
            .Select(x => new FiscalPeriodDto(x.Id, x.Sequence, x.Code, x.Name, x.JalaliMonth, x.StartDate, x.EndDate, x.IsClosed)).ToListAsync(cancellationToken);
        var facts = await db.BudgetFacts.AsNoTracking().Where(x => x.VersionId == request.VersionId && x.ValueKind == request.ValueKind && x.MeasureId == measureId && x.CoordinateHash == hash)
            .ToDictionaryAsync(x => x.PeriodId, cancellationToken);
        var values = periods.Select(p => facts.TryGetValue(p.Id, out var f)
            ? new ExpensePlanningPeriodValueDto(p.Id, p.Name, p.Sequence, f.Value, f.Id)
            : new ExpensePlanningPeriodValueDto(p.Id, p.Name, p.Sequence, 0m)).ToList();
        return new ExpensePlanningDataDto(periods, values);
    }

    public async Task<Guid> UpsertCellAsync(UpsertExpensePlanningCellRequest request, CancellationToken cancellationToken = default)
    {
        ValidateKind(request.ValueKind);
        if (request.Value < 0) throw new ArgumentOutOfRangeException(nameof(request.Value), "Expense planning amounts must be positive; income/expense nature is selected by EXPENSECLASS.");
        var context = await ResolveContextAsync(request.VersionId, cancellationToken);
        EnsureCompanyWrite(context.CompanyId);
        await provisioner.EnsureExpenseAsync(context.TenantId, cancellationToken);
        var dimensions = await ValidateDimensionsAsync(context.ModelId, context.CompanyId, request.Dimensions, cancellationToken);
        var measureId = await db.Measures.AsNoTracking().Where(x => x.BudgetModelId == context.ModelId && x.Code == MeasureCode).Select(x => x.Id).SingleAsync(cancellationToken);
        return await budgetService.UpsertFactAsync(new UpsertBudgetFactRequest(
            request.VersionId,
            request.PeriodId,
            measureId,
            request.ValueKind,
            request.Value,
            await GetBaseCurrencyAsync(context.TenantId, cancellationToken),
            dimensions,
            request.ValueKind == ValueKind.Budget ? "ExpenseBudgetPlanner" : "ExpenseForecastPlanner",
            request.Note), cancellationToken);
    }

    public async Task<ExpensePlanningMemberDto> CreateItemAsync(CreateExpenseItemRequest request, CancellationToken cancellationToken = default)
    {
        EnsureCompanyWrite(request.CompanyId);
        var tenantId = await GetCompanyTenantAsync(request.CompanyId, cancellationToken);
        await provisioner.EnsureExpenseAsync(tenantId, cancellationToken);
        var code = NormalizeCode(request.Code);
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new ArgumentException("Expense item name is required and must be at most 200 characters.");
        var dimension = await db.Dimensions.SingleAsync(x => x.TenantId == tenantId && x.Code == ItemCode && x.IsActive, cancellationToken);
        if (await db.DimensionMembers.AnyAsync(x => x.DimensionId == dimension.Id && x.Code == code && (x.CompanyId == null || x.CompanyId == request.CompanyId), cancellationToken))
            throw new InvalidOperationException("An expense item with the same code already exists.");
        var member = new DimensionMember { DimensionId = dimension.Id, CompanyId = request.CompanyId, Code = code, Name = name };
        db.DimensionMembers.Add(member);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId = user.UserId == Guid.Empty ? null : user.UserId,
            EntityType = "ExpenseItem",
            EntityId = member.Id.ToString(),
            Action = "CREATE",
            NewValueJson = JsonSerializer.Serialize(new { member.Code, member.Name, member.CompanyId })
        });
        await db.SaveChangesAsync(cancellationToken);
        return new ExpensePlanningMemberDto(member.Id, member.DimensionId, member.Code, member.Name);
    }

    private async Task<PlanningContext> ResolveContextAsync(Guid versionId, CancellationToken ct)
    {
        var context = await db.BudgetVersions.AsNoTracking().Where(x => x.Id == versionId)
            .Select(x => new PlanningContext(x.Id, x.BudgetPlan!.Company!.TenantId, x.BudgetPlan.CompanyId, x.BudgetPlan.FiscalYearId, x.BudgetPlan.BudgetModelId, x.BudgetPlan.BudgetModel!.Code))
            .SingleAsync(ct);
        if (context.TenantId != user.TenantId) throw new UnauthorizedAccessException("Budget version is outside the current tenant.");
        if (!context.ModelCode.Equals(ModelCode, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expense planning can only use the EXPENSE budget model.");
        return context;
    }

    private async Task<IReadOnlyList<DimensionSelection>> ValidateDimensionsAsync(Guid modelId, Guid companyId, IReadOnlyList<DimensionSelection> selections, CancellationToken ct)
    {
        if (selections is null || selections.Count == 0) throw new ArgumentException("Expense dimensions are required.");
        if (selections.Select(x => x.DimensionId).Distinct().Count() != selections.Count) throw new ArgumentException("A dimension can only be selected once.");
        var modelDimensions = await db.BudgetModelDimensions.AsNoTracking().Where(x => x.BudgetModelId == modelId)
            .Select(x => new { x.DimensionId, x.IsRequired, x.Dimension!.Code }).ToListAsync(ct);
        var supplied = selections.Select(x => x.DimensionId).ToHashSet();
        var allowed = modelDimensions.Select(x => x.DimensionId).ToHashSet();
        if (selections.Any(x => !allowed.Contains(x.DimensionId))) throw new ArgumentException("A selected dimension does not belong to the EXPENSE model.");
        if (modelDimensions.Any(x => (x.IsRequired || x.Code is ClassCode or ItemCode) && !supplied.Contains(x.DimensionId)))
            throw new ArgumentException("Department/account and expense class/item selections are required.");
        var dimensionIds = selections.Select(x => x.DimensionId).ToArray();
        var memberIds = selections.Select(x => x.MemberId).ToArray();
        var pairs = await db.DimensionMembers.AsNoTracking().Where(x => dimensionIds.Contains(x.DimensionId) && memberIds.Contains(x.Id) && x.IsActive && (x.CompanyId == null || x.CompanyId == companyId))
            .Select(x => new { x.DimensionId, x.Id }).ToListAsync(ct);
        if (selections.Any(s => !pairs.Any(x => x.DimensionId == s.DimensionId && x.Id == s.MemberId))) throw new ArgumentException("One or more expense dimension members are invalid for this company.");
        return selections.OrderBy(x => x.DimensionId).ToList();
    }

    private async Task<Guid> GetCompanyTenantAsync(Guid companyId, CancellationToken ct)
    {
        var tenantId = await db.Companies.AsNoTracking().Where(x => x.Id == companyId && x.IsActive).Select(x => (Guid?)x.TenantId).SingleOrDefaultAsync(ct)
            ?? throw new ArgumentException("Company was not found or is inactive.");
        if (tenantId != user.TenantId) throw new UnauthorizedAccessException("Company is outside the current tenant.");
        return tenantId;
    }

    private async Task<string> GetBaseCurrencyAsync(Guid tenantId, CancellationToken ct) =>
        await db.Currencies.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync(ct) ?? "IRR";

    private static void ValidateKind(ValueKind kind)
    {
        if (kind is not (ValueKind.Budget or ValueKind.Forecast)) throw new ArgumentException("Expense planner supports Budget and Forecast only.");
    }

    private static string NormalizeCode(string value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || code.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')))
            throw new ArgumentException("Expense item code must contain 2-64 letters, numbers, underscore, dash or dot characters.");
        return code;
    }

    private void EnsureCompany(Guid companyId) { if (!user.IsInRole("SUPERADMIN") && !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException("You do not have access to this company."); }
    private void EnsureCompanyWrite(Guid companyId) { if (!user.IsInRole("SUPERADMIN") && !user.CanWriteCompany(companyId)) throw new UnauthorizedAccessException("You do not have write access to this company."); }
    private sealed record PlanningContext(Guid VersionId, Guid TenantId, Guid CompanyId, Guid FiscalYearId, Guid ModelId, string ModelCode);
}
