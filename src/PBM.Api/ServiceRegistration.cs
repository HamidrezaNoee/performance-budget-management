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
        services.AddScoped<IIdempotencyService, SqlIdempotencyService>();
        services.AddScoped<IIdempotencyAdminService, IdempotencyAdminService>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<OutboxQueueService>();
        services.AddScoped<IOutboxAdminService, OutboxAdminService>();
        services.AddHttpClient<NotificationWebhookTransport>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IOutboxTransport>(provider => provider.GetRequiredService<NotificationWebhookTransport>());
        services.AddHostedService<OutboxDispatcherBackgroundService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<CommercialPlanningProvisioner>();
        services.AddScoped<BudgetService>();
        services.AddScoped<IBudgetService, GovernedBudgetService>();
        services.AddScoped<IPurchaseForecastService, PurchaseForecastService>();
        services.AddScoped<IPurchaseDashboardService, PurchaseDashboardService>();
        services.AddScoped<ISalesPlanningService, SalesPlanningService>();
        services.AddScoped<ISalesDashboardService, SalesDashboardService>();
        services.AddScoped<IExpensePlanningService, ExpensePlanningService>();
        services.AddScoped<IExpenseDashboardService, ExpenseDashboardService>();
        services.AddScoped<SqlApplicationLock>();
        services.AddScoped<ActualLedgerValidationService>();
        services.AddScoped<ActualLedgerProjectionService>();
        services.AddScoped<IActualLedgerService, ActualLedgerService>();
        services.AddScoped<IActualLedgerKeyPostingService, ActualLedgerKeyPostingService>();
        services.AddScoped<IActualLedgerBatchService, ActualLedgerBatchService>();
        services.AddScoped<IIntegrationCredentialService, IntegrationCredentialService>();
        services.AddScoped<IBudgetOperationsService, BudgetOperationsService>();
        services.AddScoped<IAssumptionService, AssumptionService>();
        services.AddScoped<IFormulaAdminService, FormulaAdminService>();
        services.AddScoped<IDriverTemplateService, DriverTemplateService>();
        services.AddScoped<CapexService>();
        services.AddScoped<ICapexService, CapexFacadeService>();
        services.AddScoped<ICashPlanningService, CashPlanningService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<BudgetReservationService>();
        services.AddScoped<IBudgetReservationService, NotifyingBudgetReservationService>();
        services.AddScoped<IReservationReconciliationService, ReservationReconciliationService>();
        services.AddScoped<BudgetTransferService>();
        services.AddScoped<IBudgetTransferService, NotifyingBudgetTransferService>();
        services.AddScoped<BudgetWorkflowService>();
        services.AddScoped<IBudgetWorkflowService, NotifyingBudgetWorkflowService>();
        services.AddScoped<IBudgetInboxService, BudgetInboxService>();
        services.AddScoped<IBudgetAttachmentService, BudgetAttachmentService>();
        services.AddScoped<ICalculationService, CalculationService>();
        services.AddSingleton<IDashboardMetricPolicy, ConfigurationDashboardMetricPolicy>();
        services.AddScoped<IDashboardService, ExecutiveDashboardService>();
        services.AddScoped<IDashboardAnalyticsService>(provider => (IDashboardAnalyticsService)provider.GetRequiredService<IDashboardService>());
        services.AddScoped<IVarianceAnalysisService, VarianceAnalysisService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        services.AddScoped<IPortfolioFinancialService, PortfolioFinancialService>();
        services.AddScoped<IPortfolioDimensionService, PortfolioDimensionService>();
        services.AddScoped<IFiscalCalendarService, FiscalCalendarService>();
        services.AddScoped<SecurityAdminService>();
        services.AddScoped<ISecurityAdminService, GovernedSecurityAdminService>();
        services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<IKpiService, ScoredKpiService>();
        services.AddScoped<IStrategyService, StrategyService>();
        services.AddScoped<IPerformanceBudgetingService, PerformanceBudgetingService>();
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
