using PBM.Application;
using PBM.Domain;

namespace PBM.Api;

public static class WorkbookImportEndpoints
{
    public static RouteGroupBuilder MapWorkbookImportPipelineEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/imports/workbook/normalize", async (HttpRequest request, IWorkbookNormalizationService service, CancellationToken ct) =>
        {
            var form = await ReadWorkbookFormAsync(request, ct);
            if (form.Error is not null) return form.Error;
            if (!form.Form!.TryGetValue("sheetName", out var sheetName) || string.IsNullOrWhiteSpace(sheetName)) return Results.BadRequest(new { message = "sheetName is required." });
            if (!TryProfile(form.Form["profile"], out var profile)) return Results.BadRequest(new { message = "profile is invalid." });
            await using var stream = form.File!.OpenReadStream();
            return Results.Ok(await service.NormalizeAsync(stream, sheetName.ToString(), profile, ct));
        }).DisableAntiforgery();

        api.MapPost("/imports/workbook/execute", async (HttpRequest request, IWorkbookImportExecutionService service, CancellationToken ct) =>
        {
            var form = await ReadWorkbookFormAsync(request, ct);
            if (form.Error is not null) return form.Error;
            var fields = form.Form!;
            if (!Guid.TryParse(fields["companyId"], out var companyId)) return Results.BadRequest(new { message = "companyId is invalid." });
            if (!Guid.TryParse(fields["fiscalYearId"], out var fiscalYearId)) return Results.BadRequest(new { message = "fiscalYearId is invalid." });
            var sheetName = fields["sheetName"].ToString();
            if (string.IsNullOrWhiteSpace(sheetName)) return Results.BadRequest(new { message = "sheetName is required." });
            if (!TryProfile(fields["profile"], out var profile)) return Results.BadRequest(new { message = "profile is invalid." });
            ValueKind? valueKind = null;
            if (!string.IsNullOrWhiteSpace(fields["valueKind"]) && int.TryParse(fields["valueKind"], out var kindValue) && Enum.IsDefined(typeof(ValueKind), kindValue)) valueKind = (ValueKind)kindValue;
            var importRequest = new WorkbookImportExecutionRequest(companyId, fiscalYearId, sheetName, profile, valueKind);
            await using var stream = form.File!.OpenReadStream();
            return Results.Ok(await service.ImportAsync(stream, form.File.FileName, importRequest, ct));
        }).DisableAntiforgery();

        return api;
    }

    private static bool TryProfile(string? text, out WorkbookTemplateProfile profile)
    {
        profile = WorkbookTemplateProfile.Unknown;
        return int.TryParse(text, out var value) && Enum.IsDefined(typeof(WorkbookTemplateProfile), value) && (profile = (WorkbookTemplateProfile)value) != WorkbookTemplateProfile.Unknown;
    }

    private static async Task<(IFormCollection? Form, IFormFile? File, IResult? Error)> ReadWorkbookFormAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType) return (null, null, Results.BadRequest(new { message = "multipart/form-data is required." }));
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return (form, null, Results.BadRequest(new { message = "An XLSX file is required." }));
        if (file.Length > 50 * 1024 * 1024) return (form, file, Results.BadRequest(new { message = "Workbook is larger than the 50 MB limit." }));
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return (form, file, Results.BadRequest(new { message = "Only .xlsx files are supported." }));
        return (form, file, null);
    }
}
