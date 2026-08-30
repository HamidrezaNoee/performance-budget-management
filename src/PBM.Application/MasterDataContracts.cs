namespace PBM.Application;

public sealed record MasterDataDimensionDto(
    Guid Id,
    string Code,
    string Name,
    bool IsHierarchical,
    bool IsSystem,
    bool IsActive);

public sealed record MasterDataMemberDto(
    Guid Id,
    Guid DimensionId,
    Guid? ParentId,
    Guid? CompanyId,
    string Code,
    string Name,
    string? ExternalKey,
    bool IsActive);

public sealed record CreateMasterDataMemberRequest(
    Guid DimensionId,
    Guid? CompanyId,
    string Code,
    string Name,
    string? ExternalKey);

public sealed record UpdateMasterDataMemberRequest(
    string Name,
    string? ExternalKey,
    bool IsActive);

public interface IMasterDataService
{
    Task<IReadOnlyList<MasterDataDimensionDto>> GetDimensionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MasterDataMemberDto>> GetMembersAsync(Guid dimensionId, Guid? companyId, bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<MasterDataMemberDto> CreateMemberAsync(CreateMasterDataMemberRequest request, CancellationToken cancellationToken = default);
    Task<MasterDataMemberDto> UpdateMemberAsync(Guid memberId, UpdateMasterDataMemberRequest request, CancellationToken cancellationToken = default);
}
