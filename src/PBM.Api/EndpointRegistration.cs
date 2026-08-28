namespace PBM.Api;

public static class EndpointRegistration
{
    public static RouteGroupBuilder MapPbmModuleEndpoints(this RouteGroupBuilder api)
    {
        api.MapAccountEndpoints();
        api.MapNotificationEndpoints();
        api.MapEnterpriseEndpoints();
        api.MapForecastEndpoints();
        api.MapBudgetWorkflowEndpoints();
        api.MapBudgetInboxEndpoints();
        api.MapBudgetAttachmentEndpoints();
        api.MapBudgetReservationEndpoints();
        api.MapBudgetTransferEndpoints();
        api.MapBudgetOperationsEndpoints();
        api.MapAssumptionEndpoints();
        api.MapFormulaAdminEndpoints();
        api.MapCapexEndpoints();
        api.MapCashPlanningEndpoints();
        api.MapCalculationEndpoints();
        api.MapVarianceAnalysisEndpoints();
        api.MapFinancialReportEndpoints();
        api.MapWorkbookImportPipelineEndpoints();
        api.MapFiscalCalendarEndpoints();
        api.MapSecurityAdminEndpoints();
        api.MapOrganizationAdminEndpoints();
        api.MapScenarioEndpoints();
        return api;
    }
}
