namespace PBM.Api;

public static class EndpointRegistration
{
    public static RouteGroupBuilder MapPbmModuleEndpoints(this RouteGroupBuilder api)
    {
        api.MapEnterpriseEndpoints();
        api.MapForecastEndpoints();
        api.MapBudgetWorkflowEndpoints();
        api.MapBudgetInboxEndpoints();
        api.MapBudgetOperationsEndpoints();
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
