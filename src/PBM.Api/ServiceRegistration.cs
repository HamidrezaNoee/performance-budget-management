using Microsoft.AspNetCore.Identity;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;

namespace PBM.Api;

public static class ServiceRegistration
{
    public static IServiceCollection AddPbmServices(this IServiceCollection services)
    {
        services.AddScoped<IUserContext, HttpUserContext>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IBudgetOperationsService, BudgetOperationsService>();
        services.AddScoped<IBudgetReservationService, BudgetReservationService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<BudgetWorkflowService>();
        services.AddScoped<IBudgetWorkflowService, NotifyingBudgetWorkflowService>();
        services.AddScoped<IBudgetInboxService, BudgetInboxService>();
        services.AddScoped<IBudgetAttachmentService, BudgetAttachmentService>();
        services.AddScoped<ICalculationService, CalculationService>();
        services.AddSingleton<IDashboardMetricPolicy, ConfigurationDashboardMetricPolicy>();
        services.AddScoped<IDashboardService, ExecutiveDashboardService>();
        services.AddScoped<IVarianceAnalysisService, VarianceAnalysisService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        services.AddScoped<IFiscalCalendarService, FiscalCalendarService>();
        services.AddScoped<ISecurityAdminService, SecurityAdminService>();
        services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<IKpiService, KpiService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IForecastService, ForecastService>();
        services.AddScoped<IScenarioService, ScenarioService>();
        services.AddScoped<IWorkbookImportService, OpenXmlWorkbookImportService>();
        services.AddScoped<IWorkbookNormalizationService, WorkbookNormalizationService>();
        services.AddScoped<IWorkbookImportExecutionService, WorkbookImportExecutionService>();
        services.AddSingleton<IFormulaEngine, FormulaEngine>();
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        return services;
    }
}
