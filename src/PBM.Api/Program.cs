using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PBM.Api;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("PbmDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:PbmDatabase is required. Configure it through environment variables, user secrets or a deployment secret store.");
builder.Services.AddDbContext<PbmDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddPbmServices();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").GetChildren()
    .Select(x => x.Value?.Trim())
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Cast<string>()
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    policy.AllowAnyHeader().AllowAnyMethod();
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowCredentials();
    }
    else if (builder.Environment.IsDevelopment())
    {
        policy.SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1"))
            .AllowCredentials();
    }
}));

var loginPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Login:PermitLimit") ?? 10;
var loginWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Login:WindowSeconds") ?? 60;
if (loginPermitLimit is < 1 or > 1000) throw new InvalidOperationException("RateLimiting:Login:PermitLimit must be between 1 and 1000.");
if (loginWindowSeconds is < 1 or > 3600) throw new InvalidOperationException("RateLimiting:Login:WindowSeconds must be between 1 and 3600.");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            status = StatusCodes.Status429TooManyRequests,
            title = "Too many login attempts",
            detail = "Too many authentication attempts were received from this client. Try again later."
        }, cancellationToken);
    };
    options.AddPolicy<string>("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPermitLimit,
                Window = TimeSpan.FromSeconds(loginWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key is required and must contain at least 32 UTF-8 bytes.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    ValidIssuer = builder.Configuration["Jwt:Issuer"],
    ValidAudience = builder.Configuration["Jwt:Audience"],
    IssuerSigningKey = signingKey,
    ClockSkew = TimeSpan.FromMinutes(1),
    NameClaimType = ClaimTypes.Name,
    RoleClaimType = ClaimTypes.Role
});
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/livez", () => Results.Ok(new { status = "live", service = "PBM.Api", utc = DateTime.UtcNow }));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "PBM.Api", utc = DateTime.UtcNow }));
app.MapGet("/readyz", async (PbmDbContext db, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct))
            return Results.Json(new { status = "not-ready", database = "unreachable", utc = DateTime.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var tenantCount = await db.Tenants.AsNoTracking().CountAsync(ct);
        var activeCompanyCount = await db.Companies.AsNoTracking().CountAsync(x => x.IsActive, ct);
        if (tenantCount == 0 || activeCompanyCount == 0)
            return Results.Json(new { status = "not-ready", database = "connected", tenantCount, activeCompanyCount, utc = DateTime.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);

        return Results.Ok(new { status = "ready", database = "connected", tenantCount, activeCompanyCount, utc = DateTime.UtcNow });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.Json(new { status = "not-ready", database = "error", error = ex.GetType().Name, utc = DateTime.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PbmDbContext>();
    await db.Database.EnsureCreatedAsync();

    var useDemoSeed = builder.Configuration.GetValue<bool>("Bootstrap:UseDemoSeed");
    var provisionInitialTenant = builder.Configuration.GetValue<bool>("Bootstrap:ProvisionInitialTenant");
    var hasTenant = await db.Tenants.AnyAsync();
    var demoSeedApplied = false;

    if (!hasTenant)
    {
        if (useDemoSeed)
        {
            if (!app.Environment.IsDevelopment() && !builder.Configuration.GetValue<bool>("Bootstrap:AllowDemoSeedOutsideDevelopment"))
                throw new InvalidOperationException("Demo seed is blocked outside Development. Set Bootstrap:AllowDemoSeedOutsideDevelopment=true only for a disposable non-production environment.");
            await SeedData.InitializeAsync(db);
            demoSeedApplied = true;
        }
        else if (provisionInitialTenant)
        {
            var licenseDays = builder.Configuration.GetValue<int?>("Bootstrap:LicenseDays") ?? 365;
            if (licenseDays is < 1 or > 3650) throw new InvalidOperationException("Bootstrap:LicenseDays must be between 1 and 3650.");
            var startsAt = DateTime.UtcNow.Date;
            await InitialTenantProvisioner.InitializeAsync(db, new InitialTenantProvisioningOptions(
                RequiredSetting(builder.Configuration, "Bootstrap:TenantCode"),
                RequiredSetting(builder.Configuration, "Bootstrap:TenantName"),
                RequiredSetting(builder.Configuration, "Bootstrap:CompanyCode"),
                RequiredSetting(builder.Configuration, "Bootstrap:CompanyName"),
                builder.Configuration["Bootstrap:Industry"],
                RequiredSetting(builder.Configuration, "Bootstrap:LicenseKey"),
                startsAt,
                startsAt.AddDays(licenseDays),
                builder.Configuration.GetValue<int?>("Bootstrap:MaxCompanies") ?? 5,
                builder.Configuration.GetValue<int?>("Bootstrap:MaxUsers") ?? 100));
        }
        else
        {
            throw new InvalidOperationException("PBM database has no tenant. Enable Bootstrap:ProvisionInitialTenant for first deployment, enable Bootstrap:UseDemoSeed for local demo data, or provision the tenant externally before startup.");
        }
    }

    await EnterpriseSeedData.InitializeAsync(db, includeWorkbookReferenceMembers: demoSeedApplied);
    await PlanningSeedData.InitializeAsync(db);
    await SecuritySeedData.InitializeAsync(db);

    if (!await db.Users.AnyAsync())
    {
        var tenantId = await db.Tenants.Select(x => x.Id).FirstAsync();
        var companyId = await db.Companies.Where(x => x.TenantId == tenantId && x.IsActive).Select(x => x.Id).FirstAsync();
        var role = await db.Roles.SingleAsync(x => x.TenantId == tenantId && x.Code == "SUPERADMIN");
        var bootstrapUserName = RequiredSetting(builder.Configuration, "BootstrapAdmin:UserName");
        var bootstrapPassword = RequiredSetting(builder.Configuration, "BootstrapAdmin:Password");
        ValidateBootstrapPassword(bootstrapPassword);
        var bootstrapDisplayName = builder.Configuration["BootstrapAdmin:DisplayName"]?.Trim();
        if (string.IsNullOrWhiteSpace(bootstrapDisplayName)) bootstrapDisplayName = "مدیر کل سامانه";
        var user = new AppUser
        {
            TenantId = tenantId,
            UserName = bootstrapUserName,
            DisplayName = bootstrapDisplayName,
            PasswordHash = "pending"
        };
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        user.PasswordHash = hasher.HashPassword(user, bootstrapPassword);
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserCompanyAccess.Add(new UserCompanyAccess { UserId = user.Id, CompanyId = companyId, CanRead = true, CanWrite = true });
        await db.SaveChangesAsync();
    }
}

app.MapPost("/api/v1/auth/login", async (LoginRequest request, PbmDbContext db, IPasswordHasher<AppUser> hasher, IConfiguration config) =>
{
    var normalizedUserName = request.UserName?.Trim();
    if (string.IsNullOrWhiteSpace(normalizedUserName) || string.IsNullOrEmpty(request.Password)) return Results.Unauthorized();

    var user = await db.Users.SingleOrDefaultAsync(x => x.UserName == normalizedUserName && x.IsActive);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();

    var roles = await db.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.Role!.Code).ToListAsync();
    var companyAccess = await db.UserCompanyAccess.Where(x => x.UserId == user.Id && x.CanRead).Select(x => new { x.CompanyId, x.CanWrite }).ToListAsync();
    var companyIds = companyAccess.Select(x => x.CompanyId).ToList();
    var writableCompanyIds = companyAccess.Where(x => x.CanWrite).Select(x => x.CompanyId).ToList();
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new("tenant_id", user.TenantId.ToString()),
        new(ClaimTypes.Name, user.DisplayName),
        new("username", user.UserName)
    };
    claims.AddRange(roles.Select(x => new Claim(ClaimTypes.Role, x)));
    claims.AddRange(companyIds.Select(x => new Claim("company_id", x.ToString())));
    claims.AddRange(writableCompanyIds.Select(x => new Claim("company_write_id", x.ToString())));

    var token = new JwtSecurityToken(
        config["Jwt:Issuer"],
        config["Jwt:Audience"],
        claims,
        expires: DateTime.UtcNow.AddHours(8),
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), user.DisplayName, roles, companyIds, writableCompanyIds));
}).RequireRateLimiting("login");

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
    if (!request.HasFormContentType) return Results.BadRequest(new { message = "multipart/form-data is required." });
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0) return Results.BadRequest(new { message = "An XLSX file is required." });
    if (file.Length > 50 * 1024 * 1024) return Results.BadRequest(new { message = "Workbook is larger than the 50 MB inspection limit." });
    if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { message = "Only .xlsx files are supported." });
    await using var stream = file.OpenReadStream();
    return Results.Ok(await service.InspectAsync(stream, file.FileName, file.Length, cancellationToken: ct));
}).DisableAntiforgery();
api.MapPbmModuleEndpoints();

app.Run();

static string GetClientPartitionKey(HttpContext context) =>
    context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown-client";

static string RequiredSetting(IConfiguration configuration, string key)
{
    var value = configuration[key]?.Trim();
    return string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Configuration value '{key}' is required for initial provisioning.")
        : value;
}

static void ValidateBootstrapPassword(string password)
{
    if (password.Length < 12 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
        throw new InvalidOperationException("BootstrapAdmin:Password must be at least 12 characters and contain uppercase, lowercase and numeric characters.");
}

public sealed record LoginRequest(string UserName, string Password);
public sealed record LoginResponse(string AccessToken, string DisplayName, IReadOnlyList<string> Roles, IReadOnlyList<Guid> CompanyIds, IReadOnlyList<Guid> WritableCompanyIds);
public sealed record FormulaRequest(string Expression, IReadOnlyDictionary<string, decimal> Variables);
