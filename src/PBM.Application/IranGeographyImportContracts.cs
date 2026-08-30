namespace PBM.Application;

public sealed record IranGeographyImportResultDto(
    int SourceRows,
    int Created,
    int Updated,
    int Provinces,
    int Counties,
    int Districts,
    int RuralDistricts,
    int CitiesOrVillages,
    string SourceRepository,
    string SourceCommit);

public interface IIranGeographyImportService
{
    Task<IranGeographyImportResultDto> ImportAsync(CancellationToken cancellationToken = default);
}
