using PBM.Application;

namespace PBM.Domain.Tests;

public sealed class UserContextAuthorizationExtensionsTests
{
    private static readonly Guid AllowedCompanyId = Guid.NewGuid();
    private static readonly Guid OtherCompanyId = Guid.NewGuid();

    [Fact]
    public void Super_admin_can_read_and_write_every_company()
    {
        var user = Context([SystemRoleCodes.SuperAdmin]);

        Assert.True(user.CanReadCompanyScope(OtherCompanyId));
        Assert.True(user.CanWriteCompanyScope(OtherCompanyId));
    }

    [Fact]
    public void Regular_user_is_limited_to_claimed_company_scopes()
    {
        var user = Context([], [AllowedCompanyId], [AllowedCompanyId]);

        Assert.True(user.CanReadCompanyScope(AllowedCompanyId));
        Assert.True(user.CanWriteCompanyScope(AllowedCompanyId));
        Assert.False(user.CanReadCompanyScope(OtherCompanyId));
        Assert.False(user.CanWriteCompanyScope(OtherCompanyId));
    }

    [Fact]
    public void Read_only_company_access_does_not_grant_write_access()
    {
        var user = Context([], [AllowedCompanyId]);

        user.EnsureCompanyReadAccess(AllowedCompanyId);
        Assert.Throws<UnauthorizedAccessException>(() => user.EnsureCompanyWriteAccess(AllowedCompanyId));
    }

    [Fact]
    public void Ensure_any_role_accepts_one_matching_role()
    {
        var user = Context([SystemRoleCodes.Cfo]);

        user.EnsureAnyRole(SystemRoleCodes.Admin, SystemRoleCodes.Cfo);
        Assert.Throws<UnauthorizedAccessException>(() => user.EnsureAnyRole(SystemRoleCodes.Admin, SystemRoleCodes.Ceo));
    }

    private static IUserContext Context(
        IEnumerable<string> roles,
        IEnumerable<Guid>? companyIds = null,
        IEnumerable<Guid>? writableCompanyIds = null) =>
        new TestUserContext(
            roles.ToHashSet(StringComparer.OrdinalIgnoreCase),
            (companyIds ?? []).ToHashSet(),
            (writableCompanyIds ?? []).ToHashSet());

    private sealed record TestUserContext(
        IReadOnlySet<string> Roles,
        IReadOnlySet<Guid> CompanyIds,
        IReadOnlySet<Guid> WritableCompanyIds) : IUserContext
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; } = Guid.NewGuid();
        public bool IsInRole(string role) => Roles.Contains(role);
        public bool CanAccessCompany(Guid companyId) => CompanyIds.Contains(companyId);
        public bool CanWriteCompany(Guid companyId) => WritableCompanyIds.Contains(companyId);
    }
}
