using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PBM.Api;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<ApiExceptionHandler>(); builder.Services.AddOpenApi(); builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<PbmDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("PbmDatabase")));
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<IBudgetWorkflowService, BudgetWorkflowService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IFinancialReportService, FinancialReportService>();
builder.Services.AddScoped<IFiscalCalendarService, FiscalCalendarService>();
builder.Services.AddScoped<ISecurityAdminService, SecurityAdminService>();
builder.Services.AddScoped<IReferenceDataService, ReferenceDataService>();
builder.Services.AddScoped<IKpiService, KpiService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IForecastService, ForecastService>();
builder.Services.AddScoped<IWorkbookImportService, OpenXmlWorkbookImportService>();
builder.Services.AddScoped<IWorkbookNormalizationService, WorkbookNormalizationService>();
builder.Services.AddScoped<IWorkbookImportExecutionService, WorkbookImportExecutionService>();
builder.Services.AddSingleton<IFormulaEngine, FormulaEngine>();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
    ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"], IssuerSigningKey = signingKey, ClockSkew = TimeSpan.FromMinutes(1),
    NameClaimType = ClaimTypes.Name, RoleClaimType = ClaimTypes.Role
});
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseExceptionHandler(); app.UseCors(); app.UseAuthentication(); app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "PBM.Api", utc = DateTime.UtcNow }));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PbmDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedData.InitializeAsync(db);
    await EnterpriseSeedData.InitializeAsync(db);
    await SecuritySeedData.InitializeAsync(db);
    if (app.Environment.IsDevelopment() && !await db.Users.AnyAsync())
    {
        var tenantId = await db.Tenants.Select(x => x.Id).FirstAsync();
        var companyId = await db.Companies.Select(x => x.Id).FirstAsync();
        var role = await db.Roles.SingleAsync(x => x.TenantId == tenantId && x.Code == "SUPERADMIN");
        var user = new AppUser { TenantId = tenantId, UserName = "admin", DisplayName = "مدیر سیستم", PasswordHash = "pending" };
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var bootstrapPassword = builder.Configuration["BootstrapAdmin:Password"] ?? "ChangeMe123!";
        user.PasswordHash = hasher.HashPassword(user, bootstrapPassword);
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserCompanyAccess.Add(new UserCompanyAccess { UserId = user.Id, CompanyId = companyId, CanRead = true, CanWrite = true });
        await db.SaveChangesAsync();
    }
}

app.MapPost("/api/v1/auth/login", async (LoginRequest request, PbmDbContext db, IPasswordHasher<AppUser> hasher, IConfiguration config) =>
{
    var user = await db.Users.SingleOrDefaultAsync(x => x.UserName == request.UserName && x.IsActive);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    var roles = await db.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.Role!.Code).ToListAsync();
    var companyAccess = await db.UserCompanyAccess.Where(x => x.UserId == user.Id && x.CanRead).Select(x => new { x.CompanyId, x.CanWrite }).ToListAsync();
    var companyIds = companyAccess.Select(x => x.CompanyId).ToList();
    var writableCompanyIds = companyAccess.Where(x => x.CanWrite).Select(x => x.CompanyId).ToList();
    var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new(ClaimTypes.NameIdentifier, user.Id.ToString()), new("tenant_id", user.TenantId.ToString()), new(ClaimTypes.Name, user.DisplayName), new("username", user.UserName) };
    claims.AddRange(roles.Select(x => new Claim(ClaimTypes.Role, x)));
    claims.AddRange(companyIds.Select(x => new Claim("company_id", x.ToString())));
    claims.AddRange(writableCompanyIds.Select(x => new Claim("company_write_id", x.ToString())));
    var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims, expires: DateTime.UtcNow.AddHours(8), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), user.DisplayName, roles, companyIds));
});

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapGet("/companies", (ICompanyService service, CancellationToken ct) => service.GetCompaniesAsync(ct));
api.MapGet("/reference/fiscal-years", (Guid companyId, IBudgetService service, CancellationToken ct) => service.GetFiscalYearsAsync(companyId, ct));
api.MapGet("/reference/periods", (Guid fiscalYearId, IBudgetService service, CancellationToken ct) => service.GetPeriodsAsync(fiscalYearId, ct));
api.MapGet("/reference/models", (Guid companyId, IBudgetService service, CancellationToken ct) => service.GetModelsAsync(companyId, ct));
api.MapGet("/reference/dimensions", (Guid modelId, IBudgetService service, CancellationToken ct) => service.GetDimensionsAsync(modelId, ct));
api.MapGet("/reference/dimension-members", (Guid dimensionId, Guid? companyId, IBudgetService service, CancellationToken ct) => service.GetDimensionMembersAsync(dimensionId, companyId, ct));
api.MapGet("/reference/measures", (Guid modelId, IBudgetService service, CancellationToken ct) => service.GetMeasuresAsync(modelId, ct));
api.MapGet("/budget/plans", (Guid companyId, Guid fiscalYearId, IBudgetService service, CancellationToken ct) => service.GetPlansAsync(companyId, fiscalYearId, ct));
api.MapPost("/budget/plans", (CreateBudgetPlanRequest request, IBudgetService service, CancellationToken ct) => service.CreatePlanAsync(request, ct));
api.MapPost("/budget/facts", async (UpsertBudgetFactRequest request, IBudgetService service, CancellationToken ct) => Results.Ok(new { id = await service.UpsertFactAsync(request, ct) }));
api.MapPost("/budget/grid/query", (BudgetGridQuery request, IBudgetService service, CancellationToken ct) => service.GetGridAsync(request, ct));
api.MapGet("/dashboard/summary", (Guid companyId, Guid fiscalYearId, IDashboardService service, CancellationToken ct) => service.GetSummaryAsync(companyId, fiscalYearId, ct));
api.MapPost("/formulas/evaluate", (FormulaRequest request, IFormulaEngine engine) => Results.Ok(new { value = engine.Evaluate(request.Expression, request.Variables) }));
api.MapPost("/imports/workbook/inspect", async (HttpRequest request, IWorkbookImportService service, CancellationToken ct) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { message = "multipart/form-data is required." }); var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0) return Results.BadRequest(new { message = "An XLSX file is required." }); if (file.Length > 50 * 1024 * 1024) return Results.BadRequest(new { message = "Workbook is larger than the 50 MB inspection limit." });
    if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { message = "Only .xlsx files are supported." });
    await using var stream = file.OpenReadStream(); return Results.Ok(await service.InspectAsync(stream, file.FileName, file.Length, cancellationToken: ct));
}).DisableAntiforgery();
api.MapEnterpriseEndpoints();
api.MapForecastEndpoints();
api.MapBudgetWorkflowEndpoints();
api.MapFinancialReportEndpoints();
api.MapWorkbookImportPipelineEndpoints();
api.MapFiscalCalendarEndpoints();
api.MapSecurityAdminEndpoints();

app.Run();
public sealed record LoginRequest(string UserName, string Password);
public sealed record LoginResponse(string AccessToken, string DisplayName, IReadOnlyList<string> Roles, IReadOnlyList<Guid> CompanyIds);
public sealed record FormulaRequest(string Expression, IReadOnlyDictionary<string, decimal> Variables);
