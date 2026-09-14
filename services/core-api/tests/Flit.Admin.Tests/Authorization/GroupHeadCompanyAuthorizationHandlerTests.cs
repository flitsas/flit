using System.Security.Claims;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>HU #12345 AC4/AC5 — policy GroupHeadCompany.</summary>
public sealed class GroupHeadCompanyAuthorizationHandlerTests
{
    private static readonly Guid Head = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task AC5_AdminCompanySinCabeza_NoSucceed()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Head, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Head, "CONCESIONARIO", false, null));

        var handler = new GroupHeadCompanyAuthorizationHandler(repo);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("role", AdminAuthorization.AdminCompanyRole),
            new Claim("tenant_id", Head.ToString()),
        ], "test"));

        var context = new AuthorizationHandlerContext(
            [new GroupHeadCompanyRequirement()],
            user,
            resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task AC4_AdminCompanyCabeza_Succeed()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Head, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Head, HeadTenantTypes.Concesion, true, null));

        var handler = new GroupHeadCompanyAuthorizationHandler(repo);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("role", AdminAuthorization.AdminCompanyRole),
            new Claim("tenant_id", Head.ToString()),
        ], "test"));

        var context = new AuthorizationHandlerContext(
            [new GroupHeadCompanyRequirement()],
            user,
            resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdmin_SucceedSinConsultarJerarquia()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        var handler = new GroupHeadCompanyAuthorizationHandler(repo);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("role", AdminAuthorization.SuperAdminRole),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
        ], "test"));

        var context = new AuthorizationHandlerContext(
            [new GroupHeadCompanyRequirement()],
            user,
            resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        await repo.DidNotReceiveWithAnyArgs().GetHierarchyInfoAsync(default, default);
    }
}
