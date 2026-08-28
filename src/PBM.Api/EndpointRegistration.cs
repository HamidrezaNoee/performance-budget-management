namespace PBM.Api;

public static class EndpointRegistration
{
    public static RouteGroupBuilder MapPbmModuleEndpoints(this RouteGroupBuilder api)
    {
        // Route-group conventions are applied when endpoint metadata is built, so the filter also
        // covers endpoints already mapped directly on the /api/v1 group in Program.cs.
        api.AddEndpointFilter<CorrelationIdEndpointFilter>();

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
        api.MapDriverTemplateEndpoints();
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
