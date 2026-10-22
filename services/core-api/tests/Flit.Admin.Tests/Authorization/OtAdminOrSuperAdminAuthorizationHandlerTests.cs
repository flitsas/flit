using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #12859 (Feature #12848, Épica #12751) — <see cref="OtAdminOrSuperAdminAuthorizationHandler"/>
/// acepta SOLO rol SuperAdmin u <c>ot_admin</c>; a diferencia de
/// <see cref="OtModuleAuthorizationHandler"/> (ver <see cref="OtModuleAuthorizationHandlerTests"/>),
/// NO evalúa <c>entity_type</c>: un rol OT personalizado (p. ej. <c>gestor_tramites_ot</c>) de un
/// tenant organismo de tránsito NO debe tener éxito aquí.
/// </summary>
public sealed class OtAdminOrSuperAdminAuthorizationHandlerTests
{
    private readonly OtAdminOrSuperAdminAuthorizationHandler _handler = new();

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal user) =>
        new([new OtAdminOrSuperAdminRequirement()], user, resource: null);

    [Fact]
    public async Task SuperAdminRole_Succeeds()
    {
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.SuperAdminRole)));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task OtAdminRole_Succeeds()
    {
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.OtAdminRole)));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task CustomRoleOnTransitOfficeTenant_DoesNotSucceed()
    {
        // A diferencia de OtModuleAuthorizationHandler, entity_type=TRANSIT_OFFICE NO basta aquí.
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, "gestor_tramites_ot"),
            new Claim(AdminAuthorization.EntityTypeClaimType, AdminAuthorization.TransitOfficeEntityType)));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task AdminCompanyRole_DoesNotSucceed()
    {
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.AdminCompanyRole)));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task NoRoleClaim_DoesNotSucceed()
    {
        var context = BuildContext(BuildPrincipal());

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Theory] // HU12859 — la comparación de rol es exacta: variantes de mayúsculas/espacios no cuelan.
    [InlineData("OT_ADMIN")]
    [InlineData("Ot_Admin")]
    [InlineData(" ot_admin")]
    [InlineData("ot_admin ")]
    [InlineData("superadmin")]
    [InlineData("SUPERADMIN")]
    public async Task HU12859_RoleClaimVariants_DoNotSucceed(string roleClaimValue)
    {
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, roleClaimValue)));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
