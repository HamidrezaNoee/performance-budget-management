namespace PBM.Application;

public static class SystemRoleCodes
{
    public const string SuperAdmin = "SUPERADMIN";
    public const string Admin = "ADMIN";
    public const string Auditor = "AUDITOR";
    public const string Cfo = "CFO";
    public const string Ceo = "CEO";
    public const string BudgetManager = "BUDGET_MANAGER";
    public const string BudgetExpert = "BUDGET_EXPERT";
    public const string Integration = "INTEGRATION";
}

public static class UserContextAuthorizationExtensions
{
    public static bool IsSystemAdministrator(this IUserContext user) =>
        user.IsInRole(SystemRoleCodes.SuperAdmin) || user.IsInRole(SystemRoleCodes.Admin);

    public static bool CanReadCompanyScope(this IUserContext user, Guid companyId) =>
        user.IsInRole(SystemRoleCodes.SuperAdmin) || user.CanAccessCompany(companyId);

    public static bool CanWriteCompanyScope(this IUserContext user, Guid companyId) =>
        user.IsInRole(SystemRoleCodes.SuperAdmin) || user.CanWriteCompany(companyId);

    public static void EnsureCompanyReadAccess(this IUserContext user, Guid companyId)
    {
        if (!user.CanReadCompanyScope(companyId))
            throw new UnauthorizedAccessException("You do not have access to this company.");
    }

    public static void EnsureCompanyWriteAccess(this IUserContext user, Guid companyId)
    {
        if (!user.CanWriteCompanyScope(companyId))
            throw new UnauthorizedAccessException("You do not have write access to this company.");
    }

    public static void EnsureAnyRole(this IUserContext user, params string[] roleCodes)
    {
        if (roleCodes.Any(user.IsInRole)) return;
        throw new UnauthorizedAccessException("You do not have the required role for this operation.");
    }
}
