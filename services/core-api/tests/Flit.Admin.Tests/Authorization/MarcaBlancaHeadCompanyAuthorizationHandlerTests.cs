using System.Security.Claims;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #12429 (endurecimiento del hecho 88) — policy MarcaBlancaHeadCompany: igual que
/// <see cref="GroupHeadCompanyAuthorizationHandlerTests"/> (AdminCompany + <c>is_group_parent</c>)
/// pero exige además <c>TenantType == MARCA_BLANCA</c>. Una Concesión con hijas sigue siendo cabeza
/// de grupo legítima para <see cref="GroupHeadCompanyRequirement"/> (rutas de jerarquía, #12345) pero
/// NUNCA para esta policy (marca/dominio).
/// </summary>
public sealed class MarcaBlancaHeadCompanyAuthorizationHandlerTests
{
    private static readonly Guid Head = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task AC_ConcesionCabezaDeGrupo_NoSucceed()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Head, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Head, HeadTenantTypes.Concesion, true, null));

        var handler = new MarcaBlancaHeadCompanyAuthorizationHandler(repo);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("role", AdminAuthorization.AdminCompanyRole),
            new Claim("tenant_id", Head.ToString()),
        ], "test"));

        var context = new AuthorizationHandlerContext(
            [new MarcaBlancaHeadCompanyRequirement()],
            user,
            resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("una Concesión con hijas no puede autogestionar marca/dominio");
    }

    [Fact]
    public async Task AC_MarcaBlancaCabezaDeGrupo_Succeed()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Head, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Head, HeadTenantTypes.MarcaBlanca, true, null));

        var handler = new MarcaBlancaHeadCompanyAuthorizationHandler(repo);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("role", AdminAuthorization.AdminCompanyRole),
            new Claim("tenant_id", Head.ToString()),
        ], "test"));

        var context = new AuthorizationHandlerContext(
            [new MarcaBlancaHeadCompanyRequirement()],
            user,
            resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task AC_AdminCompanySinCabeza_NoSucceed()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Head, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Head, HeadTenantTypes.MarcaBlanca, false, null));

        var handler = new MarcaBlancaHeadCompanyAuthorizationHandler(repo);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("role", AdminAuthorization.AdminCompanyRole),
            new Claim("tenant_id", Head.ToString()),
        ], "test"));

        var context = new AuthorizationHandlerContext(
            [new MarcaBlancaHeadCompanyRequirement()],
            user,
            resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task SuperAdmin_SucceedSinConsultarJerarquia()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        var handler = new MarcaBlancaHeadCompanyAuthorizationHandler(repo);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("role", AdminAuthorization.SuperAdminRole),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
        ], "test"));

        var context = new AuthorizationHandlerContext(
            [new MarcaBlancaHeadCompanyRequirement()],
            user,
            resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        await repo.DidNotReceiveWithAnyArgs().GetHierarchyInfoAsync(default, TestContext.Current.CancellationToken);
    }
}
