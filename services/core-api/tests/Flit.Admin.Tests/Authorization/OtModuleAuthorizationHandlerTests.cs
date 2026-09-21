using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Tests unitarios del <see cref="OtModuleAuthorizationHandler"/>: la policy del módulo OT
/// acepta SuperAdmin, ot_admin y cualquier rol de un tenant organismo de tránsito.
/// </summary>
public sealed class OtModuleAuthorizationHandlerTests
{
    private readonly OtModuleAuthorizationHandler _handler = new();

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal user) =>
        new([new OtModuleRequirement()], user, resource: null);

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
    public async Task CustomRoleOnTransitOfficeTenant_Succeeds()
    {
        // "Gestor OT": rol propio del organismo, sin el código ot_admin.
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, "gestor_ot"),
            new Claim(AdminAuthorization.EntityTypeClaimType, "TRANSIT_OFFICE")));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task EntityTypeIsCaseInsensitive()
    {
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, "gestor_ot"),
            new Claim(AdminAuthorization.EntityTypeClaimType, "transit_office")));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task CustomRoleOnCompanyTenant_DoesNotSucceed()
    {
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, "Radicador"),
            new Claim(AdminAuthorization.EntityTypeClaimType, "COMPANY")));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task AdminCompanyWithoutEntityType_DoesNotSucceed()
    {
        var context = BuildContext(BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.AdminCompanyRole)));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
