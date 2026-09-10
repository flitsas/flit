using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>HU #11226 — resolución de excludeTransitOffices según rol.</summary>
public sealed class CompanyTenantAccessTests
{
    [Fact]
    public void ResolveExclude_SuperAdmin_Null_DefaultsTrue()
    {
        var user = Principal("SuperAdmin");
        CompanyTenantAccess.ResolveExcludeTransitOffices(user, null).Should().BeTrue();
    }

    [Fact]
    public void ResolveExclude_SuperAdmin_False_AllowsInclude()
    {
        var user = Principal("SuperAdmin");
        CompanyTenantAccess.ResolveExcludeTransitOffices(user, false).Should().BeFalse();
    }

    [Fact]
    public void ResolveExclude_AdminCompany_False_ForcesTrue()
    {
        var user = Principal("AdminCompany", tenantId: Guid.NewGuid());
        CompanyTenantAccess.ResolveExcludeTransitOffices(user, false).Should().BeTrue();
    }

    [Fact]
    public void ForbidIfForeignTenant_AdminCompany_SameTenant_Allows()
    {
        var tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var user = Principal("AdminCompany", tenantId);
        CompanyTenantAccess.ForbidIfForeignTenant(user, tenantId).Should().BeNull();
    }

    [Fact]
    public void ForbidIfForeignTenant_AdminCompany_OtherTenant_Forbids()
    {
        var own = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var other = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var user = Principal("AdminCompany", own);
        var result = CompanyTenantAccess.ForbidIfForeignTenant(user, other);
        result.Should().NotBeNull();
    }

    [Fact]
    public void ForbidIfForeignTenant_SuperAdmin_AnyTenant_Allows()
    {
        var user = Principal("SuperAdmin");
        CompanyTenantAccess.ForbidIfForeignTenant(user, Guid.NewGuid()).Should().BeNull();
    }

    private static ClaimsPrincipal Principal(string role, Guid? tenantId = null)
    {
        var claims = new List<Claim> { new(AdminAuthorization.RoleClaimType, role) };
        if (tenantId is { } tid)
        {
            claims.Add(new Claim(AdminAuthorization.TenantIdClaimType, tid.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
